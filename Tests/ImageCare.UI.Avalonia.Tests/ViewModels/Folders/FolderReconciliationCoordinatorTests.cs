using System.Reflection;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.FileSystemWatcherService;
using ImageCare.Core.Services.FolderService;
using ImageCare.UI.Avalonia.ViewModels.Domain;
using ImageCare.UI.Avalonia.ViewModels.Folders;

using Mapster;
using MapsterMapper;

using Serilog;

namespace ImageCare.UI.Avalonia.Tests.ViewModels.Folders;

[TestFixture]
public sealed class FolderReconciliationCoordinatorTests
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private FakeFileSystemService _fileSystemService = null!;
    private FakeFolderService _folderService = null!;
    private FakeWatcherService _watcherService = null!;
    private ControlledDelay _delay = null!;
    private IMapper _mapper = null!;
    private FolderMutationCoordinator _mutationCoordinator = null!;
    private Serilog.Core.Logger _logger = null!;
    private DeviceViewModel? _root;
    private DirectoryViewModel? _selectedDirectory;
    private List<DirectoryViewModel> _savedDirectories = null!;
    private FolderReconciliationCoordinator _coordinator = null!;

    [SetUp]
    public void SetUp()
    {
        _fileSystemService = new FakeFileSystemService();
        _folderService = new FakeFolderService();
        _watcherService = new FakeWatcherService();
        _delay = new ControlledDelay();
        _logger = new LoggerConfiguration().CreateLogger();
        _mutationCoordinator = new FolderMutationCoordinator(_folderService, _fileSystemService, _logger);
        _mapper = CreateMapper();
        _savedDirectories = [];
        _coordinator = new FolderReconciliationCoordinator(
            _watcherService,
            _folderService,
            _fileSystemService,
            _mapper,
            _logger,
            new ImmediateSynchronizationContext(),
            () => _root,
            () => _selectedDirectory,
            () => FileManagerPanel.Right,
            value => _selectedDirectory = value,
            _savedDirectories.Add,
            _delay.DelayAsync);
    }

    [TearDown]
    public void TearDown()
    {
        _coordinator.Stop();
        _logger.Dispose();
    }

    [Test]
    public void StartAndStop_OwnWatcherAndSubscriptionLifetime()
    {
        var watchedPath = TestPath("watched");

        _coordinator.Start();
        _folderService.RaiseVisited(watchedPath, FileManagerPanel.Left);
        _folderService.RaiseVisited(watchedPath, FileManagerPanel.Right);
        _folderService.RaiseLeft(watchedPath, FileManagerPanel.Right);

        _coordinator.Stop();
        _folderService.RaiseVisited(TestPath("after-stop"), FileManagerPanel.Right);
        _watcherService.RaiseCreated(new DirectoryModel("ignored", TestPath("parent", "ignored")));

        Assert.Multiple(() =>
        {
            Assert.That(_watcherService.StartWatchingCallCount, Is.EqualTo(1));
            Assert.That(_watcherService.StopWatchingCallCount, Is.EqualTo(1));
            Assert.That(_watcherService.ClearWatchersCallCount, Is.EqualTo(1));
            Assert.That(_watcherService.StartedDirectories, Is.EqualTo(new[] { watchedPath }));
            Assert.That(_watcherService.StoppedDirectories, Is.EqualTo(new[] { watchedPath }));
            Assert.That(_delay.Count, Is.Zero);
        });
    }

    [Test]
    public async Task CreateThenDeleteForSameParent_CoalescesByVersionAndAddsAndRemovesStaleItems()
    {
        var parentPath = TestPath("coalesced-parent");
        var stalePath = Path.Combine(parentPath, "stale");
        var addedPath = Path.Combine(parentPath, "added");
        var stale = CreateDirectory("stale", stalePath);
        var parent = CreateDirectory("parent", parentPath, stale);
        SetExpanded(parent);
        SetRoot(parent);
        _folderService.DirectorySnapshots[parentPath] = CreateModel("parent", parentPath,
            new DirectoryModel("added", addedPath));
        _coordinator.Start();

        _watcherService.RaiseCreated(new DirectoryModel("added", addedPath));
        _watcherService.RaiseDeleted(new DirectoryModel("stale", stalePath));

        Assert.Multiple(() =>
        {
            Assert.That(_delay.Count, Is.EqualTo(2));
            Assert.That(_delay.RequestedDelays, Is.All.EqualTo(TimeSpan.FromMilliseconds(150)));
        });

        _delay.Release(0);
        _delay.Release(1);
        await WaitUntilAsync(() => parent.ChildFileSystemItems.Any(item => PathComparer.Equals(item.Path, addedPath)));

        Assert.Multiple(() =>
        {
            Assert.That(_folderService.LoadedDirectoryPaths, Is.EqualTo(new[] { parentPath }));
            Assert.That(parent.ChildFileSystemItems.Select(item => item.Path), Is.EqualTo(new[] { addedPath }));
            Assert.That(parent.ChildFileSystemItems[0].FileManagerPanel, Is.EqualTo(FileManagerPanel.Right));
            Assert.That(parent.ChildFileSystemItems, Does.Not.Contain(stale));
        });
    }

    [Test]
    public async Task ReconciliationForCollapsedParent_InvalidatesWithoutMutatingChildren()
    {
        var parentPath = TestPath("collapsed-parent");
        var child = CreateDirectory("existing", Path.Combine(parentPath, "existing"));
        var parent = CreateDirectory("parent", parentPath, child);
        SetLoadState(parent, DirectoryLoadState.Loaded);
        SetRoot(parent);
        _folderService.DirectorySnapshots[parentPath] = CreateModel("parent", parentPath);
        _coordinator.Start();

        _watcherService.RaiseRescan(parentPath);
        _delay.Release(0);
        await WaitUntilAsync(() => parent.LoadState == DirectoryLoadState.Initial);

        Assert.Multiple(() =>
        {
            Assert.That(parent.IsExpanded, Is.False);
            Assert.That(parent.ChildFileSystemItems, Is.EqualTo(new[] { child }));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Reconciliation_PreservesCanonicalSelectedOrEditingItemIdentity(bool editing)
    {
        var parentPath = TestPath(editing ? "editing-parent" : "selected-parent");
        var childPath = Path.Combine(parentPath, "same");
        var duplicate = CreateDirectory("duplicate", childPath);
        var canonical = CreateDirectory("canonical", childPath);
        if (editing)
        {
            canonical.IsEditing = true;
        }
        else
        {
            canonical.IsSelected = true;
            _selectedDirectory = canonical;
        }

        var parent = CreateDirectory("parent", parentPath, duplicate, canonical);
        SetExpanded(parent);
        SetRoot(parent);
        var actualChild = new DirectoryModel("updated", childPath) { HasSupportedMedia = true };
        _folderService.DirectorySnapshots[parentPath] = CreateModel("parent", parentPath, actualChild);
        _coordinator.Start();

        _watcherService.RaiseRescan(parentPath);
        _delay.Release(0);
        await WaitUntilAsync(() => parent.ChildFileSystemItems.Count == 1);

        Assert.Multiple(() =>
        {
            Assert.That(parent.ChildFileSystemItems[0], Is.SameAs(canonical));
            Assert.That(canonical.Name, Is.EqualTo("updated"));
            Assert.That(canonical.HasSupportedMedia, Is.True);
        });
    }

    [Test]
    public async Task RemovingStaleAncestor_ClearsSelectionAndUnregistersExpandedSubtree()
    {
        var parentPath = TestPath("ancestor-parent");
        var stalePath = Path.Combine(parentPath, "stale");
        var selectedPath = Path.Combine(stalePath, "selected");
        var selected = CreateDirectory("selected", selectedPath);
        var stale = CreateDirectory("stale", stalePath, selected);
        var parent = CreateDirectory("parent", parentPath, stale);
        SetExpanded(parent);
        SetExpanded(stale);
        SetExpanded(selected);
        selected.IsSelected = true;
        _selectedDirectory = selected;
        SetRoot(parent);
        _folderService.DirectorySnapshots[parentPath] = CreateModel("parent", parentPath);
        _coordinator.Start();

        _watcherService.RaiseDeleted(new DirectoryModel("stale", stalePath));
        _delay.Release(0);
        await WaitUntilAsync(() => parent.ChildFileSystemItems.Count == 0);

        Assert.Multiple(() =>
        {
            Assert.That(_selectedDirectory, Is.Null);
            Assert.That(_folderService.RemovedVisitingPaths, Is.EquivalentTo(new[] { stalePath, selectedPath }));
        });
    }

    [Test]
    public async Task Rename_UpdatesSubtreeAndReplacesVisitingRegistrationsAndSelectedPath()
    {
        var parentPath = TestPath("rename-parent");
        var oldPath = Path.Combine(parentPath, "old");
        var oldChildPath = Path.Combine(oldPath, "child");
        var newPath = Path.Combine(parentPath, "new");
        var newChildPath = Path.Combine(newPath, "child");
        var selected = CreateDirectory("child", oldChildPath);
        var renamed = CreateDirectory("old", oldPath, selected);
        var parent = CreateDirectory("parent", parentPath, renamed);
        SetExpanded(parent);
        SetExpanded(renamed);
        SetExpanded(selected);
        selected.IsSelected = true;
        _selectedDirectory = selected;
        SetRoot(parent);
        _folderService.DirectorySnapshots[parentPath] = CreateModel(
            "parent",
            parentPath,
            new DirectoryModel("new", newPath));
        _coordinator.Start();

        _watcherService.RaiseRenamed(
            new DirectoryRenamedModel(
                new DirectoryModel("old", oldPath),
                new DirectoryModel("new", newPath)));

        Assert.Multiple(() =>
        {
            Assert.That(parent.ChildFileSystemItems.Single(), Is.SameAs(renamed));
            Assert.That(renamed.Path, Is.EqualTo(newPath));
            Assert.That(selected.Path, Is.EqualTo(newChildPath));
            Assert.That(_folderService.RemovedVisitingPaths, Is.EquivalentTo(new[] { oldPath, oldChildPath }));
            Assert.That(_folderService.AddedVisitingPaths, Is.EquivalentTo(new[] { newPath, newChildPath }));
            Assert.That(_folderService.SelectedDirectories.Last().Path, Is.EqualTo(newChildPath));
            Assert.That(_savedDirectories.Single(), Is.SameAs(selected));
        });

        _delay.Release(0);
        await WaitUntilAsync(() => _folderService.LoadedDirectoryPaths.Count == 1);
    }

    private IMapper CreateMapper()
    {
        IMapper mapper = null!;
        var config = new TypeAdapterConfig();
        config.NewConfig<DirectoryModel, DirectoryViewModel>()
              .MapWith(source => new DirectoryViewModel(
                  source.Name,
                  source.Path,
                  Array.Empty<DirectoryViewModel>(),
                  _folderService,
                  _mutationCoordinator,
                  mapper,
                  _logger))
              .AfterMapping((source, destination) => destination.HasSupportedMedia = source.HasSupportedMedia);
        config.NewConfig<DirectoryViewModel, DirectoryModel>()
              .MapWith(source => new DirectoryModel(source.Name, source.Path));
        mapper = new Mapper(config);
        return mapper;
    }

    private void SetRoot(DirectoryViewModel parent)
    {
        _root = new DeviceViewModel(
            "Computer",
            DirectoryModel.RootPath,
            [parent],
            _folderService,
            _mutationCoordinator,
            _mapper,
            _logger);
        _root.SetFileManagerPanel(FileManagerPanel.Right);
    }

    private DirectoryViewModel CreateDirectory(
        string name,
        string path,
        params DirectoryViewModel[] children)
    {
        return new DirectoryViewModel(
            name,
            path,
            children,
            _folderService,
            _mutationCoordinator,
            _mapper,
            _logger);
    }

    private static DirectoryModel CreateModel(string name, string path, params DirectoryModel[] children)
    {
        var model = new DirectoryModel(name, path);
        model.AddDirectories(children);
        return model;
    }

    private static void SetExpanded(DirectoryViewModel directory)
    {
        typeof(DirectoryViewModel)
            .GetField("_isExpanded", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(directory, true);
    }

    private static void SetLoadState(DirectoryViewModel directory, DirectoryLoadState loadState)
    {
        typeof(DirectoryViewModel)
            .GetField("_loadState", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(directory, loadState);
    }

    private static string TestPath(params string[] parts)
    {
        return Path.Combine([Path.GetTempPath(), "ImageCareFolderReconciliationTests", .. parts]);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < timeout)
        {
            await Task.Delay(10);
        }

        Assert.That(condition(), Is.True, "Timed out waiting for asynchronous reconciliation");
    }

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
            callback(state);
        }

        public override void Send(SendOrPostCallback callback, object? state)
        {
            callback(state);
        }
    }

    private sealed class ControlledDelay
    {
        private readonly object _syncRoot = new();
        private readonly List<TaskCompletionSource> _requests = [];
        private readonly List<TimeSpan> _requestedDelays = [];

        public int Count
        {
            get
            {
                lock (_syncRoot)
                {
                    return _requests.Count;
                }
            }
        }

        public IReadOnlyList<TimeSpan> RequestedDelays
        {
            get
            {
                lock (_syncRoot)
                {
                    return _requestedDelays.ToArray();
                }
            }
        }

        public Task DelayAsync(TimeSpan delay)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_syncRoot)
            {
                _requestedDelays.Add(delay);
                _requests.Add(completion);
            }

            return completion.Task;
        }

        public void Release(int index)
        {
            TaskCompletionSource completion;
            lock (_syncRoot)
            {
                completion = _requests[index];
            }

            completion.TrySetResult();
        }
    }

    private sealed class FakeWatcherService : IMultiSourcesFileSystemWatcherService
    {
        private readonly Subject<DirectoryModel> _directoryCreated = new();
        private readonly Subject<DirectoryModel> _directoryDeleted = new();
        private readonly Subject<DirectoryRenamedModel> _directoryRenamed = new();
        private readonly Subject<string> _rescanRequested = new();

        public IObservable<FileModel> FileCreated => Observable.Never<FileModel>();
        public IObservable<FileModel> FileChanged => Observable.Never<FileModel>();
        public IObservable<FileModel> FileDeleted => Observable.Never<FileModel>();
        public IObservable<FileRenamedModel> FileRenamed => Observable.Never<FileRenamedModel>();
        public IObservable<DirectoryModel> DirectoryCreated => _directoryCreated;
        public IObservable<DirectoryModel> DirectoryDeleted => _directoryDeleted;
        public IObservable<DirectoryRenamedModel> DirectoryRenamed => _directoryRenamed;
        public IObservable<string> RescanRequested => _rescanRequested;

        public int StartWatchingCallCount { get; private set; }
        public int StopWatchingCallCount { get; private set; }
        public int ClearWatchersCallCount { get; private set; }
        public List<string> StartedDirectories { get; } = [];
        public List<string> StoppedDirectories { get; } = [];

        public void StartWatching() => StartWatchingCallCount++;
        public void StopWatching() => StopWatchingCallCount++;
        public void ClearWatchers() => ClearWatchersCallCount++;
        public void StartWatchingDirectory(string directoryPath) => StartedDirectories.Add(directoryPath);
        public void StopWatchingDirectory(string directoryPath) => StoppedDirectories.Add(directoryPath);

        public void RaiseCreated(DirectoryModel directory) => _directoryCreated.OnNext(directory);
        public void RaiseDeleted(DirectoryModel directory) => _directoryDeleted.OnNext(directory);
        public void RaiseRenamed(DirectoryRenamedModel directory) => _directoryRenamed.OnNext(directory);
        public void RaiseRescan(string path) => _rescanRequested.OnNext(path);
    }

    private sealed class FakeFolderService : IFolderService
    {
        private readonly Subject<SelectedDirectory> _folderVisited = new();
        private readonly Subject<SelectedDirectory> _folderLeft = new();

        public Dictionary<string, DirectoryModel> DirectorySnapshots { get; } = new(PathComparer);
        public List<string> LoadedDirectoryPaths { get; } = [];
        public List<string> AddedVisitingPaths { get; } = [];
        public List<string> RemovedVisitingPaths { get; } = [];
        public List<SelectedDirectory> SelectedDirectories { get; } = [];

        public IObservable<SelectedDirectory> FileSystemItemSelected => Observable.Never<SelectedDirectory>();
        public IObservable<SelectedDirectory> FolderVisited => _folderVisited;
        public IObservable<SelectedDirectory> FolderLeft => _folderLeft;

        public Task<DirectoryModel> GetDirectoryModelAsync(string directoryPath)
        {
            LoadedDirectoryPaths.Add(directoryPath);
            return Task.FromResult(DirectorySnapshots[directoryPath]);
        }

        public void SetSelectedDirectory(SelectedDirectory selectedDirectory)
        {
            SelectedDirectories.Add(selectedDirectory);
        }

        public void AddVisitingFolder(DirectoryModel directoryModel, FileManagerPanel fileManagerPanel)
        {
            AddedVisitingPaths.Add(directoryModel.Path);
        }

        public void RemoveVisitingFolder(DirectoryModel directoryModel, FileManagerPanel fileManagerPanel)
        {
            RemovedVisitingPaths.Add(directoryModel.Path);
        }

        public void RaiseVisited(string path, FileManagerPanel panel)
        {
            _folderVisited.OnNext(new SelectedDirectory("visited", path, panel));
        }

        public void RaiseLeft(string path, FileManagerPanel panel)
        {
            _folderLeft.OnNext(new SelectedDirectory("left", path, panel));
        }

        public Task<DirectoryModel> GetDirectoryModelAsync(DirectoryModel? directoryModel = null) =>
            throw new NotSupportedException();
        public Task<IEnumerable<FileModel>> GetFileModelAsync(DirectoryModel directoryModel, string searchPattern) =>
            throw new NotSupportedException();
        public Task<IEnumerable<FileModel>> GetFileModelAsync(string directoryPath, string searchPattern) =>
            throw new NotSupportedException();
        public Task<DirectoryModel> GetCustomDirectoriesLevelAsync(DirectoryModel directoryModel, bool preview = false) =>
            throw new NotSupportedException();
        public DirectoryModel? GetSelectedDirectory(FileManagerPanel fileManagerPanel) =>
            throw new NotSupportedException();
        public void RemoveFolder(DirectoryModel directoryModel) => throw new NotSupportedException();
        public DirectoryModel? CreateSubFolder(DirectoryModel directoryModel) => throw new NotSupportedException();
    }

    private sealed class FakeFileSystemService : IFileSystemService
    {
        public bool DirectoryExists(string path) => true;

        public bool FileExists(string path) => throw new NotSupportedException();
        public bool IsDirectory(string path) => throw new NotSupportedException();
        public FileInfo GetFileInfo(string path) => throw new NotSupportedException();
        public DirectoryInfo GetDirectoryInfo(string path) => throw new NotSupportedException();
        public Stream OpenRead(string path) => throw new NotSupportedException();
        public Stream Create(string path) => throw new NotSupportedException();
        public void MoveFile(string source, string destination) => throw new NotSupportedException();
        public void CopyFile(string source, string destination) => throw new NotSupportedException();
        public void DeleteFile(string path) => throw new NotSupportedException();
        public void MoveDirectory(string source, string destination) => throw new NotSupportedException();
        public void DeleteDirectory(string path, bool recursive) => throw new NotSupportedException();
        public void CreateDirectory(string path) => throw new NotSupportedException();
        public string[] GetFiles(string directory) => throw new NotSupportedException();
        public string[] GetFiles(string directory, string searchPattern) => throw new NotSupportedException();
        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern) => throw new NotSupportedException();
        public IEnumerable<FileModel> EnumerateFiles(string directory, string searchPattern, SearchOption searchOption) =>
            throw new NotSupportedException();
        public IEnumerable<string> EnumerateDirectories(
            string directory,
            string searchPattern,
            SearchOption searchOption = SearchOption.TopDirectoryOnly) => throw new NotSupportedException();
        public string[] GetDirectories(string directory) => throw new NotSupportedException();
        public void CopyFileMetadata(string source, string destination) => throw new NotSupportedException();
        public void CopyFileTimestampsAndAttributes(string source, string destination) => throw new NotSupportedException();
        public void CopyDirectoryMetadata(string source, string destination) => throw new NotSupportedException();
        public void SafeDelete(string path) => throw new NotSupportedException();
        public void SafeDeleteDirectory(string path) => throw new NotSupportedException();
        public string GetFileExtension(string path) => throw new NotSupportedException();
        public string? RenameFolder(string? newName, string path) => throw new NotSupportedException();
    }
}
