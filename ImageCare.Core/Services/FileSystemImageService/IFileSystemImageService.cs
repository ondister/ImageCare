using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.Preview;

namespace ImageCare.Core.Services.FileSystemImageService;

public interface IFileSystemImageService
{
    Task<Stream> GetJpegImageStreamAsync(MediaPreview imagePreview,
                                         MediaPreviewSize imagePreviewSize,
                                         CancellationToken cancellationToken = default);

    IAsyncEnumerable<MediaPreview> GetMediaPreviewsAsync(IEnumerable<FileModel> fileModels, CancellationToken cancellationToken = default);

    Task<MediaPreview?> GetMediaPreviewAsync(string imagePath);

    Task<IMediaMetadata> GetMediaMetadataAsync(MediaPreview mediaPreview);

    Task<DateTime> GetCreationDateTime(MediaPreview mediaPreview);
}