using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.MediaFormats;
using Microsoft.Data.Sqlite;

namespace ImageCare.Core.Services.MediaAnnotationsIndex;

internal sealed class SqliteMediaAnnotationsQueries(SqliteMediaAnnotationsDatabase database)
{
    private static readonly string[] ImageExtensions = MediaFormat.GetSupportedExtensions()
        .Where(extension => MediaFormat.GetMediaFormatByExtension(extension)?.SupportsAnnotations == true)
        .Select(extension => extension.ToLowerInvariant())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private readonly SqliteMediaAnnotationsDatabase _database = database;

    internal static StringComparer PathComparer { get; } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    internal async Task UpsertAsync(
        IndexedMediaAnnotations media,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(media);
        var mediaPath = NormalizePath(media.MediaPath);
        EnsureImagePath(mediaPath);
        await _database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _database.ExecuteSerializedMutationAsync(
            async () =>
            {
                await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var transaction = connection.BeginTransaction();
                await UpsertCoreAsync(
                        connection,
                        transaction,
                        media with { MediaPath = mediaPath },
                        cancellationToken)
                    .ConfigureAwait(false);
                transaction.Commit();
            },
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<IReadOnlyList<IndexedMediaAnnotations>> GetDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        var directory = NormalizePath(directoryPath);
        await _database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
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

    internal async Task<IReadOnlyList<IndexedMediaAnnotations>> SearchAsync(
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

        await _database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
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

    internal async Task<IReadOnlyList<string>> GetAllTagsAsync(CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
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

    internal async Task RemoveAsync(string mediaPath, CancellationToken cancellationToken = default)
    {
        var pathKey = GetPathKey(NormalizePath(mediaPath));
        await _database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _database.ExecuteSerializedMutationAsync(
            async () =>
            {
                await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var transaction = connection.BeginTransaction();
                await DeleteMediaAsync(connection, transaction, pathKey, cancellationToken).ConfigureAwait(false);
                transaction.Commit();
            },
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task MoveAsync(
        string oldMediaPath,
        string newMediaPath,
        CancellationToken cancellationToken = default)
    {
        var oldPath = NormalizePath(oldMediaPath);
        var newPath = NormalizePath(newMediaPath);
        EnsureImagePath(newPath);
        await _database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _database.ExecuteSerializedMutationAsync(
            async () =>
            {
                var file = new FileInfo(newPath);
                await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
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
            },
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<Dictionary<string, IndexedMediaAnnotations>> GetExistingUnderRootAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, IndexedMediaAnnotations>(PathComparer);
        await _database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
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

    internal async Task RemoveMissingInDirectoryAsync(
        string directory,
        HashSet<string> seen,
        Func<string, bool> fileExists,
        CancellationToken cancellationToken)
    {
        var missingPaths = new List<string>();
        await using (var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false))
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
            await RemoveIfStillMissingAsync(path, fileExists, cancellationToken).ConfigureAwait(false);
        }
    }

    internal async Task RemoveMissingUnderRootAsync(
        string root,
        HashSet<string> seen,
        Func<string, bool> fileExists,
        CancellationToken cancellationToken)
    {
        var allPaths = new List<string>();
        await using (var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false))
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
            await RemoveIfStillMissingAsync(path, fileExists, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    internal static bool IsImagePath(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    internal static bool IsUnderRoot(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private async Task RemoveIfStillMissingAsync(
        string mediaPath,
        Func<string, bool> fileExists,
        CancellationToken cancellationToken)
    {
        var normalizedPath = NormalizePath(mediaPath);
        var pathKey = GetPathKey(normalizedPath);
        await _database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _database.ExecuteSerializedMutationAsync(
            async () =>
            {
                if (fileExists(normalizedPath))
                {
                    return;
                }

                await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var transaction = connection.BeginTransaction();
                await DeleteMediaAsync(connection, transaction, pathKey, cancellationToken).ConfigureAwait(false);
                transaction.Commit();
            },
            cancellationToken).ConfigureAwait(false);
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

    private static async Task DeleteMediaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string pathKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM media WHERE path_key = @path;";
        command.Parameters.AddWithValue("@path", pathKey);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await DeleteOrphanTagsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
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

    private static string GetPathKey(string path) =>
        OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;

    private static void EnsureImagePath(string path)
    {
        if (!IsImagePath(path))
        {
            throw new NotSupportedException($"The media index only accepts current image formats: {path}");
        }
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static DateTime ReadUtc(long ticks) => new(ticks, DateTimeKind.Utc);

    private sealed class AnnotationAccumulator(
        string path,
        int rating,
        long mediaLength,
        DateTime mediaModifiedUtc,
        long annotationStorageLength,
        DateTime annotationModifiedUtc,
        DateTime indexedUtc)
    {
        internal string Path { get; } = path;
        internal int Rating { get; } = rating;
        internal long MediaLength { get; } = mediaLength;
        internal DateTime MediaModifiedUtc { get; } = mediaModifiedUtc;
        internal long AnnotationStorageLength { get; } = annotationStorageLength;
        internal DateTime AnnotationModifiedUtc { get; } = annotationModifiedUtc;
        internal DateTime IndexedUtc { get; } = indexedUtc;
        internal List<string> Tags { get; } = [];
    }
}
