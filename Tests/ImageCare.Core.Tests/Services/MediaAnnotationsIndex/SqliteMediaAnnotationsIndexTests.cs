using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Services.MediaAnnotationsIndex;
using ImageCare.Core.Services.MediaAnnotationsService;
using Microsoft.Data.Sqlite;

namespace ImageCare.Core.Tests.Services.MediaAnnotationsIndex;

[TestFixture]
public sealed class SqliteMediaAnnotationsIndexTests
{
    private string _testDirectory = null!;
    private string _databasePath = null!;

    [SetUp]
    public void SetUp()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"imagecare-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
        _databasePath = Path.Combine(_testDirectory, "cache", "media-index.db");
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Test]
    public async Task InitializeAsync_CreatesSchemaConfiguresConnectionsAndReopens()
    {
        var storage = new FakeStorage();
        var index = CreateIndex(storage);

        await index.InitializeAsync();

        Assert.That(index.DatabasePath, Is.EqualTo(Path.GetFullPath(_databasePath)));
        var database = new SqliteMediaAnnotationsDatabase(_databasePath);
        await database.InitializeAsync();
        await using (var connection = await database.OpenConnectionAsync(CancellationToken.None))
        {
            Assert.That(await ExecuteScalarAsync<long>(connection,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('media', 'tags', 'media_tags');"),
                Is.EqualTo(3));
            Assert.That(await ExecuteScalarAsync<long>(connection, "PRAGMA foreign_keys;"), Is.EqualTo(1));
            Assert.That(await ExecuteScalarAsync<string>(connection, "PRAGMA journal_mode;"),
                Is.EqualTo("wal").IgnoreCase);
            Assert.That(await ExecuteScalarAsync<long>(connection, "PRAGMA busy_timeout;"), Is.EqualTo(5000));
        }

        var reopened = CreateIndex(storage);
        Assert.That(await reopened.GetAllTagsAsync(), Is.Empty);
    }

    [Test]
    public async Task InitializeAsync_WithCorruptDatabase_RecreatesSchema()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        await File.WriteAllBytesAsync(_databasePath, [0x49, 0x6d, 0x61, 0x67, 0x65, 0x43, 0x61, 0x72, 0x65]);
        var index = CreateIndex(new FakeStorage());

        await index.InitializeAsync();

        await index.UpsertAsync(CreateIndexed(Path.Combine(_testDirectory, "recovered.jpg"), 3, ["Recovered"]));
        Assert.That(await index.GetAllTagsAsync(), Is.EqualTo(new[] { "Recovered" }));
    }

    [Test]
    public async Task UpsertAndSearch_PreserveDirectoryRootRatingTextTagPagingAndOrderingSemantics()
    {
        var root = Directory.CreateDirectory(Path.Combine(_testDirectory, "photos")).FullName;
        var nested = Directory.CreateDirectory(Path.Combine(root, "nested")).FullName;
        var sibling = Directory.CreateDirectory(root + "-sibling").FullName;
        var upper = Path.Combine(root, "A.jpg");
        var lower = Path.Combine(root, "b.jpg");
        var literal = Path.Combine(root, "literal%name.jpg");
        var child = Path.Combine(nested, "d.jpg");
        var outside = Path.Combine(sibling, "outside.jpg");
        var index = CreateIndex(new FakeStorage());
        await index.UpsertAsync(CreateIndexed(lower, 5, ["Bird"]));
        await index.UpsertAsync(CreateIndexed(upper, 5, ["Bird", "Travel"]));
        await index.UpsertAsync(CreateIndexed(literal, 2, ["Travel"]));
        await index.UpsertAsync(CreateIndexed(child, 4, ["Bird", "Travel"]));
        await index.UpsertAsync(CreateIndexed(outside, 5, ["Outside"]));

        var directoryRows = await index.GetDirectoryAsync(root);
        var rootRows = await index.SearchAsync(new MediaAnnotationsSearchQuery(RootPath: root));
        var ratingRows = await index.SearchAsync(new MediaAnnotationsSearchQuery(
            RootPath: root,
            MinimumRating: 4,
            MaximumRating: 5));
        var literalRows = await index.SearchAsync(new MediaAnnotationsSearchQuery(Text: "LITERAL%NAME"));
        var allTagRows = await index.SearchAsync(new MediaAnnotationsSearchQuery(
            RootPath: root,
            Tags: ["bird", "travel"],
            MatchAllTags: true));
        var anyTagRows = await index.SearchAsync(new MediaAnnotationsSearchQuery(
            RootPath: root,
            Tags: ["bird", "travel"],
            MatchAllTags: false));
        var page = await index.SearchAsync(new MediaAnnotationsSearchQuery(RootPath: root, Offset: 1, Limit: 2));

        Assert.That(directoryRows.Select(row => row.MediaPath), Is.EqualTo(new[] { upper, lower, literal }));
        Assert.That(rootRows.Select(row => row.MediaPath), Is.EqualTo(new[] { upper, lower, child, literal }));
        Assert.That(ratingRows.Select(row => row.MediaPath), Is.EqualTo(new[] { upper, lower, child }));
        Assert.That(literalRows.Select(row => row.MediaPath), Is.EqualTo(new[] { literal }));
        Assert.That(allTagRows.Select(row => row.MediaPath), Is.EqualTo(new[] { upper, child }));
        Assert.That(anyTagRows.Select(row => row.MediaPath), Is.EqualTo(new[] { upper, lower, child, literal }));
        Assert.That(page.Select(row => row.MediaPath), Is.EqualTo(new[] { lower, child }));
    }

    [Test]
    public void SearchAsync_WithInvalidRatingsOrLimits_ThrowsBeforeQuerying()
    {
        var index = CreateIndex(new FakeStorage());

        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            index.SearchAsync(new MediaAnnotationsSearchQuery(MinimumRating: -1)));
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            index.SearchAsync(new MediaAnnotationsSearchQuery(MaximumRating: 6)));
        Assert.ThrowsAsync<ArgumentException>(() =>
            index.SearchAsync(new MediaAnnotationsSearchQuery(MinimumRating: 4, MaximumRating: 3)));
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            index.SearchAsync(new MediaAnnotationsSearchQuery(Offset: -1)));
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            index.SearchAsync(new MediaAnnotationsSearchQuery(Limit: 0)));
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            index.SearchAsync(new MediaAnnotationsSearchQuery(Limit: 1001)));
        Assert.That(File.Exists(_databasePath), Is.False);
    }

    [Test]
    public async Task MoveAndRemove_PreserveAnnotationsAndDeleteOrphanTags()
    {
        var oldPath = Path.Combine(_testDirectory, "old.jpg");
        var newDirectory = Directory.CreateDirectory(Path.Combine(_testDirectory, "moved")).FullName;
        var newPath = Path.Combine(newDirectory, "new.jpg");
        var otherPath = Path.Combine(_testDirectory, "other.jpg");
        await File.WriteAllTextAsync(newPath, "new contents");
        var index = CreateIndex(new FakeStorage());
        await index.UpsertAsync(CreateIndexed(oldPath, 4, ["Only old", "Shared"]));
        await index.UpsertAsync(CreateIndexed(otherPath, 2, ["Shared"]));

        await index.MoveAsync(oldPath, newPath);

        Assert.That(await index.GetDirectoryAsync(_testDirectory),
            Has.None.Matches<IndexedMediaAnnotations>(row => row.MediaPath == oldPath));
        var moved = (await index.GetDirectoryAsync(newDirectory)).Single();
        Assert.That(moved.MediaPath, Is.EqualTo(newPath));
        Assert.That(moved.Annotations.Rating, Is.EqualTo(4));
        Assert.That(moved.Annotations.Tags, Is.EqualTo(new[] { "Only old", "Shared" }));

        await index.RemoveAsync(newPath);
        Assert.That(await index.GetAllTagsAsync(), Is.EqualTo(new[] { "Shared" }));
        await index.RemoveAsync(otherPath);
        Assert.That(await index.GetAllTagsAsync(), Is.Empty);
    }

    [Test]
    public async Task ReconcileDirectoryAsync_WithUnchangedFile_DoesNotReadStorage()
    {
        var mediaPath = await CreateMediaFileAsync("unchanged.jpg");
        var snapshot = CreateSnapshot(mediaPath, 3, ["Existing"]);
        var storage = new FakeStorage(path => Task.FromResult(CreateSnapshot(path, 5, ["Changed"])));
        var index = CreateIndex(storage);
        await index.UpsertAsync(ToIndexed(snapshot));

        await index.ReconcileDirectoryAsync(_testDirectory);

        Assert.That(storage.ReadPaths, Is.Empty);
        Assert.That((await index.GetDirectoryAsync(_testDirectory)).Single().Annotations.Rating, Is.EqualTo(3));
    }

    [Test]
    public async Task ReconcileDirectoryAsync_WithReadFailureAndMissingFile_KeepsFailureAndPrunesMissing()
    {
        var failedPath = await CreateMediaFileAsync("failed.jpg");
        var missingPath = Path.Combine(_testDirectory, "missing.jpg");
        var storage = new FakeStorage(_ => Task.FromException<MediaAnnotationsSnapshot>(new IOException("read failed")));
        var index = CreateIndex(storage);
        await index.UpsertAsync(CreateIndexed(failedPath, 2, ["Keep"], mediaLength: -1));
        await index.UpsertAsync(CreateIndexed(missingPath, 1, ["Remove"]));

        await index.ReconcileDirectoryAsync(_testDirectory);

        var rows = await index.GetDirectoryAsync(_testDirectory);
        Assert.That(rows.Select(row => row.MediaPath), Is.EqualTo(new[] { failedPath }));
        Assert.That(rows[0].Annotations.Tags, Is.EqualTo(new[] { "Keep" }));
        Assert.That(await index.GetAllTagsAsync(), Is.EqualTo(new[] { "Keep" }));
    }

    [Test]
    public async Task RebuildAsync_WithUnavailableRoot_RetainsIndexedRows()
    {
        var unavailableRoot = Path.Combine(_testDirectory, "offline");
        var cachedPath = Path.Combine(unavailableRoot, "cached.jpg");
        var index = CreateIndex(new FakeStorage());
        await index.UpsertAsync(CreateIndexed(cachedPath, 4, ["Offline"]));

        await index.RebuildAsync([unavailableRoot]);

        Assert.That(await index.SearchAsync(new MediaAnnotationsSearchQuery(RootPath: unavailableRoot)),
            Has.One.Matches<IndexedMediaAnnotations>(row => row.MediaPath == cachedPath));
    }

    [Test]
    public async Task RebuildAsync_WhenDirectoryDisappearsDuringScan_DoesNotPruneRoot()
    {
        var root = Directory.CreateDirectory(Path.Combine(_testDirectory, "root")).FullName;
        var disappearingDirectory = Directory.CreateDirectory(Path.Combine(root, "disappearing")).FullName;
        var triggerPath = Path.Combine(root, "trigger.jpg");
        await File.WriteAllTextAsync(triggerPath, "trigger");
        var cachedPath = Path.Combine(root, "missing.jpg");
        var storage = new FakeStorage(path =>
        {
            if (path == triggerPath)
            {
                Directory.Delete(disappearingDirectory);
            }

            return Task.FromResult(CreateSnapshot(path, 1, []));
        });
        var index = CreateIndex(storage);
        await index.UpsertAsync(CreateIndexed(cachedPath, 3, ["Retained"]));

        await index.RebuildAsync([root], force: true);

        var rows = await index.SearchAsync(new MediaAnnotationsSearchQuery(RootPath: root));
        Assert.That(rows, Has.One.Matches<IndexedMediaAnnotations>(row => row.MediaPath == cachedPath));
    }

    [Test]
    public async Task RebuildAsync_SkipsReparsePointDirectories()
    {
        var root = Directory.CreateDirectory(Path.Combine(_testDirectory, "root")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(_testDirectory, "target")).FullName;
        var linkedMedia = Path.Combine(target, "linked.jpg");
        await File.WriteAllTextAsync(linkedMedia, "linked");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(root, "link"), target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Ignore($"Symbolic links are unavailable: {exception.Message}");
            return;
        }

        var storage = new FakeStorage(path => Task.FromResult(CreateSnapshot(path, 1, [])));
        var index = CreateIndex(storage);

        await index.RebuildAsync([root], force: true);

        Assert.That(storage.ReadPaths, Is.Empty);
    }

    [Test]
    public async Task RebuildAsync_ReportsRunningAndCompletedProgress()
    {
        var root = Directory.CreateDirectory(Path.Combine(_testDirectory, "progress")).FullName;
        await File.WriteAllTextAsync(Path.Combine(root, "a.jpg"), "a");
        await File.WriteAllTextAsync(Path.Combine(root, "b.jpg"), "b");
        var storage = new FakeStorage(path => Task.FromResult(CreateSnapshot(path, 2, ["Indexed"])));
        var progress = new RecordingProgress();
        var index = CreateIndex(storage);

        await index.RebuildAsync([root], progress, force: true);

        Assert.That(progress.Statuses.Count(status => status.State == AnnotationsReconciliationState.Running),
            Is.EqualTo(2));
        Assert.That(progress.Statuses[^1], Is.EqualTo(new AnnotationsReconciliationStatus(
            AnnotationsReconciliationState.Completed,
            null,
            2)));
    }

    [Test]
    public void RebuildAsync_WhenCancelled_DoesNotReportCompletion()
    {
        var root = Directory.CreateDirectory(Path.Combine(_testDirectory, "cancel")).FullName;
        var mediaPath = Path.Combine(root, "cancel.jpg");
        File.WriteAllText(mediaPath, "cancel");
        using var cancellation = new CancellationTokenSource();
        var storage = new FakeStorage(_ =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<MediaAnnotationsSnapshot>(cancellation.Token);
        });
        var progress = new RecordingProgress();
        var index = CreateIndex(storage);

        Assert.CatchAsync<OperationCanceledException>(() =>
            index.RebuildAsync([root], progress, cancellation.Token, force: true));
        Assert.That(progress.Statuses, Has.Some.Matches<AnnotationsReconciliationStatus>(
            status => status.State == AnnotationsReconciliationState.Running));
        Assert.That(progress.Statuses, Has.None.Matches<AnnotationsReconciliationStatus>(
            status => status.State == AnnotationsReconciliationState.Completed));
    }

    [Test]
    public async Task ReconcileDirectoryAsync_WhenFileIsCreatedAndUpsertedBeforePrune_RetainsRow()
    {
        var stalePath = Path.Combine(_testDirectory, "created-during-scan.jpg");
        var blockerPath = await CreateMediaFileAsync("blocker.jpg");
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new FakeStorage(async path =>
        {
            if (path == blockerPath)
            {
                readStarted.SetResult();
                await releaseRead.Task;
            }

            return CreateSnapshot(path, 1, ["Scanned"]);
        });
        var index = CreateIndex(storage);
        await index.UpsertAsync(CreateIndexed(stalePath, 2, ["Old"]));
        var reconcile = index.ReconcileDirectoryAsync(_testDirectory);
        await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await File.WriteAllTextAsync(stalePath, "created");
        await index.UpsertAsync(ToIndexed(CreateSnapshot(stalePath, 5, ["Concurrent"])));
        releaseRead.SetResult();
        await reconcile;

        var row = (await index.GetDirectoryAsync(_testDirectory)).Single(item => item.MediaPath == stalePath);
        Assert.That(row.Annotations.Rating, Is.EqualTo(5));
        Assert.That(row.Annotations.Tags, Is.EqualTo(new[] { "Concurrent" }));
    }

    private SqliteMediaAnnotationsIndex CreateIndex(IMediaAnnotationsStorageService storage) =>
        new(storage, _databasePath);

    private async Task<string> CreateMediaFileAsync(string fileName)
    {
        var path = Path.Combine(_testDirectory, fileName);
        await File.WriteAllTextAsync(path, fileName);
        return path;
    }

    private static IndexedMediaAnnotations CreateIndexed(
        string path,
        int rating,
        IEnumerable<string> tags,
        long mediaLength = 10) =>
        new(
            path,
            new MediaAnnotations(rating, tags),
            mediaLength,
            new DateTime(638900000000000000, DateTimeKind.Utc),
            5,
            new DateTime(638900000010000000, DateTimeKind.Utc),
            new DateTime(638900000020000000, DateTimeKind.Utc));

    private static MediaAnnotationsSnapshot CreateSnapshot(
        string mediaPath,
        int rating,
        IEnumerable<string> tags)
    {
        var media = new FileInfo(mediaPath);
        media.Refresh();
        var storagePath = XmpSidecarPath.GetExternalSidecarPath(mediaPath) ?? mediaPath;
        var annotationStorage = new FileInfo(storagePath);
        annotationStorage.Refresh();
        return new MediaAnnotationsSnapshot(
            mediaPath,
            new MediaAnnotations(rating, tags),
            new AnnotationFileState(
                storagePath,
                annotationStorage.Exists,
                annotationStorage.Exists ? annotationStorage.Length : 0,
                annotationStorage.Exists ? annotationStorage.LastWriteTimeUtc : DateTime.MinValue,
                media.Length,
                media.LastWriteTimeUtc));
    }

    private static IndexedMediaAnnotations ToIndexed(MediaAnnotationsSnapshot snapshot) =>
        new(
            snapshot.MediaPath,
            snapshot.Annotations,
            snapshot.FileState.MediaLength,
            snapshot.FileState.MediaModifiedUtc,
            snapshot.FileState.StorageExists ? snapshot.FileState.StorageLength : 0,
            snapshot.FileState.StorageExists ? snapshot.FileState.StorageModifiedUtc : DateTime.MinValue,
            DateTime.UtcNow);

    private static async Task<T> ExecuteScalarAsync<T>(SqliteConnection connection, string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return (T)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("No scalar value."));
    }

    private sealed class FakeStorage(
        Func<string, Task<MediaAnnotationsSnapshot>>? read = null) : IMediaAnnotationsStorageService
    {
        private readonly Func<string, Task<MediaAnnotationsSnapshot>> _read = read
            ?? (path => Task.FromResult(CreateSnapshot(path, 0, [])));
        private readonly List<string> _readPaths = [];

        internal IReadOnlyList<string> ReadPaths
        {
            get
            {
                lock (_readPaths)
                {
                    return _readPaths.ToArray();
                }
            }
        }

        public Task<MediaAnnotationsSnapshot> ReadAsync(
            string mediaPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_readPaths)
            {
                _readPaths.Add(mediaPath);
            }

            return _read(mediaPath);
        }

        public Task<MediaAnnotationsSnapshot> WriteAsync(
            string mediaPath,
            MediaAnnotations annotations,
            AnnotationFileState? expectedState = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingProgress : IProgress<AnnotationsReconciliationStatus>
    {
        internal List<AnnotationsReconciliationStatus> Statuses { get; } = [];

        public void Report(AnnotationsReconciliationStatus value) => Statuses.Add(value);
    }
}
