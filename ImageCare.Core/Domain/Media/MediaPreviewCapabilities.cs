using ImageCare.Core.Domain.MediaFormats;

namespace ImageCare.Core.Domain.Media;

public sealed record MediaPreviewCapabilities(bool Raw, bool Video)
{
    public static MediaPreviewCapabilities Full { get; } = new(Raw: true, Video: true);

    public static MediaPreviewCapabilities JpegOnly { get; } = new(Raw: false, Video: false);

    /// <summary>
    ///     RAW photo formats are supported, but video is not. Use this profile only when no native
    ///     video backend is available.
    /// </summary>
    public static MediaPreviewCapabilities RawAndJpeg { get; } = new(Raw: true, Video: false);

    public bool Supports(MediaFormat mediaFormat)
    {
        if (mediaFormat == MediaFormat.MediaFormatJpg || mediaFormat == MediaFormat.MediaFormatUnknown)
        {
            return true;
        }

        if (Raw && mediaFormat is not null &&
            (mediaFormat == MediaFormat.MediaFormatCr3
             || mediaFormat == MediaFormat.MediaFormatArw
             || mediaFormat == MediaFormat.MediaFormatNef
             || mediaFormat == MediaFormat.MediaFormatDng))
        {
            return true;
        }

        return Video && mediaFormat == MediaFormat.MediaFormatMp4;
    }
}
