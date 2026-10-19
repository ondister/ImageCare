using ImageCare.Core.Domain;
using ImageCare.Core.Domain.Preview;

namespace ImageCare.Core.Services.MediaPreviewOperationsService;

public interface IMediaPreviewOperationsService
{
    IObservable<SelectedMediaPreview> ImagePreviewSelected { get; }

    IObservable<MediaFileOperationCompleted> FileOperationCompleted { get; }

    Task<OperationResult> CopyImagePreviewToDirectoryAsync(
        MediaPreview imagePreview,
        string selectedFolderPath,
        Progress<OperationInfo> progress);

    Task<OperationResult> CopyImagePreviewToDirectoryAsync(
        MediaPreview imagePreview,
        string selectedFolderPath,
        Progress<OperationInfo> progress,
        string destinationFileName);

    Task<OperationResult> MoveImagePreviewToDirectoryAsync(
        MediaPreview imagePreview,
        string selectedFolderPath,
        Progress<OperationInfo> progress);

    Task<OperationResult> MoveImagePreviewToDirectoryAsync(
        MediaPreview imagePreview,
        string selectedFolderPath,
        Progress<OperationInfo> progress,
        string destinationFileName);

    Task<OperationResult> DeleteImagePreviewAsync(MediaPreview imagePreview);

    bool CanUndoDelete { get; }

    IObservable<IReadOnlyList<DeletedMediaFile>> DeletedFilesChanged { get; }

    IReadOnlyList<DeletedMediaFile> GetDeletedFiles();

    Task<OperationResult> UndoLastDeleteAsync();

    Task<OperationResult> RestoreDeletedFileAsync(string bufferPath);

    Task<OperationResult> PermanentlyDeleteAsync(string bufferPath);

    void SetSelectedPreview(SelectedMediaPreview selectedImagePreview);

    void OpenInExternalProcess(MediaPreview mediaPreview, string pathToExecutable);

    void OpenInExternalProcess(MediaPreview mediaPreview, FileApplicationInfo application);

    MediaPreview? GetLastSelectedMediaPreview();
}
