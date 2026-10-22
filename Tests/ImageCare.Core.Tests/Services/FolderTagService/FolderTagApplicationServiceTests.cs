using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.FolderTagService;
using ImageCare.Core.Services.MediaAnnotationsService;

using Moq;

using Serilog;

namespace ImageCare.Core.Tests.Services.FolderTagService;

[TestFixture]
public class FolderTagApplicationServiceTests
{
    private string _root = null!;
    private FolderTagsStore _store = null!;
    private InMemoryAnnotationsService _annotations = null!;
    private Configuration _configuration = null!;
    private FolderTagApplicationService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "ImageCareFolderTags", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var fileSystemService = new Mock<IFileSystemService>();
        fileSystemService.Setup(x => x.FileExists(It.IsAny<string>())).Returns<string>(File.Exists);
        fileSystemService.Setup(x => x.CreateDirectory(It.IsAny<string>()))
            .Callback<string>(path => Directory.CreateDirectory(path));
        fileSystemService.Setup(x => x.OpenRead(It.IsAny<string>()))
            .Returns<string>(path => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        fileSystemService.Setup(x => x.DeleteFile(It.IsAny<string>())).Callback<string>(File.Delete);

        var configurationService = new Mock<IConfigurationService>();
        _configuration = new Configuration { FolderTagApplicationLogEnabled = false };
        configurationService.SetupGet(x => x.Configuration).Returns(new Lazy<Configuration>(() => _configuration));

        _store = new FolderTagsStore(fileSystemService.Object);
        _annotations = new InMemoryAnnotationsService();
        var log = new FolderTagApplicationLog(Path.Combine(_root, "logs"));

        _service = new FolderTagApplicationService(
            _annotations,
            _store,
            fileSystemService.Object,
            configurationService.Object,
            log,
            new LoggerConfiguration().CreateLogger());
    }

    [TearDown]
    public async Task TearDown()
    {
        await _service.DisposeAsync();
        await _annotations.DisposeAsync();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public async Task ApplyToFileAsync_AddsFolderTags_AndWritesOnce()
    {
        var directory = CreateDirectory(@"Birds\Bee-eater");
        await _store.WriteAsync(directory, ["золотистая щурка", "птицы"]);
        var file = CreateFile(directory, "shot.cr3");

        var result = await _service.ApplyToFileAsync(file, "move");

        Assert.That(result.Outcome, Is.EqualTo(FolderTagApplicationOutcome.Updated));
        Assert.That(result.AddedTags, Is.EquivalentTo(new[] { "золотистая щурка", "птицы" }));
        Assert.That(_annotations.Snapshot(file).Annotations.Tags,
            Is.EquivalentTo(new[] { "золотистая щурка", "птицы" }));
        Assert.That(_annotations.WriteCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ApplyToFileAsync_IsIdempotent_WhenTagsAlreadyPresent()
    {
        var directory = CreateDirectory(@"Birds\Bee-eater");
        await _store.WriteAsync(directory, ["золотистая щурка"]);
        var file = CreateFile(directory, "shot.cr3");

        await _service.ApplyToFileAsync(file, "move");
        var writesAfterFirst = _annotations.WriteCount;

        var second = await _service.ApplyToFileAsync(file, "move");

        Assert.That(second.Outcome, Is.EqualTo(FolderTagApplicationOutcome.Skipped));
        Assert.That(second.AddedTags, Is.Empty);
        Assert.That(_annotations.WriteCount, Is.EqualTo(writesAfterFirst));
    }

    [Test]
    public async Task ApplyToFileAsync_PreservesExistingManualTagsAndRating()
    {
        var directory = CreateDirectory(@"Birds\Bee-eater");
        await _store.WriteAsync(directory, ["птицы"]);
        var file = CreateFile(directory, "shot.cr3");
        await _annotations.SaveAsync(file, new MediaAnnotations(4, ["ручной тег"]));

        var result = await _service.ApplyToFileAsync(file, "move");

        Assert.That(result.AddedTags, Is.EqualTo(new[] { "птицы" }));
        var annotations = _annotations.Snapshot(file).Annotations;
        Assert.That(annotations.Rating, Is.EqualTo(4));
        Assert.That(annotations.Tags, Is.EquivalentTo(new[] { "ручной тег", "птицы" }));
    }

    [Test]
    public async Task ApplyToFileAsync_Skips_WhenNoDescriptorExists()
    {
        var directory = CreateDirectory(@"Birds\NoTags");
        var file = CreateFile(directory, "shot.cr3");

        var result = await _service.ApplyToFileAsync(file, "move");

        Assert.That(result.Outcome, Is.EqualTo(FolderTagApplicationOutcome.Skipped));
        Assert.That(_annotations.WriteCount, Is.Zero);
    }

    [Test]
    public async Task ApplyToDirectoryAsync_ProcessesOnlyDirectChildren_NonRecursively()
    {
        var directory = CreateDirectory(@"Birds\Bee-eater");
        await _store.WriteAsync(directory, ["золотистая щурка"]);
        var first = CreateFile(directory, "a.cr3");
        var second = CreateFile(directory, "b.jpg");
        var nested = CreateFile(CreateDirectory(@"Birds\Bee-eater\2024-06"), "c.cr3");

        await _service.ApplyToDirectoryAsync(directory);

        Assert.That(_annotations.Snapshot(first).Annotations.Tags, Does.Contain("золотистая щурка"));
        Assert.That(_annotations.Snapshot(second).Annotations.Tags, Does.Contain("золотистая щурка"));
        Assert.That(_annotations.Contains(nested), Is.False);
    }

    [Test]
    public async Task ApplyToDirectoryAsync_DoesNotTreatFolderTagsFileAsMedia()
    {
        var directory = CreateDirectory(@"Birds\Bee-eater");
        await _store.WriteAsync(directory, ["золотистая щурка"]);
        CreateFile(directory, "a.cr3");

        await _service.ApplyToDirectoryAsync(directory);

        Assert.That(_annotations.Contains(Path.Combine(directory, FolderTagsFile.FileName)), Is.False);
    }

    [Test]
    public void GetEffectiveTags_UnionsAllAncestors_NearestFirst()
    {
        var leaf = CreateDirectory(@"Birds\Bee-eater\2024-06");
        _store.WriteAsync(_root, ["птицы"]).GetAwaiter().GetResult();
        _store.WriteAsync(CreateDirectory(@"Birds"), ["группа"]).GetAwaiter().GetResult();
        _store.WriteAsync(CreateDirectory(@"Birds\Bee-eater"), ["золотистая щурка"])
            .GetAwaiter().GetResult();

        var tags = _service.GetEffectiveTags(leaf);

        Assert.That(tags, Is.EqualTo(new[] { "золотистая щурка", "группа", "птицы" }));
    }

    private string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateFile(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, "stub");
        return path;
    }

    private sealed class InMemoryAnnotationsService : IMediaAnnotationsService
    {
        private readonly Dictionary<string, MediaAnnotations> _annotations =
            new(StringComparer.OrdinalIgnoreCase);

        public int WriteCount { get; private set; }

        public IObservable<AnnotationsReconciliationStatus> ReconciliationStatus =>
            System.Reactive.Linq.Observable.Return(AnnotationsReconciliationStatus.Idle);

        public bool Contains(string path) => _annotations.ContainsKey(Path.GetFullPath(path));

        public MediaAnnotationsSnapshot Snapshot(string path) =>
            new(Path.GetFullPath(path), _annotations.GetValueOrDefault(Path.GetFullPath(path), MediaAnnotations.Empty),
                new AnnotationFileState(path, false, 0, DateTime.MinValue, 0, DateTime.MinValue));

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<MediaAnnotations> GetAsync(string mediaPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(Snapshot(mediaPath).Annotations);

        public Task<MediaAnnotationsSnapshot> GetSnapshotAsync(
            string mediaPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Snapshot(mediaPath));

        public Task<MediaAnnotationsSnapshot> SaveAsync(
            string mediaPath,
            MediaAnnotations annotations,
            AnnotationFileState? expectedState = null,
            CancellationToken cancellationToken = default)
        {
            WriteCount++;
            _annotations[Path.GetFullPath(mediaPath)] = annotations;
            return Task.FromResult(Snapshot(mediaPath));
        }

        public Task<IReadOnlyList<IndexedMediaAnnotations>> GetDirectoryAsync(
            string directoryPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IndexedMediaAnnotations>>([]);

        public Task<IReadOnlyList<IndexedMediaAnnotations>> SearchAsync(
            MediaAnnotationsSearchQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IndexedMediaAnnotations>>([]);

        public Task<IReadOnlyList<string>> GetAllTagsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task RegisterDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RefreshDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RegisterRootsAsync(IEnumerable<string> roots, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RebuildRootsAsync(IEnumerable<string> roots, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ReconcileRegisteredRootsAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StartBackgroundReconciliationAsync(
            TimeSpan interval,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StopBackgroundReconciliationAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
