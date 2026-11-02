using System.Collections;
using System.Reactive.Linq;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.FolderService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.ViewModels.Domain;
using ImageCare.UI.Avalonia.ViewModels.Folders;

using Mapster;
using MapsterMapper;

using Serilog;

namespace ImageCare.UI.Avalonia.Tests.ViewModels.Folders;

[TestFixture]
public sealed class FolderSearchViewModelTests
{
    private FakeFileSystemService _fileSystemService = null!;
    private FakeFolderService _folderService = null!;
    private IMapper _mapper = null!;
    private FolderSearchViewModel _viewModel = null!;
    private DirectoryViewModel? _openedResult;

    [SetUp]
    public void SetUp()
    {
        _fileSystemService = new FakeFileSystemService();
        _folderService = new FakeFolderService();
        _mapper = CreateMapper();
        _viewModel = new FolderSearchViewModel(
            _fileSystemService,
            _mapper,
            LocalizationManager.Instance,
            new ImmediateSynchronizationContext(),
            result =>
            {
                _openedResult = result;
                return Task.CompletedTask;
            });
    }

    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("  ", false)]
    [TestCase("ab", false)]
    [TestCase("abc", true)]
    [TestCase(" a ", true)]
    public void PerformSearchCommand_RequiresAtLeastThreeCharacters(string? searchText, bool expected)
    {
        _viewModel.SearchText = searchText;

        Assert.That(_viewModel.PerformSearchCommand.CanExecute(), Is.EqualTo(expected));
    }

    [Test]
    public async Task PerformSearchCommand_IsDisabledWhileHostLoadsOrSearchRuns()
    {
        _viewModel.SearchText = "trip";
        _viewModel.SetSelectedFolder(CreateDirectory("Selected", "/photos"));
        _viewModel.SetHostLoading(true);

        Assert.That(_viewModel.PerformSearchCommand.CanExecute(), Is.False);

        _viewModel.SetHostLoading(false);
        var blockingEnumeration = new BlockingEnumerable("/photos/trip");
        _fileSystemService.Enumeration = blockingEnumeration;
        var searchTask = _viewModel.PerformSearchCommand.ExecuteAsync();
        await blockingEnumeration.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.IsSearching, Is.True);
            Assert.That(_viewModel.PerformSearchCommand.CanExecute(), Is.False);
        });

        _viewModel.ClearSearchCommand.Execute();
        blockingEnumeration.Release();
        await searchTask;
    }

    [Test]
    public async Task PerformSearchCommand_SearchesBelowSelectedFolderAndReportsSuccess()
    {
        var selectedPath = Path.Combine(Path.GetTempPath(), "ImageCare", "Selected");
        var firstResult = Path.Combine(selectedPath, "trip-one");
        var secondResult = Path.Combine(selectedPath, "trip-two");
        _viewModel.SetSelectedFolder(CreateDirectory("Selected", selectedPath));
        _viewModel.SearchText = "trip";
        _fileSystemService.Enumeration = new[] { firstResult, secondResult };

        await _viewModel.PerformSearchCommand.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_fileSystemService.RequestedDirectory, Is.EqualTo(selectedPath));
            Assert.That(_fileSystemService.RequestedSearchPattern, Is.EqualTo("*trip*"));
            Assert.That(_fileSystemService.RequestedSearchOption, Is.EqualTo(SearchOption.AllDirectories));
            Assert.That(_viewModel.SearchResults.Select(result => result.Path),
                Is.EqualTo(new[] { firstResult, secondResult }));
            Assert.That(_viewModel.SearchHeader,
                Is.EqualTo(LocalizationManager.Instance.Get("Folders_SearchFound", 2, "trip")));
            Assert.That(_viewModel.IsSearching, Is.False);
            Assert.That(_viewModel.IsInSearchSession, Is.True);
        });
    }

    [Test]
    public async Task PerformSearchCommand_WhenEnumerationFails_ReportsFailureAndKeepsSession()
    {
        _viewModel.SetSelectedFolder(CreateDirectory("Selected", "/photos"));
        _viewModel.SearchText = "trip";
        _fileSystemService.Enumeration = ThrowingEnumeration();

        await _viewModel.PerformSearchCommand.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.SearchHeader,
                Is.EqualTo(LocalizationManager.Instance["Folders_SearchFailed"]));
            Assert.That(_viewModel.IsSearching, Is.False);
            Assert.That(_viewModel.IsInSearchSession, Is.True);
            Assert.That(_viewModel.SearchResults, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task ClearSearchCommand_CancelsActiveSearchAndClearsSession()
    {
        _viewModel.SetSelectedFolder(CreateDirectory("Selected", "/photos"));
        _viewModel.SearchText = "trip";
        var blockingEnumeration = new BlockingEnumerable("/photos/trip");
        _fileSystemService.Enumeration = blockingEnumeration;
        var searchTask = _viewModel.PerformSearchCommand.ExecuteAsync();
        await blockingEnumeration.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        _viewModel.ClearSearchCommand.Execute();
        blockingEnumeration.Release();
        await searchTask;

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.SearchHeader,
                Is.EqualTo(LocalizationManager.Instance["Folders_SearchCancelled"]));
            Assert.That(_viewModel.SearchText, Is.Null);
            Assert.That(_viewModel.SearchResults, Is.Empty);
            Assert.That(_viewModel.IsSearching, Is.False);
            Assert.That(_viewModel.IsInSearchSession, Is.False);
            Assert.That(_viewModel.ClearSearchCommand.CanExecute(), Is.False);
        });
    }

    [Test]
    public async Task ClearSearchCommand_AfterSuccess_ClearsSessionState()
    {
        _viewModel.SetSelectedFolder(CreateDirectory("Selected", "/photos"));
        _viewModel.SearchText = "trip";
        _fileSystemService.Enumeration = new[] { "/photos/trip" };
        await _viewModel.PerformSearchCommand.ExecuteAsync();

        _viewModel.ClearSearchCommand.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.SearchText, Is.Null);
            Assert.That(_viewModel.SearchResults, Is.Empty);
            Assert.That(_viewModel.IsSearching, Is.False);
            Assert.That(_viewModel.IsInSearchSession, Is.False);
        });
    }

    [Test]
    public async Task SetSearchResultCommand_InvokesNavigationCallbackWithSelectedResult()
    {
        var result = CreateDirectory("trip", "/photos/trip");

        await _viewModel.SetSearchResultCommand.ExecuteAsync(result);

        Assert.That(_openedResult, Is.SameAs(result));
    }

    [Test]
    public void SetSelectedFolder_UpdatesAvailabilityAndLocalizedTarget()
    {
        var selected = CreateDirectory("Selected", "/photos");

        _viewModel.SetSelectedFolder(selected);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.IsSearchEnabled, Is.True);
            Assert.That(_viewModel.SearchTarget,
                Is.EqualTo(LocalizationManager.Instance.Get("Folders_SearchFoldersIn", "Selected")));
        });
    }

    private IMapper CreateMapper()
    {
        IMapper mapper = null!;
        var logger = new LoggerConfiguration().CreateLogger();
        var mutationCoordinator = new FolderMutationCoordinator(_folderService, _fileSystemService, logger);
        var config = new TypeAdapterConfig();
        config.NewConfig<DirectoryModel, DirectoryViewModel>()
            .MapWith(source => new DirectoryViewModel(
                source.Name,
                source.Path,
                Array.Empty<DirectoryViewModel>(),
                _folderService,
                mutationCoordinator,
                mapper,
                logger));
        mapper = new Mapper(config);
        return mapper;
    }

    private DirectoryViewModel CreateDirectory(string name, string path)
    {
        return _mapper.Map<DirectoryViewModel>(new DirectoryModel(name, path));
    }

    private static IEnumerable<string> ThrowingEnumeration()
    {
        yield return "/photos/partial-trip";
        throw new IOException("Enumeration failed");
    }

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Send(SendOrPostCallback callback, object? state)
        {
            callback(state);
        }
    }

    private sealed class BlockingEnumerable(string result) : IEnumerable<string>
    {
        private readonly ManualResetEventSlim _release = new(false);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IEnumerator<string> GetEnumerator()
        {
            Started.TrySetResult();
            _release.Wait(TimeSpan.FromSeconds(5));
            yield return result;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Release()
        {
            _release.Set();
        }
    }

    private sealed class FakeFileSystemService : IFileSystemService
    {
        public IEnumerable<string> Enumeration { get; set; } = [];

        public string? RequestedDirectory { get; private set; }

        public string? RequestedSearchPattern { get; private set; }

        public SearchOption? RequestedSearchOption { get; private set; }

        public IEnumerable<string> EnumerateDirectories(
            string directory,
            string searchPattern,
            SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            RequestedDirectory = directory;
            RequestedSearchPattern = searchPattern;
            RequestedSearchOption = searchOption;
            return Enumeration;
        }

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
