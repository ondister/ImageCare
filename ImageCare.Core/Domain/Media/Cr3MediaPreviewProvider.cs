﻿using ImageCare.Core.Domain.Media.Metadata;

using LibRawDotNet;

using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace ImageCare.Core.Domain.Media;

internal sealed class Cr3MediaPreviewProvider : IMediaPreviewProvider
{
    /// <inheritdoc />
    public IMediaMetadata GetMediaMetadata(string url)
    {
        using (var stream = new FileStream(url, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var directories = ImageMetadataReader.ReadMetadata(stream);

            var mainMetadataDirectory = directories.FirstOrDefault(d => d.ContainsTag(ExifDirectoryBase.TagDateTimeOriginal) && d.ContainsTag(ExifDirectoryBase.TagExifImageWidth) && d.ContainsTag(ExifDirectoryBase.TagExifImageHeight));

            if (mainMetadataDirectory is { } exifDirectory && exifDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTime) && exifDirectory.TryGetInt32(ExifDirectoryBase.TagExifImageWidth, out var width) && exifDirectory.TryGetInt32(ExifDirectoryBase.TagExifImageHeight, out var height))
            {
                var rawMediaMetadata = new RawMediaMetadata(dateTime, width, height);

                if (exifDirectory.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out var iso))
                {
                    rawMediaMetadata.Iso = iso;
                }

                rawMediaMetadata.Aperture = exifDirectory.GetDescription(ExifDirectoryBase.TagFNumber);
                rawMediaMetadata.ShutterSpeed = exifDirectory.GetDescription(ExifDirectoryBase.TagExposureTime);

                if (directories.FirstOrDefault(d => d.Name.Equals("Exif IFD0", StringComparison.OrdinalIgnoreCase)) is { } ifd0Directory)
                {
                    if (ifd0Directory.TryGetInt32(ExifDirectoryBase.TagOrientation, out var orientationInt))
                    {
                        rawMediaMetadata.Orientation = (ExifOrientation)orientationInt;
                    }
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
}