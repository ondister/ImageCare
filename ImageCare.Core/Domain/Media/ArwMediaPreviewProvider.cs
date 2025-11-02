using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;

using LibRawDotNet;

using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

using Directory = MetadataExtractor.Directory;

namespace ImageCare.Core.Domain.Media;

internal sealed class ArwMediaPreviewProvider : IMediaPreviewProvider
{
	public IMediaMetadata GetMediaMetadata(string url)
	{
		try
		{
			using var stream = new FileStream(url, FileMode.Open, FileAccess.Read, FileShare.Read);
			var directories = ImageMetadataReader.ReadMetadata(stream);

			var mainMetadataDirectory = FindMainMetadataDirectory(directories);

			if (mainMetadataDirectory != null && TryExtractBasicMetadata(mainMetadataDirectory, out var dateTime, out var width, out var height))
			{
				return CreateRawMediaMetadata(mainMetadataDirectory, directories, dateTime, width, height);
			}
		}
		catch (FileNotFoundException ex)
		{
			throw new MediaPreviewProviderException($"Failed to open ARW file: {url}", ex);
		}
		catch (Exception _)
		{
			return CreateUnsupportedMetadata(url);
		}

		return CreateUnsupportedMetadata(url);
	}

	public Stream GetPreviewJpegStream(string url, MediaPreviewSize size)
	{
		try
		{
			using var libRawData = LibRawData.OpenFile(url);
			var previewIndex = size switch
			{
				MediaPreviewSize.Small => 1,
				MediaPreviewSize.Medium => 0,
				MediaPreviewSize.Large => 0,
				_ => throw new ArgumentOutOfRangeException(nameof(size), size, null)
			};

			return libRawData.GetPreviewJpegStream(previewIndex);
		}
		catch (Exception ex)
		{
			throw new MediaPreviewProviderException($"Failed to get preview from ARW file: {url}", ex);
		}
	}

	public DateTime? GetCreationDateTime(string url)
	{
		var metadata = GetMediaMetadata(url);
		return metadata.CreationDateTime;
	}

	// Вспомогательные методы остаются без изменений
	private static Directory? FindMainMetadataDirectory(IReadOnlyList<Directory> directories)
	{
		return directories.FirstOrDefault(d =>
			                                  d.ContainsTag(ExifDirectoryBase.TagDateTimeOriginal) && d.ContainsTag(ExifDirectoryBase.TagExifImageWidth) && d.ContainsTag(ExifDirectoryBase.TagExifImageHeight));
	}

	private static bool TryExtractBasicMetadata(Directory directory, out DateTime dateTime, out int width, out int height)
	{
		dateTime = DateTime.MinValue;
		width = 0;
		height = 0;

		return directory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out dateTime) && directory.TryGetInt32(ExifDirectoryBase.TagExifImageWidth, out width) && directory.TryGetInt32(ExifDirectoryBase.TagExifImageHeight, out height);
	}

	private static RawMediaMetadata CreateRawMediaMetadata(Directory mainDirectory, IReadOnlyList<Directory> allDirectories, DateTime dateTime, int width, int height)
	{
		var metadata = new RawMediaMetadata(dateTime, width, height);

		FillExifParameters(mainDirectory, metadata);
		FillMetadata(mainDirectory, metadata);
		FillOrientationFromIfd0(allDirectories, metadata);

		return metadata;
	}

	private static void FillExifParameters(Directory directory, RawMediaMetadata metadata)
	{
		// ISO
		if (directory.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out var iso))
		{
			metadata.Iso = iso;
		}

		// Aperture (FNumber)
		if (!string.IsNullOrEmpty(directory.GetDescription(ExifDirectoryBase.TagFNumber)))
		{
			metadata.Aperture = directory.GetDescription(ExifDirectoryBase.TagFNumber);
		}

		// Shatter Speed (ExposureTime)
		if (!string.IsNullOrEmpty(directory.GetDescription(ExifDirectoryBase.TagExposureTime)))
		{
			metadata.ShutterSpeed = directory.GetDescription(ExifDirectoryBase.TagExposureTime);
		}
	}

	private static void FillOrientationFromIfd0(IReadOnlyList<Directory> directories, RawMediaMetadata metadata)
	{
		var ifd0Directory = directories.FirstOrDefault(d =>
			                                               d.Name.Equals("Exif IFD0", StringComparison.OrdinalIgnoreCase));

		if (ifd0Directory?.TryGetInt32(ExifDirectoryBase.TagOrientation, out var orientationInt) == true)
		{
			metadata.Orientation = (ExifOrientation)orientationInt;
			FillMetadata(ifd0Directory, metadata);
		}
	}

	private static void FillMetadata(Directory directory, RawMediaMetadata metadata)
	{
		foreach (var tag in directory.Tags.Where(t => !string.IsNullOrEmpty(t.Description)))
		{
			metadata.AddOrUpdateMetadata(tag.Name, tag.Description);
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