using System.Reactive.Linq;
using System.Reactive.Subjects;

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.MediaFormats;
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
using ImageCare.UI.Avalonia.ViewModels;
using ImageCare.UI.Avalonia.ViewModels.Domain;
using ImageCare.UI.Avalonia.ViewModels.Folders;

using Mapster;
using MapsterMapper;

using Prism.Navigation;
using Prism.Navigation.Regions;

using Serilog;

namespace ImageCare.UI.Avalonia.Tests.ViewModels.Folders;

[TestFixture]
public sealed class FolderTreeViewModelTests
{
    private FakeFolderService _folderService = null!;
    private FakeFileSystemService _fileSystemService = null!;
    private FakeConfigurationService _configurationService = null!;
    private FakeDrivesWatcherService _drivesWatcherService = null!;
    private FakeMultiSourcesWatcherService _multiSourcesWatcherService = null!;
    private FakeMediaPreviewOperationsService _mediaPreviewOperations = null!;
    private FakeFolderHistoryService _folderHistoryService = null!;
    private IMapper _mapper = null!;
    private FolderMutationCoordinator _mutationCoordinator = null!;
    private Serilog.Core.Logger _logger = null!;
    private ImmediateSynchronizationContext _synchronizationContext = null!;

    [SetUp]
    public void SetUp()
    {
        _folderService = new FakeFolderService();
        _fileSystemService = new FakeFileSystemService();
        _configurationService = new FakeConfigurationService();
        _drivesWatcherService = new FakeDrivesWatcherService();
        _multiSourcesWatcherService = new FakeMultiSourcesWatcherService();
        _mediaPreviewOperations = new FakeMediaPreviewOperationsService();
        _folderHistoryService = new FakeFolderHistoryService();
        _logger = new LoggerConfiguration().CreateLogger();
        _synchronizationContext = new ImmediateSynchronizationContext();
        _mutationCoordinator = new FolderMutationCoordinator(_folderService, _fileSystemService, _logger);
        _mapper = CreateMapper();
    }

    [TearDown]
    public void TearDown()
    {
        _logger.Dispose();
    }

    [TestCase(FileManagerPanel.Left)]
    [TestCase(FileManagerPanel.Right)]
    public void Selection_PreservesIdentityPublishesPanelAndPersistsPanelPath(FileManagerPanel panel)
    {
        var tree = CreateTree();
        tree.Initialize(panel);
        var first = CreateDirectory("first", TestPath("selection", "first"));
        var second = CreateDirectory("second", TestPath("selection", "second"));

        tree.SelectedFileSystemItem = first;
        tree.SelectedFileSystemItem = second;

        var configuration = _configurationService.Configuration.Value;
        Assert.Multiple(() =>
        {
            Assert.That(tree.SelectedFileSystemItem, Is.SameAs(second));
            Assert.That(first.IsSelected, Is.False);
            Assert.That(second.IsSelected, Is.True);
            Assert.That(_folderService.SelectedDirectories.Select(item => item.FileManagerPanel),
                Is.EqualTo(new[] { panel, panel }));
            Assert.That(_folderService.SelectedDirectories.Last().Path, Is.EqualTo(second.Path));
            Assert.That(configuration.LastSourceDirectoryPath,
                Is.EqualTo(panel == FileManagerPanel.Left ? second.Path : string.Empty));
            Assert.That(configuration.LastTargetDirectoryPath,
                Is.EqualTo(panel == FileManagerPanel.Right ? second.Path : string.Empty));
            Assert.That(_configurationService.SaveCallCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Start_LoadsRootAndPropagatesPanelToEveryMappedNode()
    {
        var child = new DirectoryModel("child", TestPath("root", "drive", "child"));
        var drive = new DriveModel("drive", TestPath("root", "drive"));
        drive.AddDirectories([child]);
        _folderService.RootModel = CreateRoot(drive);
        _configurationService.Configuration.Value.RestoreLastFoldersOnStartup = false;
        var loadingChanges = new List<bool>();
        var rootLoadedCount = 0;
        var tree = CreateTree();
        tree.LoadingChanged += loadingChanges.Add;
        tree.RootLoaded += () => rootLoadedCount++;

        tree.Start(FileManagerPanel.Right);
        await tree.EnsureRootLoadedAsync();

        var root = tree.DeviceRoot!;
        Assert.Multiple(() =>
        {
            Assert.That(tree.FileManagerPanel, Is.EqualTo(FileManagerPanel.Right));
            Assert.That(root.FileManagerPanel, Is.EqualTo(FileManagerPanel.Right));
            Assert.That(root.ChildFileSystemItems.Single().FileManagerPanel, Is.EqualTo(FileManagerPanel.Right));
            Assert.That(root.ChildFileSystemItems.Single().ChildFileSystemItems.Single().FileManagerPanel,
                Is.EqualTo(FileManagerPanel.Right));
            Assert.That(root.IsExpanded, Is.True);
            Assert.That(loadingChanges, Is.EqualTo(new[] { true, false }));
            Assert.That(rootLoadedCount, Is.EqualTo(1));
            Assert.That(_drivesWatcherService.StartCallCount, Is.EqualTo(1));
        });

        tree.Stop();
    }

    [Test]
    public async Task RestoreLastFolder_WhenAvailable_LoadsEveryLevelSelectsAndRevealsTarget()
    {
        var drivePath = TestPath("restore-available", "drive");
        var parentPath = Path.Combine(drivePath, "parent");
        var targetPath = Path.Combine(parentPath, "target");
        var drive = new DriveModel("drive", drivePath);
        _folderService.RootModel = CreateRoot(drive);
        SetSnapshot(drivePath, new DirectoryModel("parent", parentPath));
        SetSnapshot(parentPath, new DirectoryModel("target", targetPath));
        SetSnapshot(targetPath);
        _fileSystemService.ExistingDirectories.Add(targetPath);
        _configurationService.Configuration.Value.LastTargetDirectoryPath = targetPath;
        DirectoryViewModel? revealed = null;
        var tree = CreateTree();
        tree.RevealRequested += item => revealed = item;

        tree.Start(FileManagerPanel.Right);
        await tree.EnsureRootLoadedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(tree.SelectedFileSystemItem?.Path, Is.EqualTo(targetPath));
            Assert.That(revealed, Is.SameAs(tree.SelectedFileSystemItem));
            Assert.That(_folderService.LoadedDirectoryPaths,
                Is.EqualTo(new[] { drivePath, parentPath, targetPath }));
            Assert.That(_folderService.SelectedDirectories.Last().FileManagerPanel, Is.EqualTo(FileManagerPanel.Right));
            Assert.That(_configurationService.Configuration.Value.LastTargetDirectoryPath, Is.EqualTo(targetPath));
        });

        tree.Stop();
    }

    [Test]
    public async Task RestoreLastFolder_WhenDriveIsUnavailable_LeavesSelectionEmpty()
    {
        var unavailablePath = TestPath("restore-unavailable", "missing", "target");
        _folderService.RootModel = CreateRoot(new DriveModel("other", TestPath("restore-unavailable", "other")));
        _fileSystemService.ExistingDirectories.Add(unavailablePath);
        _configurationService.Configuration.Value.LastSourceDirectoryPath = unavailablePath;
        var tree = CreateTree();

        tree.Start(FileManagerPanel.Left);
        await tree.EnsureRootLoadedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(tree.SelectedFileSystemItem, Is.Null);
            Assert.That(_folderService.SelectedDirectories, Is.Empty);
            Assert.That(_configurationService.SaveCallCount, Is.Zero);
        });

        tree.Stop();
    }

    [Test]
    public async Task SelectFolderInTreeAsync_LoadsMissingLevelsAndRaisesRevealForMappedIdentity()
    {
        var drivePath = TestPath("lazy", "drive");
        var parentPath = Path.Combine(drivePath, "parent");
        var targetPath = Path.Combine(parentPath, "target");
        _folderService.RootModel = CreateRoot(new DriveModel("drive", drivePath));
        SetSnapshot(drivePath, new DirectoryModel("parent", parentPath));
        SetSnapshot(parentPath, new DirectoryModel("target", targetPath));
        SetSnapshot(targetPath);
        _configurationService.Configuration.Value.RestoreLastFoldersOnStartup = false;
        DirectoryViewModel? revealed = null;
        var tree = CreateTree();
        tree.RevealRequested += item => revealed = item;
        tree.Start(FileManagerPanel.Left);
        await tree.EnsureRootLoadedAsync();

        var selected = await tree.SelectFolderInTreeAsync(targetPath);

        Assert.Multiple(() =>
        {
            Assert.That(selected, Is.True);
            Assert.That(tree.SelectedFileSystemItem?.Path, Is.EqualTo(targetPath));
            Assert.That(revealed, Is.SameAs(tree.SelectedFileSystemItem));
            Assert.That(_folderService.LoadedDirectoryPaths,
                Is.EqualTo(new[] { drivePath, parentPath, targetPath }));
        });

        tree.Stop();
    }

    [Test]
    public async Task DriveEvents_MountUpdateFreeSpaceAndUnmountSelectedDriveToEmptySelection()
    {
        _folderService.RootModel = CreateRoot();
        _configurationService.Configuration.Value.RestoreLastFoldersOnStartup = false;
        var tree = CreateTree();
        tree.Start(FileManagerPanel.Right);
        await tree.EnsureRootLoadedAsync();
        var drivePath = TestPath("drives", "usb");

        _drivesWatcherService.RaiseMounted(new RemovableDriveModel("USB", drivePath, 1_000, 800));
        var mounted = tree.DeviceRoot!.ChildFileSystemItems.OfType<RemovableDriveViewModel>().Single();
        _drivesWatcherService.RaiseFreeSpace(new AvailableFreeSpaceInfo(drivePath, 600));
        tree.SelectedFileSystemItem = mounted;
        _drivesWatcherService.RaiseUnmounted("USB");

        var emptySelection = _folderService.SelectedDirectories.Last();
        Assert.Multiple(() =>
        {
            Assert.That(mounted.FileManagerPanel, Is.EqualTo(FileManagerPanel.Right));
            Assert.That(mounted.AvailableFreeSpace, Is.EqualTo(600));
            Assert.That(tree.DeviceRoot.ChildFileSystemItems, Does.Not.Contain(mounted));
            Assert.That(tree.SelectedFileSystemItem, Is.Null);
            Assert.That(mounted.IsSelected, Is.False);
            Assert.That(emptySelection.Name, Is.EqualTo(DirectoryModel.Empty.Name));
            Assert.That(emptySelection.Path, Is.EqualTo(DirectoryModel.Empty.Path));
            Assert.That(emptySelection.FileManagerPanel, Is.EqualTo(FileManagerPanel.Right));
        });

        tree.Stop();
    }

    [Test]
    public async Task StartAndStop_OwnDriveSubscriptionsWithoutDuplicatingThem()
    {
        _folderService.RootModel = CreateRoot();
        _configurationService.Configuration.Value.RestoreLastFoldersOnStartup = false;
        var tree = CreateTree();
        tree.Initialize(FileManagerPanel.Left);

        tree.Start();
        tree.Start();
        await tree.EnsureRootLoadedAsync();
        _drivesWatcherService.RaiseMounted(new DriveModel("active", TestPath("lifetime", "active")));
        tree.Stop();
        tree.Stop();
        _drivesWatcherService.RaiseMounted(new DriveModel("ignored", TestPath("lifetime", "ignored")));

        Assert.Multiple(() =>
        {
            Assert.That(_drivesWatcherService.StartCallCount, Is.EqualTo(1));
            Assert.That(_drivesWatcherService.StopCallCount, Is.EqualTo(1));
            Assert.That(tree.DeviceRoot!.ChildFileSystemItems.Select(item => item.Name),
                Is.EqualTo(new[] { "active" }));
        });
    }

    [Test]
    public void RootWiring_UpdatesSearchEligibilityForPhysicalDriveSpecialAndRegularFolders()
    {
        var viewModel = CreateFoldersViewModel();
        viewModel.Tree.Initialize(FileManagerPanel.Left);
        var regular = CreateDirectory("regular", TestPath("search", "regular"));
        var drive = CreateDrive("drive", TestPath("search", "drive"));
        var special = CreateSpecial("recent", TestPath("search", "recent"));

        viewModel.Tree.SelectedFileSystemItem = regular;
        var regularEnabled = viewModel.Search.IsSearchEnabled;
        viewModel.Tree.SelectedFileSystemItem = drive;
        var driveEnabled = viewModel.Search.IsSearchEnabled;
        viewModel.Tree.SelectedFileSystemItem = special;
        var specialEnabled = viewModel.Search.IsSearchEnabled;

        Assert.Multiple(() =>
        {
            Assert.That(regularEnabled, Is.True);
            Assert.That(driveEnabled, Is.False);
            Assert.That(specialEnabled, Is.False);
        });
    }

    [Test]
    public void RootWiring_RemovingSelectedRecentFolderClearsTreeSelection()
    {
        var viewModel = CreateFoldersViewModel();
        viewModel.Tree.Initialize(FileManagerPanel.Right);
        var recent = CreateDirectory("recent", TestPath("recent-wiring", "selected"));
        recent.SetRecentOrder(0, true);
        viewModel.Tree.SelectedFileSystemItem = recent;

        viewModel.Recent.RemoveRecentFolderCommand.Execute(recent);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Tree.SelectedFileSystemItem, Is.Null);
            Assert.That(_folderHistoryService.RemovedPopularFolders, Is.EqualTo(new[] { recent.Path }));
        });
    }

    [Test]
    public async Task PublicForwarder_SelectsFolderAndRelaysRevealForMcpCompatibility()
    {
        var drivePath = TestPath("forwarder", "drive");
        var targetPath = Path.Combine(drivePath, "target");
        _folderService.RootModel = CreateRoot(new DriveModel("drive", drivePath));
        SetSnapshot(drivePath, new DirectoryModel("target", targetPath));
        SetSnapshot(targetPath);
        _configurationService.Configuration.Value.RestoreLastFoldersOnStartup = false;
        var viewModel = CreateFoldersViewModel();
        DirectoryViewModel? revealed = null;
        viewModel.RevealSelectedFolderRequested += item => revealed = item;
        viewModel.Tree.Start(FileManagerPanel.Left);
        await viewModel.Tree.EnsureRootLoadedAsync();

        var result = await viewModel.SelectFolderInTreeAsync(targetPath);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(viewModel.Tree.SelectedFileSystemItem?.Path, Is.EqualTo(targetPath));
            Assert.That(revealed, Is.SameAs(viewModel.Tree.SelectedFileSystemItem));
        });

        viewModel.Tree.Stop();
    }

    [Test]
    public async Task NavigationLifecycle_SubscribesMediaNavigationAndStopsAllWatchersOnExit()
    {
        var drivePath = TestPath("navigation", "drive");
        var targetPath = Path.Combine(drivePath, "target");
        _folderService.RootModel = CreateRoot(new DriveModel("drive", drivePath));
        SetSnapshot(drivePath, new DirectoryModel("target", targetPath));
        SetSnapshot(targetPath);
        _fileSystemService.ExistingDirectories.Add(targetPath);
        _configurationService.Configuration.Value.RestoreLastFoldersOnStartup = false;
        var viewModel = CreateFoldersViewModel();
        var parameters = new NavigationParameters { { "panel", FileManagerPanel.Right } };
        var context = new NavigationContext(null!, new Uri("folders", UriKind.Relative), parameters);
        context.Parameters.Add("panel", FileManagerPanel.Right);

        viewModel.OnNavigatedTo(context);
        await viewModel.Tree.EnsureRootLoadedAsync();
        _mediaPreviewOperations.RaiseNavigation(targetPath, FileManagerPanel.Right);
        await WaitUntilAsync(() => viewModel.Tree.SelectedFileSystemItem?.Path == targetPath);
        viewModel.OnNavigatedFrom(context);
        _mediaPreviewOperations.RaiseNavigation(drivePath, FileManagerPanel.Right);
        await Task.Delay(25);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.FileManagerPanel, Is.EqualTo(FileManagerPanel.Right));
            Assert.That(viewModel.Tree.SelectedFileSystemItem?.Path, Is.EqualTo(targetPath));
            Assert.That(_drivesWatcherService.StartCallCount, Is.EqualTo(1));
            Assert.That(_drivesWatcherService.StopCallCount, Is.EqualTo(1));
            Assert.That(_multiSourcesWatcherService.StartCallCount, Is.EqualTo(1));
            Assert.That(_multiSourcesWatcherService.StopCallCount, Is.EqualTo(1));
            Assert.That(_multiSourcesWatcherService.ClearCallCount, Is.EqualTo(1));
        });
    }

    private FolderTreeViewModel CreateTree()
    {
        return new FolderTreeViewModel(
            _folderService,
            _fileSystemService,
            _configurationService,
            _drivesWatcherService,
            _mapper,
            _logger,
            _synchronizationContext);
    }

    private FoldersViewModel CreateFoldersViewModel()
    {
        return new FoldersViewModel(
            _folderService,
            _mediaPreviewOperations,
            _folderHistoryService,
            new FakeFileDialogService(),
            _fileSystemService,
            new FolderTagsEditorWindowService(new FakeFolderTagsStore(), LocalizationManager.Instance, _logger),
            _configurationService,
            _multiSourcesWatcherService,
            _drivesWatcherService,
            _mapper,
            _logger,
            LocalizationManager.Instance,
            _synchronizationContext);
    }

    private IMapper CreateMapper()
    {
        var config = new TypeAdapterConfig();
        config.NewConfig<DirectoryModel, DirectoryViewModel>().MapWith(source => MapModel(source));
        config.NewConfig<DriveModel, DriveViewModel>().MapWith(source => (DriveViewModel)MapModel(source));
        config.NewConfig<RemovableDriveModel, DriveViewModel>().MapWith(source => (DriveViewModel)MapModel(source));
        config.NewConfig<DirectoryViewModel, DirectoryModel>()
              .MapWith(source => new DirectoryModel(source.Name, source.Path));
        return new Mapper(config);
    }

    private DirectoryViewModel MapModel(DirectoryModel source)
    {
        var children = source.DirectoryModels.Select(MapModel).ToArray();
        var result = source switch
        {
            DeviceModel => new DeviceViewModel(
                source.Name, source.Path, children, _folderService, _mutationCoordinator, _mapper, _logger),
            SpecialDirectoryModel => new SpecialDirectoryViewModel(
                source.Name, source.Path, children, _folderService, _mutationCoordinator, _mapper, _logger),
            RemovableDriveModel removable => new RemovableDriveViewModel(
                source.Name,
                source.Path,
                removable.TotalSize,
                removable.AvailableFreeSpace,
                children,
                _folderService,
                _mutationCoordinator,
                _mapper,
                _logger),
            DriveModel => new DriveViewModel(
                source.Name, source.Path, children, _folderService, _mutationCoordinator, _mapper, _logger),
            _ => new DirectoryViewModel(
                source.Name, source.Path, children, _folderService, _mutationCoordinator, _mapper, _logger)
        };
        result.HasSupportedMedia = source.HasSupportedMedia;
        return result;
    }

    private DirectoryViewModel CreateDirectory(string name, string path) =>
        _mapper.Map<DirectoryViewModel>(new DirectoryModel(name, path));

    private DriveViewModel CreateDrive(string name, string path) =>
        _mapper.Map<DriveViewModel>(new DriveModel(name, path));

    private SpecialDirectoryViewModel CreateSpecial(string name, string path) =>
        (SpecialDirectoryViewModel)_mapper.Map<DirectoryViewModel>(new SpecialDirectoryModel(name, path));

    private void SetSnapshot(string path, params DirectoryModel[] children)
    {
        var model = new DirectoryModel(Path.GetFileName(path), path);
        model.AddDirectories(children);
        _folderService.DirectorySnapshots[path] = model;
    }

    private static DeviceModel CreateRoot(params DirectoryModel[] children)
    {
        var root = new DeviceModel("Computer", DirectoryModel.RootPath);
        root.AddDirectories(children);
        return root;
    }

    private static string TestPath(params string[] parts) =>
        Path.Combine([Path.GetTempPath(), "ImageCareFolderTreeTests", .. parts]);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.That(condition(), Is.True, "Timed out waiting for asynchronous navigation");
    }

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
        public override void Send(SendOrPostCallback callback, object? state) => callback(state);
    }

    private sealed class FakeConfigurationService : IConfigurationService
    {
        public FakeConfigurationService()
        {
            Configuration = new Lazy<Configuration>(() => new Configuration());
        }

        public IObservable<Configuration> ConfigurationSaved => Observable.Never<Configuration>();
        public Lazy<Configuration> Configuration { get; }
        public int SaveCallCount { get; private set; }
        public void SaveConfiguration() => SaveCallCount++;
    }

    private sealed class FakeDrivesWatcherService : IDrivesWatcherService
    {
        private readonly Subject<DriveModel> _mounted = new();
        private readonly Subject<string> _unmounted = new();
        private readonly Subject<AvailableFreeSpaceInfo> _freeSpace = new();

        public IObservable<DriveModel> DriveMounted => _mounted;
        public IObservable<string> DriveUnmounted => _unmounted;
        public IObservable<AvailableFreeSpaceInfo> DriveAvailableFreeSpaceChanged => _freeSpace;
        public int StartCallCount { get; private set; }
        public int StopCallCount { get; private set; }
        public void StartWatching() => StartCallCount++;
        public void StopWatching() => StopCallCount++;
        public void RaiseMounted(DriveModel model) => _mounted.OnNext(model);
        public void RaiseUnmounted(string name) => _unmounted.OnNext(name);
        public void RaiseFreeSpace(AvailableFreeSpaceInfo info) => _freeSpace.OnNext(info);
    }

    private sealed class FakeMultiSourcesWatcherService : IMultiSourcesFileSystemWatcherService
    {
        public IObservable<FileModel> FileCreated => Observable.Never<FileModel>();
        public IObservable<FileModel> FileChanged => Observable.Never<FileModel>();
        public IObservable<FileModel> FileDeleted => Observable.Never<FileModel>();
        public IObservable<FileRenamedModel> FileRenamed => Observable.Never<FileRenamedModel>();
        public IObservable<DirectoryModel> DirectoryCreated => Observable.Never<DirectoryModel>();
        public IObservable<DirectoryModel> DirectoryDeleted => Observable.Never<DirectoryModel>();
        public IObservable<DirectoryRenamedModel> DirectoryRenamed => Observable.Never<DirectoryRenamedModel>();
        public IObservable<string> RescanRequested => Observable.Never<string>();
        public int StartCallCount { get; private set; }
        public int StopCallCount { get; private set; }
        public int ClearCallCount { get; private set; }
        public void StartWatching() => StartCallCount++;
        public void StopWatching() => StopCallCount++;
        public void ClearWatchers() => ClearCallCount++;
        public void StartWatchingDirectory(string directoryPath) { }
        public void StopWatchingDirectory(string directoryPath) { }
    }

    private sealed class FakeFolderService : IFolderService
    {
        public DeviceModel RootModel { get; set; } = CreateRoot();
        public Dictionary<string, DirectoryModel> DirectorySnapshots { get; } = new();
        public List<string> LoadedDirectoryPaths { get; } = [];
        public List<SelectedDirectory> SelectedDirectories { get; } = [];
        public IObservable<SelectedDirectory> FileSystemItemSelected => Observable.Never<SelectedDirectory>();
        public IObservable<SelectedDirectory> FolderVisited => Observable.Never<SelectedDirectory>();
        public IObservable<SelectedDirectory> FolderLeft => Observable.Never<SelectedDirectory>();

        public Task<DirectoryModel> GetDirectoryModelAsync(DirectoryModel? directoryModel = null) =>
            Task.FromResult<DirectoryModel>(RootModel);

        public Task<DirectoryModel> GetDirectoryModelAsync(string directoryPath)
        {
            LoadedDirectoryPaths.Add(directoryPath);
            return Task.FromResult(DirectorySnapshots[directoryPath]);
        }

        public void SetSelectedDirectory(SelectedDirectory selectedDirectory) =>
            SelectedDirectories.Add(selectedDirectory);

        public Task<IEnumerable<FileModel>> GetFileModelAsync(DirectoryModel directoryModel, string searchPattern) =>
            throw new NotSupportedException();
        public Task<IEnumerable<FileModel>> GetFileModelAsync(string directoryPath, string searchPattern) =>
            throw new NotSupportedException();
        public Task<DirectoryModel> GetCustomDirectoriesLevelAsync(DirectoryModel directoryModel, bool preview = false) =>
            throw new NotSupportedException();
        public DirectoryModel? GetSelectedDirectory(FileManagerPanel fileManagerPanel) => null;
        public void AddVisitingFolder(DirectoryModel directoryModel, FileManagerPanel fileManagerPanel) { }
        public void RemoveVisitingFolder(DirectoryModel directoryModel, FileManagerPanel fileManagerPanel) { }
        public void RemoveFolder(DirectoryModel directoryModel) => throw new NotSupportedException();
        public DirectoryModel? CreateSubFolder(DirectoryModel directoryModel) => throw new NotSupportedException();
    }

    private sealed class FakeFileSystemService : IFileSystemService
    {
        public HashSet<string> ExistingDirectories { get; } = [];
        public bool DirectoryExists(string path) => ExistingDirectories.Contains(path);
        public IEnumerable<string> EnumerateDirectories(
            string directory,
            string searchPattern,
            SearchOption searchOption = SearchOption.TopDirectoryOnly) => [];
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
        public string[] GetDirectories(string directory) => throw new NotSupportedException();
        public void CopyFileMetadata(string source, string destination) => throw new NotSupportedException();
        public void CopyFileTimestampsAndAttributes(string source, string destination) => throw new NotSupportedException();
        public void CopyDirectoryMetadata(string source, string destination) => throw new NotSupportedException();
        public void SafeDelete(string path) => throw new NotSupportedException();
        public void SafeDeleteDirectory(string path) => throw new NotSupportedException();
        public string GetFileExtension(string path) => throw new NotSupportedException();
        public string? RenameFolder(string? newName, string path) => throw new NotSupportedException();
    }

    private sealed class FakeFolderHistoryService : IFolderHistoryService
    {
        public event EventHandler? HistoryChanged;
        public List<string> RemovedPopularFolders { get; } = [];
        public void AddFolderToHistory(string folderPath) { }
        public void AddFolderToPopular(string folderPath) { }
        public void RemoveFolderFromPopular(string folderPath) => RemovedPopularFolders.Add(folderPath);
        public IReadOnlyList<string> GetPopularFolders() => [];
        public IReadOnlyList<string> GetRecentFolders() => [];
        public IReadOnlyList<SmartDirectoryModel> GetSmartFolders(int minIntersections = 3) => [];
        public void SaveHistory() { }
        public void LoadHistory() { }
        public void RaiseHistoryChanged() => HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeFileDialogService : IFileDialogService
    {
        public Task<string?> ShowOpenFileDialogAsync(string title, params (string Name, string[] Patterns)[] filters) =>
            Task.FromResult<string?>(null);
        public Task<string?> ShowOpenApplicationDialogAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> ShowOpenFolderDialogAsync(string title) => Task.FromResult<string?>(null);
    }

    private sealed class FakeFolderTagsStore : IFolderTagsStore
    {
        public string GetDescriptorPath(string directoryPath) => Path.Combine(directoryPath, FolderTagsFile.FileName);
        public bool HasDescriptor(string directoryPath) => false;
        public IReadOnlyList<string>? Read(string directoryPath) => null;
        public Task WriteAsync(
            string directoryPath,
            IEnumerable<string> tags,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeMediaPreviewOperationsService : IMediaPreviewOperationsService
    {
        private readonly Subject<SelectedMediaPreview> _navigation = new();
        public IObservable<SelectedMediaPreview> ImagePreviewSelected => Observable.Never<SelectedMediaPreview>();
        public IObservable<SelectedMediaPreview> MediaPreviewNavigationRequested => _navigation;
        public IObservable<MediaFileOperationCompleted> FileOperationCompleted => Observable.Never<MediaFileOperationCompleted>();
        public bool CanUndoDelete => false;
        public IObservable<IReadOnlyList<DeletedMediaFile>> DeletedFilesChanged =>
            Observable.Never<IReadOnlyList<DeletedMediaFile>>();
        public IReadOnlyList<DeletedMediaFile> GetDeletedFiles() => [];
        public void RaiseNavigation(string directoryPath, FileManagerPanel panel)
        {
            _navigation.OnNext(new SelectedMediaPreview(
                "test",
                Path.Combine(directoryPath, "image.jpg"),
                MediaFormat.MediaFormatJpg,
                100,
                panel));
        }
        public Task<OperationResult> CopyImagePreviewToDirectoryAsync(
            MediaPreview imagePreview,
            string selectedFolderPath,
            Progress<OperationInfo> progress) => throw new NotSupportedException();
        public Task<OperationResult> CopyImagePreviewToDirectoryAsync(
            MediaPreview imagePreview,
            string selectedFolderPath,
            Progress<OperationInfo> progress,
            string destinationFileName) => throw new NotSupportedException();
        public Task<OperationResult> MoveImagePreviewToDirectoryAsync(
            MediaPreview imagePreview,
            string selectedFolderPath,
            Progress<OperationInfo> progress) => throw new NotSupportedException();
        public Task<OperationResult> MoveImagePreviewToDirectoryAsync(
            MediaPreview imagePreview,
            string selectedFolderPath,
            Progress<OperationInfo> progress,
            string destinationFileName) => throw new NotSupportedException();
        public Task<OperationResult> DeleteImagePreviewAsync(MediaPreview imagePreview) => throw new NotSupportedException();
        public Task<OperationResult> UndoLastDeleteAsync() => throw new NotSupportedException();
        public Task<OperationResult> RestoreDeletedFileAsync(string bufferPath) => throw new NotSupportedException();
        public Task<OperationResult> PermanentlyDeleteAsync(string bufferPath) => throw new NotSupportedException();
        public void SetSelectedPreview(SelectedMediaPreview selectedImagePreview) { }
        public void RequestMediaPreviewNavigation(SelectedMediaPreview selectedImagePreview) =>
            _navigation.OnNext(selectedImagePreview);
        public void OpenInExternalProcess(MediaPreview mediaPreview, string pathToExecutable) =>
            throw new NotSupportedException();
        public void OpenInExternalProcess(MediaPreview mediaPreview, ImageCare.Core.Domain.FileApplicationInfo application) =>
            throw new NotSupportedException();
        public MediaPreview? GetLastSelectedMediaPreview() => null;
    }
}
