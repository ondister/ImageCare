using ImageCare.Core.Domain.Preview;

namespace ImageCare.Core.Services.MediaPreviewOperationsService;

public interface IMediaPreviewOperationsService
{
	public IObservable<SelectedMediaPreview> ImagePreviewSelected { get; }

	Task<OperationResult> CopyImagePreviewToDirectoryAsync(MediaPreview imagePreview, string selectedFolderPath, Progress<OperationInfo> progress);

	Task<OperationResult> MoveImagePreviewToDirectoryAsync(MediaPreview imagePreview, string selectedFolderPath, Progress<OperationInfo> progress);

	Task<OperationResult> DeleteImagePreviewAsync(MediaPreview imagePreview);

	void SetSelectedPreview(SelectedMediaPreview selectedImagePreview);

	void OpenInExternalProcess(MediaPreview mediaPreview, string pathToExecutable);

	MediaPreview? GetLastSelectedMediaPreview();
}