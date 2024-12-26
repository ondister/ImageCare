using ImageCare.Core.Domain;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Exceptions;
using LibRawDotNet;

using Polly;
using Polly.Fallback;
using Polly.Retry;

namespace ImageCare.Core.Services.FileSystemImageService;

public sealed class FileSystemImageService : IFileSystemImageService
{
    private const string _exceptionMessage = "Unexpected exception in Json configuration service";

    private static readonly int soiOffsetIndex = 0xe2;
    private static readonly int lenOffsetIndex = 0x34;
    private readonly AsyncRetryPolicy _fileOperationsRetryPolicy;

    public FileSystemImageService()
    {
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
            return await _fileOperationsRetryPolicy.ExecuteAsync(
                       async () =>
                       {
                           return await Task.Run(
                                      () =>
                                      {
                                          using (var libRawData = LibRawData.OpenFile(imagePreview.Url))
                                          {
                                              return libRawData.GetPreviewJpegStream((int)imagePreviewSize);
                                          }
                                      });
                       });
        }
        catch (Exception exception)
        {
            throw new ServiceException(_exceptionMessage, exception);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ImagePreview> GetImagePreviewsAsync(IEnumerable<FileModel> fileModels)
    {
        foreach (var fileModel in fileModels.Where(f => f.FullName.EndsWith("CR3")))
        {
            yield return await CreateImagePreviewAsync(fileModel);
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

    public static uint SwapBytes(uint x)
    {
        return ((x & 0x000000ff) << 24) + ((x & 0x0000ff00) << 8) + ((x & 0x00ff0000) >> 8) + ((x & 0xff000000) >> 24);
    }

    private async Task<Stream> GetImageStreamInternalAsync(ImagePreview imagePreview)
    {
        return await Task.Run(
                   () =>
                   {
                       using (var fileStream = new FileStream(imagePreview.Url, FileMode.Open, FileAccess.Read, FileShare.Read))
                       {
                           var reader = new BinaryReader(fileStream);
                           reader.BaseStream.Seek(0, SeekOrigin.Begin);
                           reader.ReadBytes(soiOffsetIndex);

                           var offset = SwapBytes(reader.ReadUInt32());
                           reader.BaseStream.Seek(offset + lenOffsetIndex, SeekOrigin.Begin);
                           var len = SwapBytes(reader.ReadUInt32());
                           var data = reader.ReadBytes((int)len);

                           var memoryStream = new MemoryStream(data);

                           reader.Dispose();

                           return memoryStream;
                       }
                   });
    }

    private async Task<ImagePreview> CreateImagePreviewAsync(FileModel fileModel)
    {
        return await Task.Run(() => new ImagePreview(fileModel.Name, fileModel.FullName, MediaFormat.MediaFormatCr3));
    }
}