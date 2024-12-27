using LibRawDotNet;

namespace ImageCare.Core.Domain.Media;

internal sealed class RawMediaPreviewProvider : IMediaPreviewProvider
{
    /// <inheritdoc />
    public Stream GetPreviewJpegStream(string url, ImagePreviewSize size)
    {
        using (var libRawData = LibRawData.OpenFile(url))
        {
            return libRawData.GetPreviewJpegStream((int)size);
        }
    }
}