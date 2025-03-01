using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.Preview;

using LibRawDotNet;

using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

using Directory = MetadataExtractor.Directory;

namespace ImageCare.Core.Domain.Media;

internal sealed class Cr3MediaPreviewProvider : IMediaPreviewProvider
{
	/// <inheritdoc />
	public IMediaMetadata GetMediaMetadata(string url)
	{
		using (var stream = new FileStream(url, FileMode.Open, FileAccess.Read, FileShare.Read))
		{
			var directories = ImageMetadataReader.ReadMetadata(stream);
			
			var location = GetLocation(directories);

			var mainMetadataDirectory = directories.FirstOrDefault(d => d.ContainsTag(ExifDirectoryBase.TagDateTimeOriginal) && d.ContainsTag(ExifDirectoryBase.TagExifImageWidth) && d.ContainsTag(ExifDirectoryBase.TagExifImageHeight));

			if (mainMetadataDirectory is { } exifDirectory && exifDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTime) && exifDirectory.TryGetInt32(ExifDirectoryBase.TagExifImageWidth, out var width) && exifDirectory.TryGetInt32(ExifDirectoryBase.TagExifImageHeight, out var height))
			{
				var rawMediaMetadata = new RawMediaMetadata(dateTime, width, height)
				{
					Location = location
				};

				if (exifDirectory.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out var iso))
				{
					rawMediaMetadata.Iso = iso;
				}

				rawMediaMetadata.Aperture = exifDirectory.GetDescription(ExifDirectoryBase.TagFNumber);
				rawMediaMetadata.ShutterSpeed = exifDirectory.GetDescription(ExifDirectoryBase.TagExposureTime);

				FillAllMetaData(mainMetadataDirectory, rawMediaMetadata);

				if (directories.FirstOrDefault(d => d.Name.Equals("Exif IFD0", StringComparison.OrdinalIgnoreCase)) is { } ifd0Directory)
				{
					if (ifd0Directory.TryGetInt32(ExifDirectoryBase.TagOrientation, out var orientationInt))
					{
						rawMediaMetadata.Orientation = (ExifOrientation)orientationInt;
					}

					FillAllMetaData(ifd0Directory, rawMediaMetadata);
				}

				return rawMediaMetadata;
			}
		}

		return new UnsupportedMediaMetadata();
	}

	/// <inheritdoc />
	public Stream GetPreviewJpegStream(string url, MediaPreviewSize size)
	{
		using (var libRawData = LibRawData.OpenFile(url))
		{
			return libRawData.GetPreviewJpegStream((int)size);
		}
	}

	private Location GetLocation(IReadOnlyList<Directory> directories)
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
			// Convert latitude to decimal
			var latDegrees = latitude[0].ToDouble();
			var latMinutes = latitude[1].ToDouble();
			var latSeconds = latitude[2].ToDouble();
			var decimalLatitude = latDegrees + latMinutes / 60.0 + latSeconds / 3600.0;

			// Convert longitude to decimal
			var lonDegrees = longitude[0].ToDouble();
			var lonMinutes = longitude[1].ToDouble();
			var lonSeconds = longitude[2].ToDouble();
			var decimalLongitude = lonDegrees + lonMinutes / 60.0 + lonSeconds / 3600.0;

			// Check for hemisphere (South or West)
			var latRef = gpsDirectory.GetString(GpsDirectory.TagLatitudeRef); // "N" or "S"
			var lonRef = gpsDirectory.GetString(GpsDirectory.TagLongitudeRef); // "E" or "W"

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

	private void FillAllMetaData(Directory metadataDirectory, RawMediaMetadata rawMediaMetadata)
	{
		foreach (var metadata in metadataDirectory.Tags.Where(t => !string.IsNullOrEmpty(t.Description)))
		{
			rawMediaMetadata.AddOrUpdateMetadata(metadata.Name, metadata.Description);
		}
	}
}