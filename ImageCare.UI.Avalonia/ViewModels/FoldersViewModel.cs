using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.DrivesWatcherService;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.FileSystemWatcherService;
using ImageCare.Core.Services.FolderHistoryService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Domain;
using ImageCare.UI.Avalonia.ViewModels.Folders;

using MapsterMapper;

using Prism.Navigation.Regions;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class FoldersViewModel : NavigatedViewModelBase
{
    private readonly LocalizationManager _localization;
    private readonly IMediaPreviewOperationsService _mediaPreviewOperations;
    private readonly FolderTagsEditorWindowService _folderTagsEditorWindowService;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly FolderReconciliationCoordinator _folderReconciliation;

    private CompositeDisposable? _subscriptions;

    public FoldersViewModel(
        IFolderService folderService,
        IMediaPreviewOperationsService mediaPreviewOperations,
        IFolderHistoryService folderHistoryService,
        IFileDialogService fileDialogService,
        IFileSystemService fileSystemService,
        FolderTagsEditorWindowService folderTagsEditorWindowService,
        IConfigurationService configurationService,
        IMultiSourcesFileSystemWatcherService multiSourcesFileSystemWatcherService,
        IDrivesWatcherService drivesWatcherService,
        IMapper mapper,
        ILogger logger,
        LocalizationManager localization,
        SynchronizationContext synchronizationContext)
    {
        ArgumentNullException.ThrowIfNull(folderService);
        _mediaPreviewOperations = mediaPreviewOperations ?? throw new ArgumentNullException(nameof(mediaPreviewOperations));
        ArgumentNullException.ThrowIfNull(folderHistoryService);
        ArgumentNullException.ThrowIfNull(fileDialogService);
        ArgumentNullException.ThrowIfNull(fileSystemService);
        _folderTagsEditorWindowService = folderTagsEditorWindowService ?? throw new ArgumentNullException(nameof(folderTagsEditorWindowService));
        ArgumentNullException.ThrowIfNull(configurationService);
        ArgumentNullException.ThrowIfNull(multiSourcesFileSystemWatcherService);
        ArgumentNullException.ThrowIfNull(drivesWatcherService);
        ArgumentNullException.ThrowIfNull(mapper);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _synchronizationContext = synchronizationContext ?? throw new ArgumentNullException(nameof(synchronizationContext));

        Tree = new FolderTreeViewModel(
            folderService,
            fileSystemService,
            configurationService,
            drivesWatcherService,
            mapper,
            logger,
            synchronizationContext);
        Search = new FolderSearchViewModel(
            fileSystemService,
            mapper,
            localization,
            synchronizationContext,
            SetSearchResultAsync);
        Recent = new RecentFoldersViewModel(
            folderHistoryService,
            fileDialogService,
            mapper,
            localization,
            logger,
            synchronizationContext,
            () => Tree.DeviceRoot,
            () => Tree.FileManagerPanel,
            Tree.ClearSelectionIfCurrent);
        _folderReconciliation = new FolderReconciliationCoordinator(
            multiSourcesFileSystemWatcherService,
            folderService,
            fileSystemService,
            mapper,
            logger,
            synchronizationContext,
            () => Tree.DeviceRoot,
            () => Tree.SelectedFileSystemItem,
            () => Tree.FileManagerPanel,
            value => Tree.SelectedFileSystemItem = value,
            Tree.SaveLastDirectoryPath);

        Tree.SelectionChanged += Search.SetSelectedFolder;
        Tree.LoadingChanged += Search.SetHostLoading;
        Tree.RootLoaded += Recent.Refresh;
        Tree.RevealRequested += OnTreeRevealRequested;
        _localization.CultureChanged += OnCultureChanged;

        EditFolderTagsCommand = CreateCommand<DirectoryViewModel>(EditFolderTags);
    }

    public FolderTreeViewModel Tree { get; }

    public FolderSearchViewModel Search { get; }

    public RecentFoldersViewModel Recent { get; }

    public ICommand EditFolderTagsCommand { get; }

    public event Action<DirectoryViewModel>? RevealSelectedFolderRequested;

    public FileManagerPanel FileManagerPanel => Tree.FileManagerPanel;

    public override void OnNavigatedTo(NavigationContext navigationContext)
    {
        try
        {
            if (navigationContext.Parameters["panel"] is not FileManagerPanel panel)
            {
                _logger.Error("Navigation parameter 'panel' is missing or invalid");
                return;
            }

            Tree.Initialize(panel);
            Recent.Start();

            _subscriptions?.Dispose();
            _subscriptions = new CompositeDisposable
            {
                _mediaPreviewOperations.MediaPreviewNavigationRequested
                                       .ObserveOn(_synchronizationContext)
                                       .Subscribe(Tree.HandleMediaPreviewNavigationRequested, OnObservableError)
            };

            Tree.Start();
            _folderReconciliation.Start();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize FoldersViewModel");
            throw;
        }
    }

    public override void OnNavigatedFrom(NavigationContext navigationContext)
    {
        try
        {
            Search.Clear();
            Recent.Stop();
            _subscriptions?.Dispose();
            _subscriptions = null;
            Tree.Stop();
            _folderReconciliation.Stop();

            base.OnNavigatedFrom(navigationContext);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during FoldersViewModel cleanup");
        }
    }

    /// <summary>
    /// Selects a folder in the tree while preserving the external MCP and shell contract.
    /// </summary>
    internal Task<bool> SelectFolderInTreeAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        return Tree.SelectFolderInTreeAsync(folderPath, cancellationToken);
    }

    private async Task SetSearchResultAsync(DirectoryViewModel directoryViewModel)
    {
        if (directoryViewModel == null || Tree.SelectedFileSystemItem == null)
        {
            return;
        }

        try
        {
            await Tree.SelectDescendantAsync(Tree.SelectedFileSystemItem, directoryViewModel.Path);
            Search.Clear();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open search result: {Path}", directoryViewModel.Path);
        }
    }

    private void EditFolderTags(DirectoryViewModel directory)
    {
        if (directory == null || string.IsNullOrWhiteSpace(directory.Path))
        {
            return;
        }

        _folderTagsEditorWindowService.Show(directory.Path);
    }

    private void OnTreeRevealRequested(DirectoryViewModel directory)
    {
        RevealSelectedFolderRequested?.Invoke(directory);
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        Search.OnCultureChanged();
    }

    private void OnObservableError(Exception ex)
    {
        _logger.Error(ex, "Error in FoldersViewModel observable subscription");
    }
}
