using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.Preview;

namespace ImageCare.Core.Services.MediaPreviewService;

public interface IMediaPreviewService
{
	Task<Stream> GetJpegImageStreamAsync(MediaPreview imagePreview,
	                                     MediaPreviewSize imagePreviewSize,
	                                     CancellationToken cancellationToken = default);

	Task<MediaPreview?> GetMediaPreviewAsync(string imagePath);

	Task<IMediaMetadata> GetMediaMetadataAsync(MediaPreview mediaPreview);

	Task<DateTime> GetCreationDateTime(MediaPreview mediaPreview);
}