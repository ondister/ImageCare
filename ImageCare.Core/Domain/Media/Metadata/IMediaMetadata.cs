namespace ImageCare.Core.Domain.Media.Metadata;

public interface IMediaMetadata
{
    int Width { get; }

    int Height { get; }

    DateTime CreationDateTime { get; }

    public ExifOrientation Orientation { get; internal set; }

    string GetString();
}