using ImageCare.Core.Domain;

namespace ImageCare.Core.Services.FileSystemImageService;

public interface IFileSystemImageService
{
    Task<Stream> GetJpegImageStreamAsync(ImagePreview imagePreview,
                                         ImagePreviewSize imagePreviewSize,
                                         CancellationToken cancellationToken = default);

    IAsyncEnumerable<ImagePreview> GetImagePreviewsAsync(IEnumerable<FileModel> fileModels, CancellationToken cancellationToken = default);

    Task<ImagePreview?> GetImagePreviewAsync(string imagePath);
}