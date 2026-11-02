using System.Reflection;
using System.Reactive.Linq;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.FolderService;
using ImageCare.UI.Avalonia.ViewModels.Domain;
using ImageCare.UI.Avalonia.ViewModels.Folders;

using Mapster;
using MapsterMapper;

using Serilog;

namespace ImageCare.UI.Avalonia.Tests.ViewModels.Folders;

[TestFixture]
public sealed class FolderMutationCoordinatorTests
{
    private FakeFolderService _folderService = null!;
    private FakeFileSystemService _fileSystemService = null!;
    private Serilog.Core.Logger _logger = null!;
    private FolderMutationCoordinator _coordinator = null!;
    private IMapper _mapper = null!;

    [SetUp]
    public void SetUp()
    {
        _folderService = new FakeFolderService();
        _fileSystemService = new FakeFileSystemService();
        _logger = new LoggerConfiguration().CreateLogger();
        _coordinator = new FolderMutationCoordinator(_folderService, _fileSystemService, _logger);
        _mapper = CreateMapper();
    }

    [TearDown]
    public void TearDown() => _logger.Dispose();

    [Test]
    public void Rename_ExpandedSubtreeUpdatesPathsSortVisitingAndSelectedDirectory()
    {
        var parentPath = TestPath("rename");
        var oldPath = Path.Combine(parentPath, "alpha");
        var oldChildPath = Path.Combine(oldPath, "child");
        var child = CreateDirectory("child", oldChildPath);
        var renamed = CreateDirectory("alpha", oldPath, child);
        var sibling = CreateDirectory("middle", Path.Combine(parentPath, "middle"));
        var parent = CreateDirectory("parent", parentPath, renamed, sibling);
        parent.SetFileManagerPanel(FileManagerPanel.Right);
        SetExpanded(renamed);
        SetExpanded(child);
        renamed.IsSelected = true;
        renamed.IsEditing = true;
        renamed.EditableName = " zulu ";
        _fileSystemService.RenameResult = "zulu";

        renamed.RenameFolderCommand.Execute(null);

        var newPath = Path.Combine(parentPath, "zulu");
        Assert.Multiple(() =>
        {
            Assert.That(renamed.Name, Is.EqualTo("zulu"));
            Assert.That(renamed.EditableName, Is.EqualTo("zulu"));
            Assert.That(renamed.Path, Is.EqualTo(newPath));
            Assert.That(child.Path, Is.EqualTo(Path.Combine(newPath, "child")));
            Assert.That(parent.ChildFileSystemItems, Is.EqualTo(new[] { sibling, renamed }));
            Assert.That(_folderService.RemovedVisitingPaths, Is.EquivalentTo(new[] { oldPath, oldChildPath }));
            Assert.That(_folderService.AddedVisitingPaths,
                Is.EquivalentTo(new[] { newPath, Path.Combine(newPath, "child") }));
            Assert.That(_folderService.SelectedDirectories.Single().Path, Is.EqualTo(newPath));
            Assert.That(_folderService.SelectedDirectories.Single().FileManagerPanel, Is.EqualTo(FileManagerPanel.Right));
            Assert.That(renamed.IsEditing, Is.False);
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Rename_InvalidNameRollsBackDisplayWithoutTouchingFileSystem(string? requestedName)
    {
        var directory = CreateDirectory("original", TestPath("invalid"));
        directory.IsEditing = true;
        directory.EditableName = requestedName;

        directory.RenameFolderCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(directory.Name, Is.EqualTo("original"));
            Assert.That(directory.EditableName, Is.EqualTo("original"));
            Assert.That(directory.IsEditing, Is.False);
            Assert.That(_fileSystemService.RenameCallCount, Is.Zero);
        });
    }

    [Test]
    public void Rename_SameNameEndsEditingWithoutTouchingFileSystem()
    {
        var directory = CreateDirectory("same", TestPath("same"));
        directory.IsEditing = true;
        directory.EditableName = "same";

        directory.RenameFolderCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(directory.EditableName, Is.EqualTo("same"));
            Assert.That(directory.IsEditing, Is.False);
            Assert.That(_fileSystemService.RenameCallCount, Is.Zero);
        });
    }

    [Test]
    public void Rename_FileSystemFailureRollsBackDisplayAndEndsEditing()
    {
        var directory = CreateDirectory("original", TestPath("failure"));
        directory.IsEditing = true;
        directory.EditableName = "requested";
        _fileSystemService.RenameException = new IOException("failed");

        directory.RenameFolderCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(directory.Name, Is.EqualTo("original"));
            Assert.That(directory.EditableName, Is.EqualTo("original"));
            Assert.That(directory.Path, Is.EqualTo(TestPath("failure")));
            Assert.That(directory.IsEditing, Is.False);
            Assert.That(_fileSystemService.RenameCallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Create_ExistingChildReusesIdentityAndStartsEditing()
    {
        var parentPath = TestPath("create-existing");
        var childPath = Path.Combine(parentPath, "New Folder");
        var existing = CreateDirectory("New Folder", childPath);
        var parent = CreateDirectory("parent", parentPath, existing);
        _folderService.CreateResult = new DirectoryModel("New Folder", childPath);

        parent.CreateFolderCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(parent.ChildFileSystemItems, Has.Count.EqualTo(1));
            Assert.That(parent.ChildFileSystemItems.Single(), Is.SameAs(existing));
            Assert.That(existing.IsEditing, Is.True);
        });
    }

    [Test]
    public void Create_NewChildAddsSortedNodePropagatesPanelAndStartsEditing()
    {
        var parentPath = TestPath("create-new");
        var sibling = CreateDirectory("zulu", Path.Combine(parentPath, "zulu"));
        var parent = CreateDirectory("parent", parentPath, sibling);
        parent.SetFileManagerPanel(FileManagerPanel.Left);
        var childPath = Path.Combine(parentPath, "alpha");
        _folderService.CreateResult = new DirectoryModel("alpha", childPath);

        parent.CreateFolderCommand.Execute(null);

        var created = parent.ChildFileSystemItems[0];
        Assert.Multiple(() =>
        {
            Assert.That(created.Path, Is.EqualTo(childPath));
            Assert.That(created.Parent, Is.SameAs(parent));
            Assert.That(created.FileManagerPanel, Is.EqualTo(FileManagerPanel.Left));
            Assert.That(created.IsEditing, Is.True);
            Assert.That(parent.ChildFileSystemItems[1], Is.SameAs(sibling));
            Assert.That(created.MutationCoordinator, Is.SameAs(_coordinator));
        });
    }

    [Test]
    public void CreateCommand_IsBlockedWhileNodeIsEditing()
    {
        var directory = CreateDirectory("parent", TestPath("create-guard"));
        directory.IsEditing = true;

        Assert.Multiple(() =>
        {
            Assert.That(directory.CreateFolderCommand.CanExecute(null), Is.False);
            Assert.That(_folderService.CreateCallCount, Is.Zero);
        });
    }

    [Test]
    public void Delete_AllowedExpandedSelectedNodeDetachesAndClearsSelection()
    {
        var parent = CreateDirectory("parent", TestPath("delete-parent"));
        var child = CreateDirectory("child", Path.Combine(parent.Path, "child"));
        parent.ChildFileSystemItems.Add(child);
        child.SetFileManagerPanel(FileManagerPanel.Right);
        child.IsSelected = true;
        SetExpanded(child);
        _fileSystemService.ExistingDirectories.Clear();

        child.DeleteFolderCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(_folderService.RemovedFolders.Single().Path, Is.EqualTo(child.Path));
            Assert.That(_folderService.RemovedVisitingPaths, Is.EqualTo(new[] { child.Path }));
            Assert.That(_folderService.SelectedDirectories.Single().Path, Is.EqualTo(DirectoryModel.Empty.Path));
            Assert.That(parent.ChildFileSystemItems, Is.Empty);
            Assert.That(child.Parent, Is.Null);
        });
    }

    [Test]
    public void Delete_MediaNodeIsBlockedByCommandAndCoordinatorGuard()
    {
        var directory = CreateDirectory("media", TestPath("delete-media"));
        directory.HasSupportedMedia = true;

        directory.DeleteFolderCommand.Execute(null);
        _coordinator.Delete(directory);

        Assert.Multiple(() =>
        {
            Assert.That(directory.DeleteFolderCommand.CanExecute(null), Is.False);
            Assert.That(_folderService.RemovedFolders, Is.Empty);
        });
    }

    [Test]
    public void Delete_WhenServiceRefusesRootLeavesNodeAndSelectionUntouched()
    {
        var root = CreateDirectory("root", Path.GetPathRoot(TestPath("root"))!);
        root.IsSelected = true;
        _folderService.RemoveException = new InvalidOperationException("root");

        root.DeleteFolderCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(root.Parent, Is.Null);
            Assert.That(root.IsSelected, Is.True);
            Assert.That(_folderService.SelectedDirectories, Is.Empty);
            Assert.That(_folderService.RemovedFolders, Is.Empty);
        });
    }

    [Test]
    public void Delete_WhenDirectoryStillExistsKeepsParentAndSelection()
    {
        var parent = CreateDirectory("parent", TestPath("delete-blocked"));
        var child = CreateDirectory("child", Path.Combine(parent.Path, "child"));
        parent.ChildFileSystemItems.Add(child);
        child.IsSelected = true;
        _fileSystemService.ExistingDirectories.Add(child.Path);

        child.DeleteFolderCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(parent.ChildFileSystemItems.Single(), Is.SameAs(child));
            Assert.That(child.Parent, Is.SameAs(parent));
            Assert.That(_folderService.SelectedDirectories, Is.Empty);
        });
    }

    [Test]
    public void RenameCommandThenLostFocus_RenamesOnlyOnce()
    {
        var directory = CreateDirectory("old", TestPath("command"));
        directory.StartRenameFolderCommand.Execute(null);
        directory.EditableName = "new";
        _fileSystemService.RenameResult = "new";

        directory.RenameFolderCommand.Execute(null);
        directory.NameTextBoxLostFocusCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(_fileSystemService.RenameCallCount, Is.EqualTo(1));
            Assert.That(directory.Name, Is.EqualTo("new"));
            Assert.That(directory.IsEditing, Is.False);
        });
    }

    [Test]
    public void LostFocus_ReentrancyDuringRenameDoesNotInvokeFileSystemTwice()
    {
        var directory = CreateDirectory("old", TestPath("reentrant-lost-focus"));
        directory.StartRenameFolderCommand.Execute(null);
        directory.EditableName = "new";
        _fileSystemService.RenameResult = "new";
        _fileSystemService.RenameCallback = () => directory.NameTextBoxLostFocusCommand.Execute(null);

        directory.NameTextBoxLostFocusCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(_fileSystemService.RenameCallCount, Is.EqualTo(1));
            Assert.That(directory.Name, Is.EqualTo("new"));
            Assert.That(directory.IsEditing, Is.False);
        });
    }

    [Test]
    public void LostFocus_WhileEditingCommitsRenameOnce()
    {
        var directory = CreateDirectory("old", TestPath("lost-focus"));
        directory.StartRenameFolderCommand.Execute(null);
        directory.EditableName = "new";
        _fileSystemService.RenameResult = "new";

        directory.NameTextBoxLostFocusCommand.Execute(null);
        directory.NameTextBoxLostFocusCommand.Execute(null);

        Assert.That(_fileSystemService.RenameCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task EnsureLoadedAsync_PreservesLazyLoadAndSharesCoordinatorWithMappedChildren()
    {
        var parentPath = TestPath("lazy");
        var childPath = Path.Combine(parentPath, "child");
        _folderService.DirectorySnapshots[parentPath] = CreateModel(
            "parent",
            parentPath,
            new DirectoryModel("child", childPath));
        var parent = CreateDirectory("parent", parentPath);
        parent.SetFileManagerPanel(FileManagerPanel.Left);

        await parent.EnsureLoadedAsync();

        var child = parent.ChildFileSystemItems.Single();
        Assert.Multiple(() =>
        {
            Assert.That(parent.LoadState, Is.EqualTo(DirectoryLoadState.Loaded));
            Assert.That(parent.IsExpanded, Is.True);
            Assert.That(child.Path, Is.EqualTo(childPath));
            Assert.That(child.Parent, Is.SameAs(parent));
            Assert.That(child.FileManagerPanel, Is.EqualTo(FileManagerPanel.Left));
            Assert.That(parent.MutationCoordinator, Is.SameAs(_coordinator));
            Assert.That(child.MutationCoordinator, Is.SameAs(_coordinator));
        });
    }

    private IMapper CreateMapper()
    {
        IMapper mapper = null!;
        var config = new TypeAdapterConfig();
        config.NewConfig<DirectoryModel, DirectoryViewModel>()
              .MapWith(source => MapModel(source, mapper));
        config.NewConfig<DirectoryViewModel, DirectoryModel>()
              .MapWith(source => new DirectoryModel(source.Name, source.Path));
        mapper = new Mapper(config);
        return mapper;
    }

    private DirectoryViewModel MapModel(DirectoryModel source, IMapper mapper)
    {
        var children = source.DirectoryModels.Select(child => MapModel(child, mapper)).ToArray();
        var result = new DirectoryViewModel(
            source.Name,
            source.Path,
            children,
            _folderService,
            _coordinator,
            mapper,
            _logger)
        {
            HasSupportedMedia = source.HasSupportedMedia
        };
        return result;
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
            _coordinator,
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

    private static string TestPath(string name) =>
        Path.Combine(Path.GetTempPath(), "ImageCareFolderMutationTests", name);

    private sealed class FakeFolderService : IFolderService
    {
        public Dictionary<string, DirectoryModel> DirectorySnapshots { get; } = [];
        public DirectoryModel? CreateResult { get; set; }
        public Exception? RemoveException { get; set; }
        public int CreateCallCount { get; private set; }
        public List<DirectoryModel> RemovedFolders { get; } = [];
        public List<string> AddedVisitingPaths { get; } = [];
        public List<string> RemovedVisitingPaths { get; } = [];
        public List<SelectedDirectory> SelectedDirectories { get; } = [];

        public IObservable<SelectedDirectory> FileSystemItemSelected => Observable.Never<SelectedDirectory>();
        public IObservable<SelectedDirectory> FolderVisited => Observable.Never<SelectedDirectory>();
        public IObservable<SelectedDirectory> FolderLeft => Observable.Never<SelectedDirectory>();

        public Task<DirectoryModel> GetDirectoryModelAsync(string directoryPath) =>
            Task.FromResult(DirectorySnapshots[directoryPath]);

        public DirectoryModel? CreateSubFolder(DirectoryModel directoryModel)
        {
            CreateCallCount++;
            return CreateResult;
        }

        public void RemoveFolder(DirectoryModel directoryModel)
        {
            if (RemoveException != null)
            {
                throw RemoveException;
            }

            RemovedFolders.Add(directoryModel);
        }

        public void AddVisitingFolder(DirectoryModel directoryModel, FileManagerPanel fileManagerPanel) =>
            AddedVisitingPaths.Add(directoryModel.Path);

        public void RemoveVisitingFolder(DirectoryModel directoryModel, FileManagerPanel fileManagerPanel) =>
            RemovedVisitingPaths.Add(directoryModel.Path);

        public void SetSelectedDirectory(SelectedDirectory selectedDirectory) =>
            SelectedDirectories.Add(selectedDirectory);

        public Task<DirectoryModel> GetDirectoryModelAsync(DirectoryModel? directoryModel = null) =>
            throw new NotSupportedException();
        public Task<IEnumerable<FileModel>> GetFileModelAsync(DirectoryModel directoryModel, string searchPattern) =>
            throw new NotSupportedException();
        public Task<IEnumerable<FileModel>> GetFileModelAsync(string directoryPath, string searchPattern) =>
            throw new NotSupportedException();
        public Task<DirectoryModel> GetCustomDirectoriesLevelAsync(DirectoryModel directoryModel, bool preview = false) =>
            throw new NotSupportedException();
        public DirectoryModel? GetSelectedDirectory(FileManagerPanel fileManagerPanel) => null;
    }

    private sealed class FakeFileSystemService : IFileSystemService
    {
        public string? RenameResult { get; set; }
        public Exception? RenameException { get; set; }
        public Action? RenameCallback { get; set; }
        public int RenameCallCount { get; private set; }
        public HashSet<string> ExistingDirectories { get; } = [];

        public string? RenameFolder(string? newName, string path)
        {
            RenameCallCount++;
            RenameCallback?.Invoke();
            if (RenameException != null)
            {
                throw RenameException;
            }

            return RenameResult;
        }

        public bool DirectoryExists(string path) => ExistingDirectories.Contains(path);
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
    }
}
