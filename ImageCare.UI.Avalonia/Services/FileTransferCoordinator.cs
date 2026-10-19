using System;
using System.Threading.Tasks;

using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.MediaPreviewOperationsService;

namespace ImageCare.UI.Avalonia.Services;

public sealed class FileTransferCoordinator(
    IMediaPreviewOperationsService operationsService,
    RenameFileWindowService renameFileWindowService)
{
    public bool RenameEnabled { get; set; }

    public Task<OperationResult> CopyAsync(
        MediaPreview mediaPreview,
        string targetDirectoryPath,
        Progress<OperationInfo> progress) =>
        ExecuteAsync(mediaPreview, targetDirectoryPath, progress, copy: true);

    public Task<OperationResult> MoveAsync(
        MediaPreview mediaPreview,
        string targetDirectoryPath,
        Progress<OperationInfo> progress) =>
        ExecuteAsync(mediaPreview, targetDirectoryPath, progress, copy: false);

    private async Task<OperationResult> ExecuteAsync(
        MediaPreview mediaPreview,
        string targetDirectoryPath,
        Progress<OperationInfo> progress,
        bool copy)
    {
        if (!RenameEnabled)
        {
            return copy
                ? await operationsService.CopyImagePreviewToDirectoryAsync(
                    mediaPreview,
                    targetDirectoryPath,
                    progress)
                : await operationsService.MoveImagePreviewToDirectoryAsync(
                    mediaPreview,
                    targetDirectoryPath,
                    progress);
        }

        var destinationFileName = await renameFileWindowService.RequestFileNameAsync(
            mediaPreview.Url,
            targetDirectoryPath);
        if (destinationFileName == null)
        {
            return OperationResult.Cancelled;
        }

        return copy
            ? await operationsService.CopyImagePreviewToDirectoryAsync(
                mediaPreview,
                targetDirectoryPath,
                progress,
                destinationFileName)
            : await operationsService.MoveImagePreviewToDirectoryAsync(
                mediaPreview,
                targetDirectoryPath,
                progress,
                destinationFileName);
    }
}
