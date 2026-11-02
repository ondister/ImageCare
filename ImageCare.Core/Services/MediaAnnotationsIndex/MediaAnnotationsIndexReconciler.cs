using System.Xml;
using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Services.MediaAnnotationsService;

namespace ImageCare.Core.Services.MediaAnnotationsIndex;

internal sealed class MediaAnnotationsIndexReconciler(
    IMediaAnnotationsStorageService storage,
    SqliteMediaAnnotationsDatabase database,
    SqliteMediaAnnotationsQueries queries)
{
    private readonly IMediaAnnotationsStorageService _storage = storage;
    private readonly SqliteMediaAnnotationsDatabase _database = database;
    private readonly SqliteMediaAnnotationsQueries _queries = queries;

    internal async Task ReconcileDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        var directory = SqliteMediaAnnotationsQueries.NormalizePath(directoryPath);
        await _database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!Directory.Exists(directory))
        {
            return;
        }

        var existing = (await _queries.GetDirectoryAsync(directory, cancellationToken).ConfigureAwait(false))
            .ToDictionary(
                item => SqliteMediaAnnotationsQueries.NormalizePath(item.MediaPath),
                SqliteMediaAnnotationsQueries.PathComparer);
        var seen = new HashSet<string>(SqliteMediaAnnotationsQueries.PathComparer);
        try
        {
            foreach (var mediaPath in Directory.EnumerateFiles(directory)
                         .Where(SqliteMediaAnnotationsQueries.IsImagePath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var normalizedPath = SqliteMediaAnnotationsQueries.NormalizePath(mediaPath);
                seen.Add(normalizedPath);
                try
                {
                    if (existing.TryGetValue(normalizedPath, out var indexed)
                        && IsSourceStateUnchanged(normalizedPath, indexed))
                    {
                        continue;
                    }

                    var snapshot = await _storage.ReadAsync(mediaPath, cancellationToken).ConfigureAwait(false);
                    await _queries.UpsertAsync(ToIndexed(snapshot), cancellationToken).ConfigureAwait(false);
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

        await _queries.RemoveMissingInDirectoryAsync(directory, seen, File.Exists, cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task RebuildAsync(
        IEnumerable<string> roots,
        IProgress<AnnotationsReconciliationStatus>? progress = null,
        CancellationToken cancellationToken = default,
        bool force = false)
    {
        ArgumentNullException.ThrowIfNull(roots);
        await _database.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var processed = 0;
        foreach (var rootValue in roots.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = SqliteMediaAnnotationsQueries.NormalizePath(rootValue);
            if (!Directory.Exists(root))
            {
                // Unavailable roots retain their disposable cache rows.
                continue;
            }

            var existing = await _queries.GetExistingUnderRootAsync(root, cancellationToken).ConfigureAwait(false);
            var seen = new HashSet<string>(SqliteMediaAnnotationsQueries.PathComparer);
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

                foreach (var mediaPath in files.Where(SqliteMediaAnnotationsQueries.IsImagePath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var normalizedPath = SqliteMediaAnnotationsQueries.NormalizePath(mediaPath);
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
                        await _queries.UpsertAsync(ToIndexed(snapshot), cancellationToken).ConfigureAwait(false);
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
                await _queries.RemoveMissingUnderRootAsync(root, seen, File.Exists, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        progress?.Report(new AnnotationsReconciliationStatus(
            AnnotationsReconciliationState.Completed,
            null,
            processed));
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
}
