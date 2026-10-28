using ImageCare.Core.Domain.MediaFormats;

namespace ImageCare.Core.Domain.Media;

internal sealed class MediaPreviewProvidersFactory
{
    private readonly IMediaPreviewProvider _unsupportedMediaPreviewProvider = new UnsupportedMediaPreviewProvider();
    private readonly Dictionary<MediaFormat, IMediaPreviewProvider> _mediaProviders;

    public MediaPreviewProvidersFactory(
        MediaPreviewCapabilities? capabilities = null,
        IVideoFrameExtractor? videoFrameExtractor = null)
    {
        Capabilities = capabilities ?? MediaPreviewCapabilities.Full;
        _mediaProviders = new Dictionary<MediaFormat, IMediaPreviewProvider>
        {
            { MediaFormat.MediaFormatJpg, new JpegMediaPreviewProvider() }
        };

        if (Capabilities.Raw)
        {
            _mediaProviders.Add(MediaFormat.MediaFormatCr3, new Cr3MediaPreviewProvider());
            _mediaProviders.Add(MediaFormat.MediaFormatArw, new ArwMediaPreviewProvider());
            _mediaProviders.Add(MediaFormat.MediaFormatNef, new NefMediaPreviewProvider());
            _mediaProviders.Add(MediaFormat.MediaFormatDng, new DngMediaPreviewProvider());
        }

        if (Capabilities.Video)
        {
            _mediaProviders.Add(
                MediaFormat.MediaFormatMp4,
                new Mp4MediaPreviewProvider(videoFrameExtractor));
        }
    }

    public MediaPreviewCapabilities Capabilities { get; }

    public IMediaPreviewProvider GetMediaPreviewProvider(MediaFormat mediaFormat)
    {
        return _mediaProviders.GetValueOrDefault(mediaFormat, _unsupportedMediaPreviewProvider);
    }
}
