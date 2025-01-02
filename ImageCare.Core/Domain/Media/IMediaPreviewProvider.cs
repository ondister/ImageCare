using ImageCare.Core.Domain.Media.Metadata;

namespace ImageCare.Core.Domain.Media;

internal interface IMediaPreviewProvider
{
    IMediaMetadata GetMediaMetadata(string url);

    Stream GetPreviewJpegStream(string url, MediaPreviewSize size);
}