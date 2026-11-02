using System.Reactive.Linq;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.FolderHistoryService;
using ImageCare.Core.Services.FolderService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Domain;
using ImageCare.UI.Avalonia.ViewModels.Folders;

using Mapster;
using MapsterMapper;

using Serilog;

namespace ImageCare.UI.Avalonia.Tests.ViewModels.Folders;

[TestFixture]
public sealed class RecentFoldersViewModelTests
{
    private FakeFolderHistoryService _history = null!;
    private FakeFileDialogService _fileDialog = null!;
    private FakeFolderService _folderService = null!;
    private FakeFileSystemService _fileSystemService = null!;
    private ImmediateSynchronizationContext _synchronizationContext = null!;
    private IMapper _mapper = null!;
    private FolderMutationCoordinator _mutationCoordinator = null!;
    private Serilog.Core.Logger _logger = null!;
    private SpecialDirectoryViewModel _recentNode = null!;
    private DeviceViewModel _root = null!;
    private DirectoryViewModel? _selectedItem;
    private RecentFoldersViewModel _viewModel = null!;

    [SetUp]
    public void SetUp()
    {
        _history = new FakeFolderHistoryService();
        _fileDialog = new FakeFileDialogService();
        _folderService = new FakeFolderService();
        _fileSystemService = new FakeFileSystemService();
        _synchronizationContext = new ImmediateSynchronizationContext();
        _logger = new LoggerConfiguration().CreateLogger();
        _mutationCoordinator = new FolderMutationCoordinator(_folderService, _fileSystemService, _logger);
        _mapper = CreateMapper();
        _recentNode = new SpecialDirectoryViewModel(
            "Popular",
            DirectoryModel.RootPath,
            [],
            _folderService,
            _mutationCoordinator,
            _mapper,
            _logger);
        _root = new DeviceViewModel(
            "Computer",
            DirectoryModel.RootPath,
            [_recentNode],
            _folderService,
            _mutationCoordinator,
            _mapper,
            _logger);
        _root.SetFileManagerPanel(FileManagerPanel.Right);
        _viewModel = CreateViewModel(StringComparer.Ordinal);
    }

    [TearDown]
    public void TearDown()
    {
        _logger.Dispose();
    }

    [Test]
    public void Refresh_PutsPinnedBeforeAutomaticAndDeduplicatesOverlap()
    {
        var pinnedOne = TestPath("pinned-one");
        var pinnedTwo = TestPath("pinned-two");
        var automatic = TestPath("automatic");
        _history.PopularFolders = [pinnedOne, pinnedTwo];
        _history.SmartFolders =
        [
            new SmartDirectoryModel("duplicate", pinnedTwo, 12),
            new SmartDirectoryModel("automatic", automatic, 11)
        ];

        _viewModel.Refresh();

        Assert.Multiple(() =>
        {
            Assert.That(_recentNode.ChildFileSystemItems.Select(item => item.Path),
                Is.EqualTo(new[] { pinnedOne, pinnedTwo, automatic }));
            Assert.That(_recentNode.ChildFileSystemItems.Select(item => item.IsPinnedPopular),
                Is.EqualTo(new[] { true, true, false }));
            Assert.That(_recentNode.ChildFileSystemItems[2].FileManagerPanel, Is.EqualTo(FileManagerPanel.Right));
            Assert.That(_recentNode.IsExpanded, Is.True);
        });
    }

    [Test]
    public void Refresh_WithCaseInsensitiveComparer_DeduplicatesPathCase()
    {
        var path = TestPath("Photos");
        _history.PopularFolders = [path];
        _history.SmartFolders = [new SmartDirectoryModel("photos", path.ToUpperInvariant(), 12)];
        _viewModel = CreateViewModel(StringComparer.OrdinalIgnoreCase);

        _viewModel.Refresh();

        Assert.That(_recentNode.ChildFileSystemItems, Has.Count.EqualTo(1));
        Assert.That(_recentNode.ChildFileSystemItems[0].Path, Is.EqualTo(path));
        Assert.That(_recentNode.ChildFileSystemItems[0].IsPinnedPopular, Is.True);
    }

    [Test]
    public void Refresh_LimitsAutomaticEntriesToTenWithoutLimitingPinnedEntries()
    {
        var pinned = Enumerable.Range(0, 12).Select(index => TestPath($"pinned-{index}")).ToArray();
        var automatic = Enumerable.Range(0, 12)
                                  .Select(index => new SmartDirectoryModel(
                                      $"automatic-{index}",
                                      TestPath($"automatic-{index}"),
                                      20 - index))
                                  .ToArray();
        _history.PopularFolders = pinned;
        _history.SmartFolders = automatic;

        _viewModel.Refresh();

        Assert.Multiple(() =>
        {
            Assert.That(_recentNode.ChildFileSystemItems, Has.Count.EqualTo(22));
            Assert.That(_recentNode.ChildFileSystemItems.Take(12).Select(item => item.Path), Is.EqualTo(pinned));
            Assert.That(_recentNode.ChildFileSystemItems.Skip(12).Select(item => item.Path),
                Is.EqualTo(automatic.Take(10).Select(item => item.Path)));
        });
    }

    [Test]
    public void Refresh_ReusesIdentityUpdatesOrderAndRemovesStaleEntries()
    {
        var retainedPath = TestPath("retained");
        var movedPath = TestPath("moved");
        var stale = CreateDirectory("stale", TestPath("stale"));
        var retained = CreateDirectory("retained", retainedPath);
        retained.SetRecentOrder(4);
        _recentNode.ChildFileSystemItems.Add(stale);
        _recentNode.ChildFileSystemItems.Add(retained);
        _history.PopularFolders = [movedPath];
        _history.SmartFolders = [new SmartDirectoryModel("retained", retainedPath, 12)];

        _viewModel.Refresh();
        var moved = _recentNode.ChildFileSystemItems[0];
        _history.PopularFolders = [retainedPath];
        _history.SmartFolders = [new SmartDirectoryModel("moved", movedPath, 12)];

        _viewModel.Refresh();

        Assert.Multiple(() =>
        {
            Assert.That(_recentNode.ChildFileSystemItems, Has.Count.EqualTo(2));
            Assert.That(_recentNode.ChildFileSystemItems[0], Is.SameAs(retained));
            Assert.That(_recentNode.ChildFileSystemItems[1], Is.SameAs(moved));
            Assert.That(_recentNode.ChildFileSystemItems.Select(item => item.RecentOrder),
                Is.EqualTo(new[] { 0, 1 }));
            Assert.That(retained.IsPinnedPopular, Is.True);
            Assert.That(moved.IsPinnedPopular, Is.False);
            Assert.That(_recentNode.ChildFileSystemItems, Does.Not.Contain(stale));
        });
    }

    [Test]
    public void RemoveRecentFolderCommand_ClearsSelectedPinnedEntryAndSavesHistory()
    {
        var pinned = CreateDirectory("pinned", TestPath("pinned"));
        pinned.SetRecentOrder(0, true);
        _selectedItem = pinned;

        _viewModel.RemoveRecentFolderCommand.Execute(pinned);

        Assert.Multiple(() =>
        {
            Assert.That(_selectedItem, Is.Null);
            Assert.That(_history.RemovedPopularFolders, Is.EqualTo(new[] { pinned.Path }));
            Assert.That(_history.SaveHistoryCallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void AddFolderToRecentCommand_PinsOnlyPhysicalNonRecentFolderAndSavesHistory()
    {
        var physical = CreateDirectory("physical", TestPath("physical"));
        var recent = CreateDirectory("recent", TestPath("recent"));
        recent.SetRecentOrder(0);

        _viewModel.AddFolderToRecentCommand.Execute(physical);
        _viewModel.AddFolderToRecentCommand.Execute(recent);
        _viewModel.AddFolderToRecentCommand.Execute(_root);
        _viewModel.AddFolderToRecentCommand.Execute(_recentNode);

        Assert.Multiple(() =>
        {
            Assert.That(_history.AddedPopularFolders, Is.EqualTo(new[] { physical.Path }));
            Assert.That(_history.SaveHistoryCallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AddRecentFolderCommand_WhenPickerIsCancelled_DoesNotChangeOrSaveHistory()
    {
        _fileDialog.SelectedFolder = null;

        await _viewModel.AddRecentFolderCommand.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_history.AddedPopularFolders, Is.Empty);
            Assert.That(_history.SaveHistoryCallCount, Is.Zero);
            Assert.That(_fileDialog.LastFolderDialogTitle,
                Is.EqualTo(LocalizationManager.Instance["Folders_SelectRecent"]));
        });
    }

    [Test]
    public async Task AddRecentFolderCommand_WhenPickerSucceeds_AddsFolderAndSavesHistory()
    {
        var selectedFolder = TestPath("picked");
        _fileDialog.SelectedFolder = selectedFolder;

        await _viewModel.AddRecentFolderCommand.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_history.AddedPopularFolders, Is.EqualTo(new[] { selectedFolder }));
            Assert.That(_history.SaveHistoryCallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void HistoryChanged_RefreshesOnlyBetweenStartAndStop()
    {
        _history.PopularFolders = [TestPath("first")];
        _viewModel.Start();
        _history.PopularFolders = [TestPath("second")];

        _history.RaiseHistoryChanged();
        _viewModel.Stop();
        _history.PopularFolders = [TestPath("ignored")];
        _history.RaiseHistoryChanged();

        Assert.Multiple(() =>
        {
            Assert.That(_recentNode.ChildFileSystemItems.Select(item => item.Path),
                Is.EqualTo(new[] { TestPath("second") }));
            Assert.That(_synchronizationContext.PostCallCount, Is.EqualTo(1));
            Assert.That(_history.SubscriberCount, Is.Zero);
        });
    }

    private RecentFoldersViewModel CreateViewModel(StringComparer pathComparer)
    {
        return new RecentFoldersViewModel(
            _history,
            _fileDialog,
            _mapper,
            LocalizationManager.Instance,
            _logger,
            _synchronizationContext,
            () => _root,
            () => FileManagerPanel.Right,
            directory =>
            {
                if (ReferenceEquals(_selectedItem, directory))
                {
                    _selectedItem = null;
                }
            },
            pathComparer);
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
                  _logger));
        mapper = new Mapper(config);
        return mapper;
    }

    private DirectoryViewModel CreateDirectory(string name, string path)
    {
        return _mapper.Map<DirectoryViewModel>(new DirectoryModel(name, path));
    }

    private static string TestPath(string name)
    {
        return Path.Combine(Path.GetTempPath(), "ImageCareRecentFoldersTests", name);
    }

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public int PostCallCount { get; private set; }

        public override void Post(SendOrPostCallback callback, object? state)
        {
            PostCallCount++;
            callback(state);
        }
    }

    private sealed class FakeFolderHistoryService : IFolderHistoryService
    {
        private EventHandler? _historyChanged;

        public IReadOnlyList<string> PopularFolders { get; set; } = [];

        public IReadOnlyList<SmartDirectoryModel> SmartFolders { get; set; } = [];

        public List<string> AddedPopularFolders { get; } = [];

        public List<string> RemovedPopularFolders { get; } = [];

        public int SaveHistoryCallCount { get; private set; }

        public int SubscriberCount { get; private set; }

        public event EventHandler? HistoryChanged
        {
            add
            {
                _historyChanged += value;
                SubscriberCount++;
            }
            remove
            {
                _historyChanged -= value;
                SubscriberCount--;
            }
        }

        public void RaiseHistoryChanged()
        {
            _historyChanged?.Invoke(this, EventArgs.Empty);
        }

        public void AddFolderToHistory(string folderPath) => throw new NotSupportedException();

        public void AddFolderToPopular(string folderPath)
        {
            AddedPopularFolders.Add(folderPath);
        }

        public void RemoveFolderFromPopular(string folderPath)
        {
            RemovedPopularFolders.Add(folderPath);
        }

        public IReadOnlyList<string> GetPopularFolders() => PopularFolders;

        public IReadOnlyList<string> GetRecentFolders() => throw new NotSupportedException();

        public IReadOnlyList<SmartDirectoryModel> GetSmartFolders(int minIntersections = 3)
        {
            Assert.That(minIntersections, Is.EqualTo(10));
            return SmartFolders;
        }

        public void SaveHistory()
        {
            SaveHistoryCallCount++;
        }

        public void LoadHistory() => throw new NotSupportedException();
    }

    private sealed class FakeFileDialogService : IFileDialogService
    {
        public string? SelectedFolder { get; set; }

        public string? LastFolderDialogTitle { get; private set; }

        public Task<string?> ShowOpenFolderDialogAsync(string title)
        {
            LastFolderDialogTitle = title;
            return Task.FromResult(SelectedFolder);
        }

        public Task<string?> ShowOpenFileDialogAsync(
            string title,
            params (string Name, string[] Patterns)[] filters) => throw new NotSupportedException();

        public Task<string?> ShowOpenApplicationDialogAsync(string title) => throw new NotSupportedException();
    }

    private sealed class FakeFileSystemService : IFileSystemService
    {
        public bool FileExists(string path) => throw new NotSupportedException();
        public bool DirectoryExists(string path) => throw new NotSupportedException();
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
        public IEnumerable<FileModel> EnumerateFiles(string directory, string searchPattern, SearchOption searchOption) => throw new NotSupportedException();
        public IEnumerable<string> EnumerateDirectories(string directory, string searchPattern, SearchOption searchOption = SearchOption.TopDirectoryOnly) => throw new NotSupportedException();
        public string[] GetDirectories(string directory) => throw new NotSupportedException();
        public void CopyFileMetadata(string source, string destination) => throw new NotSupportedException();
        public void CopyFileTimestampsAndAttributes(string source, string destination) => throw new NotSupportedException();
        public void CopyDirectoryMetadata(string source, string destination) => throw new NotSupportedException();
        public void SafeDelete(string path) => throw new NotSupportedException();
        public void SafeDeleteDirectory(string path) => throw new NotSupportedException();
        public string GetFileExtension(string path) => throw new NotSupportedException();
        public string? RenameFolder(string? newName, string path) => throw new NotSupportedException();
    }

    private sealed class FakeFolderService : IFolderService
    {
        public IObservable<SelectedDirectory> FileSystemItemSelected => Observable.Never<SelectedDirectory>();
        public IObservable<SelectedDirectory> FolderVisited => Observable.Never<SelectedDirectory>();
        public IObservable<SelectedDirectory> FolderLeft => Observable.Never<SelectedDirectory>();

        public Task<DirectoryModel> GetDirectoryModelAsync(DirectoryModel? directoryModel = null) => throw new NotSupportedException();
        public Task<DirectoryModel> GetDirectoryModelAsync(string directoryPath) => throw new NotSupportedException();
        public Task<IEnumerable<FileModel>> GetFileModelAsync(DirectoryModel directoryModel, string searchPattern) => throw new NotSupportedException();
        public Task<IEnumerable<FileModel>> GetFileModelAsync(string directoryPath, string searchPattern) => throw new NotSupportedException();
        public Task<DirectoryModel> GetCustomDirectoriesLevelAsync(DirectoryModel directoryModel, bool preview = false) => throw new NotSupportedException();
        public void SetSelectedDirectory(SelectedDirectory selectedDirectory) => throw new NotSupportedException();
        public DirectoryModel? GetSelectedDirectory(FileManagerPanel fileManagerPanel) => throw new NotSupportedException();
        public void AddVisitingFolder(DirectoryModel directoryModel, FileManagerPanel fileManagerPanel) => throw new NotSupportedException();
        public void RemoveVisitingFolder(DirectoryModel directoryModel, FileManagerPanel fileManagerPanel) => throw new NotSupportedException();
        public void RemoveFolder(DirectoryModel directoryModel) => throw new NotSupportedException();
        public DirectoryModel? CreateSubFolder(DirectoryModel directoryModel) => throw new NotSupportedException();
    }
}
