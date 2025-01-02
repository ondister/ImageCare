namespace ImageCare.Core.Domain.Media.Metadata;

public sealed class UnsupportedMediaMetadata : IMediaMetadata
{
    /// <inheritdoc />
    public int Width { get; } = 0;

    /// <inheritdoc />
    public int Height { get; } = 0;

    /// <inheritdoc />
    public DateTime CreationDateTime { get; } = DateTime.MinValue;

    /// <inheritdoc />
    public string GetString()
    {
        return "-";
    }
}