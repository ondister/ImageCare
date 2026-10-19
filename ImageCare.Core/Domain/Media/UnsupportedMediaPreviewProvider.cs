using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.Preview;

namespace ImageCare.Core.Domain.Media;

internal sealed class UnsupportedMediaPreviewProvider : IMediaPreviewProvider
{
    public IMediaMetadata GetMediaMetadata(string url)
    {
        var fileInfo = new FileInfo(url);
        return new UnsupportedMediaMetadata(fileInfo.CreationTime);
    }

    public Stream GetPreviewJpegStream(string url, MediaPreviewSize size)
    {
        var previewPath = Path.Combine(
            AppContext.BaseDirectory,
            "Domain",
            "Media",
            "Assets",
            "unknown_media_preview.jpg");
        return File.OpenRead(previewPath);
    }

    public DateTime? GetCreationDateTime(string url)
    {
        return GetMediaMetadata(url).CreationDateTime;
    }
}
