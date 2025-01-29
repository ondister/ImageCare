using ImageCare.Core.Domain.MediaFormats;

namespace ImageCare.Core.Domain.Media;

internal sealed class MediaPreviewProvidersFactory
{
    private static readonly IMediaPreviewProvider _unsupportedMediaPreviewProvider = new UnsupportedMediaPreviewProvider();
    private static readonly Dictionary<MediaFormat, IMediaPreviewProvider> _mediaProviders = new()
    {
        { MediaFormat.MediaFormatCr3, new Cr3MediaPreviewProvider() },
        { MediaFormat.MediaFormatArw, new ArwMediaPreviewProvider() },
        { MediaFormat.MediaFormatJpg, new JpegMediaPreviewProvider() },
        { MediaFormat.MediaFormatMp4, new Mp4MediaPreviewProvider() }
    };

    public IMediaPreviewProvider GetMediaPreviewProvider(MediaFormat mediaFormat)
    {
        return _mediaProviders.GetValueOrDefault(mediaFormat, _unsupportedMediaPreviewProvider);
    }
}