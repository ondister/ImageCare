using System.Collections.Concurrent;
using System.Globalization;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ImageCare.Core.Domain;
using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.ProcessService;

namespace ImageCare.Core.Services.MediaPreviewOperationsService;

public sealed class WindowsMediaPreviewOperationsService : IMediaPreviewOperationsService, IDisposable
{
    private const string SafeDeleteFilePrefix = "ImageCare_Deleted_";

    private readonly Subject<SelectedMediaPreview> _selectedImagePreviewSubject;
    private readonly Subject<MediaFileOperationCompleted> _fileOperationCompletedSubject;
    private readonly IFileSystemService _fileSystemService;
    private readonly IProcessService _processService;
    private readonly IConfigurationService? _configurationService;
    private readonly Configuration _fallbackConfiguration = new() { SafeDeletionEnabled = false };
    private readonly ConcurrentDictionary<FileManagerPanel, MediaPreview> _selectedImagePreviews = new();
    private readonly ConcurrentStack<PendingDeletion> _pendingDeletions = new();
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
    }

    public IObservable<SelectedMediaPreview> ImagePreviewSelected => _selectedImagePreviewSubject.AsObservable();

    public IObservable<MediaFileOperationCompleted> FileOperationCompleted =>
        _fileOperationCompletedSubject.AsObservable();

    public bool CanUndoDelete
    {
        get
        {
            lock (_safeDeleteLock)
            {
                RemoveMissingPendingDeletions();
                return !_pendingDeletions.IsEmpty;
            }
        }
    }

    public void Dispose()
    {
        _selectedImagePreviewSubject.Dispose();
        _fileOperationCompletedSubject.Dispose();
    }

    public Task<OperationResult> CopyImagePreviewToDirectoryAsync(
        MediaPreview imagePreview,
        string selectedFolderPath,
        Progress<OperationInfo> progress)
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

            var fileName = GetUniqueFileName(selectedFolderPath, Path.GetFileName(imagePreview.Url));
            var destinationFilePath = Path.Combine(selectedFolderPath, fileName);

            _fileSystemService.CopyFile(imagePreview.Url, destinationFilePath);
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
        Progress<OperationInfo> progress)
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

            var fileName = GetUniqueFileName(selectedFolderPath, Path.GetFileName(imagePreview.Url));
            var destinationFilePath = Path.Combine(selectedFolderPath, fileName);

            _fileSystemService.MoveFile(imagePreview.Url, destinationFilePath);
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

    public Task<OperationResult> DeleteImagePreviewAsync(MediaPreview imagePreview)
    {
        try
        {
            if (!_fileSystemService.FileExists(imagePreview.Url))
            {
                throw new ServiceException($"Media file does not exist: {imagePreview.Url}");
            }

            var configuration = GetConfiguration();
            if (configuration.SafeDeletionEnabled)
            {
                MoveToSafeDeleteBuffer(imagePreview.Url, configuration);
            }
            else
            {
                _fileSystemService.SafeDelete(imagePreview.Url);
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
        try
        {
            PendingDeletion pendingDeletion;
            lock (_safeDeleteLock)
            {
                RemoveMissingPendingDeletions();
                if (!_pendingDeletions.TryPeek(out pendingDeletion!))
                {
                    return Task.FromResult(OperationResult.Failed);
                }

                if (_fileSystemService.FileExists(pendingDeletion.OriginalPath))
                {
                    return Task.FromResult(OperationResult.Failed);
                }

                var originalDirectory = Path.GetDirectoryName(pendingDeletion.OriginalPath);
                if (string.IsNullOrWhiteSpace(originalDirectory) ||
                    !_fileSystemService.DirectoryExists(originalDirectory))
                {
                    return Task.FromResult(OperationResult.Failed);
                }

                MoveFileReliably(pendingDeletion.BufferPath, pendingDeletion.OriginalPath);
                _pendingDeletions.TryPop(out _);
            }

            _fileOperationCompletedSubject.OnNext(
                new MediaFileOperationCompleted(
                    MediaFileOperationKind.Restore,
                    pendingDeletion.BufferPath,
                    pendingDeletion.OriginalPath));

            return Task.FromResult(OperationResult.Success);
        }
        catch (Exception ex)
        {
            throw new ServiceException("Failed to restore the last deleted image preview", ex);
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

    private void MoveToSafeDeleteBuffer(string sourcePath, Configuration configuration)
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
            MoveFileReliably(sourcePath, destinationPath);
            _pendingDeletions.Push(new PendingDeletion(sourcePath, destinationPath));
            TrimSafeDeleteBuffer(bufferDirectory, Math.Max(1, configuration.SafeDeletionMaxFileCount));
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

    private void TrimSafeDeleteBuffer(string bufferDirectory, int maximumFileCount)
    {
        var managedFiles = _fileSystemService.GetFiles(bufferDirectory)
            .Where(path => Path.GetFileName(path).StartsWith(SafeDeleteFilePrefix, StringComparison.Ordinal))
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToArray();
        var filesToDelete = managedFiles.Take(Math.Max(0, managedFiles.Length - maximumFileCount));

        foreach (var filePath in filesToDelete)
        {
            _fileSystemService.SafeDelete(filePath);
        }

        RemoveMissingPendingDeletions();
    }

    private void MoveFileReliably(string sourcePath, string destinationPath)
    {
        try
        {
            _fileSystemService.MoveFile(sourcePath, destinationPath);
        }
        catch (IOException) when (!_fileSystemService.FileExists(destinationPath))
        {
            try
            {
                _fileSystemService.CopyFile(sourcePath, destinationPath);
                _fileSystemService.CopyFileMetadata(sourcePath, destinationPath);
                _fileSystemService.SafeDelete(sourcePath);
            }
            catch
            {
                if (_fileSystemService.FileExists(destinationPath))
                {
                    _fileSystemService.SafeDelete(destinationPath);
                }

                throw;
            }
        }
    }

    private void RemoveMissingPendingDeletions()
    {
        while (_pendingDeletions.TryPeek(out var pendingDeletion) &&
               !_fileSystemService.FileExists(pendingDeletion.BufferPath))
        {
            _pendingDeletions.TryPop(out _);
        }
    }

    private string GetUniqueFileName(string directory, string fileName)
    {
        var files = _fileSystemService.GetFiles(directory);
        var existingFiles = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);

        if (!existingFiles.Contains(Path.Combine(directory, fileName)))
        {
            return fileName;
        }

        var extension = Path.GetExtension(fileName);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var counter = 1;

        while (true)
        {
            var newFileName = $"{fileNameWithoutExtension} ({counter}){extension}";
            var fullPath = Path.Combine(directory, newFileName);

            if (!existingFiles.Contains(fullPath))
            {
                return newFileName;
            }

            counter++;
        }
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

    private sealed record PendingDeletion(string OriginalPath, string BufferPath);
}
