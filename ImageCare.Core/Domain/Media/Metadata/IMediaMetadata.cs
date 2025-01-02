namespace ImageCare.Core.Domain.Media.Metadata;

public interface IMediaMetadata
{
    int Width { get; }

    int Height { get; }

    DateTime CreationDateTime { get; }

    string GetString();
}