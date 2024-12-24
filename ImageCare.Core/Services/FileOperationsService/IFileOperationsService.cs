using ImageCare.Core.Domain;

namespace ImageCare.Core.Services.FileOperationsService;

public interface IFileOperationsService
{
    public IObservable<SelectedImagePreview> ImagePreviewSelected { get; }

    Task<OperationResult> MoveWithProgressAsync(string source, string destination, IProgress<OperationInfo> progress, CancellationToken cancellationToken = default);

    Task<OperationResult> CopyWithProgressAsync(string source, string destination, IProgress<OperationInfo> progress, CancellationToken cancellationToken = default);

    Task<OperationResult> CopyImagePreviewToDirectoryAsync(ImagePreview imagePreview, string selectedFolderPath, Progress<OperationInfo> progress);

    Task<OperationResult> MoveImagePreviewToDirectoryAsync(ImagePreview imagePreview, string selectedFolderPath, Progress<OperationInfo> progress);

    Task<OperationResult> DeleteImagePreviewAsync(ImagePreview imagePreview);

    void SetSelectedPreview(SelectedImagePreview selectedImagePreview);
}