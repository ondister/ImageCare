using Microsoft.Data.Sqlite;

namespace ImageCare.Core.Services.MediaAnnotationsIndex;

internal sealed class SqliteMediaAnnotationsDatabase
{
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly object _initializationGate = new();
    private Task? _initializationTask;

    internal SqliteMediaAnnotationsDatabase(string? databasePath = null)
    {
        DatabasePath = Path.GetFullPath(databasePath ?? GetDefaultDatabasePath());
    }

    internal string DatabasePath { get; }

    internal async Task InitializeAsync(CancellationToken cancellationToken = default)
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

    internal async Task<SqliteConnection> OpenConnectionAsync(
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

    internal async Task ExecuteSerializedMutationAsync(
        Func<Task> mutation,
        CancellationToken cancellationToken)
    {
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await mutation().ConfigureAwait(false);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

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
        await using var connection = await OpenConnectionAsync(CancellationToken.None, configure: false)
            .ConfigureAwait(false);
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

    private static string GetDefaultDatabasePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageCare",
            "Cache",
            "media-index.db");
}
