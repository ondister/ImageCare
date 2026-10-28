using ImageCare.Core.Domain.Preview;

namespace ImageCare.Core.Services.MediaBatchOperationsService;

/// <summary>
/// Runs the same file operation over a group of media files, sequentially, while reporting
/// per-item progress and collecting failures instead of stopping on the first error.
/// </summary>
public interface IMediaBatchOperationsService
{
    Task<MediaBatchOperationSummary> CopyAsync(
        IReadOnlyList<MediaPreview> mediaPreviews,
        string destinationDirectoryPath,
        IProgress<MediaBatchOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<MediaBatchOperationSummary> MoveAsync(
        IReadOnlyList<MediaPreview> mediaPreviews,
        string destinationDirectoryPath,
        IProgress<MediaBatchOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<MediaBatchOperationSummary> DeleteAsync(
        IReadOnlyList<MediaPreview> mediaPreviews,
        IProgress<MediaBatchOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the largest available JPEG preview for each image. RAW exports receive common EXIF
    /// fields (capture time, orientation, exposure, ISO, aperture, and GPS); rating and tags are
    /// embedded as XMP. Existing JPEG files retain their original metadata.
    /// </summary>
    Task<MediaBatchOperationSummary> ExportJpegAsync(
        IReadOnlyList<MediaPreview> mediaPreviews,
        string destinationDirectoryPath,
        IProgress<MediaBatchOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
