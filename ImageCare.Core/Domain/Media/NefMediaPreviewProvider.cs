using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;

using LibRawDotNet;

using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

using Directory = MetadataExtractor.Directory;

namespace ImageCare.Core.Domain.Media;

internal sealed class NefMediaPreviewProvider : IMediaPreviewProvider
{
    public IMediaMetadata GetMediaMetadata(string url)
    {
        try
        {
            using var stream = new FileStream(url, FileMode.Open, FileAccess.Read, FileShare.Read);
            var directories = ImageMetadataReader.ReadMetadata(stream);

            if (TryExtractBasicMetadata(directories, out var dateTime, out var width, out var height))
            {
                return CreateRawMediaMetadata(directories, dateTime, width, height);
            }
        }
        catch (FileNotFoundException ex)
        {
            throw new MediaPreviewProviderException($"Failed to open NEF file: {url}", ex);
        }
        catch (Exception ex)
        {
            throw new MediaPreviewProviderException($"Failed to get metadata from NEF file: {url}", ex);
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
                MediaPreviewSize.Small => 3,
                MediaPreviewSize.Medium => 2,
                MediaPreviewSize.Large => 1,
                _ => throw new ArgumentOutOfRangeException(nameof(size), size, null)
            };

            return libRawData.GetPreviewJpegStream(previewIndex);
        }
        catch (Exception ex)
        {
            throw new MediaPreviewProviderException($"Failed to get preview from NEF file: {url}", ex);
        }
    }

    public DateTime? GetCreationDateTime(string url)
    {
        var metadata = GetMediaMetadata(url);
        return metadata.CreationDateTime;
    }

    private static bool TryExtractBasicMetadata(IReadOnlyList<Directory> allDirectories, out DateTime dateTime, out int width, out int height)
    {
        dateTime = DateTime.MinValue;
        width = 0;
        height = 0;

        var dateFound = false;
        var widthFound = false;
        var heightFound = false;

        foreach (var directory in allDirectories)
        {

            if (!dateFound)
            {
                dateFound = directory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out dateTime);
            }

            if (!widthFound)
            {
                widthFound = directory.TryGetInt32(ExifDirectoryBase.TagImageWidth, out width);
            }

            if (!heightFound)
            {
                heightFound = directory.TryGetInt32(ExifDirectoryBase.TagImageHeight, out height);
            }

            if (dateFound && widthFound && heightFound)
            {
                return true;
            }
        }

        return dateFound && widthFound && heightFound;
    }

    private static RawMediaMetadata CreateRawMediaMetadata(IReadOnlyList<Directory> allDirectories, DateTime dateTime, int width, int height)
    {
        var metadata = new RawMediaMetadata(dateTime, width, height);

        FillExifParameters(allDirectories, metadata);
        FillOrientationFromIfd0(allDirectories, metadata);

        return metadata;
    }

    private static void FillExifParameters(IReadOnlyList<Directory> allDirectories, RawMediaMetadata metadata)
    {
       
        var directory = allDirectories.FirstOrDefault(d =>
                                                          d.Name.Equals("Exif SubIFD", StringComparison.OrdinalIgnoreCase) && d.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out _));

        if (directory == null)
        {
            return;
        }

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

        FillMetadata(directory, metadata);

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