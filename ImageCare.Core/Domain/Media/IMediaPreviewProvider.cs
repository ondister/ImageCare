namespace ImageCare.Core.Domain.Media;

internal interface IMediaPreviewProvider
{
    Stream GetPreviewJpegStream(string url, ImagePreviewSize size);
}