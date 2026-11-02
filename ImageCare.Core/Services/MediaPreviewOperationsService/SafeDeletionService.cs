using System.Globalization;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Exceptions;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.MediaAnnotationsService;

namespace ImageCare.Core.Services.MediaPreviewOperationsService;

internal sealed record SafeDeletionRestoreResult(string BufferPath, string DestinationPath);

internal sealed class SafeDeletionService : IDisposable
{
    internal const string SafeDeleteFilePrefix = "ImageCare_Deleted_";

    private readonly IFileSystemService _fileSystemService;
    private readonly MediaTransferService _mediaTransferService;
    private readonly IDeletionManifestStore _manifestStore;
    private readonly Func<Configuration> _getConfiguration;
    private readonly List<DeletedMediaFile> _deletedFiles = new();
    private readonly object _safeDeleteLock = new();
    private readonly BehaviorSubject<IReadOnlyList<DeletedMediaFile>> _deletedFilesSubject;

    public SafeDeletionService(
        IFileSystemService fileSystemService,
        MediaTransferService mediaTransferService,
        IDeletionManifestStore manifestStore,
        Func<Configuration> getConfiguration)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _mediaTransferService = mediaTransferService ?? throw new ArgumentNullException(nameof(mediaTransferService));
        _manifestStore = manifestStore ?? throw new ArgumentNullException(nameof(manifestStore));
        _getConfiguration = getConfiguration ?? throw new ArgumentNullException(nameof(getConfiguration));

        try
        {
            _deletedFiles.AddRange(_manifestStore.Load());
            ReconcileDeletionJournal(publishChanges: false);
        }
        catch
        {
            // A damaged manifest, unavailable path, or startup recovery failure must not
            // prevent the application from starting. Recovery is retried on the next query.
        }

        _deletedFilesSubject = new BehaviorSubject<IReadOnlyList<DeletedMediaFile>>(
            CreateDeletedFilesSnapshot());
    }

    public IObservable<IReadOnlyList<DeletedMediaFile>> DeletedFilesChanged =>
        _deletedFilesSubject.AsObservable();

    public IReadOnlyList<DeletedMediaFile> GetDeletedFiles()
    {
        lock (_safeDeleteLock)
        {
            ReconcileDeletionJournal();
            return CreateDeletedFilesSnapshot();
        }
    }

    public bool CanUndoDelete
    {
        get
        {
            lock (_safeDeleteLock)
            {
                ReconcileDeletionJournal();
                return _deletedFiles.Any(IsUndoable);
            }
        }
    }

    public void Delete(string sourcePath, string? sourceSidecarPath)
    {
        var configuration = _getConfiguration();
        if (configuration.SafeDeletionEnabled)
        {
            MoveToSafeDeleteBuffer(sourcePath, sourceSidecarPath, configuration);
        }
        else
        {
            DeleteBundlePermanently(sourcePath, sourceSidecarPath);
        }
    }

    public SafeDeletionRestoreResult? UndoLastDelete()
    {
        DeletedMediaFile? deletedFile;
        lock (_safeDeleteLock)
        {
            ReconcileDeletionJournal();
            deletedFile = _deletedFiles
                .Where(IsUndoable)
                .MaxBy(file => file.DeletedAtUtc);
        }

        return deletedFile == null ? null : RestoreDeletedFile(deletedFile.BufferPath);
    }

    public SafeDeletionRestoreResult? RestoreDeletedFile(string bufferPath)
    {
        DeletedMediaFile? deletedFile;
        string destinationPath;
        lock (_safeDeleteLock)
        {
            ReconcileDeletionJournal();
            deletedFile = _deletedFiles.FirstOrDefault(file =>
                IsUndoable(file) && PathsEqual(file.BufferPath, bufferPath));
            if (deletedFile == null)
            {
                return null;
            }

            var hasSidecar = deletedFile.OriginalSidecarPath is not null
                || deletedFile.BufferSidecarPath is not null;
            if (hasSidecar
                && (deletedFile.OriginalSidecarPath is null
                    || deletedFile.BufferSidecarPath is null
                    || !_fileSystemService.FileExists(deletedFile.BufferSidecarPath)))
            {
                return null;
            }

            var originalDirectory = Path.GetDirectoryName(deletedFile.OriginalPath);
            if (string.IsNullOrWhiteSpace(originalDirectory)
                || !_fileSystemService.DirectoryExists(originalDirectory))
            {
                return null;
            }

            destinationPath = deletedFile.OriginalPath;
            if (_mediaTransferService.BundleDestinationExists(
                    destinationPath,
                    hasSidecar ? deletedFile.OriginalPath : null))
            {
                destinationPath = Path.Combine(
                    originalDirectory,
                    _mediaTransferService.GetUniqueFileName(
                        originalDirectory,
                        Path.GetFileName(deletedFile.OriginalPath),
                        hasSidecar ? deletedFile.OriginalPath : null));
            }

            var destinationSidecarPath = hasSidecar
                ? PathsEqual(destinationPath, deletedFile.OriginalPath)
                    ? deletedFile.OriginalSidecarPath
                    : XmpSidecarPath.GetDestinationExternalSidecarPath(
                        deletedFile.OriginalPath,
                        destinationPath)
                : null;
            var pendingRestore = deletedFile with
            {
                State = DeletedMediaFileState.PendingRestore,
                RestorePath = destinationPath,
                RestoreSidecarPath = destinationSidecarPath
            };
            ReplaceDeletedFile(deletedFile, pendingRestore);
            SaveDeletionManifest();
            deletedFile = pendingRestore;

            try
            {
                _mediaTransferService.MoveBundle(
                    deletedFile.BufferPath,
                    destinationPath,
                    deletedFile.BufferSidecarPath,
                    destinationSidecarPath);
            }
            catch
            {
                ReconcilePendingRestore(deletedFile);
                SaveDeletionManifest();
                throw;
            }

            _deletedFiles.Remove(deletedFile);
            try
            {
                SaveDeletionManifest();
            }
            catch
            {
                // The filesystem move is the restore commit. The durable PendingRestore
                // record lets startup remove the stale journal entry.
            }

            PublishDeletedFiles();
        }

        return new SafeDeletionRestoreResult(deletedFile.BufferPath, destinationPath);
    }

    public bool PermanentlyDelete(string bufferPath)
    {
        lock (_safeDeleteLock)
        {
            ReconcileDeletionJournal();
            var deletedFile = _deletedFiles.FirstOrDefault(file =>
                IsUndoable(file) && PathsEqual(file.BufferPath, bufferPath));
            if (deletedFile == null)
            {
                return false;
            }

            MarkCleanupPendingAndPersist(deletedFile);
            ProcessCleanupPending(
                _deletedFiles.First(file => PathsEqual(file.BufferPath, bufferPath)));
            try
            {
                SaveDeletionManifest();
            }
            catch
            {
                // CleanupPending was persisted before media deletion. Startup retries an
                // idempotent cleanup if removing the completed tombstone cannot be persisted.
            }

            PublishDeletedFiles();
            return true;
        }
    }

    public void Dispose()
    {
        _deletedFilesSubject.Dispose();
    }

    private void MoveToSafeDeleteBuffer(
        string sourcePath,
        string? sourceSidecarPath,
        Configuration configuration)
    {
        var bufferDirectory = configuration.SafeDeletionDirectoryPath;
        if (string.IsNullOrWhiteSpace(bufferDirectory))
        {
            throw new ServiceException("Safe deletion directory is not configured");
        }

        var sourceDirectory = Path.GetDirectoryName(sourcePath);
        if (PathsEqual(sourceDirectory, bufferDirectory))
        {
            throw new ServiceException("Safe deletion directory cannot be the source directory");
        }

        lock (_safeDeleteLock)
        {
            if (!_fileSystemService.DirectoryExists(bufferDirectory))
            {
                _fileSystemService.CreateDirectory(bufferDirectory);
            }

            var destinationPath = CreateSafeDeletePath(bufferDirectory, Path.GetFileName(sourcePath));
            var destinationSidecarPath = sourceSidecarPath is null
                ? null
                : CreateSafeDeletePath(bufferDirectory, Path.GetFileName(sourceSidecarPath));
            var deletedFile = new DeletedMediaFile(
                sourcePath,
                destinationPath,
                DateTime.UtcNow,
                sourceSidecarPath,
                destinationSidecarPath,
                DeletedMediaFileState.PendingDelete);
            _deletedFiles.Add(deletedFile);
            try
            {
                SaveDeletionManifest();
            }
            catch
            {
                _deletedFiles.Remove(deletedFile);
                throw;
            }

            try
            {
                _mediaTransferService.MoveBundle(
                    sourcePath,
                    destinationPath,
                    sourceSidecarPath,
                    destinationSidecarPath);
            }
            catch (Exception operationException)
            {
                try
                {
                    ReconcilePendingDelete(deletedFile);
                    SaveDeletionManifest();
                }
                catch (Exception recoveryException)
                {
                    throw new AggregateException(
                        "Safe deletion failed and its journal recovery could not be persisted.",
                        operationException,
                        recoveryException);
                }

                throw;
            }

            var committed = deletedFile with { State = DeletedMediaFileState.Committed };
            ReplaceDeletedFile(deletedFile, committed);
            try
            {
                SaveDeletionManifest();
                TrimSafeDeleteBuffer(Math.Max(1, configuration.SafeDeletionMaxFileCount));
                SaveDeletionManifest();
            }
            catch
            {
                // Moving media into the buffer commits deletion. PendingDelete is already
                // durable, so post-commit journal and trim failures do not fail the operation.
            }

            PublishDeletedFiles();
        }
    }

    private string CreateSafeDeletePath(string bufferDirectory, string originalFileName)
    {
        while (true)
        {
            var timestamp = DateTime.UtcNow.ToString(
                "yyyyMMddHHmmssfffffff",
                CultureInfo.InvariantCulture);
            var candidateName =
                $"{SafeDeleteFilePrefix}{timestamp}_{Guid.NewGuid():N}__{originalFileName}";
            var candidatePath = Path.Combine(bufferDirectory, candidateName);
            if (!_fileSystemService.FileExists(candidatePath))
            {
                return candidatePath;
            }
        }
    }

    private void TrimSafeDeleteBuffer(int maximumFileCount)
    {
        var undoableFiles = _deletedFiles
            .Where(IsUndoable)
            .OrderBy(file => file.DeletedAtUtc)
            .ToArray();
        var filesToDelete = undoableFiles
            .Take(Math.Max(0, undoableFiles.Length - maximumFileCount))
            .ToArray();

        foreach (var deletedFile in filesToDelete)
        {
            MarkCleanupPendingAndPersist(deletedFile);
            var cleanupPending = _deletedFiles.First(file =>
                PathsEqual(file.BufferPath, deletedFile.BufferPath));
            ProcessCleanupPending(cleanupPending);
            SaveDeletionManifest();
        }
    }

    private void MarkCleanupPendingAndPersist(DeletedMediaFile deletedFile)
    {
        var cleanupPending = deletedFile with
        {
            State = DeletedMediaFileState.CleanupPending,
            RestorePath = null,
            RestoreSidecarPath = null
        };
        ReplaceDeletedFile(deletedFile, cleanupPending);
        try
        {
            SaveDeletionManifest();
        }
        catch
        {
            ReplaceDeletedFile(cleanupPending, deletedFile);
            throw;
        }
    }

    private void ProcessCleanupPending(DeletedMediaFile deletedFile)
    {
        if (deletedFile.State != DeletedMediaFileState.CleanupPending)
        {
            return;
        }

        if (_fileSystemService.FileExists(deletedFile.BufferPath))
        {
            _fileSystemService.SafeDelete(deletedFile.BufferPath);
        }

        if (_fileSystemService.FileExists(deletedFile.BufferPath))
        {
            return;
        }

        if (deletedFile.BufferSidecarPath is { } sidecarPath
            && _fileSystemService.FileExists(sidecarPath))
        {
            try
            {
                _fileSystemService.SafeDelete(sidecarPath);
            }
            catch
            {
                return;
            }
        }

        if (deletedFile.BufferSidecarPath is null
            || !_fileSystemService.FileExists(deletedFile.BufferSidecarPath))
        {
            _deletedFiles.Remove(deletedFile);
        }
    }

    private void DeleteBundlePermanently(string mediaPath, string? sidecarPath)
    {
        // Media deletion is the commit. Failures before it remain meaningful to callers.
        if (_fileSystemService.FileExists(mediaPath))
        {
            _fileSystemService.SafeDelete(mediaPath);
        }

        // Once media is gone the bundle is no longer restorable. Sidecar cleanup is
        // idempotent and best effort.
        if (sidecarPath is not null && _fileSystemService.FileExists(sidecarPath))
        {
            try
            {
                _fileSystemService.SafeDelete(sidecarPath);
            }
            catch
            {
                // Do not report that committed media deletion failed because an orphan remains.
            }
        }
    }

    private void ReconcileDeletionJournal(bool publishChanges = true)
    {
        var before = _deletedFiles.ToArray();
        foreach (var file in before)
        {
            if (!_deletedFiles.Contains(file))
            {
                continue;
            }

            try
            {
                switch (file.State)
                {
                    case DeletedMediaFileState.PendingDelete:
                        ReconcilePendingDelete(file);
                        break;
                    case DeletedMediaFileState.PendingRestore:
                        ReconcilePendingRestore(file);
                        break;
                    case DeletedMediaFileState.CleanupPending:
                        ProcessCleanupPending(file);
                        break;
                    case DeletedMediaFileState.Committed when
                        !_fileSystemService.FileExists(file.BufferPath):
                        MarkCleanupPendingAndPersist(file);
                        var cleanupPending = _deletedFiles.First(candidate =>
                            PathsEqual(candidate.BufferPath, file.BufferPath));
                        ProcessCleanupPending(cleanupPending);
                        break;
                }
            }
            catch
            {
                // Recovery is retried on the next startup or deleted-files query. Keep the
                // durable phase and every known path instead of guessing after an I/O failure.
            }
        }

        if (!before.SequenceEqual(_deletedFiles))
        {
            SaveDeletionManifest();
            if (publishChanges)
            {
                PublishDeletedFiles();
            }
        }
    }

    private void ReconcilePendingDelete(DeletedMediaFile file)
    {
        var bufferMediaExists = _fileSystemService.FileExists(file.BufferPath);
        var originalMediaExists = _fileSystemService.FileExists(file.OriginalPath);
        if (bufferMediaExists)
        {
            if (file.OriginalSidecarPath is { } originalSidecarPath
                && file.BufferSidecarPath is { } bufferSidecarPath
                && !_fileSystemService.FileExists(bufferSidecarPath)
                && _fileSystemService.FileExists(originalSidecarPath))
            {
                _mediaTransferService.MoveFileReliably(originalSidecarPath, bufferSidecarPath);
            }

            if (originalMediaExists)
            {
                _fileSystemService.SafeDelete(file.OriginalPath);
                if (_fileSystemService.FileExists(file.OriginalPath))
                {
                    return;
                }
            }

            if (file.OriginalSidecarPath is { } duplicateOriginalSidecar
                && file.BufferSidecarPath is { } canonicalBufferSidecar
                && _fileSystemService.FileExists(duplicateOriginalSidecar)
                && _fileSystemService.FileExists(canonicalBufferSidecar))
            {
                try
                {
                    _fileSystemService.SafeDelete(duplicateOriginalSidecar);
                }
                catch
                {
                    // A harmless duplicate can remain; the buffered sidecar is durable.
                }
            }

            ReplaceDeletedFile(file, file with { State = DeletedMediaFileState.Committed });
            return;
        }

        if (originalMediaExists)
        {
            if (file.BufferSidecarPath is { } bufferSidecarPath
                && _fileSystemService.FileExists(bufferSidecarPath))
            {
                if (file.OriginalSidecarPath is { } originalSidecarPath
                    && !_fileSystemService.FileExists(originalSidecarPath))
                {
                    _mediaTransferService.MoveFileReliably(bufferSidecarPath, originalSidecarPath);
                }
                else if (file.OriginalSidecarPath is not null
                         && _fileSystemService.FileExists(file.OriginalSidecarPath))
                {
                    MarkCleanupPendingAndPersist(file);
                    var cleanupPending = _deletedFiles.First(candidate =>
                        PathsEqual(candidate.BufferPath, file.BufferPath));
                    ProcessCleanupPending(cleanupPending);
                    return;
                }
                else
                {
                    // There is no known safe owner path for this sidecar. Retain the intent and
                    // path rather than deleting the only surviving annotation artifact.
                    return;
                }
            }

            _deletedFiles.Remove(file);
            return;
        }

        MarkCleanupPendingAndPersist(file);
        var lostMediaCleanup = _deletedFiles.First(candidate =>
            PathsEqual(candidate.BufferPath, file.BufferPath));
        ProcessCleanupPending(lostMediaCleanup);
    }

    private void ReconcilePendingRestore(DeletedMediaFile file)
    {
        var destinationPath = file.RestorePath ?? file.OriginalPath;
        var destinationSidecarPath = file.RestoreSidecarPath ?? file.OriginalSidecarPath;
        var destinationMediaExists = _fileSystemService.FileExists(destinationPath);
        var bufferMediaExists = _fileSystemService.FileExists(file.BufferPath);

        if (destinationMediaExists && bufferMediaExists)
        {
            if (file.BufferSidecarPath is { } bufferSidecarPath
                && !_fileSystemService.FileExists(bufferSidecarPath)
                && destinationSidecarPath is not null
                && _fileSystemService.FileExists(destinationSidecarPath))
            {
                _fileSystemService.CopyFile(destinationSidecarPath, bufferSidecarPath);
            }

            ReplaceDeletedFile(file, file with
            {
                State = DeletedMediaFileState.Committed,
                RestorePath = null,
                RestoreSidecarPath = null
            });
            return;
        }

        if (destinationMediaExists)
        {
            if (destinationSidecarPath is not null
                && !_fileSystemService.FileExists(destinationSidecarPath)
                && file.BufferSidecarPath is { } bufferSidecarPath
                && _fileSystemService.FileExists(bufferSidecarPath))
            {
                _mediaTransferService.MoveFileReliably(bufferSidecarPath, destinationSidecarPath);
            }

            if (file.BufferSidecarPath is { } duplicateBufferSidecar
                && _fileSystemService.FileExists(duplicateBufferSidecar))
            {
                MarkCleanupPendingAndPersist(file);
                var cleanupPending = _deletedFiles.First(candidate =>
                    PathsEqual(candidate.BufferPath, file.BufferPath));
                ProcessCleanupPending(cleanupPending);
                return;
            }

            _deletedFiles.Remove(file);
            return;
        }

        if (bufferMediaExists)
        {
            if (destinationSidecarPath is not null
                && _fileSystemService.FileExists(destinationSidecarPath))
            {
                if (file.BufferSidecarPath is not { } bufferSidecarPath)
                {
                    return;
                }

                if (_fileSystemService.FileExists(bufferSidecarPath))
                {
                    _fileSystemService.SafeDelete(destinationSidecarPath);
                }
                else
                {
                    _mediaTransferService.MoveFileReliably(destinationSidecarPath, bufferSidecarPath);
                }
            }

            ReplaceDeletedFile(file, file with
            {
                State = DeletedMediaFileState.Committed,
                RestorePath = null,
                RestoreSidecarPath = null
            });
            return;
        }

        MarkCleanupPendingAndPersist(file);
        var missingMediaCleanup = _deletedFiles.First(candidate =>
            PathsEqual(candidate.BufferPath, file.BufferPath));
        ProcessCleanupPending(missingMediaCleanup);
    }

    private void SaveDeletionManifest()
    {
        _manifestStore.Save(_deletedFiles);
    }

    private IReadOnlyList<DeletedMediaFile> CreateDeletedFilesSnapshot() =>
        _deletedFiles
            .Where(IsUndoable)
            .OrderByDescending(file => file.DeletedAtUtc)
            .ToArray();

    private bool IsUndoable(DeletedMediaFile file) =>
        file.State == DeletedMediaFileState.Committed
        && _fileSystemService.FileExists(file.BufferPath);

    private void ReplaceDeletedFile(DeletedMediaFile current, DeletedMediaFile replacement)
    {
        var index = _deletedFiles.IndexOf(current);
        if (index >= 0)
        {
            _deletedFiles[index] = replacement;
        }
    }

    private void PublishDeletedFiles()
    {
        _deletedFilesSubject.OnNext(CreateDeletedFilesSnapshot());
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (left == null || right == null)
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            comparison);
    }
}
