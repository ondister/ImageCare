using ImageCare.Core.Domain.Preview;

namespace ImageCare.Core.Services.FileOperationsService;

public interface IFileOperationsService
{
    public IObservable<SelectedMediaPreview> ImagePreviewSelected { get; }

    Task<OperationResult> MoveWithProgressAsync(string source, string destination, IProgress<OperationInfo> progress, CancellationToken cancellationToken = default);

    Task<OperationResult> CopyWithProgressAsync(string source, string destination, IProgress<OperationInfo> progress, CancellationToken cancellationToken = default);

    Task<OperationResult> CopyImagePreviewToDirectoryAsync(MediaPreview imagePreview, string selectedFolderPath, Progress<OperationInfo> progress);

    Task<OperationResult> MoveImagePreviewToDirectoryAsync(MediaPreview imagePreview, string selectedFolderPath, Progress<OperationInfo> progress);

    Task<OperationResult> DeleteImagePreviewAsync(MediaPreview imagePreview);

    void SetSelectedPreview(SelectedMediaPreview selectedImagePreview);

    void OpenInExternalProcess(MediaPreview mediaPreview, string pathToExecutable);

    MediaPreview? GetLastSelectedMediaPreview();

}