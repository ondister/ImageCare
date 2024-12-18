using ImageCare.Core.Domain;
using System.Security.AccessControl;

namespace ImageCare.Core.Services.FileOperationsService;

public interface IFileOperationsService
{
    OperationResult MoveWithProgress(string source, string destination, IProgress<OperationInfo> progress, CancellationToken cancellationToken = default);

    Task<OperationResult> MoveWithProgressAsync(string source, string destination, IProgress<OperationInfo> progress, CancellationToken cancellationToken = default);

    OperationResult CopyWithProgress(string source, string destination, IProgress<OperationInfo> progress, CancellationToken cancellationToken);

    Task<OperationResult> CopyWithProgressAsync(string source, string destination, IProgress<OperationInfo> progress, CancellationToken cancellationToken = default);


    Task<OperationResult> CopyImagePreviewToDirectoryAsync(ImagePreview imagePreview, string selectedFolderPath, Progress<OperationInfo> progress);

    Task<OperationResult> MoveImagePreviewToDirectoryAsync(ImagePreview imagePreview, string selectedFolderPath, Progress<OperationInfo> progress);

    Task<OperationResult> DeleteImagePreviewAsync(ImagePreview imagePreview);
}