using ImageCare.Core.Domain;
using ImageCare.Core.Domain.Preview;

namespace ImageCare.Core.Services.MediaPreviewOperationsService;

public interface IMediaPreviewOperationsService
{
	public IObservable<SelectedMediaPreview> ImagePreviewSelected { get; }

	IObservable<MediaFileOperationCompleted> FileOperationCompleted { get; }

	Task<OperationResult> CopyImagePreviewToDirectoryAsync(MediaPreview imagePreview, string selectedFolderPath, Progress<OperationInfo> progress);

	Task<OperationResult> MoveImagePreviewToDirectoryAsync(MediaPreview imagePreview, string selectedFolderPath, Progress<OperationInfo> progress);

	Task<OperationResult> DeleteImagePreviewAsync(MediaPreview imagePreview);

    bool CanUndoDelete { get; }

    Task<OperationResult> UndoLastDeleteAsync();

	void SetSelectedPreview(SelectedMediaPreview selectedImagePreview);

	void OpenInExternalProcess(MediaPreview mediaPreview, string pathToExecutable);

    void OpenInExternalProcess(MediaPreview mediaPreview, FileApplicationInfo application);

	MediaPreview? GetLastSelectedMediaPreview();
}