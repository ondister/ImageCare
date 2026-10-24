using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.MediaPreviewService;

using Serilog;

using SkiaSharp;

namespace ImageCare.Mcp.Media;

public sealed record McpImage(
    string Path,
    byte[] Bytes,
    string MimeType,
    int Width,
    int Height);

/// <summary>
/// Produces compact JPEG previews for multimodal models. Full-resolution files are never
/// returned: images are decoded, scaled to a bounded edge and re-encoded, which keeps
/// payloads and token usage predictable.
/// </summary>
public sealed class ThumbnailService
{
    private const int CacheCapacity = 96;

    private readonly IMediaPreviewService _mediaPreviewService;
    private readonly ImageCareMcpOptions _options;
    private readonly ILogger _logger;
    private readonly Dictionary<string, McpImage> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _cacheOrder = new();
    private readonly object _cacheGate = new();

    public ThumbnailService(
        IMediaPreviewService mediaPreviewService,
        ImageCareMcpOptions options,
        ILogger logger)
    {
        _mediaPreviewService = mediaPreviewService;
        _options = options;
        _logger = logger;
    }

    public async Task<McpImage?> CreateAsync(
        string path,
        int? maxEdgeOverride = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var maxEdge = Math.Clamp(maxEdgeOverride ?? _options.PreviewMaxEdge, 256, 2048);
        var key = BuildCacheKey(path, maxEdge);

        lock (_cacheGate)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        try
        {
            var preview = await _mediaPreviewService.GetMediaPreviewAsync(path, cancellationToken)
                .ConfigureAwait(false);
            if (preview is null)
            {
                return null;
            }

            await using var source = await _mediaPreviewService
                .GetJpegImageStreamAsync(preview, MediaPreviewSize.Large, cancellationToken)
                .ConfigureAwait(false);

            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var result = Downscale(buffer.ToArray(), path, maxEdge);
            if (result is not null)
            {
                Remember(key, result);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Warning(exception, "Failed to build preview for {Path}", path);
            return null;
        }
    }

    private McpImage? Downscale(byte[] sourceBytes, string path, int maxEdge)
    {
        using var data = SKData.CreateCopy(sourceBytes);
        using var bitmap = SKBitmap.Decode(data);
        if (bitmap is null || bitmap.Width == 0 || bitmap.Height == 0)
        {
            return null;
        }

        var scale = Math.Min(1.0, maxEdge / (double)Math.Max(bitmap.Width, bitmap.Height));
        var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));

        using var resized = scale >= 1.0
            ? null
            : bitmap.Resize(
                new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

        var finalBitmap = resized ?? bitmap;
        using var image = SKImage.FromBitmap(finalBitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, _options.PreviewQuality);
        if (encoded is null)
        {
            return null;
        }

        return new McpImage(path, encoded.ToArray(), "image/jpeg", finalBitmap.Width, finalBitmap.Height);
    }

    private void Remember(string key, McpImage image)
    {
        lock (_cacheGate)
        {
            if (_cache.TryAdd(key, image))
            {
                _cacheOrder.Enqueue(key);
            }

            while (_cacheOrder.Count > CacheCapacity
                   && _cacheOrder.TryDequeue(out var oldest))
            {
                _cache.Remove(oldest);
            }
        }
    }

    private static string BuildCacheKey(string path, int maxEdge)
    {
        try
        {
            var info = new FileInfo(path);
            return $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{maxEdge}";
        }
        catch (IOException)
        {
            return $"{path}|{maxEdge}";
        }
    }
}
