using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.Preview;

namespace ImageCare.Core.Services.MediaPreviewService;

public interface IMediaPreviewService
{
	Task<Stream> GetJpegImageStreamAsync(MediaPreview imagePreview,
	                                     MediaPreviewSize imagePreviewSize,
	                                     CancellationToken cancellationToken = default);

    Task<MediaPreview?> GetMediaPreviewAsync(
        string imagePath,
        CancellationToken cancellationToken = default);

    Task<MediaPreview?> GetMediaPreviewAsync(
        FileModel fileModel,
        CancellationToken cancellationToken = default);

    Task<IMediaMetadata> GetMediaMetadataAsync(
        MediaPreview mediaPreview,
        CancellationToken cancellationToken = default);

	Task<DateTime> GetCreationDateTime(MediaPreview mediaPreview);
}