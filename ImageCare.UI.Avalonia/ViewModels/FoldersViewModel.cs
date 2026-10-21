using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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

using MapsterMapper;

using Prism.Navigation.Regions;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class FoldersViewModel : NavigatedViewModelBase
{
    private const int RecentFoldersDisplayLimit = 10;
    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromSeconds(10);

    private readonly LocalizationManager _localization;
    private readonly IFolderService _folderService;
    private readonly IMediaPreviewOperationsService _mediaPreviewOperations;
    private readonly IFolderHistoryService _folderHistoryService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IConfigurationService _configurationService;
    private readonly IMultiSourcesFileSystemWatcherService _multiSourcesFileSystemWatcherService;
    private readonly IDrivesWatcherService _drivesWatcherService;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly object _reconciliationSyncRoot = new();
    private readonly Dictionary<string, long> _reconciliationVersions = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private long _reconciliationVersion;

    private DirectoryViewModel? _selectedFileSystemItem;
    private CompositeDisposable _compositeDisposable;
    private CancellationTokenSource? _searchCancellationTokenSource;
    private CancellationTokenSource? _mediaNavigationCancellationTokenSource;

    private string? _searchText;
    private bool _isLoading;
    private bool _isSearching;
    private string? _searchHeader;
    private bool _isSearchEnabled;
    private bool _isInSearchSession;
    private string _searchTarget = string.Empty;

    public FoldersViewModel(IFolderService folderService,
                            IMediaPreviewOperationsService mediaPreviewOperations,
                            IFolderHistoryService folderHistoryService,
                            IFileDialogService fileDialogService,
                            IFileSystemService fileSystemService,
                            IConfigurationService configurationService,
                            IMultiSourcesFileSystemWatcherService multiSourcesFileSystemWatcherService,
                            IDrivesWatcherService drivesWatcherService,
                            IMapper mapper,
                            ILogger logger,
                            LocalizationManager localization,
                            SynchronizationContext synchronizationContext)
    {
        _folderService = folderService ?? throw new ArgumentNullException(nameof(folderService));
        _mediaPreviewOperations = mediaPreviewOperations ?? throw new ArgumentNullException(nameof(mediaPreviewOperations));
        _folderHistoryService = folderHistoryService ?? throw new ArgumentNullException(nameof(folderHistoryService));
        _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _multiSourcesFileSystemWatcherService = multiSourcesFileSystemWatcherService ?? throw new ArgumentNullException(nameof(multiSourcesFileSystemWatcherService));
        _drivesWatcherService = drivesWatcherService ?? throw new ArgumentNullException(nameof(drivesWatcherService));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _synchronizationContext = synchronizationContext ?? throw new ArgumentNullException(nameof(synchronizationContext));
        _searchTarget = _localization["Folders_SearchFolders"];
        _localization.CultureChanged += OnCultureChanged;

        OnViewLoadedCommand = CreateAsyncCommand(OnViewLoadedAsync, () => !IsLoading);

        PerformSearchCommand = CreateAsyncCommand(PerformSearchAsync, CanPerformSearch)
            .ObservesProperty(()=>SearchText)
            .ObservesProperty(()=>IsSearching)
            .ObservesProperty(()=>IsLoading);
        ClearSearchCommand = CreateCommand(ClearSearch, CanClearSearch).ObservesProperty(() => IsInSearchSession);
        SetSearchResultCommand = CreateAsyncCommand<DirectoryViewModel>(SetSearchResultAsync);
        AddRecentFolderCommand = CreateAsyncCommand(AddRecentFolderAsync);
        AddFolderToRecentCommand = CreateCommand<DirectoryViewModel>(AddFolderToRecent);
        RemoveRecentFolderCommand = CreateCommand<DirectoryViewModel>(RemoveRecentFolder);

        FileSystemItemViewModels = new Collections.SortedObservableCollection<DirectoryViewModel>();
        SearchResults = new ObservableCollection<DirectoryViewModel>();
    }

    public ICommand SetSearchResultCommand { get; set; }

    public Collections.SortedObservableCollection<DirectoryViewModel> FileSystemItemViewModels { get; }

    public ObservableCollection<DirectoryViewModel> SearchResults { get; }

    public ICommand OnViewLoadedCommand { get; }

    public ICommand PerformSearchCommand { get; }

    public ICommand ClearSearchCommand { get; }

    public ICommand AddRecentFolderCommand { get; }

    public ICommand AddFolderToRecentCommand { get; }

    public ICommand RemoveRecentFolderCommand { get; }

    public event Action<DirectoryViewModel>? RevealSelectedFolderRequested;

    public string SearchTarget
    {
        get => _searchTarget;
        set => SetProperty(ref _searchTarget, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsSearchEnabled
    {
        get => _isSearchEnabled;
        set => SetProperty(ref _isSearchEnabled, value);
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set => SetProperty(ref _isSearching, value);
    }

    public bool IsInSearchSession
    {
        get => _isInSearchSession;
        private set => SetProperty(ref _isInSearchSession, value);
    }

    public string? SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public string? SearchHeader
    {
        get => _searchHeader;
        private set => SetProperty(ref _searchHeader, value);
    }

    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText) && SearchText.Length>=3;

    public DirectoryViewModel? SelectedFileSystemItem
    {
        get => _selectedFileSystemItem;
        set => SetSelectedFileSystemItem(value);
    }

    public FileManagerPanel FileManagerPanel { get; private set; } = FileManagerPanel.Left;

    public override void OnNavigatedTo(NavigationContext navigationContext)
    {
        try
        {
            if (navigationContext.Parameters["panel"] is not FileManagerPanel panel)
            {
                _logger.Error("Navigation parameter 'panel' is missing or invalid");
                return;
            }

            FileManagerPanel = panel;
            _folderHistoryService.HistoryChanged -= OnFolderHistoryChanged;
            _folderHistoryService.HistoryChanged += OnFolderHistoryChanged;

            _compositeDisposable = new CompositeDisposable
            {
                _mediaPreviewOperations.MediaPreviewNavigationRequested
                                       .ObserveOn(_synchronizationContext)
                                       .Subscribe(OnMediaPreviewNavigationRequested, OnObservableError),
                _drivesWatcherService.DriveMounted
                                     .ObserveOn(_synchronizationContext)
                                     .Subscribe(OnDriveMounted, OnObservableError),
                _drivesWatcherService.DriveUnmounted
                                     .ObserveOn(_synchronizationContext)
                                     .Subscribe(OnDriveUnmounted, OnObservableError),
                _drivesWatcherService.DriveAvailableFreeSpaceChanged
                                     .DistinctUntilChanged()
                                     .ObserveOn(_synchronizationContext)
                                     .Subscribe(OnFreeSpaceChanged, OnObservableError),
                _folderService.FolderVisited
                              .Where(folder => folder.FileManagerPanel == FileManagerPanel)
                              .Subscribe(OnFolderVisited, OnObservableError),
                _folderService.FolderLeft
                              .Where(folder => folder.FileManagerPanel == FileManagerPanel)
                              .Subscribe(OnFolderLeft, OnObservableError),
                _multiSourcesFileSystemWatcherService.DirectoryCreated
                                                     .ObserveOn(_synchronizationContext)
                                                     .Subscribe(OnDirectoryCreated, OnObservableError),
                _multiSourcesFileSystemWatcherService.DirectoryDeleted
                                                     .DistinctUntilChanged(folder => folder.Path)
                                                     .ObserveOn(_synchronizationContext)
                                                     .Subscribe(OnDirectoryRemoved, OnObservableError),
                _multiSourcesFileSystemWatcherService.DirectoryRenamed
                                                     .DistinctUntilChanged(folder => folder.NewDirectoryModel.Path)
                                                     .ObserveOn(_synchronizationContext)
                                                     .Subscribe(OnDirectoryRenamed, OnObservableError),
                _multiSourcesFileSystemWatcherService.RescanRequested
                                                     .ObserveOn(_synchronizationContext)
                                                     .Subscribe(
                                                         path => ScheduleDirectoryReconciliation(path, "watcher-overflow"),
                                                         OnObservableError)
            };

            _drivesWatcherService.StartWatching();
            _multiSourcesFileSystemWatcherService.StartWatching();

            if (FileSystemItemViewModels.Count == 0 && !IsLoading)
            {
                _ = OnViewLoadedAsync();
            }
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
            ClearSearch();
            _mediaNavigationCancellationTokenSource?.Cancel();
            _folderHistoryService.HistoryChanged -= OnFolderHistoryChanged;
            _compositeDisposable?.Dispose();

            _drivesWatcherService.StopWatching();
            _multiSourcesFileSystemWatcherService.StopWatching();
            _multiSourcesFileSystemWatcherService.ClearWatchers();

            base.OnNavigatedFrom(navigationContext);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during FoldersViewModel cleanup");
        }
    }

    private void OnMediaPreviewNavigationRequested(SelectedMediaPreview requestedPreview)
    {
        if (requestedPreview.FileManagerPanel != FileManagerPanel ||
            string.IsNullOrWhiteSpace(requestedPreview.Url))
        {
            return;
        }

        var directoryPath = Path.GetDirectoryName(requestedPreview.Url);
        if (string.IsNullOrWhiteSpace(directoryPath) ||
            (SelectedFileSystemItem != null && PathsEqual(SelectedFileSystemItem.Path, directoryPath)))
        {
            return;
        }

        _mediaNavigationCancellationTokenSource?.Cancel();
        var cancellation = new CancellationTokenSource();
        _mediaNavigationCancellationTokenSource = cancellation;
        _ = NavigateToMediaFolderAsync(directoryPath, cancellation);
    }

    private async Task NavigateToMediaFolderAsync(
        string directoryPath,
        CancellationTokenSource cancellation)
    {
        var cancellationToken = cancellation.Token;
        try
        {
            var directoryExists = await Task.Run(
                () => _fileSystemService.DirectoryExists(directoryPath),
                cancellationToken);
            if (!directoryExists)
            {
                return;
            }

            var root = FileSystemItemViewModels.OfType<DeviceViewModel>().FirstOrDefault();
            if (root == null)
            {
                _logger.Warning("Cannot navigate to media folder before folder tree is loaded: {Path}", directoryPath);
                return;
            }

            var targetPath = NormalizePath(directoryPath);
            var drive = root.ChildFileSystemItems
                            .OfType<DriveViewModel>()
                            .Where(item => item is not SpecialDirectoryViewModel)
                            .Where(item => IsSamePathOrDescendant(item.Path, targetPath))
                            .OrderByDescending(item => NormalizePath(item.Path).Length)
                            .FirstOrDefault();
            if (drive == null)
            {
                _logger.Warning(
                    "Cannot navigate to media folder because its drive is unavailable: {Path}, panel: {Panel}",
                    directoryPath,
                    FileManagerPanel);
                return;
            }

            var targetDirectory = await GoToFolderFromSelectedAsync(drive, targetPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_mediaNavigationCancellationTokenSource, cancellation))
            {
                return;
            }

            SelectedFileSystemItem = targetDirectory;
            RevealSelectedFolderRequested?.Invoke(targetDirectory);
        }
        catch (OperationCanceledException)
        {
            // A newer media navigation request replaced this one.
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to navigate to media folder: {Path}", directoryPath);
        }
        finally
        {
            if (ReferenceEquals(_mediaNavigationCancellationTokenSource, cancellation))
            {
                _mediaNavigationCancellationTokenSource = null;
            }

            cancellation.Dispose();
        }
    }

    private async Task SetSearchResultAsync(DirectoryViewModel directoryViewModel)
    {
        if (directoryViewModel == null || SelectedFileSystemItem == null)
        {
            return;
        }

        var sourceDirectory = SelectedFileSystemItem;
        var targetPath = directoryViewModel.Path;

        try
        {
            var targetDirectory = await GoToFolderFromSelectedAsync(sourceDirectory, targetPath);
            SelectedFileSystemItem = targetDirectory;
            ClearSearch();
            RevealSelectedFolderRequested?.Invoke(targetDirectory);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open search result: {Path}", targetPath);
        }
    }

    private async Task PerformSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText) || SelectedFileSystemItem == null)
        {
            return;
        }

        CancelSearch(); // Cancel previous search if any

        _searchCancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _searchCancellationTokenSource.Token;

        try
        {
            SearchResults.Clear();
            IsSearching = true;
            IsInSearchSession = true;

            SearchHeader = _localization.Get(
                "Folders_SearchHeader",
                SearchText,
                SelectedFileSystemItem?.Name ?? _localization["Folders_AllFolders"]);

            await Task.Run(
                () =>
                {
                    var directories = _fileSystemService.EnumerateDirectories(SelectedFileSystemItem.Path, $"*{SearchText}*", SearchOption.AllDirectories)
                                                        .Select(d => new DirectoryModel(Path.GetFileName(d), d));
                    foreach (var directory in directories)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        _synchronizationContext.Send(d => { SearchResults.Add(_mapper.Map<DirectoryViewModel>(directory)); }, null);
                    }
                },
                cancellationToken);

            SearchHeader = _localization.Get("Folders_SearchFound", SearchResults.Count, SearchText);
        }
        catch (OperationCanceledException)
        {
            SearchHeader = _localization["Folders_SearchCancelled"];
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to perform search");

            SearchHeader = _localization["Folders_SearchFailed"];
        }
        finally
        {
            IsSearching = false;
            if (cancellationToken.IsCancellationRequested)
            {
                ClearSearch();
            }
        }
    }

    private void ClearSearch()
    {
        CancelSearch();
        SearchText = null;
        SearchResults.Clear();
        IsSearching = false;
        IsInSearchSession = false;
    }

    private void CancelSearch()
    {
        _searchCancellationTokenSource?.Cancel();
        _searchCancellationTokenSource?.Dispose();
        _searchCancellationTokenSource = null;
    }

    private bool CanPerformSearch()
    {
        return !IsLoading && HasSearchText && !IsSearching;
    }

    private bool CanClearSearch()
    {
        return IsInSearchSession;
    }

    private void SetSelectedFileSystemItem(DirectoryViewModel? value)
    {
        if (ReferenceEquals(_selectedFileSystemItem, value))
        {
            return;
        }

        if (_selectedFileSystemItem != null)
        {
            _selectedFileSystemItem.IsSelected = false;
        }

        if (SetProperty(ref _selectedFileSystemItem, value) && value != null)
        {
            try
            {
                value.IsSelected = true;
                var selectedDirectory = new SelectedDirectory(value.Name, value.Path, FileManagerPanel);
                _folderService.SetSelectedDirectory(selectedDirectory);
                SaveLastDirectoryPath(value);

                IsSearchEnabled = value is not (SpecialDirectoryViewModel or DriveViewModel or DeviceViewModel);
                SearchTarget = _localization.Get("Folders_SearchFoldersIn", _selectedFileSystemItem.Name);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to set selected directory: {Path}", value.Path);
            }
        }
    }

    private async Task AddRecentFolderAsync()
    {
        try
        {
            var folderPath = await _fileDialogService.ShowOpenFolderDialogAsync(
                _localization["Folders_SelectRecent"]);
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            _folderHistoryService.AddFolderToPopular(folderPath);
            _folderHistoryService.SaveHistory();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to add a folder to Recent");
        }
    }

    private void AddFolderToRecent(DirectoryViewModel directory)
    {
        if (directory == null || directory.IsRecentEntry ||
            directory is DeviceViewModel or SpecialDirectoryViewModel)
        {
            return;
        }

        try
        {
            _folderHistoryService.AddFolderToPopular(directory.Path);
            _folderHistoryService.SaveHistory();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to add a folder to Recent: {Path}", directory.Path);
        }
    }

    private void RemoveRecentFolder(DirectoryViewModel directory)
    {
        if (directory == null || !directory.IsPinnedPopular)
        {
            return;
        }

        try
        {
            if (ReferenceEquals(SelectedFileSystemItem, directory))
            {
                SelectedFileSystemItem = null;
            }

            _folderHistoryService.RemoveFolderFromPopular(directory.Path);
            _folderHistoryService.SaveHistory();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to remove a folder from Recent: {Path}", directory.Path);
        }
    }

    private void OnFolderHistoryChanged(object? sender, EventArgs e)
    {
        _synchronizationContext.Post(_ => UpdateRecentFolderEntries(), null);
    }

    private void UpdateRecentFolderEntries()
    {
        try
        {
            var root = FileSystemItemViewModels.OfType<DeviceViewModel>().FirstOrDefault();
            var recentFolder = root?.ChildFileSystemItems.OfType<SpecialDirectoryViewModel>().FirstOrDefault();
            if (recentFolder == null)
            {
                return;
            }

            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var pinnedPaths = (_folderHistoryService.GetPopularFolders() ?? [])
                             .Where(path => !string.IsNullOrWhiteSpace(path))
                             .Distinct(comparer)
                             .ToArray();
            var automaticPaths = (_folderHistoryService.GetSmartFolders(10) ?? [])
                                .Select(folder => folder.Path)
                                .Where(path => !string.IsNullOrWhiteSpace(path))
                                .Distinct(comparer)
                                .Take(RecentFoldersDisplayLimit);
            var recentPaths = pinnedPaths
                             .Concat(automaticPaths)
                             .Distinct(comparer)
                             .ToArray();

            var staleItems = recentFolder.ChildFileSystemItems
                                         .Where(item => !recentPaths.Any(path => PathsEqual(path, item.Path)))
                                         .ToArray();
            foreach (var staleItem in staleItems)
            {
                recentFolder.ChildFileSystemItems.Remove(staleItem);
            }

            for (var index = 0; index < recentPaths.Length; index++)
            {
                var path = recentPaths[index];
                var item = recentFolder.ChildFileSystemItems.FirstOrDefault(existing => PathsEqual(existing.Path, path));
                if (item == null)
                {
                    var name = Path.GetFileName(path);
                    var model = new DirectoryModel(string.IsNullOrWhiteSpace(name) ? path : name, path);
                    item = _mapper.Map<DirectoryViewModel>(model);
                    item.SetFileManagerPanel(FileManagerPanel);
                    item.SetRecentOrder(index, pinnedPaths.Any(pinnedPath => PathsEqual(pinnedPath, path)));
                    recentFolder.ChildFileSystemItems.Add(item);
                }
                else
                {
                    item.SetRecentOrder(index, pinnedPaths.Any(pinnedPath => PathsEqual(pinnedPath, path)));
                }

                var currentIndex = recentFolder.ChildFileSystemItems.IndexOf(item);
                if (currentIndex != index)
                {
                    recentFolder.ChildFileSystemItems.Move(currentIndex, index);
                }
            }

            recentFolder.IsExpanded = true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to refresh Recent folders");
        }
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        SearchTarget = _selectedFileSystemItem == null
            ? _localization["Folders_SearchFolders"]
            : _localization.Get("Folders_SearchFoldersIn", _selectedFileSystemItem.Name);

        if (IsSearching)
        {
            SearchHeader = _localization.Get(
                "Folders_SearchHeader",
                SearchText,
                _selectedFileSystemItem?.Name ?? _localization["Folders_AllFolders"]);
        }
    }

    private async Task OnViewLoadedAsync()
    {
        IsLoading = true;

        try
        {
            var root = await _folderService.GetDirectoryModelAsync();

            var rootViewModel = _mapper.Map<DirectoryViewModel>(root);
            rootViewModel.SetFileManagerPanel(FileManagerPanel);
            rootViewModel.IsExpanded = true;

            FileSystemItemViewModels.Add(rootViewModel);
            UpdateRecentFolderEntries();

            await RestoreLastFolderAsync(rootViewModel);
        }
        catch (OperationCanceledException)
        {
            // Expected - no logging needed
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load root directories");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnDirectoryCreated(DirectoryModel directoryModel)
    {
        ScheduleParentReconciliation(directoryModel, "create");
    }

    private void OnDirectoryRemoved(DirectoryModel directoryModel)
    {
        ScheduleParentReconciliation(directoryModel, "delete");
    }

    private void OnDirectoryRenamed(DirectoryRenamedModel directoryRenamedModel)
    {
        try
        {
            var oldParent = directoryRenamedModel.OldDirectoryModel.GetParent();
            var newParent = directoryRenamedModel.NewDirectoryModel.GetParent();
            var parentViewModel = oldParent == null ? null : FindPhysicalDirectory(oldParent.Path);
            if (parentViewModel != null)
            {
                var renamedViewModel = parentViewModel.ChildFileSystemItems.FirstOrDefault(child =>
                    PathsEqual(child.Path, directoryRenamedModel.NewDirectoryModel.Path));
                renamedViewModel ??= parentViewModel.ChildFileSystemItems.FirstOrDefault(child =>
                    PathsEqual(child.Path, directoryRenamedModel.OldDirectoryModel.Path));

                if (renamedViewModel != null &&
                    !PathsEqual(renamedViewModel.Path, directoryRenamedModel.NewDirectoryModel.Path))
                {
                    var expandedDirectories = EnumerateExpandedSubtree(renamedViewModel)
                                             .Select(item => _mapper.Map<DirectoryModel>(item))
                                             .ToArray();
                    foreach (var expandedDirectory in expandedDirectories)
                    {
                        _folderService.RemoveVisitingFolder(expandedDirectory, FileManagerPanel);
                    }

                    renamedViewModel.UpdateDirectory(directoryRenamedModel.NewDirectoryModel);

                    foreach (var expandedDirectory in EnumerateExpandedSubtree(renamedViewModel))
                    {
                        _folderService.AddVisitingFolder(
                            _mapper.Map<DirectoryModel>(expandedDirectory),
                            FileManagerPanel);
                    }
                }

                parentViewModel.ChildFileSystemItems.RefreshSort();

                if (SelectedFileSystemItem != null &&
                    IsSamePathOrDescendant(
                        directoryRenamedModel.NewDirectoryModel.Path,
                        SelectedFileSystemItem.Path))
                {
                    _folderService.SetSelectedDirectory(
                        new SelectedDirectory(
                            SelectedFileSystemItem.Name,
                            SelectedFileSystemItem.Path,
                            FileManagerPanel));
                    SaveLastDirectoryPath(SelectedFileSystemItem);
                }
            }

            if (oldParent != null)
            {
                ScheduleDirectoryReconciliation(oldParent.Path, "rename");
            }

            if (newParent != null && (oldParent == null || !PathsEqual(oldParent.Path, newParent.Path)))
            {
                ScheduleDirectoryReconciliation(newParent.Path, "move");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(
                ex,
                "Failed to process directory rename from {OldPath} to {NewPath}",
                directoryRenamedModel.OldDirectoryModel.Path,
                directoryRenamedModel.NewDirectoryModel.Path);
        }
    }

    private void ScheduleParentReconciliation(DirectoryModel directoryModel, string reason)
    {
        try
        {
            var parent = directoryModel.GetParent();
            if (parent != null)
            {
                ScheduleDirectoryReconciliation(parent.Path, reason);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to schedule directory reconciliation for: {Path}", directoryModel.Path);
        }
    }

    private void ScheduleDirectoryReconciliation(string parentPath, string reason)
    {
        string normalizedPath;
        try
        {
            normalizedPath = NormalizePath(parentPath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to normalize directory for reconciliation: {Path}", parentPath);
            return;
        }

        long version;
        lock (_reconciliationSyncRoot)
        {
            version = ++_reconciliationVersion;
            _reconciliationVersions[normalizedPath] = version;
        }

        _ = ReconcileDirectoryChildrenAsync(normalizedPath, version, reason);
    }

    private async Task ReconcileDirectoryChildrenAsync(string parentPath, long version, string reason)
    {
        try
        {
            await Task.Delay(150);

            lock (_reconciliationSyncRoot)
            {
                if (!_reconciliationVersions.TryGetValue(parentPath, out var currentVersion) ||
                    currentVersion != version)
                {
                    return;
                }
            }

            if (!await Task.Run(() => _fileSystemService.DirectoryExists(parentPath)))
            {
                return;
            }

            var actualDirectory = await _folderService.GetDirectoryModelAsync(parentPath);
            _synchronizationContext.Post(
                _ => ApplyDirectoryReconciliation(parentPath, actualDirectory, version),
                null);
        }
        catch (Exception ex)
        {
            _logger.Error(
                ex,
                "Failed to reconcile directory after {Reason}: {Path}",
                reason,
                parentPath);
        }
    }

    private void ApplyDirectoryReconciliation(string parentPath, DirectoryModel actualDirectory, long version)
    {
        lock (_reconciliationSyncRoot)
        {
            if (!_reconciliationVersions.TryGetValue(parentPath, out var currentVersion) ||
                currentVersion != version)
            {
                return;
            }

            _reconciliationVersions.Remove(parentPath);
        }

        var parentViewModel = FindPhysicalDirectory(parentPath);
        if (parentViewModel == null)
        {
            return;
        }

        if (!parentViewModel.IsExpanded)
        {
            parentViewModel.InvalidateChildren();
            return;
        }

        var existingItems = parentViewModel.ChildFileSystemItems.ToArray();
        var retainedItems = new HashSet<DirectoryViewModel>();

        foreach (var actualChild in actualDirectory.DirectoryModels)
        {
            var candidates = existingItems.Where(item => PathsEqual(item.Path, actualChild.Path)).ToArray();
            var canonicalItem = candidates.FirstOrDefault(item => item.IsSelected || item.IsEditing)
                                ?? candidates.FirstOrDefault();
            if (canonicalItem == null)
            {
                canonicalItem = _mapper.Map<DirectoryViewModel>(actualChild);
                canonicalItem.SetFileManagerPanel(FileManagerPanel);
                parentViewModel.ChildFileSystemItems.Add(canonicalItem);
            }
            else
            {
                if (!PathsEqual(canonicalItem.Path, actualChild.Path) ||
                    !string.Equals(canonicalItem.Name, actualChild.Name, StringComparison.Ordinal))
                {
                    canonicalItem.UpdateDirectory(actualChild);
                }

                canonicalItem.HasSupportedMedia = actualChild.HasSupportedMedia;
            }

            retainedItems.Add(canonicalItem);
        }

        foreach (var staleItem in existingItems.Where(item => !retainedItems.Contains(item)))
        {
            if (SelectedFileSystemItem != null &&
                IsSamePathOrDescendant(staleItem.Path, SelectedFileSystemItem.Path))
            {
                SelectedFileSystemItem = null;
            }

            UnregisterExpandedSubtree(staleItem);
            parentViewModel.ChildFileSystemItems.Remove(staleItem);
        }

        parentViewModel.ChildFileSystemItems.RefreshSort();
    }

    private void UnregisterExpandedSubtree(DirectoryViewModel root)
    {
        foreach (var directory in EnumerateExpandedSubtree(root))
        {
            _folderService.RemoveVisitingFolder(
                _mapper.Map<DirectoryModel>(directory),
                FileManagerPanel);
        }
    }

    private static IEnumerable<DirectoryViewModel> EnumerateExpandedSubtree(DirectoryViewModel root)
    {
        var pending = new Stack<DirectoryViewModel>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current.IsExpanded)
            {
                yield return current;
            }

            foreach (var child in current.ChildFileSystemItems)
            {
                pending.Push(child);
            }
        }
    }

    private DirectoryViewModel? FindPhysicalDirectory(string path)
    {
        var root = FileSystemItemViewModels.OfType<DeviceViewModel>().FirstOrDefault();
        if (root == null)
        {
            return null;
        }

        var pending = new Stack<DirectoryViewModel>(
            root.ChildFileSystemItems.Where(item => item is not SpecialDirectoryViewModel));
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (PathsEqual(current.Path, path))
            {
                return current;
            }

            foreach (var child in current.ChildFileSystemItems)
            {
                pending.Push(child);
            }
        }

        return null;
    }

    private void OnDriveUnmounted(string driveName)
    {
        try
        {
            var root = FileSystemItemViewModels.FirstOrDefault(d => d is DeviceViewModel);
            var driveForRemove = root?.ChildFileSystemItems.FirstOrDefault(d => d.Name != null && d.Name.Equals(driveName, StringComparison.OrdinalIgnoreCase));

            if (driveForRemove != null)
            {
                // Reset selected drive to empty for clearing preview panel
                if (SelectedFileSystemItem != null && SelectedFileSystemItem.Path.StartsWith(driveName, StringComparison.OrdinalIgnoreCase))
                {
                    _folderService.SetSelectedDirectory(new SelectedDirectory(DirectoryModel.Empty, FileManagerPanel));
                }

                root?.ChildFileSystemItems.Remove(driveForRemove);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to process drive unmount: {DriveName}", driveName);
        }
    }

    private void OnDriveMounted(DriveModel model)
    {
        try
        {
            var root = FileSystemItemViewModels.FirstOrDefault(d => d is DeviceViewModel);
            root?.ChildFileSystemItems.Add(_mapper.Map<DriveViewModel>(model));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to process drive mount: {DrivePath}", model.Path);
        }
    }

    private void OnFreeSpaceChanged(AvailableFreeSpaceInfo freeSpaceInfo)
    {
        try
        {
            var root = FileSystemItemViewModels.FirstOrDefault(d => d is DeviceViewModel);
            var driveForUpdate = root?.ChildFileSystemItems.OfType<RemovableDriveViewModel>().FirstOrDefault(d => d.Path.Equals(freeSpaceInfo.DrivePath, StringComparison.OrdinalIgnoreCase));
            if (driveForUpdate is not null)
            {
                driveForUpdate.AvailableFreeSpace = freeSpaceInfo.FreeSpace;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to update free space for drive: {DrivePath}", freeSpaceInfo.DrivePath);
        }
    }

    private void OnFolderVisited(DirectoryModel directoryModel)
    {
        if (directoryModel is DeviceModel or SpecialDirectoryModel)
        {
            return;
        }

        try
        {
            _multiSourcesFileSystemWatcherService.StartWatchingDirectory(directoryModel.Path);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to start watching directory: {Path}", directoryModel.Path);
        }
    }

    private void OnFolderLeft(DirectoryModel directoryModel)
    {
        try
        {
            _multiSourcesFileSystemWatcherService.StopWatchingDirectory(directoryModel.Path);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to stop watching directory: {Path}", directoryModel.Path);
        }
    }

    private void OnObservableError(Exception ex)
    {
        _logger.Error(ex, "Error in observable subscription");
    }

    private async Task<DirectoryViewModel> GoToFolderFromSelectedAsync(
        DirectoryViewModel selectedFileSystemItem,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedFileSystemItem);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var targetPath = NormalizePath(path);
        if (!IsSamePathOrDescendant(selectedFileSystemItem.Path, targetPath))
        {
            throw new InvalidOperationException("Cannot navigate to folder outside current tree");
        }

        var relativePath = Path.GetRelativePath(NormalizePath(selectedFileSystemItem.Path), targetPath);
        var pathSegments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        var currentFolder = selectedFileSystemItem;

        foreach (var segment in pathSegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await currentFolder.EnsureLoadedAsync(cancellationToken);

            var expectedPath = NormalizePath(Path.Combine(currentFolder.Path, segment));
            currentFolder = currentFolder.ChildFileSystemItems.FirstOrDefault(child => PathsEqual(child.Path, expectedPath))
                            ?? throw new DirectoryNotFoundException(
                                $"Folder '{segment}' not found in '{currentFolder.Path}'");
        }

        await currentFolder.EnsureLoadedAsync(cancellationToken);
        return currentFolder;
    }

    private async Task RestoreLastFolderAsync(DirectoryViewModel rootViewModel)
    {
        var configuration = _configurationService.Configuration.Value;
        if (!configuration.RestoreLastFoldersOnStartup)
        {
            return;
        }

        var path = FileManagerPanel switch
        {
            FileManagerPanel.Left => configuration.LastSourceDirectoryPath,
            FileManagerPanel.Right => configuration.LastTargetDirectoryPath,
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        using var timeoutCancellationTokenSource = new CancellationTokenSource(RestoreTimeout);
        var cancellationToken = timeoutCancellationTokenSource.Token;

        try
        {
            var directoryExistsTask = Task.Run(() => _fileSystemService.DirectoryExists(path));
            if (!await directoryExistsTask.WaitAsync(cancellationToken))
            {
                return;
            }

            var targetPath = NormalizePath(path);
            var drive = rootViewModel.ChildFileSystemItems
                                     .OfType<DriveViewModel>()
                                     .Where(item => item is not SpecialDirectoryViewModel)
                                     .Where(item => IsSamePathOrDescendant(item.Path, targetPath))
                                     .OrderByDescending(item => NormalizePath(item.Path).Length)
                                     .FirstOrDefault();
            if (drive == null)
            {
                _logger.Warning(
                    "Cannot restore folder because its drive is unavailable: {Path}, panel: {Panel}",
                    path,
                    FileManagerPanel);
                return;
            }

            var restoredFolder = await GoToFolderFromSelectedAsync(drive, targetPath, cancellationToken);
            SelectedFileSystemItem = restoredFolder;
            RevealSelectedFolderRequested?.Invoke(restoredFolder);
        }
        catch (OperationCanceledException)
        {
            _logger.Warning(
                "Timed out restoring folder: {Path}, panel: {Panel}",
                path,
                FileManagerPanel);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                ex,
                "Failed to restore folder: {Path}, panel: {Panel}",
                path,
                FileManagerPanel);
        }
    }

    private void SaveLastDirectoryPath(DirectoryViewModel directory)
    {
        if (directory is DeviceViewModel or SpecialDirectoryViewModel || string.IsNullOrWhiteSpace(directory.Path))
        {
            return;
        }

        var configuration = _configurationService.Configuration.Value;
        switch (FileManagerPanel)
        {
            case FileManagerPanel.Left:
                configuration.LastSourceDirectoryPath = directory.Path;
                break;
            case FileManagerPanel.Right:
                configuration.LastTargetDirectoryPath = directory.Path;
                break;
        }
    }

    private static bool PathsEqual(string firstPath, string secondPath)
    {
        return string.Equals(NormalizePath(firstPath), NormalizePath(secondPath), GetPathComparison());
    }

    private static bool IsSamePathOrDescendant(string parentPath, string path)
    {
        try
        {
            var normalizedParentPath = NormalizePath(parentPath);
            var normalizedPath = NormalizePath(path);
            if (string.Equals(normalizedParentPath, normalizedPath, GetPathComparison()))
            {
                return true;
            }

            var relativePath = Path.GetRelativePath(normalizedParentPath, normalizedPath);
            return !Path.IsPathRooted(relativePath)
                   && !string.Equals(relativePath, "..", GetPathComparison())
                   && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", GetPathComparison())
                   && !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", GetPathComparison());
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string NormalizePath(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }
}