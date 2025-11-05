using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;

using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;

using Directory = MetadataExtractor.Directory;

namespace ImageCare.Core.Domain.Media;

internal sealed class JpegMediaPreviewProvider : IMediaPreviewProvider
{
	public IMediaMetadata GetMediaMetadata(string url)
	{
		try
		{
			var readers = new IJpegSegmentMetadataReader[] { new ExifReader() };
			var directories = JpegMetadataReader.ReadMetadata(url, readers);

			var exifDirectory = FindExifSubIfdDirectory(directories);

			if (exifDirectory != null && TryExtractBasicMetadata(exifDirectory, out var dateTime, out var width, out var height))
			{
				return CreateJpegMediaMetadata(exifDirectory, directories, dateTime, width, height);
			}
		}
		catch (FileNotFoundException ex)
		{
			throw new MediaPreviewProviderException($"Failed to open JPEG file: {url}", ex);
		}
		catch (Exception ex)
		{
			throw new MediaPreviewProviderException($"Failed to get metadata from JPEG file: {url}", ex);
		}

		return CreateUnsupportedMetadata(url);
	}

	public Stream GetPreviewJpegStream(string url, MediaPreviewSize size)
	{
		try
		{
			return File.OpenRead(url);
		}
		catch (Exception ex)
		{
			throw new MediaPreviewProviderException($"Failed to get preview from JPEG file: {url}", ex);
		}
	}

	public DateTime? GetCreationDateTime(string url)
	{
		var metadata = GetMediaMetadata(url);
		return metadata.CreationDateTime;
	}

	private static Directory? FindExifSubIfdDirectory(IReadOnlyList<Directory> directories)
	{
		return directories.FirstOrDefault(d =>
			d.Name.Equals("Exif SubIFD", StringComparison.OrdinalIgnoreCase));
	}

	private static bool TryExtractBasicMetadata(Directory directory, out DateTime dateTime, out int width, out int height)
	{
		dateTime = DateTime.MinValue;
		width = 0;
		height = 0;

		return directory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out dateTime) &&
			   directory.TryGetInt32(ExifDirectoryBase.TagExifImageWidth, out width) &&
			   directory.TryGetInt32(ExifDirectoryBase.TagExifImageHeight, out height);
	}

	private static JpegMediaMetadata CreateJpegMediaMetadata(Directory exifDirectory, IReadOnlyList<Directory> allDirectories, DateTime dateTime, int width, int height)
	{
		var metadata = new JpegMediaMetadata(dateTime, width, height);

		FillExifParameters(exifDirectory, metadata);
		FillMetadata(exifDirectory, metadata);
		FillOrientationFromIfd0(allDirectories, metadata);

		return metadata;
	}

	private static void FillExifParameters(Directory directory, JpegMediaMetadata metadata)
	{
		if (directory.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out var iso))
		{
			metadata.Iso = iso;
		}

		metadata.Aperture = directory.GetDescription(ExifDirectoryBase.TagFNumber);
		metadata.ShutterSpeed = directory.GetDescription(ExifDirectoryBase.TagExposureTime);
	}

	private static void FillOrientationFromIfd0(IReadOnlyList<Directory> directories, JpegMediaMetadata metadata)
	{
		var ifd0Directory = directories.FirstOrDefault(d =>
			d.Name.Equals("Exif IFD0", StringComparison.OrdinalIgnoreCase));

		if (ifd0Directory?.TryGetInt32(ExifDirectoryBase.TagOrientation, out var orientationInt) == true)
		{
			metadata.Orientation = (ExifOrientation)orientationInt;
			FillMetadata(ifd0Directory, metadata);
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