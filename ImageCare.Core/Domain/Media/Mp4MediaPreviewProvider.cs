using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.Preview;
using MetadataExtractor;
using MetadataExtractor.Formats.QuickTime;

using Directory = MetadataExtractor.Directory;

namespace ImageCare.Core.Domain.Media;

internal sealed class Mp4MediaPreviewProvider : IMediaPreviewProvider
{
    private const string _unsupportedMediaPreview = @"Domain\Media\Assets\mp4_media_preview.jpg";

    /// <inheritdoc />
    public IMediaMetadata GetMediaMetadata(string url)
    {
        using (var stream = new FileStream(url, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var directories = QuickTimeMetadataReader.ReadMetadata(stream);

            if (directories.FirstOrDefault(d => d.Name.Equals("QuickTime Track Header", StringComparison.OrdinalIgnoreCase)) is { } trackMetadataDirectory
             && trackMetadataDirectory.TryGetDateTime(QuickTimeTrackHeaderDirectory.TagCreated, out var dateTime)
             && trackMetadataDirectory.TryGetInt32(QuickTimeTrackHeaderDirectory.TagWidth, out var width)
             && trackMetadataDirectory.TryGetInt32(QuickTimeTrackHeaderDirectory.TagHeight, out var height))
            {
                var videoMediaMetadata = new VideoMediaMetadata(dateTime, width, height);
                FillAllMetaData(trackMetadataDirectory,videoMediaMetadata);

                if (directories.FirstOrDefault(d => d.Name.Equals("QuickTime Movie Header", StringComparison.OrdinalIgnoreCase)) is { } trackMovieDirectory)
                {
                    if (trackMovieDirectory.GetObject(QuickTimeMovieHeaderDirectory.TagDuration) is TimeSpan duration)
                    {
                        videoMediaMetadata.Duration = duration;
                    }

                    FillAllMetaData(trackMovieDirectory, videoMediaMetadata);

                    return videoMediaMetadata;
                }

                return videoMediaMetadata;
            }
        }

        return new UnsupportedMediaMetadata();
    }

    /// <inheritdoc />
    public Stream GetPreviewJpegStream(string url, MediaPreviewSize size)
    {
        return File.OpenRead(_unsupportedMediaPreview);
    }

    private void FillAllMetaData(Directory metadataDirectory, AllMetadataWrapper mediaMetadata)
    {
        foreach (var metadata in metadataDirectory.Tags.Where(t => !string.IsNullOrEmpty(t.Description)))
        {
            mediaMetadata.AddOrUpdateMetadata(metadata.Name, metadata.Description);
        }
    }
}