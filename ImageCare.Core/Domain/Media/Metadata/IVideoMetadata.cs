namespace ImageCare.Core.Domain.Media.Metadata;

public interface IVideoMetadata : IMediaMetadata
{
    TimeSpan Duration { get; set; }
}