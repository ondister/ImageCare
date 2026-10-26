using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;

using LibRawDotNet;

using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

using Directory = MetadataExtractor.Directory;

namespace ImageCare.Core.Domain.Media;

internal sealed class DngMediaPreviewProvider : IMediaPreviewProvider
{
	private const int NewSubfileTypeTag = 0x00FE;
	private const int DefaultCropSizeTag = 0xC620;
	private const int FullResolutionSubfileType = 0;
	private static readonly int[] PreviewIndices = [1, 2, 0, 3, 4, 5, 6];

	public IMediaMetadata GetMediaMetadata(string url)
	{
		try
		{
			using var stream = new FileStream(url, FileMode.Open, FileAccess.Read, FileShare.Read);
			var directories = ImageMetadataReader.ReadMetadata(stream);
			var fallbackCreationTime = new FileInfo(url).CreationTime;

			if (TryExtractBasicMetadata(directories, fallbackCreationTime, out var dateTime, out var width, out var height))
			{
				return CreateRawMediaMetadata(directories, dateTime, width, height);
			}
		}
		catch (FileNotFoundException ex)
		{
			throw new MediaPreviewProviderException($"Failed to open DNG file: {url}", ex);
		}
		catch (Exception ex)
		{
			throw new MediaPreviewProviderException($"Failed to get metadata from DNG file: {url}", ex);
		}

		return CreateUnsupportedMetadata(url);
	}

	public Stream GetPreviewJpegStream(string url, MediaPreviewSize size)
	{
		try
		{
			_ = size switch
			{
				MediaPreviewSize.Small or MediaPreviewSize.Medium or MediaPreviewSize.Large => true,
				_ => throw new ArgumentOutOfRangeException(nameof(size), size, null)
			};

			using var libRawData = LibRawData.OpenFile(url);

			// DNG files normally expose one embedded JPEG preview. The index varies between
			// writers, so try the common positions before falling back to decoding the raw data.
			foreach (var previewIndex in PreviewIndices)
			{
				try
				{
					return libRawData.GetPreviewJpegStream(previewIndex);
				}
				catch (LibRawException)
				{
					// Try the next preview index.
				}
			}

			return libRawData.GetRawJpegStream(90);
		}
		catch (Exception ex)
		{
			throw new MediaPreviewProviderException($"Failed to get preview from DNG file: {url}", ex);
		}
	}

	public DateTime? GetCreationDateTime(string url)
	{
		var metadata = GetMediaMetadata(url);
		return metadata.CreationDateTime;
	}

	private static bool TryExtractBasicMetadata(
		IReadOnlyList<Directory> directories,
		DateTime fallbackCreationTime,
		out DateTime dateTime,
		out int width,
		out int height)
	{
		dateTime = FindCreationDateTime(directories) ?? fallbackCreationTime;

		var mainDirectory = FindFullResolutionDirectory(directories)
		                    ?? FindLargestImageDirectory(directories);

		if (mainDirectory == null
		    || !TryGetImageDimensions(mainDirectory, out width, out height)
		    || dateTime == DateTime.MinValue)
		{
			width = 0;
			height = 0;
			return false;
		}

		ApplyDefaultCropSize(mainDirectory, ref width, ref height);
		return true;
	}

	private static RawMediaMetadata CreateRawMediaMetadata(
		IReadOnlyList<Directory> directories,
		DateTime dateTime,
		int width,
		int height)
	{
		var metadata = new RawMediaMetadata(dateTime, width, height)
		{
			Location = GetLocation(directories)
		};

		var mainDirectory = FindFullResolutionDirectory(directories)
		                    ?? FindLargestImageDirectory(directories);
		var exposureDirectory = FindExposureDirectory(directories);
		var ifd0Directory = directories.OfType<ExifIfd0Directory>().FirstOrDefault();

		FillExifParameters(exposureDirectory, metadata);

		if (mainDirectory != null)
		{
			FillMetadata(mainDirectory, metadata);
		}

		if (ifd0Directory != null)
		{
			if (ifd0Directory.TryGetInt32(ExifDirectoryBase.TagOrientation, out var orientationInt))
			{
				metadata.Orientation = (ExifOrientation)orientationInt;
			}

			FillMetadata(ifd0Directory, metadata);
		}

		if (exposureDirectory != null)
		{
			FillMetadata(exposureDirectory, metadata);
		}

		return metadata;
	}

	private static DateTime? FindCreationDateTime(IReadOnlyList<Directory> directories)
	{
		int[] dateTags =
		[
			ExifDirectoryBase.TagDateTimeOriginal,
			ExifDirectoryBase.TagDateTimeDigitized,
			ExifDirectoryBase.TagDateTime
		];

		foreach (var tag in dateTags)
		{
			foreach (var directory in directories)
			{
				if (directory.TryGetDateTime(tag, out var dateTime) && dateTime > DateTime.MinValue)
				{
					return dateTime;
				}
			}
		}

		return null;
	}

	private static Directory? FindFullResolutionDirectory(IReadOnlyList<Directory> directories)
	{
		return directories.FirstOrDefault(directory =>
			directory.TryGetInt32(NewSubfileTypeTag, out var subfileType)
			&& subfileType == FullResolutionSubfileType
			&& TryGetImageDimensions(directory, out _, out _));
	}

	private static Directory? FindLargestImageDirectory(IReadOnlyList<Directory> directories)
	{
		Directory? result = null;
		long largestArea = 0;

		foreach (var directory in directories)
		{
			if (!TryGetImageDimensions(directory, out var width, out var height))
			{
				continue;
			}

			var area = (long)width * height;
			if (area > largestArea)
			{
				largestArea = area;
				result = directory;
			}
		}

		return result;
	}

	private static bool TryGetImageDimensions(Directory directory, out int width, out int height)
	{
		if (!directory.TryGetInt32(ExifDirectoryBase.TagImageWidth, out width)
		    && !directory.TryGetInt32(ExifDirectoryBase.TagExifImageWidth, out width))
		{
			height = 0;
			return false;
		}

		if (!directory.TryGetInt32(ExifDirectoryBase.TagImageHeight, out height)
		    && !directory.TryGetInt32(ExifDirectoryBase.TagExifImageHeight, out height))
		{
			width = 0;
			return false;
		}

		return width > 0 && height > 0;
	}

	private static void ApplyDefaultCropSize(Directory directory, ref int width, ref int height)
	{
		var cropSize = directory.GetRationalArray(DefaultCropSizeTag);
		if (cropSize is not { Length: >= 2 })
		{
			return;
		}

		var cropWidth = (int)Math.Round(cropSize[0].ToDouble());
		var cropHeight = (int)Math.Round(cropSize[1].ToDouble());
		if (cropWidth > 0 && cropHeight > 0 && cropWidth <= width && cropHeight <= height)
		{
			width = cropWidth;
			height = cropHeight;
		}
	}

	private static Directory? FindExposureDirectory(IReadOnlyList<Directory> directories)
	{
		return directories.FirstOrDefault(directory =>
			       directory.ContainsTag(ExifDirectoryBase.TagIsoEquivalent))
		       ?? directories.FirstOrDefault(directory =>
			       directory.ContainsTag(ExifDirectoryBase.TagFNumber)
			       || directory.ContainsTag(ExifDirectoryBase.TagExposureTime));
	}

	private static void FillExifParameters(Directory? directory, RawMediaMetadata metadata)
	{
		if (directory == null)
		{
			return;
		}

		if (directory.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out var iso))
		{
			metadata.Iso = iso;
		}

		var aperture = directory.GetDescription(ExifDirectoryBase.TagFNumber);
		if (!string.IsNullOrEmpty(aperture))
		{
			metadata.Aperture = aperture;
		}

		var shutterSpeed = directory.GetDescription(ExifDirectoryBase.TagExposureTime);
		if (!string.IsNullOrEmpty(shutterSpeed))
		{
			metadata.ShutterSpeed = shutterSpeed;
		}
	}

	private static void FillMetadata(Directory directory, RawMediaMetadata metadata)
	{
		foreach (var tag in directory.Tags)
		{
			var description = tag.Description;
			if (string.IsNullOrEmpty(description))
			{
				continue;
			}

			metadata.AddOrUpdateMetadata(tag.Name, description);
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

	private static Location GetLocation(IReadOnlyList<Directory> directories)
	{
		var gpsDirectory = directories.OfType<GpsDirectory>().FirstOrDefault();
		if (gpsDirectory == null)
		{
			return Location.Empty;
		}

		var latitude = gpsDirectory.GetRationalArray(GpsDirectory.TagLatitude);
		var longitude = gpsDirectory.GetRationalArray(GpsDirectory.TagLongitude);
		var altitude = 0.0;

		if (gpsDirectory.TryGetRational(GpsDirectory.TagAltitude, out var rationalAltitude))
		{
			altitude = rationalAltitude.ToDouble();
		}

		if (latitude is { Length: 3 } && longitude is { Length: 3 })
		{
			var decimalLatitude = ConvertGpsCoordinateToDecimal(latitude);
			var decimalLongitude = ConvertGpsCoordinateToDecimal(longitude);

			var latRef = gpsDirectory.GetString(GpsDirectory.TagLatitudeRef);
			var lonRef = gpsDirectory.GetString(GpsDirectory.TagLongitudeRef);

			if (latRef == "S")
			{
				decimalLatitude = -decimalLatitude;
			}

			if (lonRef == "W")
			{
				decimalLongitude = -decimalLongitude;
			}

			return new Location(decimalLongitude, decimalLatitude, altitude);
		}

		return Location.Empty;
	}

	private static double ConvertGpsCoordinateToDecimal(Rational[] coordinate)
	{
		var degrees = coordinate[0].ToDouble();
		var minutes = coordinate[1].ToDouble();
		var seconds = coordinate[2].ToDouble();

		return degrees + minutes / 60.0 + seconds / 3600.0;
	}
}
