using System.ComponentModel;

using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.Core.Services.MediaPreviewService;

using ImageCare.Mcp.Media;
using ImageCare.Mcp.Sessions;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ImageCare.Mcp.Tools;

[McpServerToolType]
public sealed class MediaTools(
    ThumbnailService thumbnailService,
    IMediaPreviewService mediaPreviewService,
    IMediaAnnotationsService annotationsService,
    ReviewSessionService sessionService,
    McpPathGuard pathGuard,
    ImageCareMcpOptions options)
{
    [McpServerTool(
        Name = "imagecare_media_previews",
        Title = "Get image previews",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false)]
    [Description(
        "Returns downscaled JPEG previews for up to maxImages files so a multimodal model can " +
        "inspect the actual pictures. Use it on the current batch or on explicit paths.")]
    public async Task<IEnumerable<ContentBlock>> MediaPreviews(
        [Description("Absolute paths to media files. Omit to preview the current session batch.")]
        string[]? paths = null,
        [Description("Longest edge of the returned image, in pixels (256-2048).")]
        int maxEdge = 1024,
        [Description("Maximum number of images to return (1-24).")]
        int maxImages = 8,
        CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(maxImages, 1, options.MaxPreviewCount);
        var targets = ResolveTargets(paths, limit);

        var images = new List<McpImage>();
        var skipped = new List<string>();
        foreach (var path in targets)
        {
            var image = await thumbnailService.CreateAsync(path, maxEdge, cancellationToken)
                .ConfigureAwait(false);
            if (image is null)
            {
                skipped.Add(path);
            }
            else
            {
                images.Add(image);
            }
        }

        var summary = ToolSupport.Json(new
        {
            requested = targets.Count,
            returned = images.Count,
            maxEdge = Math.Clamp(maxEdge, 256, 2048),
            images = images.Select(image => new
            {
                path = image.Path,
                name = Path.GetFileName(image.Path),
                width = image.Width,
                height = image.Height,
                bytes = image.Bytes.Length
            }),
            skipped
        });

        return ToolSupport.WithImages(summary, images);
    }

    [McpServerTool(
        Name = "imagecare_media_metadata",
        Title = "Read media metadata",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false)]
    [Description(
        "Returns file-system data, EXIF metadata and current rating/tags for one media file.")]
    public async Task<string> MediaMetadata(
        [Description("Absolute path to a media file.")]
        string path,
        CancellationToken cancellationToken = default)
    {
        var fullPath = pathGuard.Require(path, nameof(path));
        if (!File.Exists(fullPath))
        {
            throw new McpException($"File not found: {fullPath}");
        }

        var fileInfo = new FileInfo(fullPath);
        object? metadata = null;
        string? metadataError = null;

        try
        {
            var preview = await mediaPreviewService.GetMediaPreviewAsync(fullPath, cancellationToken)
                .ConfigureAwait(false);
            if (preview is not null)
            {
                var mediaMetadata = await mediaPreviewService
                    .GetMediaMetadataAsync(preview, cancellationToken)
                    .ConfigureAwait(false);
                metadata = new
                {
                    creationDateTime = mediaMetadata.CreationDateTime.ToString("O"),
                    width = mediaMetadata.Width,
                    height = mediaMetadata.Height,
                    values = mediaMetadata.AllMetadata
                };
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            metadataError = exception.Message;
        }

        var annotations = await annotationsService.GetAsync(fullPath, cancellationToken)
            .ConfigureAwait(false);

        return ToolSupport.Json(new
        {
            path = fullPath,
            name = fileInfo.Name,
            extension = fileInfo.Extension,
            length = fileInfo.Length,
            createdUtc = fileInfo.CreationTimeUtc.ToString("O"),
            modifiedUtc = fileInfo.LastWriteTimeUtc.ToString("O"),
            captureTime = MediaLibrary.GetCaptureTime(fullPath)?.ToString("O"),
            rating = annotations.Rating,
            tags = annotations.Tags,
            metadata,
            metadataError
        });
    }

    private IReadOnlyList<string> ResolveTargets(string[]? paths, int limit)
    {
        if (paths is { Length: > 0 })
        {
            return paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => pathGuard.Require(path, nameof(paths)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .ToArray();
        }

        try
        {
            var session = sessionService.GetRequired(null);
            return session.FindBatch(null)?.Items.Take(limit).ToArray() ?? [];
        }
        catch (InvalidOperationException)
        {
            throw new McpException(
                "No paths were supplied and no review session exists. Pass explicit paths.");
        }
    }
}
