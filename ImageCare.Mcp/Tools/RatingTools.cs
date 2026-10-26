using System.ComponentModel;

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Services.MediaAnnotationsService;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace ImageCare.Mcp.Tools;

public sealed record RatingSetFileResult(
    string Path,
    bool Changed,
    int PreviousRating,
    int Rating,
    string? Error);

[McpServerToolType]
public sealed class RatingTools(
    IMediaAnnotationsService annotationsService,
    McpPathGuard pathGuard)
{
    [McpServerTool(
        Name = "imagecare_rating_set",
        Title = "Set media rating",
        ReadOnly = false,
        Idempotent = true,
        Destructive = false)]
    [Description(
        "Sets the star rating (0-5) of the given media files while preserving their tags. " +
        "Use 0 to clear the rating. RAW sidecars and embedded JPEG XMP are handled by ImageCare. " +
        "dryRun defaults to true.")]
    public async Task<string> RatingSet(
        [Description("Absolute paths to media files.")]
        string[] paths,
        [Description("New rating from 0 (unrated) to 5 stars.")]
        int rating,
        [Description("When true (default) only reports what would be written.")]
        bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        if (rating is < 0 or > 5)
        {
            throw new McpException("Rating must be between 0 and 5.");
        }

        var targets = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => pathGuard.Require(path, nameof(paths)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (targets.Length == 0)
        {
            throw new McpException("At least one path is required.");
        }

        var results = new List<RatingSetFileResult>(targets.Length);
        foreach (var path in targets)
        {
            if (!File.Exists(path))
            {
                results.Add(new RatingSetFileResult(path, false, 0, rating, "file not found"));
                continue;
            }

            var previousRating = 0;
            try
            {
                var snapshot = await annotationsService.GetSnapshotAsync(path, cancellationToken)
                    .ConfigureAwait(false);
                previousRating = snapshot.Annotations.Rating;
                if (previousRating == rating)
                {
                    results.Add(new RatingSetFileResult(path, false, previousRating, rating, null));
                    continue;
                }

                if (!dryRun)
                {
                    var updated = new MediaAnnotations(rating, snapshot.Annotations.Tags);
                    await annotationsService
                        .SaveAsync(path, updated, snapshot.FileState, cancellationToken)
                        .ConfigureAwait(false);
                }

                results.Add(new RatingSetFileResult(path, true, previousRating, rating, null));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                results.Add(new RatingSetFileResult(path, false, previousRating, rating, exception.Message));
            }
        }

        return ToolSupport.Json(new
        {
            dryRun,
            rating,
            changed = results.Count(result => result.Changed),
            unchanged = results.Count(result => !result.Changed && result.Error is null),
            failed = results.Count(result => result.Error is not null),
            results = results.Select(result => new
            {
                file = Path.GetFileName(result.Path),
                path = result.Path,
                result.Changed,
                previousRating = result.PreviousRating,
                result.Rating,
                result.Error
            })
        });
    }
}
