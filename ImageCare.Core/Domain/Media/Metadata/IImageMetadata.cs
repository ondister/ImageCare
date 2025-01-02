namespace ImageCare.Core.Domain.Media.Metadata;

public interface IImageMetadata : IMediaMetadata
{
    string? Aperture { get; }

    string? ShutterSpeed { get; }

    int? Iso { get; }
}