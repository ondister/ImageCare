namespace ImageCare.Core.Domain.Media;

internal sealed class UnsupportedMediaPreviewProvider : IMediaPreviewProvider
{
    private const string _unsupportedMediaPreview = @"Domain\Media\Assets\unknown_media_preview.jpg";

    /// <inheritdoc />
    public Stream GetPreviewJpegStream(string url, ImagePreviewSize size)
    {
        return File.OpenRead(_unsupportedMediaPreview);
    }
}