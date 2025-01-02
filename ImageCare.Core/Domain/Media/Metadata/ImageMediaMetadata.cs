namespace ImageCare.Core.Domain.Media.Metadata;

public abstract class ImageMediaMetadata : IImageMetadata
{
    protected ImageMediaMetadata(DateTime creationDateTime, int width, int height)
    {
        CreationDateTime = creationDateTime;
        Width = width;
        Height = height;
    }

    /// <inheritdoc />
    public int Width { get; }

    /// <inheritdoc />
    public int Height { get; }

    /// <inheritdoc />
    public DateTime CreationDateTime { get; }

    /// <inheritdoc />
    public string? Aperture { get; internal set; }

    /// <inheritdoc />
    public string? ShutterSpeed { get; internal set; }

    /// <inheritdoc />
    public int? Iso { get; internal set; }

    public abstract string GetString();
}