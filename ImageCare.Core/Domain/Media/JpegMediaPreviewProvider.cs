namespace ImageCare.Core.Domain.Media;

internal sealed class JpegMediaPreviewProvider : IMediaPreviewProvider
{
    /// <inheritdoc />
    public Stream GetPreviewJpegStream(string url, ImagePreviewSize size)
    {
        return File.OpenRead(url);
    }
}