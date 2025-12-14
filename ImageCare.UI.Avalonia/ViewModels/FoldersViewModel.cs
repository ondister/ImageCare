using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using AutoMapper;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.DrivesWatcherService;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.FileSystemWatcherService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Mvvm.Collections;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Prism.Navigation.Regions;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class FoldersViewModel : NavigatedViewModelBase
{
    private const string SearchTargetText = "Search folders";
    private readonly IFolderService _folderService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IMultiSourcesFileSystemWatcherService _multiSourcesFileSystemWatcherService;
    private readonly IDrivesWatcherService _drivesWatcherService;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;

    private DirectoryViewModel? _selectedFileSystemItem;
    private CompositeDisposable _compositeDisposable;
    private DirectoryModel? _createdSubFolder;
    private CancellationTokenSource? _searchCancellationTokenSource;

    private string? _searchText;
    private bool _isLoading;
    private bool _isSearching;
    private string? _searchHeader;
    private bool _isSearchEnabled;
    private bool _isInSearchSession;
    private string _searchTarget = SearchTargetText;

    public FoldersViewModel(IFolderService folderService,
                            IFileSystemService fileSystemService,
                            IMultiSourcesFileSystemWatcherService multiSourcesFileSystemWatcherService,
                            IDrivesWatcherService drivesWatcherService,
                            IMapper mapper,
                            ILogger logger,
                            SynchronizationContext synchronizationContext)
    {
        _folderService = folderService ?? throw new ArgumentNullException(nameof(folderService));
        _fileSystemService = fileSystemService;
        _multiSourcesFileSystemWatcherService = multiSourcesFileSystemWatcherService ?? throw new ArgumentNullException(nameof(multiSourcesFileSystemWatcherService));
        _drivesWatcherService = drivesWatcherService ?? throw new ArgumentNullException(nameof(drivesWatcherService));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _synchronizationContext = synchronizationContext ?? throw new ArgumentNullException(nameof(synchronizationContext));

        OnViewLoadedCommand = CreateAsyncCommand(OnViewLoadedAsync, () => !IsLoading);
        DeleteFolderCommand = CreateCommand(DeleteFolder, CanDeleteFolder);
        CreateFolderCommand = CreateCommand(CreateFolder, CanCreateFolder);

        PerformSearchCommand = CreateAsyncCommand(PerformSearchAsync, CanPerformSearch);
        ClearSearchCommand = CreateCommand(ClearSearch, CanClearSearch).ObservesProperty(() => IsInSearchSession);
        SetSearchResultCommand = CreateAsyncCommand<DirectoryViewModel>(SetSearchResultAsync);

        FileSystemItemViewModels = new SortedObservableCollection<DirectoryViewModel>();
        SearchResults = new ObservableCollection<DirectoryViewModel>();
    }

    public ICommand SetSearchResultCommand { get; set; }

    public SortedObservableCollection<DirectoryViewModel> FileSystemItemViewModels { get; }

    public ObservableCollection<DirectoryViewModel> SearchResults { get; }

    public ICommand OnViewLoadedCommand { get; }

    public ICommand CreateFolderCommand { get; }

    public ICommand DeleteFolderCommand { get; }

    public ICommand PerformSearchCommand { get; }

    public ICommand ClearSearchCommand { get; }

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

    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

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

            _compositeDisposable = new CompositeDisposable
            {
                _drivesWatcherService.DriveMounted.Subscribe(OnDriveMounted, OnObservableError),
                _drivesWatcherService.DriveUnmounted.Subscribe(OnDriveUnmounted, OnObservableError),
                _drivesWatcherService.DriveAvailableFreeSpaceChanged
                                     .DistinctUntilChanged()
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
                                                     .Subscribe(OnDirectoryRenamed, OnObservableError)
            };

            _drivesWatcherService.StartWatching();
            _multiSourcesFileSystemWatcherService.StartWatching();
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

    private async Task SetSearchResultAsync(DirectoryViewModel directoryViewModel)
    {
        if (directoryViewModel == null)
        {
            return;
        }

        try
        {
            ClearSearch();

            if (SelectedFileSystemItem == null)
            {
                return;
            }

            SelectedFileSystemItem = await GoToFolderFromSelectedAsync(SelectedFileSystemItem, directoryViewModel.Path);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open search result: {Path}", directoryViewModel.Path);
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

            SearchHeader = $"Searching for \"{SearchText}\" in {SelectedFileSystemItem?.Name ?? "all folders"}...";

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

            SearchHeader = $"Found {SearchResults.Count} results for \"{SearchText}\"";
        }
        catch (OperationCanceledException)
        {
            SearchHeader = "Search cancelled";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to perform search");

            SearchHeader = "Search failed";
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
        if (_selectedFileSystemItem != null)
        {
            _selectedFileSystemItem.IsSelected = false;
        }
        

        if (SetProperty(ref _selectedFileSystemItem, value) && value != null)
        {
            try
            {
                _selectedFileSystemItem.IsSelected=true;
                var selectedDirectory = new SelectedDirectory(value.Name, value.Path, FileManagerPanel);
                _folderService.SetSelectedDirectory(selectedDirectory);

                IsSearchEnabled = value is not (SpecialDirectoryViewModel or DriveViewModel or DeviceViewModel);
                SearchTarget = $"{SearchTargetText} in {_selectedFileSystemItem.Name}";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to set selected directory: {Path}", value.Path);
            }
        }
    }

    private async Task OnViewLoadedAsync()
    {
        IsLoading = true;

        try
        {
            var root = await _folderService.GetDirectoryModelAsync().ConfigureAwait(false);

            var rootViewModel = _mapper.Map<DirectoryViewModel>(root);
            rootViewModel.SetFileManagerPanel(FileManagerPanel);
            rootViewModel.IsExpanded = true;

            FileSystemItemViewModels.Add(rootViewModel);
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
        try
        {
            var parent = directoryModel.GetParent();
            if (parent == null)
            {
                return;
            }

            foreach (var directoryViewModel in FileSystemItemViewModels)
            {
                if (directoryViewModel.FindChildByPathRecursively(parent.Path) is { } parentViewModel)
                {
                    if (!parentViewModel.ChildFileSystemItems.Any(d => d.Path.Equals(directoryModel.Path, StringComparison.OrdinalIgnoreCase)))
                    {
                        var createdViewModel = _mapper.Map<DirectoryViewModel>(directoryModel);
                        parentViewModel.ChildFileSystemItems.Add(createdViewModel);

                        if (createdViewModel.Path.Equals(_createdSubFolder?.Path, StringComparison.OrdinalIgnoreCase))
                        {
                            createdViewModel.IsEditing = true;
                        }
                    }

                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to process directory creation: {Path}", directoryModel.Path);
        }
    }

    private void OnDirectoryRemoved(DirectoryModel directoryModel)
    {
        try
        {
            var parent = directoryModel.GetParent();
            if (parent == null)
            {
                return;
            }

            foreach (var directoryViewModel in FileSystemItemViewModels)
            {
                if (directoryViewModel.FindChildByPathRecursively(parent.Path) is { } parentViewModel)
                {
                    if (parentViewModel.ChildFileSystemItems.FirstOrDefault(d => d.Path.Equals(directoryModel.Path, StringComparison.OrdinalIgnoreCase)) is { } directoryViewModelForDelete)
                    {
                        if (directoryViewModelForDelete.Path == SelectedFileSystemItem?.Path)
                        {
                            SelectedFileSystemItem = null;
                        }

                        parentViewModel.ChildFileSystemItems.Remove(directoryViewModelForDelete);
                        if (parentViewModel.IsExpanded && !parentViewModel.ChildFileSystemItems.Any())
                        {
                            parentViewModel.IsExpanded = false;
                        }
                    }

                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to process directory removal: {Path}", directoryModel.Path);
        }
    }

    private void OnDirectoryRenamed(DirectoryRenamedModel directoryRenamedModel)
    {
        try
        {
            var parent = directoryRenamedModel.OldDirectoryModel.GetParent();
            if (parent == null)
            {
                return;
            }

            foreach (var directoryViewModel in FileSystemItemViewModels)
            {
                if (directoryViewModel.FindChildByPathRecursively(parent.Path) is { } parentViewModel)
                {
                    if (parentViewModel.ChildFileSystemItems.FirstOrDefault(d => d.Path.Equals(directoryRenamedModel.OldDirectoryModel.Path, StringComparison.OrdinalIgnoreCase)) is { } directoryViewModelForRename)
                    {
                        directoryViewModelForRename.UpdateDirectory(directoryRenamedModel.NewDirectoryModel);
                        parentViewModel.ChildFileSystemItems.RefreshSort();
                        if (directoryRenamedModel.OldDirectoryModel.Path == SelectedFileSystemItem?.Path)
                        {
                            SelectedFileSystemItem = null;
                            SelectedFileSystemItem = directoryViewModelForRename;
                        }
                    }

                    return;
                }
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

    private void CreateFolder()
    {
        try
        {
            if (SelectedFileSystemItem == null)
            {
                return;
            }

            _createdSubFolder = _folderService.CreateSubFolder(_mapper.Map<DirectoryModel>(SelectedFileSystemItem));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to create folder for: {SelectedPath}", SelectedFileSystemItem?.Path);
        }
    }

    private void DeleteFolder()
    {
        try
        {
            if (SelectedFileSystemItem != null && !SelectedFileSystemItem.HasSupportedMedia)
            {
                _folderService.RemoveFolder(_mapper.Map<DirectoryModel>(SelectedFileSystemItem));
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete folder: {Path}", SelectedFileSystemItem?.Path);
        }
    }

    private bool CanCreateFolder()
    {
        return SelectedFileSystemItem != null;
    }

    private bool CanDeleteFolder()
    {
        return SelectedFileSystemItem != null && !SelectedFileSystemItem.HasSupportedMedia;
    }

    private void OnObservableError(Exception ex)
    {
        _logger.Error(ex, "Error in observable subscription");
    }

    private async Task<DirectoryViewModel> GoToFolderFromSelectedAsync(DirectoryViewModel selectedFileSystemItem, string path)
    {
        if (selectedFileSystemItem == null)
        {
            throw new ArgumentNullException(nameof(selectedFileSystemItem));
        }

        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentNullException(nameof(path));
        }

        path = path.TrimEnd(Path.DirectorySeparatorChar);

       
        if (selectedFileSystemItem.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
        {
            return selectedFileSystemItem;
        }

      
        string relativePath;
        if (path.StartsWith(selectedFileSystemItem.Path, StringComparison.OrdinalIgnoreCase))
        {
            relativePath = path.Substring(selectedFileSystemItem.Path.Length)
                               .TrimStart(Path.DirectorySeparatorChar);
        }
        else
        {
            throw new InvalidOperationException("Cannot navigate to folder outside current tree");
        }

        var pathSegments = relativePath.Split(
            [Path.DirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        var currentFolder = selectedFileSystemItem;

        foreach (var segment in pathSegments)
        {
           
            if (!currentFolder.IsExpanded)
            {
                currentFolder.IsExpanded = true;

                await currentFolder.WaitForLoadingAsync();
            }
            else
            {
                await currentFolder.EnsureLoadedAsync();
            }

            var nextFolder = currentFolder.ChildFileSystemItems
                                          .FirstOrDefault(c =>
                                                              string.Equals(c.Name, segment, StringComparison.OrdinalIgnoreCase) || c.Path.EndsWith(segment, StringComparison.OrdinalIgnoreCase));

            currentFolder = nextFolder ?? throw new DirectoryNotFoundException($"Folder '{segment}' not found in '{currentFolder.Path}'");
        }

        if (!currentFolder.IsExpanded)
        {
            currentFolder.IsExpanded = true;
            await currentFolder.WaitForLoadingAsync();
        }

        return currentFolder;
    }
}