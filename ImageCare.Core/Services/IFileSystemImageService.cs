using ImageCare.Core.Domain;

namespace ImageCare.Core.Services;

public interface IFileSystemImageService
{
    Task<Stream> GetJpegImageStreamAsync(ImagePreview imagePreview,
                                         ImagePreviewSize imagePreviewSize,
                                         CancellationToken cancellationToken = default);

    IAsyncEnumerable<ImagePreview> GetImagePreviewsAsync(IEnumerable<FileModel> fileModels);

    Task<ImagePreview?> GetImagePreviewAsync(string imagePath);
}