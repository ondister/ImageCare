using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;

using LibRawDotNet;

using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

using Directory = MetadataExtractor.Directory;

namespace ImageCare.Core.Domain.Media;

internal sealed class Cr3MediaPreviewProvider : IMediaPreviewProvider
{
	public IMediaMetadata GetMediaMetadata(string url)
	{
		try
		{
			using var stream = new FileStream(url, FileMode.Open, FileAccess.Read, FileShare.Read);
			var directories = ImageMetadataReader.ReadMetadata(stream);

			var location = GetLocation(directories);
			var mainMetadataDirectory = FindMainMetadataDirectory(directories);

			if (mainMetadataDirectory != null && TryExtractBasicMetadata(mainMetadataDirectory, out var dateTime, out var width, out var height))
			{
				return CreateRawMediaMetadata(mainMetadataDirectory, directories, dateTime, width, height, location);
			}
		}
		catch (FileNotFoundException ex)
		{
			throw new MediaPreviewProviderException($"Failed to open CR3 file: {url}", ex);
		}
		catch (Exception ex)
		{
			throw new MediaPreviewProviderException($"Failed to get metadata from CR3 file: {url}", ex);
		}

		return CreateUnsupportedMetadata(url);
	}

	public Stream GetPreviewJpegStream(string url, MediaPreviewSize size)
	{
		try
		{
			using var libRawData = LibRawData.OpenFile(url);
			return libRawData.GetPreviewJpegStream((int)size);
		}
		catch (Exception ex)
		{
			throw new MediaPreviewProviderException($"Failed to get preview from CR3 file: {url}", ex);
		}
	}

	public DateTime? GetCreationDateTime(string url)
	{
		var metadata = GetMediaMetadata(url);
		return metadata.CreationDateTime;
	}

	private static Directory? FindMainMetadataDirectory(IReadOnlyList<Directory> directories)
	{
		return directories.FirstOrDefault(d =>
			d.ContainsTag(ExifDirectoryBase.TagDateTimeOriginal) &&
			d.ContainsTag(ExifDirectoryBase.TagExifImageWidth) &&
			d.ContainsTag(ExifDirectoryBase.TagExifImageHeight));
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

	private static RawMediaMetadata CreateRawMediaMetadata(Directory mainDirectory, IReadOnlyList<Directory> allDirectories, DateTime dateTime, int width, int height, Location location)
	{
		var metadata = new RawMediaMetadata(dateTime, width, height)
		{
			Location = location
		};

		FillExifParameters(mainDirectory, metadata);
		FillMetadata(mainDirectory, metadata);
		FillOrientationFromIfd0(allDirectories, metadata);

		return metadata;
	}

	private static void FillExifParameters(Directory directory, RawMediaMetadata metadata)
	{
		if (directory.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out var iso))
		{
			metadata.Iso = iso;
		}

		metadata.Aperture = directory.GetDescription(ExifDirectoryBase.TagFNumber);
		metadata.ShutterSpeed = directory.GetDescription(ExifDirectoryBase.TagExposureTime);
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