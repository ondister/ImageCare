using ImageCare.Core.Domain.Media.Metadata;

using LibRawDotNet;

using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;

namespace ImageCare.Core.Domain.Media;

internal sealed class RawMediaPreviewProvider : IMediaPreviewProvider
{
    /// <inheritdoc />
    public IMediaMetadata GetMediaMetadata(string url)
    {
        using (var stream = new FileStream(url,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            var directories = QuickTimeMetadataReader.ReadMetadata(stream);

            if (directories.FirstOrDefault(d => d.Name.Equals("Exif SubIFD", StringComparison.OrdinalIgnoreCase)) is { } exifDirectory &&
                exifDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTime) &&
                exifDirectory.TryGetInt32(ExifDirectoryBase.TagExifImageWidth, out var width) &&
                exifDirectory.TryGetInt32(ExifDirectoryBase.TagExifImageHeight, out var height))
            {
                var rawMediaMetadata = new RawMediaMetadata(dateTime, width, height);

                if (exifDirectory.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out var iso))
                {
                    rawMediaMetadata.Iso = iso;
                }

                rawMediaMetadata.Aperture = exifDirectory.GetDescription(ExifDirectoryBase.TagAperture);
                rawMediaMetadata.ShutterSpeed = exifDirectory.GetDescription(ExifDirectoryBase.TagShutterSpeed);

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