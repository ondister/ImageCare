using ImageCare.Core.Domain.Annotations;

namespace ImageCare.Core.Services.MediaAnnotationsService;

public interface IMediaAnnotationsStorageService
{
    Task<MediaAnnotationsSnapshot> ReadAsync(
        string mediaPath,
        CancellationToken cancellationToken = default);

    Task<MediaAnnotationsSnapshot> WriteAsync(
        string mediaPath,
        MediaAnnotations annotations,
        AnnotationFileState? expectedState = null,
        CancellationToken cancellationToken = default);
}

public sealed class MediaAnnotationsConcurrentModificationException : IOException
{
    public MediaAnnotationsConcurrentModificationException(string path)
        : base($"The annotations source was modified concurrently: {path}")
    {
        Path = path;
    }

    public string Path { get; }
}
