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
    private const int MaxMetadataCacheSize = 512;

    private readonly AsyncRetryPolicy _fileOperationsRetryPolicy;
    private readonly MediaPreviewProvidersFactory _previewProvidersFactory;
    private readonly ConcurrentDictionary<string, IMediaMetadata> _cachedMetadata;
    private readonly CancellationTokenSource _disposalTokenSource = new();

    public CommonMediaPreviewService(
        MediaPreviewCapabilities? capabilities = null,
        IVideoFrameExtractor? videoFrameExtractor = null)
    {
        _previewProvidersFactory = new MediaPreviewProvidersFactory(capabilities, videoFrameExtractor);
        _cachedMetadata = new ConcurrentDictionary<string, IMediaMetadata>();

        _fileOperationsRetryPolicy = Policy
                                     .Handle<MediaPreviewProviderException>(exception => exception.InnerException is IOException)
                                     .WaitAndRetryAsync(
                                         retryCount: 2,
                                         sleepDurationProvider: retryAttempt => TimeSpan.FromMilliseconds(150 * retryAttempt));
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
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _disposalTokenSource.Token);
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

    public async Task<MediaPreview?> GetMediaPreviewAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileInfo = new FileInfo(imagePath);
            var fileModel = new FileModel(fileInfo.Name, fileInfo.FullName, fileInfo.LastWriteTime);

            return await GetMediaPreviewAsync(fileModel, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ServiceException(ExceptionMessage, exception);
        }
    }

    public Task<MediaPreview?> GetMediaPreviewAsync(
        FileModel fileModel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileModel);
        cancellationToken.ThrowIfCancellationRequested();
        var detectedFormat = MediaFormat.Create(fileModel);
        var enabledFormat = _previewProvidersFactory.Capabilities.Supports(detectedFormat)
            ? detectedFormat
            : MediaFormat.MediaFormatUnknown;
        MediaPreview result = new(fileModel.Name, fileModel.FullName, enabledFormat, 200);
        return Task.FromResult<MediaPreview?>(result);
    }

    public async Task<IMediaMetadata> GetMediaMetadataAsync(
        MediaPreview mediaPreview,
        CancellationToken cancellationToken = default)
    {
        if (_cachedMetadata.TryGetValue(mediaPreview.Url, out var cachedMetadata))
        {
            return cachedMetadata;
        }

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _disposalTokenSource.Token);
            var metadata = await _fileOperationsRetryPolicy.ExecuteAsync(
                               async ct =>
                               {
                                   var previewProvider = _previewProvidersFactory.GetMediaPreviewProvider(mediaPreview.MediaFormat);
                                   return await Task.Run(
                                              () => previewProvider.GetMediaMetadata(mediaPreview.Url),
                                              ct);
                               },
                               linkedCts.Token);

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

}
