using System;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.DrivesWatcherService;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.FolderService;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using MapsterMapper;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Folders;

internal sealed class FolderTreeViewModel : ViewModelBase
{
    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TreeWaitTimeout = TimeSpan.FromSeconds(3);

    private readonly IFolderService _folderService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IConfigurationService _configurationService;
    private readonly IDrivesWatcherService _drivesWatcherService;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;

    private DirectoryViewModel? _selectedFileSystemItem;
    private CompositeDisposable? _subscriptions;
    private CancellationTokenSource? _mediaNavigationCancellationTokenSource;
    private Task? _rootLoadingTask;
    private bool _isInitialized;
    private bool _isStarted;
    private bool _isLoading;

    public FolderTreeViewModel(
        IFolderService folderService,
        IFileSystemService fileSystemService,
        IConfigurationService configurationService,
        IDrivesWatcherService drivesWatcherService,
        IMapper mapper,
        ILogger logger,
        SynchronizationContext synchronizationContext)
    {
        _folderService = folderService ?? throw new ArgumentNullException(nameof(folderService));
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _drivesWatcherService = drivesWatcherService ?? throw new ArgumentNullException(nameof(drivesWatcherService));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _synchronizationContext = synchronizationContext ?? throw new ArgumentNullException(nameof(synchronizationContext));

        FileSystemItemViewModels = new Collections.SortedObservableCollection<DirectoryViewModel>();
    }

    public Collections.SortedObservableCollection<DirectoryViewModel> FileSystemItemViewModels { get; }

    public event Action<DirectoryViewModel?>? SelectionChanged;

    public event Action<bool>? LoadingChanged;

    public event Action? RootLoaded;

    public event Action<DirectoryViewModel>? RevealRequested;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                LoadingChanged?.Invoke(value);
            }
        }
    }

    public DirectoryViewModel? SelectedFileSystemItem
    {
        get => _selectedFileSystemItem;
        set => SetSelectedFileSystemItem(value);
    }

    public FileManagerPanel FileManagerPanel { get; private set; }

    public DeviceViewModel? DeviceRoot => FileSystemItemViewModels.OfType<DeviceViewModel>().FirstOrDefault();

    public void Initialize(FileManagerPanel panel)
    {
        if (_isStarted && _isInitialized && FileManagerPanel != panel)
        {
            throw new InvalidOperationException("Cannot change the folder tree panel while it is running");
        }

        FileManagerPanel = panel;
        _isInitialized = true;
    }

    public void Start()
    {
        EnsureInitialized();
        if (_isStarted)
        {
            return;
        }

        _subscriptions = new CompositeDisposable
        {
            _drivesWatcherService.DriveMounted
                                 .ObserveOn(_synchronizationContext)
                                 .Subscribe(OnDriveMounted, OnObservableError),
            _drivesWatcherService.DriveUnmounted
                                 .ObserveOn(_synchronizationContext)
                                 .Subscribe(OnDriveUnmounted, OnObservableError),
            _drivesWatcherService.DriveAvailableFreeSpaceChanged
                                 .DistinctUntilChanged()
                                 .ObserveOn(_synchronizationContext)
                                 .Subscribe(OnFreeSpaceChanged, OnObservableError)
        };

        _drivesWatcherService.StartWatching();
        _isStarted = true;

        if (FileSystemItemViewModels.Count == 0 && !IsLoading)
        {
            _ = EnsureRootLoadedAsync();
        }
    }

    public void Start(FileManagerPanel panel)
    {
        Initialize(panel);
        Start();
    }

    public void Stop()
    {
        if (!_isStarted)
        {
            return;
        }

        _mediaNavigationCancellationTokenSource?.Cancel();
        _mediaNavigationCancellationTokenSource = null;
        _subscriptions?.Dispose();
        _subscriptions = null;
        _drivesWatcherService.StopWatching();
        _isStarted = false;
    }

    internal async Task EnsureRootLoadedAsync()
    {
        EnsureInitialized();
        var loadingTask = _rootLoadingTask;
        if (loadingTask == null && DeviceRoot != null)
        {
            return;
        }

        if (loadingTask == null)
        {
            loadingTask = LoadRootAsync();
            _rootLoadingTask = loadingTask;
        }

        try
        {
            await loadingTask;
        }
        finally
        {
            if (ReferenceEquals(_rootLoadingTask, loadingTask))
            {
                _rootLoadingTask = null;
            }
        }
    }

    internal async Task SelectDescendantAsync(
        DirectoryViewModel sourceDirectory,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        var targetDirectory = await GoToFolderFromSelectedAsync(sourceDirectory, targetPath, cancellationToken);
        SelectedFileSystemItem = targetDirectory;
        RevealRequested?.Invoke(targetDirectory);
    }

    internal void HandleMediaPreviewNavigationRequested(SelectedMediaPreview requestedPreview)
    {
        EnsureInitialized();
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

    internal async Task<bool> SelectFolderInTreeAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        EnsureInitialized();

        try
        {
            var root = await WaitForFolderTreeAsync(cancellationToken);
            if (root == null)
            {
                return false;
            }

            var drive = FindPhysicalDrive(root, folderPath);
            if (drive == null)
            {
                return false;
            }

            await SelectDescendantAsync(drive, folderPath, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to select folder in the tree: {Path}", folderPath);
            return false;
        }
    }

    internal void ClearSelectionIfCurrent(DirectoryViewModel directory)
    {
        if (ReferenceEquals(SelectedFileSystemItem, directory))
        {
            SelectedFileSystemItem = null;
        }
    }

    internal void SaveLastDirectoryPath(DirectoryViewModel directory)
    {
        EnsureInitialized();
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
            default:
                return;
        }

        _configurationService.SaveConfiguration();
    }

    private async Task LoadRootAsync()
    {
        IsLoading = true;

        try
        {
            var root = await _folderService.GetDirectoryModelAsync();
            var rootViewModel = _mapper.Map<DirectoryViewModel>(root);
            rootViewModel.SetFileManagerPanel(FileManagerPanel);
            rootViewModel.IsExpanded = true;

            FileSystemItemViewModels.Add(rootViewModel);
            RootLoaded?.Invoke();

            await RestoreLastFolderAsync(rootViewModel);
        }
        catch (OperationCanceledException)
        {
            // Expected during timed restore or shutdown.
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

        if (!SetProperty(ref _selectedFileSystemItem, value))
        {
            return;
        }

        SelectionChanged?.Invoke(value);
        if (value == null)
        {
            return;
        }

        try
        {
            value.IsSelected = true;
            _folderService.SetSelectedDirectory(new SelectedDirectory(value.Name, value.Path, FileManagerPanel));
            SaveLastDirectoryPath(value);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to set selected directory: {Path}", value.Path);
        }
    }

    private async Task NavigateToMediaFolderAsync(string directoryPath, CancellationTokenSource cancellation)
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

            var root = DeviceRoot;
            if (root == null)
            {
                _logger.Warning("Cannot navigate to media folder before folder tree is loaded: {Path}", directoryPath);
                return;
            }

            var drive = FindPhysicalDrive(root, directoryPath);
            if (drive == null)
            {
                _logger.Warning(
                    "Cannot navigate to media folder because its drive is unavailable: {Path}, panel: {Panel}",
                    directoryPath,
                    FileManagerPanel);
                return;
            }

            var targetDirectory = await GoToFolderFromSelectedAsync(drive, directoryPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_mediaNavigationCancellationTokenSource, cancellation))
            {
                return;
            }

            SelectedFileSystemItem = targetDirectory;
            RevealRequested?.Invoke(targetDirectory);
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

    private void OnDriveUnmounted(string driveName)
    {
        try
        {
            var root = DeviceRoot;
            var driveForRemove = root?.ChildFileSystemItems.FirstOrDefault(directory =>
                directory.Name != null && directory.Name.Equals(driveName, StringComparison.OrdinalIgnoreCase));

            if (driveForRemove == null)
            {
                return;
            }

            if (SelectedFileSystemItem != null &&
                IsSamePathOrDescendant(driveForRemove.Path, SelectedFileSystemItem.Path))
            {
                SelectedFileSystemItem = null;
                _folderService.SetSelectedDirectory(new SelectedDirectory(DirectoryModel.Empty, FileManagerPanel));
            }

            root?.ChildFileSystemItems.Remove(driveForRemove);
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
            var drive = _mapper.Map<DriveViewModel>(model);
            drive.SetFileManagerPanel(FileManagerPanel);
            DeviceRoot?.ChildFileSystemItems.Add(drive);
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
            var driveForUpdate = DeviceRoot?.ChildFileSystemItems
                                                .OfType<RemovableDriveViewModel>()
                                                .FirstOrDefault(drive => drive.Path.Equals(
                                                    freeSpaceInfo.DrivePath,
                                                    StringComparison.OrdinalIgnoreCase));
            if (driveForUpdate != null)
            {
                driveForUpdate.AvailableFreeSpace = freeSpaceInfo.FreeSpace;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to update free space for drive: {DrivePath}", freeSpaceInfo.DrivePath);
        }
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
            currentFolder = currentFolder.ChildFileSystemItems.FirstOrDefault(child =>
                                PathsEqual(child.Path, expectedPath))
                            ?? throw new DirectoryNotFoundException(
                                $"Folder '{segment}' not found in '{currentFolder.Path}'");
        }

        await currentFolder.EnsureLoadedAsync(cancellationToken);
        return currentFolder;
    }

    private async Task<DeviceViewModel?> WaitForFolderTreeAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.Add(TreeWaitTimeout);
        while (true)
        {
            var root = DeviceRoot;
            if (root != null || DateTime.UtcNow >= deadline)
            {
                return root;
            }

            await Task.Delay(50, cancellationToken);
        }
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

            var drive = FindPhysicalDrive(rootViewModel, path);
            if (drive == null)
            {
                _logger.Warning(
                    "Cannot restore folder because its drive is unavailable: {Path}, panel: {Panel}",
                    path,
                    FileManagerPanel);
                return;
            }

            await SelectDescendantAsync(drive, path, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("Timed out restoring folder: {Path}, panel: {Panel}", path, FileManagerPanel);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to restore folder: {Path}, panel: {Panel}", path, FileManagerPanel);
        }
    }

    private static DriveViewModel? FindPhysicalDrive(DirectoryViewModel root, string path)
    {
        var targetPath = NormalizePath(path);
        return root.ChildFileSystemItems
                   .OfType<DriveViewModel>()
                   .Where(item => item is not SpecialDirectoryViewModel)
                   .Where(item => IsSamePathOrDescendant(item.Path, targetPath))
                   .OrderByDescending(item => NormalizePath(item.Path).Length)
                   .FirstOrDefault();
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("Initialize the folder tree with a panel before using it");
        }
    }

    private void OnObservableError(Exception ex)
    {
        _logger.Error(ex, "Error in folder tree observable subscription");
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
