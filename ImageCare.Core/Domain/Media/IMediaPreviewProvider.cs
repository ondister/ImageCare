using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.Preview;

namespace ImageCare.Core.Domain.Media;

internal interface IMediaPreviewProvider
{
    IMediaMetadata GetMediaMetadata(string url);

    Stream GetPreviewJpegStream(string url, MediaPreviewSize size);
}