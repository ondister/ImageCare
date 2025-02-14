namespace ImageCare.Core.Domain.Media.Metadata;

public interface IMediaMetadata
{
    int Width { get; }

    int Height { get; }

    DateTime CreationDateTime { get; }

    ExifOrientation Orientation { get; internal set; }

    IReadOnlyDictionary<string, string> AllMetadata { get;}

    string GetString();
}