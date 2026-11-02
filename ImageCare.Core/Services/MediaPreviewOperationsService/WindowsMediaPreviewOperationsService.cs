using System.Reactive.Linq;
using System.Reactive.Subjects;

using ImageCare.Core.Domain;
using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.Core.Services.ProcessService;

namespace ImageCare.Core.Services.MediaPreviewOperationsService;

public sealed class WindowsMediaPreviewOperationsService : IMediaPreviewOperationsService, IDisposable
{
    private readonly Subject<MediaFileOperationCompleted> _fileOperationCompletedSubject;
    private readonly IFileSystemService _fileSystemService;
    private readonly MediaTransferService _mediaTransferService;
    private readonly SafeDeletionService _safeDeletionService;
    private readonly MediaPreviewNavigationService _navigationService;
    private readonly ExternalProcessLauncher _externalProcessLauncher;
    private readonly IConfigurationService? _configurationService;
    private readonly Configuration _fallbackConfiguration = new() { SafeDeletionEnabled = false };

    public WindowsMediaPreviewOperationsService(
        IFileSystemService fileSystemService,
        IProcessService processService,
        IConfigurationService? configurationService = null)
        : this(fileSystemService, processService, configurationService, manifestStore: null)
    {
    }

    internal WindowsMediaPreviewOperationsService(
        IFileSystemService fileSystemService,
        IProcessService processService,
        IConfigurationService? configurationService,
        IDeletionManifestStore? manifestStore)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _mediaTransferService = new MediaTransferService(_fileSystemService);
        var validatedProcessService = processService ?? throw new ArgumentNullException(nameof(processService));
        _navigationService = new MediaPreviewNavigationService();
        _externalProcessLauncher = new ExternalProcessLauncher(_fileSystemService, validatedProcessService);
        _configurationService = configurationService;
        _fileOperationCompletedSubject = new Subject<MediaFileOperationCompleted>();
        Func<Configuration> getConfiguration = GetConfiguration;
        _safeDeletionService = new SafeDeletionService(
            _fileSystemService,
            _mediaTransferService,
            manifestStore ?? new DeletionManifestStore(getConfiguration),
            getConfiguration);
    }

    public IObservable<SelectedMediaPreview> ImagePreviewSelected =>
        _navigationService.ImagePreviewSelected;

    public IObservable<SelectedMediaPreview> MediaPreviewNavigationRequested =>
        _navigationService.MediaPreviewNavigationRequested;

    public IObservable<MediaFileOperationCompleted> FileOperationCompleted =>
        _fileOperationCompletedSubject.AsObservable();

    public IObservable<IReadOnlyList<DeletedMediaFile>> DeletedFilesChanged =>
        _safeDeletionService.DeletedFilesChanged;

    public IReadOnlyList<DeletedMediaFile> GetDeletedFiles() =>
        _safeDeletionService.GetDeletedFiles();

    public bool CanUndoDelete => _safeDeletionService.CanUndoDelete;

    public void Dispose()
    {
        _navigationService.Dispose();
        _fileOperationCompletedSubject.Dispose();
        _safeDeletionService.Dispose();
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
            var transfer = _mediaTransferService.CopyToDirectory(
                imagePreview.Url,
                selectedFolderPath,
                destinationFileName);
            _fileOperationCompletedSubject.OnNext(
                new MediaFileOperationCompleted(
                    MediaFileOperationKind.Copy,
                    transfer.SourcePath,
                    transfer.DestinationPath));

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
            var transfer = _mediaTransferService.MoveToDirectory(
                imagePreview.Url,
                selectedFolderPath,
                destinationFileName);
            _fileOperationCompletedSubject.OnNext(
                new MediaFileOperationCompleted(
                    MediaFileOperationKind.Move,
                    transfer.SourcePath,
                    transfer.DestinationPath));

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

            var sourceSidecarPath = XmpSidecarPath.GetExistingExternalSidecarPath(
                imagePreview.Url,
                _fileSystemService.FileExists);
            _safeDeletionService.Delete(imagePreview.Url, sourceSidecarPath);

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
            return CompleteRestore(_safeDeletionService.UndoLastDelete());
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

    public Task<OperationResult> RestoreDeletedFileAsync(string bufferPath)
    {
        try
        {
            return CompleteRestore(_safeDeletionService.RestoreDeletedFile(bufferPath));
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
            return Task.FromResult(
                _safeDeletionService.PermanentlyDelete(bufferPath)
                    ? OperationResult.Success
                    : OperationResult.Failed);
        }
        catch (Exception ex)
        {
            throw new ServiceException("Failed to permanently delete image preview", ex);
        }
    }

    public void SetSelectedPreview(SelectedMediaPreview selectedImagePreview) =>
        _navigationService.SetSelected(selectedImagePreview);

    public void RequestMediaPreviewNavigation(SelectedMediaPreview selectedImagePreview) =>
        _navigationService.RequestNavigation(selectedImagePreview);

    public void OpenInExternalProcess(MediaPreview mediaPreview, string pathToExecutable) =>
        _externalProcessLauncher.Open(mediaPreview, pathToExecutable);

    public void OpenInExternalProcess(MediaPreview mediaPreview, FileApplicationInfo application) =>
        _externalProcessLauncher.Open(mediaPreview, application);

    public MediaPreview? GetLastSelectedMediaPreview() =>
        _navigationService.GetLastSelected();

    private Task<OperationResult> CompleteRestore(SafeDeletionRestoreResult? restoreResult)
    {
        if (restoreResult == null)
        {
            return Task.FromResult(OperationResult.Failed);
        }

        _fileOperationCompletedSubject.OnNext(
            new MediaFileOperationCompleted(
                MediaFileOperationKind.Restore,
                restoreResult.BufferPath,
                restoreResult.DestinationPath));
        return Task.FromResult(OperationResult.Success);
    }

    private Configuration GetConfiguration()
    {
        return _configurationService?.Configuration.Value ?? _fallbackConfiguration;
    }
}
