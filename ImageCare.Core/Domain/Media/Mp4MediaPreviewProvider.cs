using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;

using MetadataExtractor;
using MetadataExtractor.Formats.QuickTime;

using Directory = MetadataExtractor.Directory;

namespace ImageCare.Core.Domain.Media;

internal sealed class Mp4MediaPreviewProvider : IMediaPreviewProvider
{
    private const int MaxCachedFrames = 128;
    private const long MaxCachedBytes = 64L * 1024 * 1024;

    private readonly IVideoFrameExtractor _videoFrameExtractor;
    private readonly object _extractionGate = new();
    private readonly object _cacheGate = new();
    private readonly Dictionary<FrameCacheKey, CachedFrame> _frameCache = [];
    private readonly LinkedList<FrameCacheKey> _cacheOrder = [];
    private long _cachedBytes;

    public Mp4MediaPreviewProvider(IVideoFrameExtractor? videoFrameExtractor = null)
    {
        _videoFrameExtractor = videoFrameExtractor ?? NoOpVideoFrameExtractor.Instance;
    }

    public IMediaMetadata GetMediaMetadata(string url)
    {
        try
        {
            using var stream = new FileStream(url, FileMode.Open, FileAccess.Read, FileShare.Read);
            var directories = QuickTimeMetadataReader.ReadMetadata(stream);

            var trackMetadataDirectory = FindTrackMetadataDirectory(directories);

            if (trackMetadataDirectory != null &&
                TryExtractBasicMetadata(trackMetadataDirectory, out var dateTime, out var width, out var height))
            {
                return CreateVideoMediaMetadata(trackMetadataDirectory, directories, dateTime, width, height);
            }
        }
        catch (FileNotFoundException ex)
        {
            throw new MediaPreviewProviderException($"Failed to open MP4 file: {url}", ex);
        }
        catch (Exception ex)
        {
            throw new MediaPreviewProviderException($"Failed to get metadata from MP4 file: {url}", ex);
        }

        return CreateUnsupportedMetadata(url);
    }

    public Stream GetPreviewJpegStream(string url, MediaPreviewSize size)
    {
        try
        {
            var frame = TryGetVideoFrame(url, size);
            return frame is { Length: > 0 }
                ? new MemoryStream(frame, writable: false)
                : OpenFallbackPreview();
        }
        catch (Exception ex)
        {
            throw new MediaPreviewProviderException($"Failed to get preview for MP4 file: {url}", ex);
        }
    }

    public DateTime? GetCreationDateTime(string url)
    {
        var metadata = GetMediaMetadata(url);
        return metadata.CreationDateTime;
    }

    private byte[]? TryGetVideoFrame(string url, MediaPreviewSize size)
    {
        if (!File.Exists(url))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(url);
            var fileInfo = new FileInfo(fullPath);
            var maxPixelSize = GetMaxPixelSize(size);
            var key = new FrameCacheKey(
                NormalizePath(fullPath),
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc.Ticks,
                maxPixelSize);

            if (TryGetCachedFrame(key, out var cachedFrame))
            {
                return cachedFrame;
            }

            lock (_extractionGate)
            {
                if (TryGetCachedFrame(key, out cachedFrame))
                {
                    return cachedFrame;
                }

                var position = GetFramePosition(fullPath);
                var frame = _videoFrameExtractor.ExtractJpegFrame(fullPath, position, maxPixelSize);
                if (!IsJpeg(frame) && position > TimeSpan.Zero)
                {
                    frame = _videoFrameExtractor.ExtractJpegFrame(fullPath, TimeSpan.Zero, maxPixelSize);
                }

                if (!IsJpeg(frame))
                {
                    return null;
                }

                RememberFrame(key, frame!);
                return frame;
            }
        }
        catch (Exception ex) when (IsRecoverableExtractionError(ex))
        {
            return null;
        }
    }

    private TimeSpan GetFramePosition(string path)
    {
        try
        {
            if (GetMediaMetadata(path) is VideoMediaMetadata { Duration: var duration } && duration > TimeSpan.Zero)
            {
                if (duration <= TimeSpan.FromSeconds(2))
                {
                    return TimeSpan.FromTicks(duration.Ticks / 2);
                }

                return TimeSpan.FromSeconds(Math.Clamp(duration.TotalSeconds * 0.1, 1, 10));
            }
        }
        catch (MediaPreviewProviderException)
        {
            // Frame extraction may still succeed when the QuickTime metadata is incomplete.
        }

        return TimeSpan.FromSeconds(1);
    }

    private bool TryGetCachedFrame(FrameCacheKey key, out byte[]? frame)
    {
        lock (_cacheGate)
        {
            if (!_frameCache.TryGetValue(key, out var cached))
            {
                frame = null;
                return false;
            }

            _cacheOrder.Remove(cached.OrderNode);
            _cacheOrder.AddLast(cached.OrderNode);
            frame = cached.Bytes;
            return true;
        }
    }

    private void RememberFrame(FrameCacheKey key, byte[] frame)
    {
        if (frame.LongLength > MaxCachedBytes)
        {
            return;
        }

        lock (_cacheGate)
        {
            if (_frameCache.TryGetValue(key, out var existing))
            {
                _cacheOrder.Remove(existing.OrderNode);
                _cacheOrder.AddLast(existing.OrderNode);
                return;
            }

            while (_cacheOrder.Count > 0 &&
                   (_frameCache.Count >= MaxCachedFrames || _cachedBytes + frame.LongLength > MaxCachedBytes))
            {
                var oldestKey = _cacheOrder.First!.Value;
                _cacheOrder.RemoveFirst();
                if (_frameCache.Remove(oldestKey, out var oldest))
                {
                    _cachedBytes -= oldest.Bytes.LongLength;
                }
            }

            var node = _cacheOrder.AddLast(key);
            _frameCache.Add(key, new CachedFrame(frame, node));
            _cachedBytes += frame.LongLength;
        }
    }

    private static Stream OpenFallbackPreview()
    {
        var previewPath = Path.Combine(
            AppContext.BaseDirectory,
            "Domain",
            "Media",
            "Assets",
            "mp4_media_preview.jpg");
        return File.OpenRead(previewPath);
    }

    private static int GetMaxPixelSize(MediaPreviewSize size) => size switch
    {
        MediaPreviewSize.Small => 384,
        MediaPreviewSize.Medium => 768,
        MediaPreviewSize.Large => 1536,
        _ => 768
    };

    private static string NormalizePath(string path) =>
        OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;

    private static bool IsJpeg(byte[]? bytes) =>
        bytes is { Length: > 3 } && bytes[0] == 0xFF && bytes[1] == 0xD8;

    private static bool IsRecoverableExtractionError(Exception exception) =>
        exception is not OutOfMemoryException and not StackOverflowException and not AccessViolationException;

    private static Directory? FindTrackMetadataDirectory(IReadOnlyList<Directory> directories)
    {
        return directories.FirstOrDefault(d =>
            d.Name.Equals("QuickTime Track Header", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryExtractBasicMetadata(Directory directory, out DateTime dateTime, out int width, out int height)
    {
        dateTime = DateTime.MinValue;
        width = 0;
        height = 0;

        return directory.TryGetDateTime(QuickTimeTrackHeaderDirectory.TagCreated, out dateTime) &&
               directory.TryGetInt32(QuickTimeTrackHeaderDirectory.TagWidth, out width) &&
               directory.TryGetInt32(QuickTimeTrackHeaderDirectory.TagHeight, out height);
    }

    private static VideoMediaMetadata CreateVideoMediaMetadata(Directory trackDirectory, IReadOnlyList<Directory> allDirectories, DateTime dateTime, int width, int height)
    {
        var metadata = new VideoMediaMetadata(dateTime, width, height);

        FillMetadata(trackDirectory, metadata);
        FillDurationFromMovieHeader(allDirectories, metadata);

        return metadata;
    }

    private static void FillDurationFromMovieHeader(IReadOnlyList<Directory> directories, VideoMediaMetadata metadata)
    {
        var movieHeaderDirectory = directories.FirstOrDefault(d =>
            d.Name.Equals("QuickTime Movie Header", StringComparison.OrdinalIgnoreCase));

        if (movieHeaderDirectory?.GetObject(QuickTimeMovieHeaderDirectory.TagDuration) is TimeSpan duration)
        {
            metadata.Duration = duration;
            FillMetadata(movieHeaderDirectory, metadata);
        }
    }

    private static void FillMetadata(Directory directory, AllMetadataWrapper mediaMetadata)
    {
        foreach (var tag in directory.Tags)
        {
            var description = tag.Description;
            if (string.IsNullOrEmpty(description))
            {
                continue;
            }

            mediaMetadata.AddOrUpdateMetadata(tag.Name, description);
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

    private readonly record struct FrameCacheKey(
        string Path,
        long FileLength,
        long LastWriteTimeUtcTicks,
        int MaxPixelSize);

    private sealed record CachedFrame(byte[] Bytes, LinkedListNode<FrameCacheKey> OrderNode);
}
