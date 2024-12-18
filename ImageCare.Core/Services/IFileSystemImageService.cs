using ImageCare.Core.Domain;

namespace ImageCare.Core.Services;

public interface IFileSystemImageService
{
    Task<Stream> GetImageStreamAsync(ImagePreview imagePreview);

    IAsyncEnumerable<ImagePreview> GetImagePreviewsAsync(IEnumerable<FileModel> fileModels);

    Task<ImagePreview?> GetImagePreviewAsync(string imagePath);
}