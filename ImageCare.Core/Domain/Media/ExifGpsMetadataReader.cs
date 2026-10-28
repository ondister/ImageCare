using ImageCare.Core.Domain.Media.Metadata;

using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

using Directory = MetadataExtractor.Directory;

namespace ImageCare.Core.Domain.Media;

internal static class ExifGpsMetadataReader
{
    public static void Apply(IReadOnlyList<Directory> directories, ImageMediaMetadata metadata)
    {
        var gpsDirectory = directories.OfType<GpsDirectory>().FirstOrDefault();
        if (gpsDirectory == null)
        {
            return;
        }

        foreach (var tag in gpsDirectory.Tags)
        {
            if (!string.IsNullOrEmpty(tag.Description))
            {
                metadata.AddOrUpdateMetadata(tag.Name, tag.Description);
            }
        }

        metadata.Location = GetLocation(gpsDirectory);
    }

    private static Location GetLocation(GpsDirectory gpsDirectory)
    {
        var latitude = gpsDirectory.GetRationalArray(GpsDirectory.TagLatitude);
        var longitude = gpsDirectory.GetRationalArray(GpsDirectory.TagLongitude);
        if (latitude is not { Length: >= 3 } || longitude is not { Length: >= 3 })
        {
            return Location.Empty;
        }

        var decimalLatitude = ConvertGpsCoordinateToDecimal(latitude);
        var decimalLongitude = ConvertGpsCoordinateToDecimal(longitude);
        var latitudeReference = gpsDirectory.GetString(GpsDirectory.TagLatitudeRef);
        var longitudeReference = gpsDirectory.GetString(GpsDirectory.TagLongitudeRef);

        if (string.Equals(latitudeReference, "S", StringComparison.OrdinalIgnoreCase))
        {
            decimalLatitude = -decimalLatitude;
        }

        if (string.Equals(longitudeReference, "W", StringComparison.OrdinalIgnoreCase))
        {
            decimalLongitude = -decimalLongitude;
        }

        if (!double.IsFinite(decimalLatitude) ||
            !double.IsFinite(decimalLongitude) ||
            decimalLatitude is < -90 or > 90 ||
            decimalLongitude is < -180 or > 180)
        {
            return Location.Empty;
        }

        var altitude = 0.0;
        if (gpsDirectory.TryGetRational(GpsDirectory.TagAltitude, out var rationalAltitude))
        {
            altitude = rationalAltitude.ToDouble();
            if (gpsDirectory.TryGetInt32(GpsDirectory.TagAltitudeRef, out var altitudeReference) &&
                altitudeReference == 1)
            {
                altitude = -altitude;
            }
        }

        return new Location(decimalLongitude, decimalLatitude, altitude);
    }

    private static double ConvertGpsCoordinateToDecimal(Rational[] coordinate)
    {
        var degrees = coordinate[0].ToDouble();
        var minutes = coordinate[1].ToDouble();
        var seconds = coordinate[2].ToDouble();

        return degrees + minutes / 60.0 + seconds / 3600.0;
    }
}
