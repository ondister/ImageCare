namespace ImageCare.Core.Domain.Media.Metadata;

public sealed class JpegMediaMetadata : ImageMediaMetadata
{
    /// <inheritdoc />
    public JpegMediaMetadata(DateTime creationDateTime, int width, int height)
        : base(creationDateTime, width, height) { }

    /// <inheritdoc />
    public override string GetString()
    {
        return $"{Aperture} - {ShutterSpeed} - @{Iso}";
    }
}