﻿using ImageCare.Core.Domain.Media.Metadata;

using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;

namespace ImageCare.Core.Domain.Media;

internal sealed class JpegMediaPreviewProvider : IMediaPreviewProvider
{
    /// <inheritdoc />
    public IMediaMetadata GetMediaMetadata(string url)
    {
        var readers = new IJpegSegmentMetadataReader[] { new ExifReader() };

        var directories = JpegMetadataReader.ReadMetadata(url, readers);

        if (directories.FirstOrDefault(d => d.Name.Equals("Exif SubIFD", StringComparison.OrdinalIgnoreCase)) is { } exifDirectory &&
            exifDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTime) && 
            exifDirectory.TryGetInt32(ExifDirectoryBase.TagExifImageWidth, out var width) && 
            exifDirectory.TryGetInt32(ExifDirectoryBase.TagExifImageHeight, out var height))
        {
            var jpegMediaMetadata = new JpegMediaMetadata(dateTime, width, height);

            if (exifDirectory.TryGetInt32(ExifSubIfdDirectory.TagIsoEquivalent, out var iso))
            {
                jpegMediaMetadata.Iso = iso;
            }

            jpegMediaMetadata.Aperture = exifDirectory.GetDescription(ExifDirectoryBase.TagFNumber);
            jpegMediaMetadata.ShutterSpeed = exifDirectory.GetDescription(ExifDirectoryBase.TagExposureTime);

            if (directories.FirstOrDefault(d => d.Name.Equals("Exif IFD0", StringComparison.OrdinalIgnoreCase)) is { } ifd0Directory)
            {
                if (ifd0Directory.TryGetInt32(ExifDirectoryBase.TagOrientation, out var orientationInt))
                {
                    jpegMediaMetadata.Orientation = (ExifOrientation)orientationInt;
                }
            }

            return jpegMediaMetadata;
        }

        return new UnsupportedMediaMetadata();
    }

    /// <inheritdoc />
    public Stream GetPreviewJpegStream(string url, MediaPreviewSize size)
    {
        return File.OpenRead(url);
    }
}