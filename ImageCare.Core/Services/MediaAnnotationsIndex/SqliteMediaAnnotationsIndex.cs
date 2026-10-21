using System.Text;
using System.Xml;
using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Services.MediaAnnotationsService;
using Microsoft.Data.Sqlite;

namespace ImageCare.Core.Services.MediaAnnotationsIndex;

public sealed class SqliteMediaAnnotationsIndex : IMediaAnnotationsIndex
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private static readonly string[] ImageExtensions = MediaFormat.GetSupportedExtensions()
        .Where(extension => MediaFormat.GetMediaFormatByExtension(extension)?.MediaType == MediaType.Image)
        .Select(extension => extension.ToLowerInvariant())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private readonly IMediaAnnotationsStorageService _storage;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _initializationGate = new();
    private Task? _initializationTask;

    public SqliteMediaAnnotationsIndex(IMediaAnnotationsStorageService storage, string? databasePath = null)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        DatabasePath = Path.GetFullPath(databasePath ?? GetDefaultDatabasePath());
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Task initialization;
        lock (_initializationGate)
        {
            _initializationTask ??= InitializeCoreAsync();
            initialization = _initializationTask;
        }

        try
        {
            await initialization.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (initialization.IsCanceled || initialization.IsFaulted)
            {
                lock (_initializationGate)
                {
                    if (ReferenceEquals(_initializationTask, initialization))
                    {
                        _initializationTask = null;
                    }
                }
            }

            throw;
        }
    }

    public async Task UpsertAsync(
        IndexedMediaAnnotations media,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(media);
        var mediaPath = NormalizePath(media.MediaPath);
        EnsureImagePath(mediaPath);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();
            await UpsertCoreAsync(connection, transaction, media with { MediaPath = mediaPath }, cancellationToken)
                .ConfigureAwait(false);
            transaction.Commit();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<IReadOnlyList<IndexedMediaAnnotations>> GetDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        var directory = NormalizePath(directoryPath);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT m.id, m.path, m.rating, m.media_length, m.media_mtime_utc,
                   m.annotation_length, m.annotation_mtime_utc, m.indexed_utc, t.name
            FROM media AS m
            LEFT JOIN media_tags AS mt ON mt.media_id = m.id
            LEFT JOIN tags AS t ON t.id = mt.tag_id
            WHERE m.directory_key = @directory
              AND m.extension IN ({AddImageExtensionParameters(command)})
            ORDER BY m.file_name COLLATE NOCASE, m.path, t.normalized_name;
            """;
        command.Parameters.AddWithValue("@directory", GetPathKey(directory));
        return await ReadAnnotationsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IndexedMediaAnnotations>> SearchAsync(
        MediaAnnotationsSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.MinimumRating is < 0 or > 5 || query.MaximumRating is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Ratings must be between 0 and 5.");
        }

        if (query.MinimumRating > query.MaximumRating)
        {
            throw new ArgumentException("MinimumRating cannot exceed MaximumRating.", nameof(query));
        }

        if (query.Offset < 0 || query.Limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Offset must be non-negative and limit must be 1..1000.");
        }

        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var conditions = new List<string>
        {
            $"m.extension IN ({AddImageExtensionParameters(command)})"
        };

        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            conditions.Add("(LOWER(m.file_name) LIKE LOWER(@text) ESCAPE '\\' OR LOWER(m.path) LIKE LOWER(@text) ESCAPE '\\')");
            command.Parameters.AddWithValue("@text", $"%{EscapeLike(query.Text.Trim())}%");
        }

        if (!string.IsNullOrWhiteSpace(query.RootPath))
        {
            var rootKey = GetPathKey(NormalizePath(query.RootPath));
            conditions.Add("(m.path_key = @root OR m.path_key LIKE @rootPrefix ESCAPE '\\')");
            command.Parameters.AddWithValue("@root", rootKey);
            var rootPrefix = Path.TrimEndingDirectorySeparator(rootKey) + Path.DirectorySeparatorChar;
            command.Parameters.AddWithValue("@rootPrefix", EscapeLike(rootPrefix) + "%");
        }

        if (query.MinimumRating is not null)
        {
            conditions.Add("m.rating >= @minimumRating");
            command.Parameters.AddWithValue("@minimumRating", query.MinimumRating.Value);
        }

        if (query.MaximumRating is not null)
        {
            conditions.Add("m.rating <= @maximumRating");
            command.Parameters.AddWithValue("@maximumRating", query.MaximumRating.Value);
        }

        var tagKeys = (query.Tags ?? [])
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(MediaAnnotations.GetTagKey)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (tagKeys.Length > 0)
        {
            var parameters = new string[tagKeys.Length];
            for (var index = 0; index < tagKeys.Length; index++)
            {
                parameters[index] = $"@tag{index}";
                command.Parameters.AddWithValue(parameters[index], tagKeys[index]);
            }

            var matchCount = query.MatchAllTags ? " = @tagCount" : " > 0";
            conditions.Add($"""
                (SELECT COUNT(DISTINCT filter_tags.normalized_name)
                 FROM media_tags AS filter_mt
                 JOIN tags AS filter_tags ON filter_tags.id = filter_mt.tag_id
                 WHERE filter_mt.media_id = m.id
                   AND filter_tags.normalized_name IN ({string.Join(", ", parameters)})){matchCount}
                """);
            if (query.MatchAllTags)
            {
                command.Parameters.AddWithValue("@tagCount", tagKeys.Length);
            }
        }

        command.Parameters.AddWithValue("@limit", query.Limit);
        command.Parameters.AddWithValue("@offset", query.Offset);
        command.CommandText = $"""
            WITH selected_media AS (
                SELECT m.id
                FROM media AS m
                WHERE {string.Join(" AND ", conditions)}
                ORDER BY m.rating DESC, m.file_name COLLATE NOCASE, m.path
                LIMIT @limit OFFSET @offset
            )
            SELECT m.id, m.path, m.rating, m.media_length, m.media_mtime_utc,
                   m.annotation_length, m.annotation_mtime_utc, m.indexed_utc, t.name
            FROM selected_media AS selected
            JOIN media AS m ON m.id = selected.id
            LEFT JOIN media_tags AS mt ON mt.media_id = m.id
            LEFT JOIN tags AS t ON t.id = mt.tag_id
            ORDER BY m.rating DESC, m.file_name COLLATE NOCASE, m.path, t.normalized_name;
            """;
        return await ReadAnnotationsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> GetAllTagsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM tags ORDER BY normalized_name, name;";
        var tags = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            tags.Add(reader.GetString(0));
        }

        return tags;
    }

    public async Task RemoveAsync(string mediaPath, CancellationToken cancellationToken = default)
    {
        var pathKey = GetPathKey(NormalizePath(mediaPath));
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM media WHERE path_key = @path;";
            command.Parameters.AddWithValue("@path", pathKey);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await DeleteOrphanTagsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            transaction.Commit();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task RemoveIfStillMissingAsync(
        string mediaPath,
        CancellationToken cancellationToken)
    {
        var normalizedPath = NormalizePath(mediaPath);
        var pathKey = GetPathKey(normalizedPath);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Hold the mutation gate across the filesystem recheck and SQL delete. If an
            // operation upsert won the gate, its already-created file prevents pruning; if it
            // follows us, that upsert recreates the row after this stale row is removed.
            if (File.Exists(normalizedPath))
            {
                return;
            }

            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM media WHERE path_key = @path;";
            command.Parameters.AddWithValue("@path", pathKey);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await DeleteOrphanTagsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            transaction.Commit();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task MoveAsync(
        string oldMediaPath,
        string newMediaPath,
        CancellationToken cancellationToken = default)
    {
        var oldPath = NormalizePath(oldMediaPath);
        var newPath = NormalizePath(newMediaPath);
        EnsureImagePath(newPath);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = new FileInfo(newPath);
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE media
                SET path = @newPath,
                    path_key = @newPathKey,
                    directory_path = @directoryPath,
                    directory_key = @directoryKey,
                    file_name = @fileName,
                    extension = @extension,
                    media_length = CASE WHEN @exists = 1 THEN @length ELSE media_length END,
                    media_mtime_utc = CASE WHEN @exists = 1 THEN @mtime ELSE media_mtime_utc END,
                    indexed_utc = @indexed
                WHERE path_key = @oldPathKey;
                """;
            AddPathParameters(command, newPath);
            command.Parameters.AddWithValue("@oldPathKey", GetPathKey(oldPath));
            command.Parameters.AddWithValue("@exists", file.Exists ? 1 : 0);
            command.Parameters.AddWithValue("@length", file.Exists ? file.Length : 0);
            command.Parameters.AddWithValue("@mtime", file.Exists ? file.LastWriteTimeUtc.Ticks : 0);
            command.Parameters.AddWithValue("@indexed", DateTime.UtcNow.Ticks);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task ReconcileDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        var directory = NormalizePath(directoryPath);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!Directory.Exists(directory))
        {
            return;
        }

        var existing = (await GetDirectoryAsync(directory, cancellationToken).ConfigureAwait(false))
            .ToDictionary(item => NormalizePath(item.MediaPath), PathComparer);
        var seen = new HashSet<string>(PathComparer);
        try
        {
            foreach (var mediaPath in Directory.EnumerateFiles(directory).Where(IsImagePath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var normalizedPath = NormalizePath(mediaPath);
                seen.Add(normalizedPath);
                try
                {
                    if (existing.TryGetValue(normalizedPath, out var indexed)
                        && IsSourceStateUnchanged(normalizedPath, indexed))
                    {
                        continue;
                    }

                    var snapshot = await _storage.ReadAsync(mediaPath, cancellationToken).ConfigureAwait(false);
                    await UpsertAsync(ToIndexed(snapshot), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
                {
                    // Keep an existing row for a file that was enumerated but could not be read.
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        await RemoveMissingInDirectoryAsync(directory, seen, cancellationToken).ConfigureAwait(false);
    }

    public async Task RebuildAsync(
        IEnumerable<string> roots,
        IProgress<AnnotationsReconciliationStatus>? progress = null,
        CancellationToken cancellationToken = default,
        bool force = false)
    {
        ArgumentNullException.ThrowIfNull(roots);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        var processed = 0;
        foreach (var rootValue in roots.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = NormalizePath(rootValue);
            if (!Directory.Exists(root))
            {
                // Unavailable roots retain their disposable cache rows.
                continue;
            }

            var existing = await GetExistingUnderRootAsync(root, cancellationToken).ConfigureAwait(false);
            var seen = new HashSet<string>(PathComparer);
            var scanComplete = true;
            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(root);
            while (pendingDirectories.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = pendingDirectories.Pop();
                string[] files;
                string[] directories;
                try
                {
                    files = Directory.GetFiles(directory);
                    directories = Directory.GetDirectories(directory);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    scanComplete = false;
                    continue;
                }

                foreach (var child in directories)
                {
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                        {
                            pendingDirectories.Push(child);
                        }
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        scanComplete = false;
                    }
                }

                foreach (var mediaPath in files.Where(IsImagePath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var normalizedPath = NormalizePath(mediaPath);
                    seen.Add(normalizedPath);
                    progress?.Report(new AnnotationsReconciliationStatus(
                        AnnotationsReconciliationState.Running,
                        mediaPath,
                        processed));
                    try
                    {
                        if (!force && existing.TryGetValue(normalizedPath, out var indexed)
                            && IsSourceStateUnchanged(normalizedPath, indexed))
                        {
                            continue;
                        }

                        var snapshot = await _storage.ReadAsync(mediaPath, cancellationToken).ConfigureAwait(false);
                        await UpsertAsync(ToIndexed(snapshot), cancellationToken).ConfigureAwait(false);
                        processed++;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException or InvalidDataException)
                    {
                        // A per-file failure does not abort the root or remove its existing row.
                    }
                }
            }

            if (scanComplete)
            {
                await RemoveMissingUnderRootAsync(root, seen, cancellationToken).ConfigureAwait(false);
            }
        }

        progress?.Report(new AnnotationsReconciliationStatus(
            AnnotationsReconciliationState.Completed,
            null,
            processed));
    }

    private async Task<Dictionary<string, IndexedMediaAnnotations>> GetExistingUnderRootAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, IndexedMediaAnnotations>(PathComparer);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT path, media_length, media_mtime_utc, annotation_length,
                   annotation_mtime_utc, indexed_utc
            FROM media;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var path = reader.GetString(0);
            if (!IsUnderRoot(path, root))
            {
                continue;
            }

            result[NormalizePath(path)] = new IndexedMediaAnnotations(
                path,
                MediaAnnotations.Empty,
                reader.GetInt64(1),
                ReadUtc(reader.GetInt64(2)),
                reader.GetInt64(3),
                ReadUtc(reader.GetInt64(4)),
                ReadUtc(reader.GetInt64(5)));
        }

        return result;
    }

    private static bool IsSourceStateUnchanged(string mediaPath, IndexedMediaAnnotations indexed)
    {
        XmpSidecarPath.EnsureUnambiguousRawOwner(mediaPath);
        var media = new FileInfo(mediaPath);
        media.Refresh();
        if (!media.Exists
            || media.Length != indexed.MediaLength
            || media.LastWriteTimeUtc != indexed.MediaModifiedUtc)
        {
            return false;
        }

        var storagePath = XmpSidecarPath.GetExternalSidecarPath(mediaPath) ?? mediaPath;
        var storage = new FileInfo(storagePath);
        storage.Refresh();
        var storageLength = storage.Exists ? storage.Length : 0;
        var storageModifiedUtc = storage.Exists ? storage.LastWriteTimeUtc : DateTime.MinValue;
        return storageLength == indexed.AnnotationStorageLength
            && storageModifiedUtc == indexed.AnnotationModifiedUtc;
    }

    private static IndexedMediaAnnotations ToIndexed(MediaAnnotationsSnapshot snapshot) =>
        new(
            snapshot.MediaPath,
            snapshot.Annotations,
            snapshot.FileState.MediaLength,
            snapshot.FileState.MediaModifiedUtc,
            snapshot.FileState.StorageExists ? snapshot.FileState.StorageLength : 0,
            snapshot.FileState.StorageExists
                ? snapshot.FileState.StorageModifiedUtc
                : DateTime.MinValue,
            DateTime.UtcNow);

    private async Task InitializeCoreAsync()
    {
        var directory = Path.GetDirectoryName(DatabasePath)
            ?? throw new InvalidOperationException($"Cannot determine a directory for {DatabasePath}.");
        Directory.CreateDirectory(directory);

        try
        {
            await CreateSchemaAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (IsRecoverableDatabaseFailure(exception))
        {
            SqliteConnection.ClearAllPools();
            DeleteDatabaseFiles();
            await CreateSchemaAsync().ConfigureAwait(false);
        }
    }

    private async Task CreateSchemaAsync()
    {
        await using var connection = await OpenConnectionAsync(CancellationToken.None, configure: false).ConfigureAwait(false);
        await ConfigureConnectionAsync(connection, CancellationToken.None).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS media (
                id INTEGER PRIMARY KEY,
                path TEXT NOT NULL,
                path_key TEXT NOT NULL UNIQUE,
                directory_path TEXT NOT NULL,
                directory_key TEXT NOT NULL,
                file_name TEXT NOT NULL,
                extension TEXT NOT NULL,
                rating INTEGER NOT NULL CHECK (rating BETWEEN 0 AND 5),
                media_length INTEGER NOT NULL,
                media_mtime_utc INTEGER NOT NULL,
                annotation_length INTEGER NOT NULL,
                annotation_mtime_utc INTEGER NOT NULL,
                indexed_utc INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS tags (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                normalized_name TEXT NOT NULL UNIQUE
            );
            CREATE TABLE IF NOT EXISTS media_tags (
                media_id INTEGER NOT NULL REFERENCES media(id) ON DELETE CASCADE,
                tag_id INTEGER NOT NULL REFERENCES tags(id) ON DELETE CASCADE,
                PRIMARY KEY (media_id, tag_id)
            );
            CREATE INDEX IF NOT EXISTS ix_media_rating ON media(rating);
            CREATE INDEX IF NOT EXISTS ix_media_directory ON media(directory_key);
            CREATE INDEX IF NOT EXISTS ix_media_path ON media(path_key);
            CREATE INDEX IF NOT EXISTS ix_tags_name ON tags(normalized_name);
            CREATE INDEX IF NOT EXISTS ix_media_tags_tag ON media_tags(tag_id, media_id);
            SELECT path, path_key, directory_path, directory_key, file_name, extension,
                   rating, media_length, media_mtime_utc, annotation_length, annotation_mtime_utc, indexed_utc
            FROM media LIMIT 0;
            SELECT name, normalized_name FROM tags LIMIT 0;
            SELECT media_id, tag_id FROM media_tags LIMIT 0;
            """;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);

        await using var integrityCommand = connection.CreateCommand();
        integrityCommand.CommandText = "PRAGMA quick_check;";
        var integrityResult = await integrityCommand.ExecuteScalarAsync().ConfigureAwait(false) as string;
        if (!string.Equals(integrityResult, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The media index integrity check failed: {integrityResult}");
        }
    }

    private static bool IsRecoverableDatabaseFailure(Exception exception)
    {
        if (exception is InvalidDataException)
        {
            return true;
        }

        if (exception is not SqliteException sqliteException)
        {
            return false;
        }

        if (sqliteException.SqliteErrorCode is 11 or 17 or 26)
        {
            return true;
        }

        return sqliteException.SqliteErrorCode == 1 &&
               (sqliteException.Message.Contains("schema", StringComparison.OrdinalIgnoreCase) ||
                sqliteException.Message.Contains("malformed", StringComparison.OrdinalIgnoreCase) ||
                sqliteException.Message.Contains("no such column", StringComparison.OrdinalIgnoreCase) ||
                sqliteException.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase));
    }

    private void DeleteDatabaseFiles()
    {
        foreach (var path in new[] { DatabasePath, DatabasePath + "-wal", DatabasePath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(
        CancellationToken cancellationToken,
        bool configure = true)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (configure)
        {
            await ConfigureConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        }

        return connection;
    }

    private static async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = WAL;
            PRAGMA busy_timeout = 5000;
            PRAGMA case_sensitive_like = ON;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertCoreAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IndexedMediaAnnotations media,
        CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO media (
                    path, path_key, directory_path, directory_key, file_name, extension,
                    rating, media_length, media_mtime_utc, annotation_length, annotation_mtime_utc, indexed_utc)
                VALUES (
                    @newPath, @newPathKey, @directoryPath, @directoryKey, @fileName, @extension,
                    @rating, @mediaLength, @mediaMtime, @annotationLength, @annotationMtime, @indexed)
                ON CONFLICT(path_key) DO UPDATE SET
                    path = excluded.path,
                    directory_path = excluded.directory_path,
                    directory_key = excluded.directory_key,
                    file_name = excluded.file_name,
                    extension = excluded.extension,
                    rating = excluded.rating,
                    media_length = excluded.media_length,
                    media_mtime_utc = excluded.media_mtime_utc,
                    annotation_length = excluded.annotation_length,
                    annotation_mtime_utc = excluded.annotation_mtime_utc,
                    indexed_utc = excluded.indexed_utc;
                """;
            AddPathParameters(command, media.MediaPath);
            command.Parameters.AddWithValue("@rating", media.Annotations.Rating);
            command.Parameters.AddWithValue("@mediaLength", media.MediaLength);
            command.Parameters.AddWithValue("@mediaMtime", media.MediaModifiedUtc.Ticks);
            command.Parameters.AddWithValue("@annotationLength", media.AnnotationStorageLength);
            command.Parameters.AddWithValue("@annotationMtime", media.AnnotationModifiedUtc.Ticks);
            command.Parameters.AddWithValue("@indexed", media.IndexedUtc.Ticks);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        long mediaId;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT id FROM media WHERE path_key = @path;";
            command.Parameters.AddWithValue("@path", GetPathKey(media.MediaPath));
            mediaId = (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The indexed media row was not created."));
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM media_tags WHERE media_id = @mediaId;";
            command.Parameters.AddWithValue("@mediaId", mediaId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var tag in media.Annotations.Tags)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO tags (name, normalized_name)
                VALUES (@name, @normalizedName)
                ON CONFLICT(normalized_name) DO NOTHING;
                INSERT OR IGNORE INTO media_tags (media_id, tag_id)
                SELECT @mediaId, id FROM tags WHERE normalized_name = @normalizedName;
                """;
            command.Parameters.AddWithValue("@name", tag);
            command.Parameters.AddWithValue("@normalizedName", MediaAnnotations.GetTagKey(tag));
            command.Parameters.AddWithValue("@mediaId", mediaId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await DeleteOrphanTagsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
    }

    private async Task RemoveMissingInDirectoryAsync(
        string directory,
        HashSet<string> seen,
        CancellationToken cancellationToken)
    {
        var missingPaths = new List<string>();
        await using (var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT path FROM media WHERE directory_key = @directory;";
            command.Parameters.AddWithValue("@directory", GetPathKey(directory));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var path = reader.GetString(0);
                if (!seen.Contains(path))
                {
                    missingPaths.Add(path);
                }
            }
        }

        foreach (var path in missingPaths)
        {
            await RemoveIfStillMissingAsync(path, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RemoveMissingUnderRootAsync(
        string root,
        HashSet<string> seen,
        CancellationToken cancellationToken)
    {
        var allPaths = new List<string>();
        await using (var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT path FROM media;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var path = reader.GetString(0);
                if (IsUnderRoot(path, root) && !seen.Contains(path))
                {
                    allPaths.Add(path);
                }
            }
        }

        foreach (var path in allPaths)
        {
            await RemoveIfStillMissingAsync(path, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<IndexedMediaAnnotations>> ReadAnnotationsAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var rows = new Dictionary<long, AnnotationAccumulator>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetInt64(0);
            if (!rows.TryGetValue(id, out var row))
            {
                row = new AnnotationAccumulator(
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetInt64(3),
                    ReadUtc(reader.GetInt64(4)),
                    reader.GetInt64(5),
                    ReadUtc(reader.GetInt64(6)),
                    ReadUtc(reader.GetInt64(7)));
                rows.Add(id, row);
            }

            if (!reader.IsDBNull(8))
            {
                row.Tags.Add(reader.GetString(8));
            }
        }

        return rows.Values
            .Select(row => new IndexedMediaAnnotations(
                row.Path,
                new MediaAnnotations(row.Rating, row.Tags),
                row.MediaLength,
                row.MediaModifiedUtc,
                row.AnnotationStorageLength,
                row.AnnotationModifiedUtc,
                row.IndexedUtc))
            .ToArray();
    }

    private static async Task DeleteOrphanTagsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM tags WHERE NOT EXISTS (SELECT 1 FROM media_tags WHERE tag_id = tags.id);";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string AddImageExtensionParameters(SqliteCommand command)
    {
        var names = new string[ImageExtensions.Length];
        for (var index = 0; index < ImageExtensions.Length; index++)
        {
            names[index] = $"@imageExtension{index}";
            command.Parameters.AddWithValue(names[index], ImageExtensions[index]);
        }

        return string.Join(", ", names);
    }

    private static void AddPathParameters(SqliteCommand command, string path)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("Media path must have a directory.", nameof(path));
        command.Parameters.AddWithValue("@newPath", path);
        command.Parameters.AddWithValue("@newPathKey", GetPathKey(path));
        command.Parameters.AddWithValue("@directoryPath", directory);
        command.Parameters.AddWithValue("@directoryKey", GetPathKey(directory));
        command.Parameters.AddWithValue("@fileName", Path.GetFileName(path));
        command.Parameters.AddWithValue("@extension", Path.GetExtension(path).ToLowerInvariant());
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static string GetPathKey(string path) =>
        OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;

    private static bool IsImagePath(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static void EnsureImagePath(string path)
    {
        if (!IsImagePath(path))
        {
            throw new NotSupportedException($"The media index only accepts current image formats: {path}");
        }
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static DateTime ReadUtc(long ticks) => new(ticks, DateTimeKind.Utc);

    private static string GetDefaultDatabasePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageCare",
            "Cache",
            "media-index.db");

    private sealed class AnnotationAccumulator(
        string path,
        int rating,
        long mediaLength,
        DateTime mediaModifiedUtc,
        long annotationStorageLength,
        DateTime annotationModifiedUtc,
        DateTime indexedUtc)
    {
        public string Path { get; } = path;
        public int Rating { get; } = rating;
        public long MediaLength { get; } = mediaLength;
        public DateTime MediaModifiedUtc { get; } = mediaModifiedUtc;
        public long AnnotationStorageLength { get; } = annotationStorageLength;
        public DateTime AnnotationModifiedUtc { get; } = annotationModifiedUtc;
        public DateTime IndexedUtc { get; } = indexedUtc;
        public List<string> Tags { get; } = [];
    }
}
