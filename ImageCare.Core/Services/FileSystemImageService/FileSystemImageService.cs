using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;

using Polly;
using Polly.Retry;

namespace ImageCare.Core.Services.FileSystemImageService;

public sealed class FileSystemImageService : IFileSystemImageService
{
	private const string _exceptionMessage = "Unexpected exception in Image service";

	private readonly RetryPolicy _fileOperationsRetryPolicy;
	private readonly MediaPreviewProvidersFactory _previewProvidersFactory;

	private readonly ConcurrentDictionary<string, IMediaMetadata> _cachedMetadata;

	public FileSystemImageService()
	{
		_previewProvidersFactory = new MediaPreviewProvidersFactory();
		_cachedMetadata = new ConcurrentDictionary<string, IMediaMetadata>();

		_fileOperationsRetryPolicy = Policy
		                             .Handle<Exception>()
		                             .WaitAndRetry(
			                             10,
			                             retryAttempt =>
			                             {
				                             var delay = TimeSpan.FromMilliseconds(Math.Pow(10, retryAttempt));
				                             return delay;
			                             });
	}

	public async Task<Stream> GetJpegImageStreamAsync(MediaPreview imagePreview, MediaPreviewSize imagePreviewSize, CancellationToken cancellationToken = default)
	{
		return await Task.Run(
			       () =>
			       {
				       try
				       {
					       return _fileOperationsRetryPolicy.Execute(
						       () =>
						       {
							       var previewProvider = _previewProvidersFactory.GetMediaPreviewProvider(imagePreview.MediaFormat);

							       return previewProvider.GetPreviewJpegStream(imagePreview.Url, imagePreviewSize);
						       });
				       }
				       catch (Exception exception)
				       {
					       throw new ServiceException(_exceptionMessage, exception);
				       }
			       },
			       cancellationToken);
	}

	/// <inheritdoc />
	public async IAsyncEnumerable<MediaPreview> GetMediaPreviewsAsync(IEnumerable<FileModel> fileModels, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		foreach (var fileModel in fileModels.Where(FileIsSupported))
		{
			if (cancellationToken.IsCancellationRequested)
			{
				break;
			}

			yield return await CreateMediaPreviewAsync(fileModel).ConfigureAwait(false);
		}
	}

	/// <inheritdoc />
	public async Task<MediaPreview?> GetMediaPreviewAsync(string imagePath)
	{
		try
		{
			var fileInfo = new FileInfo(imagePath);

			return await CreateMediaPreviewAsync(new FileModel(fileInfo.Name, fileInfo.FullName, fileInfo.LastWriteTime));
		}
		catch (Exception exception)
		{
			throw new ServiceException(_exceptionMessage, exception);
		}
	}

	/// <inheritdoc />
	public async Task<IMediaMetadata> GetMediaMetadataAsync(MediaPreview mediaPreview)
	{
		return await Task.Run(
			       () =>
			       {
				       if (_cachedMetadata.TryGetValue(mediaPreview.Url, out var metadata))
				       {
					       return metadata;
				       }

				       try
				       {
					       return _fileOperationsRetryPolicy.Execute(
						       () =>
						       {
							       var previewProvider = _previewProvidersFactory.GetMediaPreviewProvider(mediaPreview.MediaFormat);

							       var mediaMetadata = previewProvider.GetMediaMetadata(mediaPreview.Url);

							       _cachedMetadata.TryAdd(mediaPreview.Url, mediaMetadata);

							       return mediaMetadata;
						       });
				       }
				       catch (Exception exception)
				       {
					       throw new ServiceException(_exceptionMessage, exception);
				       }
			       });
	}

	/// <inheritdoc />
	public async Task<DateTime> GetCreationDateTime(MediaPreview mediaPreview)
	{
		return await Task.Run(
			       async () =>
			       {
				       try
				       {
					       var previewProvider = _previewProvidersFactory.GetMediaPreviewProvider(mediaPreview.MediaFormat);
					       var dateTime = previewProvider.GetCreationDateTime(mediaPreview.Url);
					       if (dateTime == null)
					       {
						       var metadata = await GetMediaMetadataAsync(mediaPreview);
						       dateTime = metadata.CreationDateTime;
					       }

					       return dateTime.Value;
				       }
				       catch (Exception exception)
				       {
					       throw new ServiceException(_exceptionMessage, exception);
				       }
			       });
	}

	private async Task<MediaPreview> CreateMediaPreviewAsync(FileModel fileModel)
	{
		var mediaFormat = MediaFormat.Create(fileModel);
		return await Task.Run(() => new MediaPreview(fileModel.Name, fileModel.FullName, mediaFormat, 200));
	}

	private bool FileIsSupported(FileModel model)
	{
		var fileInfo = new FileInfo(model.FullName);
		return MediaFormat.IsSupportedExtension(fileInfo.Extension);
	}
}