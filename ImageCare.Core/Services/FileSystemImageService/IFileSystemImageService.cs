using ImageCare.Core.Domain;
using ImageCare.Core.Domain.Media.Metadata;

namespace ImageCare.Core.Services.FileSystemImageService;

public interface IFileSystemImageService
{
    Task<Stream> GetJpegImageStreamAsync(MediaPreview imagePreview,
                                         MediaPreviewSize imagePreviewSize,
                                         CancellationToken cancellationToken = default);

    IAsyncEnumerable<MediaPreview> GetMediaPreviewsAsync(IEnumerable<FileModel> fileModels, CancellationToken cancellationToken = default);

    Task<MediaPreview?> GetMediaPreviewAsync(string imagePath);

    Task<IMediaMetadata> GetMediaMetadataAsync(MediaPreview mediaPreview);
}