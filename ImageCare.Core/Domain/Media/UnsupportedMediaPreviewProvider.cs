using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.Preview;

namespace ImageCare.Core.Domain.Media;

internal sealed class UnsupportedMediaPreviewProvider : IMediaPreviewProvider
{
    private const string _unsupportedMediaPreview = @"Domain\Media\Assets\unknown_media_preview.jpg";

    /// <inheritdoc />
    public IMediaMetadata GetMediaMetadata(string url)
    {
        return new UnsupportedMediaMetadata();
    }

    /// <inheritdoc />
    public Stream GetPreviewJpegStream(string url, MediaPreviewSize size)
    {
        return File.OpenRead(_unsupportedMediaPreview);
    }
}