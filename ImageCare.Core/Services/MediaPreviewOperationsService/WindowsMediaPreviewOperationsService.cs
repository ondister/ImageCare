using System.Collections.Concurrent;
using System.Globalization;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;

using ImageCare.Core.Domain;
using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.Core.Services.ProcessService;

namespace ImageCare.Core.Services.MediaPreviewOperationsService;

public sealed class WindowsMediaPreviewOperationsService : IMediaPreviewOperationsService, IDisposable
{
    private const string SafeDeleteFilePrefix = "ImageCare_Deleted_";
    private const string DeletionManifestFileName = "deletion-manifest.json";

    private readonly Subject<SelectedMediaPreview> _selectedImagePreviewSubject;
    private readonly Subject<MediaFileOperationCompleted> _fileOperationCompletedSubject;
    private readonly BehaviorSubject<IReadOnlyList<DeletedMediaFile>> _deletedFilesSubject;
    private readonly IFileSystemService _fileSystemService;
    private readonly IProcessService _processService;
    private readonly IConfigurationService? _configurationService;
    private readonly Configuration _fallbackConfiguration = new() { SafeDeletionEnabled = false };
    private readonly ConcurrentDictionary<FileManagerPanel, MediaPreview> _selectedImagePreviews = new();
    private readonly List<DeletedMediaFile> _deletedFiles = new();
    private readonly object _safeDeleteLock = new();
    private SelectedMediaPreview? _lastSelectedMediaPreview;

    public WindowsMediaPreviewOperationsService(
        IFileSystemService fileSystemService,
        IProcessService processService,
        IConfigurationService? configurationService = null)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _configurationService = configurationService;
        _selectedImagePreviewSubject = new Subject<SelectedMediaPreview>();
        _fileOperationCompletedSubject = new Subject<MediaFileOperationCompleted>();
        LoadDeletionManifest();
        _deletedFilesSubject = new BehaviorSubject<IReadOnlyList<DeletedMediaFile>>(CreateDeletedFilesSnapshot());
    }

    public IObservable<SelectedMediaPreview> ImagePreviewSelected => _selectedImagePreviewSubject.AsObservable();

    public IObservable<MediaFileOperationCompleted> FileOperationCompleted =>
        _fileOperationCompletedSubject.AsObservable();

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

    public void Dispose()
    {
        _selectedImagePreviewSubject.Dispose();
        _fileOperationCompletedSubject.Dispose();
        _deletedFilesSubject.Dispose();
    }

    public Task<OperationResult> CopyImagePreviewToDirectoryAsync(
        MediaPreview imagePreview,
        string selectedFolderPath,
        Progress<OperationInfo> progress) =>
        CopyImagePreviewToDirectoryInternalAsync(imagePreview, selectedFolderPath, progress, null);

    public Task<OperationResult> CopyImagePreviewToDirectoryAsync(
        MediaPreview imagePreview,
        string selectedFolderPath,
        Progress<OperationInfo> progress,
        string destinationFileName) =>
        CopyImagePreviewToDirectoryInternalAsync(
            imagePreview,
            selectedFolderPath,
            progress,
            destinationFileName);

    private Task<OperationResult> CopyImagePreviewToDirectoryInternalAsync(
        MediaPreview imagePreview,
        string selectedFolderPath,
        Progress<OperationInfo> progress,
        string? destinationFileName)
    {
        try
        {
            if (!_fileSystemService.DirectoryExists(selectedFolderPath))
            {
                throw new ServiceException($"Destination directory does not exist: {selectedFolderPath}");
            }

            if (!_fileSystemService.FileExists(imagePreview.Url))
            {
                throw new ServiceException($"Source file does not exist: {imagePreview.Url}");
            }

            var sourceSidecarPath = XmpSidecarPath.GetExistingExternalSidecarPath(
                imagePreview.Url,
                _fileSystemService.FileExists);
            var fileName = GetDestinationFileName(
                selectedFolderPath,
                imagePreview.Url,
                destinationFileName,
                sourceSidecarPath is not null);
            var destinationFilePath = Path.Combine(selectedFolderPath, fileName);
            var destinationSidecarPath = sourceSidecarPath is null
                ? null
                : XmpSidecarPath.GetDestinationExternalSidecarPath(imagePreview.Url, destinationFilePath);

            CopyBundle(imagePreview.Url, destinationFilePath, sourceSidecarPath, destinationSidecarPath);
            _fileOperationCompletedSubject.OnNext(
                new MediaFileOperationCompleted(MediaFileOperationKind.Copy, imagePreview.Url, destinationFilePath));

            return Task.FromResult(OperationResult.Success);
        }
        catch (ServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ServiceException("Failed to copy image preview", ex);
        }
    }

    public Task<OperationResult> MoveImagePreviewToDirectoryAsync(
        MediaPreview imagePreview,
        string selectedFolderPath,
        Progress<OperationInfo> progress) =>
        MoveImagePreviewToDirectoryInternalAsync(imagePreview, selectedFolderPath, progress, null);

    public Task<OperationResult> MoveImagePreviewToDirectoryAsync(
        MediaPreview imagePreview,
        string selectedFolderPath,
        Progress<OperationInfo> progress,
        string destinationFileName) =>
        MoveImagePreviewToDirectoryInternalAsync(
            imagePreview,
            selectedFolderPath,
            progress,
            destinationFileName);

    private Task<OperationResult> MoveImagePreviewToDirectoryInternalAsync(
        MediaPreview imagePreview,
        string selectedFolderPath,
        Progress<OperationInfo> progress,
        string? destinationFileName)
    {
        try
        {
            if (!_fileSystemService.DirectoryExists(selectedFolderPath))
            {
                throw new ServiceException($"Destination directory does not exist: {selectedFolderPath}");
            }

            if (!_fileSystemService.FileExists(imagePreview.Url))
            {
                throw new ServiceException($"Source file does not exist: {imagePreview.Url}");
            }

            var sourceSidecarPath = XmpSidecarPath.GetExistingExternalSidecarPath(
                imagePreview.Url,
                _fileSystemService.FileExists);
            var fileName = GetDestinationFileName(
                selectedFolderPath,
                imagePreview.Url,
                destinationFileName,
                sourceSidecarPath is not null);
            var destinationFilePath = Path.Combine(selectedFolderPath, fileName);
            var destinationSidecarPath = sourceSidecarPath is null
                ? null
                : XmpSidecarPath.GetDestinationExternalSidecarPath(imagePreview.Url, destinationFilePath);

            MoveBundle(imagePreview.Url, destinationFilePath, sourceSidecarPath, destinationSidecarPath);
            _fileOperationCompletedSubject.OnNext(
                new MediaFileOperationCompleted(MediaFileOperationKind.Move, imagePreview.Url, destinationFilePath));

            return Task.FromResult(OperationResult.Success);
        }
        catch (ServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ServiceException("Failed to move image preview", ex);
        }
    }

    private string GetDestinationFileName(
        string selectedFolderPath,
        string sourcePath,
        string? requestedFileName,
        bool hasExternalSidecar)
    {
        if (requestedFileName == null)
        {
            return GetUniqueFileName(
                selectedFolderPath,
                Path.GetFileName(sourcePath),
                hasExternalSidecar ? sourcePath : null);
        }

        if (!PortableFileNameValidator.IsValid(requestedFileName))
        {
            throw new ServiceException($"Invalid destination file name: {requestedFileName}");
        }

        var destinationFilePath = Path.Combine(selectedFolderPath, requestedFileName);
        if (BundleDestinationExists(
                destinationFilePath,
                hasExternalSidecar ? sourcePath : null))
        {
            throw new ServiceException($"Destination file or sidecar already exists: {destinationFilePath}");
        }

        return requestedFileName;
    }

    public Task<OperationResult> DeleteImagePreviewAsync(MediaPreview imagePreview)
    {
        try
        {
            if (!_fileSystemService.FileExists(imagePreview.Url))
            {
                throw new ServiceException($"Media file does not exist: {imagePreview.Url}");
            }

            var sourceSidecarPath = XmpSidecarPath.GetExistingExternalSidecarPath(
                imagePreview.Url,
                _fileSystemService.FileExists);
            var configuration = GetConfiguration();
            if (configuration.SafeDeletionEnabled)
            {
                MoveToSafeDeleteBuffer(imagePreview.Url, sourceSidecarPath, configuration);
            }
            else
            {
                DeleteBundlePermanently(imagePreview.Url, sourceSidecarPath);
            }

            _fileOperationCompletedSubject.OnNext(
                new MediaFileOperationCompleted(MediaFileOperationKind.Delete, imagePreview.Url));

            return Task.FromResult(OperationResult.Success);
        }
        catch (ServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ServiceException("Failed to delete image preview", ex);
        }
    }

    public Task<OperationResult> UndoLastDeleteAsync()
    {
        DeletedMediaFile? deletedFile;
        lock (_safeDeleteLock)
        {
            ReconcileDeletionJournal();
            deletedFile = _deletedFiles
                .Where(IsUndoable)
                .MaxBy(file => file.DeletedAtUtc);
        }

        return deletedFile == null
            ? Task.FromResult(OperationResult.Failed)
            : RestoreDeletedFileAsync(deletedFile.BufferPath);
    }

    public Task<OperationResult> RestoreDeletedFileAsync(string bufferPath)
    {
        try
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
                    return Task.FromResult(OperationResult.Failed);
                }

                var hasSidecar = deletedFile.OriginalSidecarPath is not null
                    || deletedFile.BufferSidecarPath is not null;
                if (hasSidecar
                    && (deletedFile.OriginalSidecarPath is null
                        || deletedFile.BufferSidecarPath is null
                        || !_fileSystemService.FileExists(deletedFile.BufferSidecarPath)))
                {
                    return Task.FromResult(OperationResult.Failed);
                }

                var originalDirectory = Path.GetDirectoryName(deletedFile.OriginalPath);
                if (string.IsNullOrWhiteSpace(originalDirectory) ||
                    !_fileSystemService.DirectoryExists(originalDirectory))
                {
                    return Task.FromResult(OperationResult.Failed);
                }

                destinationPath = deletedFile.OriginalPath;
                if (BundleDestinationExists(
                        destinationPath,
                        hasSidecar ? deletedFile.OriginalPath : null))
                {
                    destinationPath = Path.Combine(
                        originalDirectory,
                        GetUniqueFileName(
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
                    MoveBundle(
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
                    // record lets startup remove the stale journal entry, so do not report that
                    // the media remained deleted after it has already been restored.
                }

                PublishDeletedFiles();
            }

            _fileOperationCompletedSubject.OnNext(
                new MediaFileOperationCompleted(
                    MediaFileOperationKind.Restore,
                    deletedFile.BufferPath,
                    destinationPath));

            return Task.FromResult(OperationResult.Success);
        }
        catch (ServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ServiceException("Failed to restore deleted image preview", ex);
        }
    }

    public Task<OperationResult> PermanentlyDeleteAsync(string bufferPath)
    {
        try
        {
            lock (_safeDeleteLock)
            {
                ReconcileDeletionJournal();
                var deletedFile = _deletedFiles.FirstOrDefault(file =>
                    IsUndoable(file) && PathsEqual(file.BufferPath, bufferPath));
                if (deletedFile == null)
                {
                    return Task.FromResult(OperationResult.Failed);
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
                    // CleanupPending was persisted before media deletion. If removing the
                    // completed tombstone cannot be persisted, startup will retry idempotently.
                }

                PublishDeletedFiles();
            }

            return Task.FromResult(OperationResult.Success);
        }
        catch (Exception ex)
        {
            throw new ServiceException("Failed to permanently delete image preview", ex);
        }
    }

    public void SetSelectedPreview(SelectedMediaPreview selectedImagePreview)
    {
        _selectedImagePreviews.AddOrUpdate(
            selectedImagePreview.FileManagerPanel,
            _ => selectedImagePreview,
            (_, _) => selectedImagePreview);

        _lastSelectedMediaPreview = selectedImagePreview;
        _selectedImagePreviewSubject.OnNext(selectedImagePreview);
    }

    public void OpenInExternalProcess(MediaPreview mediaPreview, string pathToExecutable)
    {
        try
        {
            if (!_fileSystemService.FileExists(pathToExecutable))
            {
                throw new ServiceException($"Executable not found: {pathToExecutable}");
            }

            if (!_fileSystemService.FileExists(mediaPreview.Url))
            {
                throw new ServiceException($"Media file not found: {mediaPreview.Url}");
            }

            _processService.StartProcess(pathToExecutable, $"\"{mediaPreview.Url}\"");
        }
        catch (ServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ServiceException("Failed to open in external process", ex);
        }
    }

    public void OpenInExternalProcess(MediaPreview mediaPreview, FileApplicationInfo application)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(application);

            if (!_fileSystemService.FileExists(mediaPreview.Url))
            {
                throw new ServiceException($"Media file not found: {mediaPreview.Url}");
            }

            switch (application.LaunchMode)
            {
                case ApplicationLaunchMode.Executable when !_fileSystemService.FileExists(application.ApplicationPath):
                    throw new ServiceException($"Executable not found: {application.ApplicationPath}");
                case ApplicationLaunchMode.MacApplication when !_fileSystemService.DirectoryExists(application.ApplicationPath):
                    throw new ServiceException($"Application not found: {application.ApplicationPath}");
            }

            _processService.OpenFile(mediaPreview.Url, application.LaunchMode, application.ApplicationPath);
        }
        catch (ServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ServiceException("Failed to open in external application", ex);
        }
    }

    public MediaPreview? GetLastSelectedMediaPreview()
    {
        return _lastSelectedMediaPreview;
    }

    private Configuration GetConfiguration()
    {
        return _configurationService?.Configuration.Value ?? _fallbackConfiguration;
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
                MoveBundle(sourcePath, destinationPath, sourceSidecarPath, destinationSidecarPath);
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
                // durable and startup can normalize it, so post-commit journal/trim failures
                // must not make the API report that the source media remains in place.
            }

            PublishDeletedFiles();
        }
    }

    private string CreateSafeDeletePath(string bufferDirectory, string originalFileName)
    {
        while (true)
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfffffff", CultureInfo.InvariantCulture);
            var candidateName = $"{SafeDeleteFilePrefix}{timestamp}_{Guid.NewGuid():N}__{originalFileName}";
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

    private void CopyBundle(
        string sourcePath,
        string destinationPath,
        string? sourceSidecarPath,
        string? destinationSidecarPath)
    {
        var sidecarCreated = false;
        var mediaCreated = false;
        try
        {
            if (sourceSidecarPath is not null && destinationSidecarPath is not null)
            {
                _fileSystemService.CopyFile(sourceSidecarPath, destinationSidecarPath);
                sidecarCreated = true;
            }

            _fileSystemService.CopyFile(sourcePath, destinationPath);
            mediaCreated = true;
        }
        catch (Exception operationException)
        {
            var rollbackExceptions = new List<Exception>();
            if (mediaCreated)
            {
                TryDeleteRollbackDestination(destinationPath, rollbackExceptions);
            }

            if (sidecarCreated)
            {
                TryDeleteRollbackDestination(destinationSidecarPath, rollbackExceptions);
            }

            ThrowWithRollbackFailures(operationException, rollbackExceptions);
            throw;
        }
    }

    private void MoveBundle(
        string sourcePath,
        string destinationPath,
        string? sourceSidecarPath,
        string? destinationSidecarPath)
    {
        var sidecarMoved = false;
        var mediaMoved = false;
        try
        {
            if (sourceSidecarPath is not null && destinationSidecarPath is not null)
            {
                MoveFileReliably(sourceSidecarPath, destinationSidecarPath);
                sidecarMoved = true;
            }

            MoveFileReliably(sourcePath, destinationPath);
            mediaMoved = true;
        }
        catch (Exception operationException)
        {
            var rollbackExceptions = new List<Exception>();
            if (mediaMoved)
            {
                TryRollbackMove(destinationPath, sourcePath, rollbackExceptions);
            }

            if (sidecarMoved)
            {
                TryRollbackMove(destinationSidecarPath, sourceSidecarPath, rollbackExceptions);
            }

            ThrowWithRollbackFailures(operationException, rollbackExceptions);
            throw;
        }
    }

    private void MoveFileReliably(string sourcePath, string destinationPath)
    {
        try
        {
            _fileSystemService.MoveFile(sourcePath, destinationPath);
        }
        catch (IOException) when (!_fileSystemService.FileExists(destinationPath))
        {
            var destinationCreated = false;
            try
            {
                _fileSystemService.CopyFile(sourcePath, destinationPath);
                destinationCreated = true;
                _fileSystemService.CopyFileMetadata(sourcePath, destinationPath);
                _fileSystemService.SafeDelete(sourcePath);
            }
            catch
            {
                if (destinationCreated && _fileSystemService.FileExists(destinationPath))
                {
                    _fileSystemService.SafeDelete(destinationPath);
                }

                throw;
            }
        }
    }

    private void DeleteBundlePermanently(string mediaPath, string? sidecarPath)
    {
        // Media deletion is the commit. Failures before it remain meaningful to callers.
        if (_fileSystemService.FileExists(mediaPath))
        {
            _fileSystemService.SafeDelete(mediaPath);
        }

        // Once SafeDelete returns, media deletion is committed. The abstraction reports
        // failures by throwing; a second existence probe is unreliable for mocked/remote stores.

        // Once media is gone the bundle is no longer restorable. Sidecar cleanup is idempotent
        // and best effort; startup reconciliation retries orphan cleanup.
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

    private void TryDeleteRollbackDestination(string? path, ICollection<Exception> rollbackExceptions)
    {
        if (path is null || !_fileSystemService.FileExists(path))
        {
            return;
        }

        try
        {
            _fileSystemService.SafeDelete(path);
        }
        catch (Exception ex)
        {
            rollbackExceptions.Add(ex);
        }
    }

    private void TryRollbackMove(
        string? destinationPath,
        string? sourcePath,
        ICollection<Exception> rollbackExceptions)
    {
        if (destinationPath is null || sourcePath is null || !_fileSystemService.FileExists(destinationPath))
        {
            return;
        }

        try
        {
            if (_fileSystemService.FileExists(sourcePath))
            {
                _fileSystemService.SafeDelete(destinationPath);
            }
            else
            {
                MoveFileReliably(destinationPath, sourcePath);
            }
        }
        catch (Exception ex)
        {
            rollbackExceptions.Add(ex);
        }
    }

    private static void ThrowWithRollbackFailures(
        Exception operationException,
        IReadOnlyCollection<Exception> rollbackExceptions)
    {
        if (rollbackExceptions.Count > 0)
        {
            throw new AggregateException(
                "The file operation failed and one or more rollback actions also failed.",
                new[] { operationException }.Concat(rollbackExceptions));
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
                MoveFileReliably(originalSidecarPath, bufferSidecarPath);
            }

            if (originalMediaExists)
            {
                _fileSystemService.SafeDelete(file.OriginalPath);
                if (_fileSystemService.FileExists(file.OriginalPath))
                {
                    return;
                }
            }

            // The original media is gone, so a duplicate original sidecar is no longer the
            // only sidecar of a live source. The buffered copy remains the canonical bundle.
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
                    MoveFileReliably(bufferSidecarPath, originalSidecarPath);
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
            // A file may have raced into the reserved-looking destination after preflight. With
            // no durable file identity we cannot prove that it is our cross-volume copy, so the
            // buffer always wins. Copy a sidecar back when needed (never steal the destination's
            // possible sidecar), keep the destination media untouched, and make Undo available.
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
                MoveFileReliably(bufferSidecarPath, destinationSidecarPath);
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
            // Deterministic partial-restore policy: if only the sidecar reached the destination,
            // roll it back to the still-buffered media and expose the entry as undoable again.
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
                    MoveFileReliably(destinationSidecarPath, bufferSidecarPath);
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

    private void LoadDeletionManifest()
    {
        var manifestPath = GetDeletionManifestPath();
        if (!File.Exists(manifestPath))
        {
            return;
        }

        try
        {
            using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var deletedFiles = JsonSerializer.Deserialize(
                stream,
                typeof(List<DeletedMediaFile>),
                ConfigurationSourceGenerationContext.Default) as List<DeletedMediaFile>;
            if (deletedFiles != null)
            {
                _deletedFiles.AddRange(deletedFiles);
                ReconcileDeletionJournal(publishChanges: false);
            }
        }
        catch
        {
            // A damaged manifest or temporarily unavailable path must not prevent startup.
            // The original file remains untouched and recovery will be retried when possible.
        }
    }

    private void SaveDeletionManifest()
    {
        var manifestPath = GetDeletionManifestPath();
        var directory = Path.GetDirectoryName(manifestPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = manifestPath + ".tmp";
        var content = JsonSerializer.Serialize(
            _deletedFiles,
            typeof(List<DeletedMediaFile>),
            ConfigurationSourceGenerationContext.Default);
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        using (var stream = new FileStream(
                   temporaryPath,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.None,
                   16 * 1024,
                   FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, manifestPath, true);
    }

    private string GetDeletionManifestPath()
    {
        var bufferDirectory = GetConfiguration().SafeDeletionDirectoryPath;
        if (string.IsNullOrWhiteSpace(bufferDirectory))
        {
            bufferDirectory = Configuration.GetDefaultSafeDeletionDirectoryPath();
        }

        return Path.Combine(bufferDirectory, DeletionManifestFileName);
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
        _deletedFilesSubject?.OnNext(CreateDeletedFilesSnapshot());
    }

    private string GetUniqueFileName(
        string directory,
        string fileName,
        string? sidecarOwnerMediaPath = null)
    {
        var files = _fileSystemService.GetFiles(directory);
        var existingFiles = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        var extension = Path.GetExtension(fileName);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var counter = 0;

        while (true)
        {
            var candidateFileName = counter == 0
                ? fileName
                : $"{fileNameWithoutExtension} ({counter}){extension}";
            var candidatePath = Path.Combine(directory, candidateFileName);
            var sidecarPath = sidecarOwnerMediaPath is null
                ? null
                : XmpSidecarPath.GetDestinationExternalSidecarPath(
                    sidecarOwnerMediaPath,
                    candidatePath);
            var destinationExists = existingFiles.Contains(candidatePath)
                || _fileSystemService.FileExists(candidatePath)
                || XmpSidecarPath.HasAnotherRawOwner(candidatePath)
                || (sidecarPath is not null
                    && (existingFiles.Contains(sidecarPath)
                        || _fileSystemService.FileExists(sidecarPath)));
            if (!destinationExists)
            {
                return candidateFileName;
            }

            counter++;
        }
    }

    private bool BundleDestinationExists(string destinationPath, string? sidecarOwnerMediaPath)
    {
        if (_fileSystemService.FileExists(destinationPath)
            || XmpSidecarPath.HasAnotherRawOwner(destinationPath))
        {
            return true;
        }

        var sidecarPath = sidecarOwnerMediaPath is null
            ? null
            : XmpSidecarPath.GetDestinationExternalSidecarPath(
                sidecarOwnerMediaPath,
                destinationPath);
        return sidecarPath is not null && _fileSystemService.FileExists(sidecarPath);
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