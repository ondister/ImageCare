﻿using ImageCare.Core.Domain;
using ImageCare.Core.Domain.MediaFormats;

using Polly;
using Polly.Retry;

namespace ImageCare.Core.Services;

public sealed class FileSystemImageService : IFileSystemImageService
{
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
                                             var delay = TimeSpan.FromMilliseconds(Math.Pow(20, retryAttempt));
                                             return delay;
                                         });
    }

    public async Task<Stream> GetImageStreamAsync(ImagePreview imagePreview)
    {
       return await _fileOperationsRetryPolicy.ExecuteAsync(async () => await GetImageStreamInternalAsync(imagePreview));
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
        var fileInfo = new FileInfo(imagePath);
        if (fileInfo.Exists)
        {
            return await CreateImagePreviewAsync(new FileModel(fileInfo.Name, fileInfo.FullName));
        }

        return null;
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