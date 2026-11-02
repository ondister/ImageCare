using System.Text.Json;

using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Services.MediaPreviewOperationsService;

namespace ImageCare.Core.Tests.Services.MediaPreviewOperationsService;

[TestFixture]
public class DeletionManifestStoreTests
{
    private string _temporaryDirectory;
    private Configuration _configuration;
    private DeletionManifestStore _store;

    [SetUp]
    public void SetUp()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"ImageCare-DeletionManifestStoreTests-{Guid.NewGuid():N}");
        _configuration = new Configuration
        {
            SafeDeletionDirectoryPath = _temporaryDirectory
        };
        _store = new DeletionManifestStore(() => _configuration);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    [Test]
    public void SaveAndLoad_PreserveManifestSchemaAndRemoveTemporaryFile()
    {
        var deletedFile = new DeletedMediaFile(
            "/photos/photo.jpg",
            "/buffer/photo.jpg",
            new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            "/photos/photo.jpg.xmp",
            "/buffer/photo.jpg.xmp",
            DeletedMediaFileState.PendingRestore,
            "/photos/photo (1).jpg",
            "/photos/photo (1).jpg.xmp");

        _store.Save([deletedFile]);
        var loaded = _store.Load();
        var manifestPath = Path.Combine(
            _temporaryDirectory,
            DeletionManifestStore.ManifestFileName);
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var record = document.RootElement[0];

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.EqualTo(new[] { deletedFile }));
            Assert.That(Path.GetFileName(_store.GetManifestPath()),
                Is.EqualTo("deletion-manifest.json"));
            Assert.That(record.GetProperty("OriginalPath").GetString(),
                Is.EqualTo(deletedFile.OriginalPath));
            Assert.That(record.GetProperty("BufferPath").GetString(),
                Is.EqualTo(deletedFile.BufferPath));
            Assert.That(record.GetProperty("State").GetInt32(),
                Is.EqualTo((int)DeletedMediaFileState.PendingRestore));
            Assert.That(record.GetProperty("RestorePath").GetString(),
                Is.EqualTo(deletedFile.RestorePath));
            Assert.That(File.Exists(manifestPath + ".tmp"), Is.False);
        });
    }

    [Test]
    public void Save_ReplacesExistingManifestAtomically()
    {
        var first = new DeletedMediaFile("first", "buffer-first", DateTime.UtcNow);
        var second = new DeletedMediaFile("second", "buffer-second", DateTime.UtcNow);

        _store.Save([first]);
        _store.Save([second]);

        Assert.Multiple(() =>
        {
            Assert.That(_store.Load(), Is.EqualTo(new[] { second }));
            Assert.That(File.Exists(_store.GetManifestPath() + ".tmp"), Is.False);
        });
    }

    [Test]
    public void Load_WhenManifestIsCorrupt_ReturnsEmptyAndLeavesManifestUntouched()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var manifestPath = _store.GetManifestPath();
        const string corruptContent = "{ definitely not json";
        File.WriteAllText(manifestPath, corruptContent);

        var loaded = _store.Load();

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Empty);
            Assert.That(File.ReadAllText(manifestPath), Is.EqualTo(corruptContent));
        });
    }

    [Test]
    public void Load_LegacyManifestWithoutJournalFieldsDefaultsToCommitted()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var manifestPath = _store.GetManifestPath();
        File.WriteAllText(
            manifestPath,
            """
            [
              {
                "OriginalPath": "/photos/legacy.jpg",
                "BufferPath": "/buffer/legacy.jpg",
                "DeletedAtUtc": "2025-01-02T03:04:05Z"
              }
            ]
            """);

        var loaded = _store.Load().Single();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.State, Is.EqualTo(DeletedMediaFileState.Committed));
            Assert.That(loaded.OriginalSidecarPath, Is.Null);
            Assert.That(loaded.RestorePath, Is.Null);
        });
    }

    [Test]
    public void Save_WhenReplaceFails_RemovesTemporaryFileAndPreservesFailure()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var manifestPath = _store.GetManifestPath();
        Directory.CreateDirectory(manifestPath);

        // File.Move reports a directory occupying the destination as IOException on Unix
        // and UnauthorizedAccessException on Windows.
        var exception = Assert.Catch<Exception>(
            () => _store.Save([
                new DeletedMediaFile("source", "buffer", DateTime.UtcNow)
            ]));

        Assert.Multiple(() =>
        {
            Assert.That(
                exception,
                Is.TypeOf<IOException>().Or.TypeOf<UnauthorizedAccessException>());
            Assert.That(Directory.Exists(manifestPath), Is.True);
            Assert.That(File.Exists(manifestPath + ".tmp"), Is.False);
        });
    }
}
