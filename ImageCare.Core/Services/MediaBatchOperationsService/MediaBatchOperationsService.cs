using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;

namespace ImageCare.Core.Services.MediaBatchOperationsService;

public sealed class MediaBatchOperationsService(
    IMediaPreviewOperationsService mediaPreviewOperationsService,
    IMediaPreviewService mediaPreviewService,
    IMediaAnnotationsService annotationsService,
    IFileSystemService fileSystemService) : IMediaBatchOperationsService
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public Task<MediaBatchOperationSummary> CopyAsync(
        IReadOnlyList<MediaPreview> mediaPreviews,
        string destinationDirectoryPath,
        IProgress<MediaBatchOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            MediaBatchOperationKind.Copy,
            mediaPreviews,
            progress,
            cancellationToken,
            async (preview, _) =>
            {
                var result = await mediaPreviewOperationsService.CopyImagePreviewToDirectoryAsync(
                    preview,
                    destinationDirectoryPath,
                    new Progress<OperationInfo>());
                EnsureSucceeded(result, preview.Url);
                return null;
            });
    }

    public Task<MediaBatchOperationSummary> MoveAsync(
        IReadOnlyList<MediaPreview> mediaPreviews,
        string destinationDirectoryPath,
        IProgress<MediaBatchOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            MediaBatchOperationKind.Move,
            mediaPreviews,
            progress,
            cancellationToken,
            async (preview, _) =>
            {
                var result = await mediaPreviewOperationsService.MoveImagePreviewToDirectoryAsync(
                    preview,
                    destinationDirectoryPath,
                    new Progress<OperationInfo>());
                EnsureSucceeded(result, preview.Url);
                return null;
            });
    }

    public Task<MediaBatchOperationSummary> DeleteAsync(
        IReadOnlyList<MediaPreview> mediaPreviews,
        IProgress<MediaBatchOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            MediaBatchOperationKind.Delete,
            mediaPreviews,
            progress,
            cancellationToken,
            async (preview, _) =>
            {
                var result = await mediaPreviewOperationsService.DeleteImagePreviewAsync(preview);
                EnsureSucceeded(result, preview.Url);
                return null;
            });
    }

    public Task<MediaBatchOperationSummary> ExportJpegAsync(
        IReadOnlyList<MediaPreview> mediaPreviews,
        string destinationDirectoryPath,
        IProgress<MediaBatchOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!fileSystemService.DirectoryExists(destinationDirectoryPath))
        {
            throw new ServiceException($"Export destination directory does not exist: {destinationDirectoryPath}");
        }

        var reservedNames = new HashSet<string>(
            fileSystemService.GetFiles(destinationDirectoryPath)
                .Select(Path.GetFileName)
                .OfType<string>(),
            PathComparer);

        return ExecuteAsync(
            MediaBatchOperationKind.ExportJpeg,
            mediaPreviews,
            progress,
            cancellationToken,
            (preview, token) => ExportJpegAsync(
                preview,
                destinationDirectoryPath,
                reservedNames,
                token));
    }

    private async Task<MediaBatchOperationSummary> ExecuteAsync(
        MediaBatchOperationKind kind,
        IReadOnlyList<MediaPreview> mediaPreviews,
        IProgress<MediaBatchOperationProgress>? progress,
        CancellationToken cancellationToken,
        Func<MediaPreview, CancellationToken, Task<string?>> operation)
    {
        ArgumentNullException.ThrowIfNull(mediaPreviews);

        var items = mediaPreviews.Where(preview => preview.Url.Length > 0).ToArray();
        var failures = new List<MediaBatchOperationFailure>();
        var destinationPaths = new List<string>();
        var succeeded = 0;
        var processed = 0;
        var cancelled = false;

        if (items.Length == 0)
        {
            return new MediaBatchOperationSummary(kind, 0, 0, failures, false, destinationPaths);
        }

        progress?.Report(new MediaBatchOperationProgress(kind, 0, items.Length, items[0].Url));

        foreach (var preview in items)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            try
            {
                var destinationPath = await operation(preview, cancellationToken).ConfigureAwait(false);
                if (destinationPath is not null)
                {
                    destinationPaths.Add(destinationPath);
                }

                succeeded++;
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                break;
            }
            catch (Exception exception)
            {
                failures.Add(new MediaBatchOperationFailure(preview.Url, exception.Message));
            }
            finally
            {
                processed++;
                progress?.Report(new MediaBatchOperationProgress(kind, processed, items.Length, preview.Url));
            }
        }

        return new MediaBatchOperationSummary(
            kind,
            items.Length,
            succeeded,
            failures,
            cancelled,
            destinationPaths);
    }

    private async Task<string?> ExportJpegAsync(
        MediaPreview preview,
        string destinationDirectoryPath,
        HashSet<string> reservedNames,
        CancellationToken cancellationToken)
    {
        if (!preview.MediaFormat.SupportsAnnotations)
        {
            throw new ServiceException($"JPEG export is only available for images: {preview.Url}");
        }

        var destinationPath = BuildUniqueDestinationPath(
            preview.Url,
            destinationDirectoryPath,
            reservedNames);
        var annotations = await annotationsService.GetAsync(preview.Url, cancellationToken).ConfigureAwait(false);

        try
        {
            await using var source = await mediaPreviewService.GetJpegImageStreamAsync(
                preview,
                MediaPreviewSize.Large,
                cancellationToken).ConfigureAwait(false);

            await using (var destination = fileSystemService.Create(destinationPath))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            if (annotations.Rating > 0 || annotations.Tags.Length > 0)
            {
                await annotationsService.SaveAsync(
                    destinationPath,
                    annotations,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            return destinationPath;
        }
        catch
        {
            TryDelete(destinationPath);
            throw;
        }
    }

    private static string BuildUniqueDestinationPath(
        string sourcePath,
        string destinationDirectoryPath,
        HashSet<string> reservedNames)
    {
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "export";
        }

        var candidate = $"{baseName}.jpg";
        var index = 1;
        while (reservedNames.Contains(candidate))
        {
            candidate = $"{baseName} ({index}).jpg";
            index++;
        }

        reservedNames.Add(candidate);
        return Path.Combine(destinationDirectoryPath, candidate);
    }

    private static void EnsureSucceeded(OperationResult result, string sourcePath)
    {
        if (result != OperationResult.Success)
        {
            throw new ServiceException($"File operation did not complete for {sourcePath}: {result}");
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (fileSystemService.FileExists(path))
            {
                fileSystemService.DeleteFile(path);
            }
        }
        catch
        {
            // Preserve the original export failure. A leftover partial file is reported by the UI.
        }
    }
}
