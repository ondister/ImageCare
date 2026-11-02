using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.FileSystemWatcherService;
using ImageCare.Core.Services.FolderService;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using MapsterMapper;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Folders;

internal sealed class FolderReconciliationCoordinator
{
    private static readonly TimeSpan ReconciliationDelay = TimeSpan.FromMilliseconds(150);

    private readonly IMultiSourcesFileSystemWatcherService _watcherService;
    private readonly IFolderService _folderService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly Func<DeviceViewModel?> _getDeviceRoot;
    private readonly Func<DirectoryViewModel?> _getSelectedDirectory;
    private readonly Func<FileManagerPanel> _getFileManagerPanel;
    private readonly Action<DirectoryViewModel?> _setSelectedDirectory;
    private readonly Action<DirectoryViewModel> _saveLastDirectoryPath;
    private readonly Func<TimeSpan, Task> _delayAsync;
    private readonly object _reconciliationSyncRoot = new();
    private readonly Dictionary<string, long> _reconciliationVersions = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private CompositeDisposable? _subscriptions;
    private long _reconciliationVersion;

    public FolderReconciliationCoordinator(
        IMultiSourcesFileSystemWatcherService watcherService,
        IFolderService folderService,
        IFileSystemService fileSystemService,
        IMapper mapper,
        ILogger logger,
        SynchronizationContext synchronizationContext,
        Func<DeviceViewModel?> getDeviceRoot,
        Func<DirectoryViewModel?> getSelectedDirectory,
        Func<FileManagerPanel> getFileManagerPanel,
        Action<DirectoryViewModel?> setSelectedDirectory,
        Action<DirectoryViewModel> saveLastDirectoryPath,
        Func<TimeSpan, Task>? delayAsync = null)
    {
        _watcherService = watcherService ?? throw new ArgumentNullException(nameof(watcherService));
        _folderService = folderService ?? throw new ArgumentNullException(nameof(folderService));
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _synchronizationContext = synchronizationContext ?? throw new ArgumentNullException(nameof(synchronizationContext));
        _getDeviceRoot = getDeviceRoot ?? throw new ArgumentNullException(nameof(getDeviceRoot));
        _getSelectedDirectory = getSelectedDirectory ?? throw new ArgumentNullException(nameof(getSelectedDirectory));
        _getFileManagerPanel = getFileManagerPanel ?? throw new ArgumentNullException(nameof(getFileManagerPanel));
        _setSelectedDirectory = setSelectedDirectory ?? throw new ArgumentNullException(nameof(setSelectedDirectory));
        _saveLastDirectoryPath = saveLastDirectoryPath ?? throw new ArgumentNullException(nameof(saveLastDirectoryPath));
        _delayAsync = delayAsync ?? Task.Delay;
    }

    public void Start()
    {
        if (_subscriptions != null)
        {
            return;
        }

        _subscriptions = new CompositeDisposable
        {
            _folderService.FolderVisited
                          .Where(folder => folder.FileManagerPanel == _getFileManagerPanel())
                          .Subscribe(OnFolderVisited, OnObservableError),
            _folderService.FolderLeft
                          .Where(folder => folder.FileManagerPanel == _getFileManagerPanel())
                          .Subscribe(OnFolderLeft, OnObservableError),
            _watcherService.DirectoryCreated
                           .ObserveOn(_synchronizationContext)
                           .Subscribe(OnDirectoryCreated, OnObservableError),
            _watcherService.DirectoryDeleted
                           .DistinctUntilChanged(folder => folder.Path)
                           .ObserveOn(_synchronizationContext)
                           .Subscribe(OnDirectoryRemoved, OnObservableError),
            _watcherService.DirectoryRenamed
                           .DistinctUntilChanged(folder => folder.NewDirectoryModel.Path)
                           .ObserveOn(_synchronizationContext)
                           .Subscribe(OnDirectoryRenamed, OnObservableError),
            _watcherService.RescanRequested
                           .ObserveOn(_synchronizationContext)
                           .Subscribe(
                               path => ScheduleDirectoryReconciliation(path, "watcher-overflow"),
                               OnObservableError)
        };

        _watcherService.StartWatching();
    }

    public void Stop()
    {
        _subscriptions?.Dispose();
        _subscriptions = null;
        _watcherService.StopWatching();
        _watcherService.ClearWatchers();
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
                        _folderService.RemoveVisitingFolder(expandedDirectory, _getFileManagerPanel());
                    }

                    renamedViewModel.UpdateDirectory(directoryRenamedModel.NewDirectoryModel);

                    foreach (var expandedDirectory in EnumerateExpandedSubtree(renamedViewModel))
                    {
                        _folderService.AddVisitingFolder(
                            _mapper.Map<DirectoryModel>(expandedDirectory),
                            _getFileManagerPanel());
                    }
                }

                parentViewModel.ChildFileSystemItems.RefreshSort();

                var selectedDirectory = _getSelectedDirectory();
                if (selectedDirectory != null &&
                    IsSamePathOrDescendant(
                        directoryRenamedModel.NewDirectoryModel.Path,
                        selectedDirectory.Path))
                {
                    _folderService.SetSelectedDirectory(
                        new SelectedDirectory(
                            selectedDirectory.Name,
                            selectedDirectory.Path,
                            _getFileManagerPanel()));
                    _saveLastDirectoryPath(selectedDirectory);
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
            await _delayAsync(ReconciliationDelay);

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
                canonicalItem.SetFileManagerPanel(_getFileManagerPanel());
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
            var selectedDirectory = _getSelectedDirectory();
            if (selectedDirectory != null &&
                IsSamePathOrDescendant(staleItem.Path, selectedDirectory.Path))
            {
                _setSelectedDirectory(null);
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
                _getFileManagerPanel());
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
        var root = _getDeviceRoot();
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

    private void OnFolderVisited(DirectoryModel directoryModel)
    {
        if (directoryModel is DeviceModel or SpecialDirectoryModel)
        {
            return;
        }

        try
        {
            _watcherService.StartWatchingDirectory(directoryModel.Path);
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
            _watcherService.StopWatchingDirectory(directoryModel.Path);
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
