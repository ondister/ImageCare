using System.Runtime.CompilerServices;

using ImageCare.Core.Domain;
using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Exceptions;

using Polly;
using Polly.Retry;

namespace ImageCare.Core.Services.FileSystemImageService;

public sealed class FileSystemImageService : IFileSystemImageService
{
    private const string _exceptionMessage = "Unexpected exception in Image service";

    private readonly AsyncRetryPolicy _fileOperationsRetryPolicy;
    private readonly MediaPreviewProvidersFactory _previewProvidersFactory;

    public FileSystemImageService()
    {
        _previewProvidersFactory = new MediaPreviewProvidersFactory();

        _fileOperationsRetryPolicy = Policy
                                     .Handle<Exception>()
                                     .WaitAndRetryAsync(
                                         5,
                                         retryAttempt =>
                                         {
                                             var delay = TimeSpan.FromMilliseconds(Math.Pow(25, retryAttempt));
                                             return delay;
                                         });
    }

    public async Task<Stream> GetJpegImageStreamAsync(ImagePreview imagePreview, ImagePreviewSize imagePreviewSize, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Task.Run(
                       () =>
                       {
                           var previewProvider = _previewProvidersFactory.GetMediaPreviewProvider(imagePreview.MediaFormat);

                           return previewProvider.GetPreviewJpegStream(imagePreview.Url, imagePreviewSize);
                       },
                       cancellationToken);
        }
        catch (Exception exception)
        {
            throw new ServiceException(_exceptionMessage, exception);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ImagePreview> GetImagePreviewsAsync(IEnumerable<FileModel> fileModels, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var fileModel in fileModels.Where(FileIsSupported))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            yield return await CreateImagePreviewAsync(fileModel).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<ImagePreview?> GetImagePreviewAsync(string imagePath)
    {
        try
        {
            var fileInfo = new FileInfo(imagePath);

            return await CreateImagePreviewAsync(new FileModel(fileInfo.Name, fileInfo.FullName));
        }
        catch (Exception exception)
        {
            throw new ServiceException(_exceptionMessage, exception);
        }
    }

    private async Task<ImagePreview> CreateImagePreviewAsync(FileModel fileModel)
    {
        var mediaFormat = MediaFormat.Create(fileModel);
        return await Task.Run(() => new ImagePreview(fileModel.Name, fileModel.FullName, mediaFormat));
    }

    private bool FileIsSupported(FileModel model)
    {
        var fileInfo = new FileInfo(model.FullName);
        return MediaFormat.IsSupportedExtension(fileInfo.Extension);
    }
}