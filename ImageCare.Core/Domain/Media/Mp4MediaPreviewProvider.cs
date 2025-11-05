using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;

using MetadataExtractor;
using MetadataExtractor.Formats.QuickTime;

using Directory = MetadataExtractor.Directory;

namespace ImageCare.Core.Domain.Media;

internal sealed class Mp4MediaPreviewProvider : IMediaPreviewProvider
{
	private const string _mp4MediaPreview = @"Domain\Media\Assets\mp4_media_preview.jpg";

	public IMediaMetadata GetMediaMetadata(string url)
	{
		try
		{
			using var stream = new FileStream(url, FileMode.Open, FileAccess.Read, FileShare.Read);
			var directories = QuickTimeMetadataReader.ReadMetadata(stream);

			var trackMetadataDirectory = FindTrackMetadataDirectory(directories);

			if (trackMetadataDirectory != null &&
				TryExtractBasicMetadata(trackMetadataDirectory, out var dateTime, out var width, out var height))
			{
				return CreateVideoMediaMetadata(trackMetadataDirectory, directories, dateTime, width, height);
			}
		}
		catch (FileNotFoundException ex)
		{
			throw new MediaPreviewProviderException($"Failed to open MP4 file: {url}", ex);
		}
		catch (Exception ex)
		{
			throw new MediaPreviewProviderException($"Failed to get metadata from MP4 file: {url}", ex);
		}

		return CreateUnsupportedMetadata(url);
	}

	public Stream GetPreviewJpegStream(string url, MediaPreviewSize size)
	{
		try
		{
			var previewPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _mp4MediaPreview);
			return File.OpenRead(previewPath);
		}
		catch (Exception ex)
		{
			throw new MediaPreviewProviderException($"Failed to get preview for MP4 file: {url}", ex);
		}
	}

	public DateTime? GetCreationDateTime(string url)
	{
		var metadata = GetMediaMetadata(url);
		return metadata.CreationDateTime;
	}

	private static Directory? FindTrackMetadataDirectory(IReadOnlyList<Directory> directories)
	{
		return directories.FirstOrDefault(d =>
			d.Name.Equals("QuickTime Track Header", StringComparison.OrdinalIgnoreCase));
	}

	private static bool TryExtractBasicMetadata(Directory directory, out DateTime dateTime, out int width, out int height)
	{
		dateTime = DateTime.MinValue;
		width = 0;
		height = 0;

		return directory.TryGetDateTime(QuickTimeTrackHeaderDirectory.TagCreated, out dateTime) &&
			   directory.TryGetInt32(QuickTimeTrackHeaderDirectory.TagWidth, out width) &&
			   directory.TryGetInt32(QuickTimeTrackHeaderDirectory.TagHeight, out height);
	}

	private static VideoMediaMetadata CreateVideoMediaMetadata(Directory trackDirectory, IReadOnlyList<Directory> allDirectories, DateTime dateTime, int width, int height)
	{
		var metadata = new VideoMediaMetadata(dateTime, width, height);

		FillMetadata(trackDirectory, metadata);
		FillDurationFromMovieHeader(allDirectories, metadata);

		return metadata;
	}

	private static void FillDurationFromMovieHeader(IReadOnlyList<Directory> directories, VideoMediaMetadata metadata)
	{
		var movieHeaderDirectory = directories.FirstOrDefault(d =>
			d.Name.Equals("QuickTime Movie Header", StringComparison.OrdinalIgnoreCase));

		if (movieHeaderDirectory?.GetObject(QuickTimeMovieHeaderDirectory.TagDuration) is TimeSpan duration)
		{
			metadata.Duration = duration;
			FillMetadata(movieHeaderDirectory, metadata);
		}
	}

	private static void FillMetadata(Directory directory, AllMetadataWrapper mediaMetadata)
	{
		foreach (var tag in directory.Tags.Where(t => !string.IsNullOrEmpty(t.Description)))
		{
			mediaMetadata.AddOrUpdateMetadata(tag.Name, tag.Description);
		}
	}

	private static UnsupportedMediaMetadata CreateUnsupportedMetadata(string url)
	{
		try
		{
			var fileInfo = new FileInfo(url);
			return new UnsupportedMediaMetadata(fileInfo.CreationTime);
		}
		catch (Exception ex)
		{
			throw new MediaPreviewProviderException($"Failed to create unsupported metadata for file: {url}", ex);
		}
	}
}