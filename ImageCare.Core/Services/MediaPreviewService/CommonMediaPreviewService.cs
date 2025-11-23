using System.Collections.Concurrent;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;

using Polly;
using Polly.Retry;

namespace ImageCare.Core.Services.MediaPreviewService;

public sealed class CommonMediaPreviewService : IMediaPreviewService, IDisposable
{
    private const string ExceptionMessage = "Unexpected exception in Image service";
    private const int MaxMetadataCacheSize = 10000;

    private readonly AsyncRetryPolicy _fileOperationsRetryPolicy;
    private readonly MediaPreviewProvidersFactory _previewProvidersFactory;
    private readonly ConcurrentDictionary<string, IMediaMetadata> _cachedMetadata;
    private readonly CancellationTokenSource _disposalTokenSource = new();

    public CommonMediaPreviewService()
    {
        _previewProvidersFactory = new MediaPreviewProvidersFactory();
        _cachedMetadata = new ConcurrentDictionary<string, IMediaMetadata>();

        _fileOperationsRetryPolicy = Policy
                                     .Handle<Exception>()
                                     .WaitAndRetryAsync(
                                         retryCount: 6, // Exponential backoff: 1s, 2s, 4s, 8s, 16s, 32s
                                         sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt - 1)));
    }

    public void Dispose()
    {
        _disposalTokenSource.Cancel();
        _disposalTokenSource.Dispose();
        _cachedMetadata.Clear();
    }

    public async Task<Stream> GetJpegImageStreamAsync(MediaPreview imagePreview,
                                                      MediaPreviewSize imagePreviewSize,
                                                      CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposalTokenSource.Token);

        try
        {
            return await _fileOperationsRetryPolicy.ExecuteAsync(
                       async ct =>
                       {
                           var previewProvider = _previewProvidersFactory.GetMediaPreviewProvider(imagePreview.MediaFormat);
                           return await Task.Run(
                                      () => previewProvider.GetPreviewJpegStream(imagePreview.Url, imagePreviewSize),
                                      ct);
                       },
                       linkedCts.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ServiceException(ExceptionMessage, exception);
        }
    }

    public async Task<MediaPreview?> GetMediaPreviewAsync(string imagePath)
    {
        try
        {
            var fileInfo = new FileInfo(imagePath);
            var fileModel = new FileModel(fileInfo.Name, fileInfo.FullName, fileInfo.LastWriteTime);

            return await CreateMediaPreviewAsync(fileModel);
        }
        catch (Exception exception)
        {
            throw new ServiceException(ExceptionMessage, exception);
        }
    }

    public async Task<IMediaMetadata> GetMediaMetadataAsync(MediaPreview mediaPreview)
    {
        if (_cachedMetadata.TryGetValue(mediaPreview.Url, out var cachedMetadata))
        {
            return cachedMetadata;
        }

        try
        {
            var metadata = await _fileOperationsRetryPolicy.ExecuteAsync(
                               async ct =>
                               {
                                   var previewProvider = _previewProvidersFactory.GetMediaPreviewProvider(mediaPreview.MediaFormat);
                                   return await Task.Run(
                                              () => previewProvider.GetMediaMetadata(mediaPreview.Url),
                                              ct);
                               },
                               _disposalTokenSource.Token);

            // Limit cache size
            if (_cachedMetadata.Count >= MaxMetadataCacheSize)
            {
                var firstKey = _cachedMetadata.Keys.First();
                _cachedMetadata.TryRemove(firstKey, out _);
            }

            _cachedMetadata.TryAdd(mediaPreview.Url, metadata);
            return metadata;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ServiceException(ExceptionMessage, exception);
        }
    }

    public async Task<DateTime> GetCreationDateTime(MediaPreview mediaPreview)
    {
        try
        {
            var metadata = await GetMediaMetadataAsync(mediaPreview);
            return metadata.CreationDateTime;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ServiceException(ExceptionMessage, exception);
        }
    }

    private async Task<MediaPreview> CreateMediaPreviewAsync(FileModel fileModel)
    {
        return await Task.Run(() =>
        {
            var mediaFormat = MediaFormat.Create(fileModel);
            return new MediaPreview(fileModel.Name, fileModel.FullName, mediaFormat, 200);
        });
    }
}