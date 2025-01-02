namespace ImageCare.Core.Domain.Media.Metadata;

public sealed class RawMediaMetadata : ImageMediaMetadata
{
    /// <inheritdoc />
    public RawMediaMetadata(DateTime creationDateTime, int width, int height)
        : base(creationDateTime, width, height) { }

    /// <inheritdoc />
    public override string GetString()
    {
        return $"{Aperture} - {ShutterSpeed} - @{Iso}";
    }
}