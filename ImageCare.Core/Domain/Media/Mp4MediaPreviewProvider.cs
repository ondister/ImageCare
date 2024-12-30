namespace ImageCare.Core.Domain.Media;

internal sealed class Mp4MediaPreviewProvider : IMediaPreviewProvider
{
    private const string _unsupportedMediaPreview = @"Domain\Media\Assets\mp4_media_preview.jpg";

    /// <inheritdoc />
    public Stream GetPreviewJpegStream(string url, ImagePreviewSize size)
    {
        return File.OpenRead(_unsupportedMediaPreview);
    }
}