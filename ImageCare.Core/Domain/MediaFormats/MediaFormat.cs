namespace ImageCare.Core.Domain.MediaFormats;

public sealed class MediaFormat
{
    public static MediaFormat MediaFormatCr3 = new("CR3");

    public static MediaFormat MediaFormatUnknown = new("-");

    private MediaFormat(string mediaType)
    {
        MediaType = mediaType;
    }

    public string MediaType { get; }
}