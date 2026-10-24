using System.ComponentModel;

using ImageCare.Mcp.Media;
using ImageCare.Mcp.Sessions;
using ImageCare.Mcp.Tools;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ImageCare.Mcp.Tools;

public sealed record SessionSummary(
    string SessionId,
    string Name,
    string SourcePath,
    int BatchSize,
    int TotalFiles,
    int Batches,
    int CurrentBatchIndex,
    int CurrentBatchSize,
    int PendingBatches,
    string CreatedUtc);

[McpServerToolType]
public sealed class SessionTools(
    ReviewSessionService sessionService,
    ThumbnailService thumbnailService,
    McpPathGuard pathGuard,
    ImageCareMcpOptions options)
{
    [McpServerTool(
        Name = "imagecare_session_create",
        Title = "Start a review session",
        ReadOnly = false,
        Idempotent = false,
        Destructive = false)]
    [Description(
        "Starts (or resumes) a culling session for a folder. Media files are ordered by capture " +
        "time, grouped into shooting clusters and packed into batches of roughly batchSize files " +
        "without splitting a cluster. Returns the session summary; call imagecare_session_get to " +
        "receive the first batch.")]
    public async Task<string> SessionCreate(
        [Description("Absolute path to the folder to review (for example a DCIM subfolder on a card).")]
        string sourcePath,
        [Description("Target number of files per batch. Clusters are kept whole.")]
        int batchSize = 50,
        [Description("Include files in subfolders.")]
        bool recursive = false,
        [Description("A file counts as kept when its rating is at least this value. 0 disables rating-based keep.")]
        int keepRating = 1,
        [Description("Create a new session even if an unfinished one exists for the same folder.")]
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var path = pathGuard.Require(sourcePath, nameof(sourcePath));
        var session = await sessionService.CreateAsync(
                path,
                Math.Clamp(batchSize, 5, 500),
                recursive,
                Math.Clamp(keepRating, 0, 5),
                force,
                cancellationToken)
            .ConfigureAwait(false);

        return ToolSupport.Json(new
        {
            session = ToSummary(session),
            note = "Use imagecare_session_get with includePreviews=true to see the current batch, " +
                   "then work in ImageCare and call imagecare_session_sync to read the user's decisions."
        });
    }

    [McpServerTool(
        Name = "imagecare_session_get",
        Title = "Get current batch",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false)]
    [Description(
        "Returns the current (or requested) batch of a review session, including per-file keep " +
        "status derived from ratings and tags. Set includePreviews=true to receive downscaled " +
        "images for multimodal inspection.")]
    public async Task<IEnumerable<ContentBlock>> SessionGet(
        [Description("Session id. Omit to use the most recent session.")]
        string? sessionId = null,
        [Description("Batch number, starting at 1. Omit to use the current batch.")]
        int? batchIndex = null,
        [Description("Include the list of files and their statuses.")]
        bool includeFiles = true,
        [Description("Include downscaled image previews so a multimodal model can look at them.")]
        bool includePreviews = false,
        [Description("Maximum number of images to return when previews are enabled.")]
        int maxPreviews = 8,
        CancellationToken cancellationToken = default)
    {
        var session = sessionService.GetRequired(sessionId);
        var sync = await sessionService.SyncAsync(session.Id, batchIndex, cancellationToken)
            .ConfigureAwait(false);

        var summary = new
        {
            session = ToSummary(session),
            batchIndex = sync.BatchIndex,
            totals = sync.Totals,
            files = includeFiles
                ? sync.Items.Select(item => new
                {
                    path = item.Path,
                    name = Path.GetFileName(item.Path),
                    present = item.Present,
                    rating = item.Rating,
                    tags = item.Tags,
                    status = item.Status
                })
                : null,
            previewHint = includePreviews
                ? null
                : "Previews are disabled. Repeat with includePreviews=true to inspect the images."
        };

        if (!includePreviews)
        {
            return [new TextContentBlock { Text = ToolSupport.Json(summary) }];
        }

        var limit = Math.Clamp(maxPreviews, 1, options.MaxPreviewCount);
        var previewTargets = sync.Items
            .Where(item => item.Present)
            .Where(item => item.Status == "undecided")
            .Select(item => item.Path)
            .Take(limit)
            .ToArray();

        var images = new List<McpImage>();
        foreach (var path in previewTargets)
        {
            var image = await thumbnailService.CreateAsync(path, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (image is not null)
            {
                images.Add(image);
            }
        }

        return ToolSupport.WithImages(ToolSupport.Json(new
        {
            summary,
            previewsReturned = images.Count,
            previewNote = images.Count < sync.Totals.Undecided
                ? "Only undecided files are previewed; call imagecare_media_previews for specific paths."
                : null
        }), images);
    }

    [McpServerTool(
        Name = "imagecare_session_sync",
        Title = "Read user decisions",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false)]
    [Description(
        "Re-reads the current batch from disk and reports what changed since the batch was " +
        "loaded: which files remain, which were kept (rating or tags) and which were removed " +
        "(deleted or moved out). Use this after the user finishes culling in the UI.")]
    public async Task<string> SessionSync(
        [Description("Session id. Omit to use the most recent session.")]
        string? sessionId = null,
        [Description("Batch number, starting at 1. Omit to use the current batch.")]
        int? batchIndex = null,
        CancellationToken cancellationToken = default)
    {
        var session = sessionService.GetRequired(sessionId);
        var sync = await sessionService.SyncAsync(session.Id, batchIndex, cancellationToken)
            .ConfigureAwait(false);

        return ToolSupport.Json(new
        {
            sessionId = session.Id,
            batchIndex = sync.BatchIndex,
            totals = sync.Totals,
            kept = sync.Items.Where(item => item.Status == "kept")
                .Select(item => new { path = item.Path, rating = item.Rating, tags = item.Tags }),
            undecided = sync.Items.Where(item => item.Status == "undecided").Select(item => item.Path),
            removed = sync.Items.Where(item => item.Status == "removed").Select(item => item.Path)
        });
    }

    [McpServerTool(
        Name = "imagecare_session_next",
        Title = "Advance to the next batch",
        ReadOnly = false,
        Idempotent = false,
        Destructive = false)]
    [Description(
        "Marks the current batch as completed and activates the next pending batch. Call after " +
        "the user has finished reviewing the current batch (and optionally after deleting the rest).")]
    public async Task<IEnumerable<ContentBlock>> SessionNext(
        [Description("Session id. Omit to use the most recent session.")]
        string? sessionId = null,
        [Description("Return previews of the next batch immediately.")]
        bool includePreviews = false,
        [Description("Maximum number of images to return when previews are enabled.")]
        int maxPreviews = 8,
        CancellationToken cancellationToken = default)
    {
        var session = sessionService.Advance(sessionId);
        var sync = await sessionService.SyncAsync(session.Id, session.CurrentBatchIndex, cancellationToken)
            .ConfigureAwait(false);

        if (!includePreviews)
        {
            return
            [
                new TextContentBlock
                {
                    Text = ToolSupport.Json(new
                    {
                        session = ToSummary(session),
                        batchIndex = sync.BatchIndex,
                        totals = sync.Totals
                    })
                }
            ];
        }

        var limit = Math.Clamp(maxPreviews, 1, options.MaxPreviewCount);
        var images = new List<McpImage>();
        foreach (var item in sync.Items.Where(item => item.Present).Take(limit))
        {
            var image = await thumbnailService.CreateAsync(item.Path, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (image is not null)
            {
                images.Add(image);
            }
        }

        return ToolSupport.WithImages(ToolSupport.Json(new
        {
            session = ToSummary(session),
            batchIndex = sync.BatchIndex,
            totals = sync.Totals,
            previewsReturned = images.Count
        }), images);
    }

    [McpServerTool(
        Name = "imagecare_session_delete_remaining",
        Title = "Delete files the user did not keep",
        ReadOnly = false,
        Idempotent = false,
        Destructive = true)]
    [Description(
        "Deletes every file in the batch (or session) that is still present and neither rated at " +
        "keepRating nor tagged. Files the user moved out or deleted already are skipped. " +
        "SAFETY: dryRun defaults to true and returns the exact list without deleting anything. " +
        "Set dryRun=false only after the user explicitly asks to delete the rest.")]
    public async Task<string> SessionDeleteRemaining(
        [Description("Session id. Omit to use the most recent session.")]
        string? sessionId = null,
        [Description("Batch number, starting at 1. Omit to use the current batch.")]
        int? batchIndex = null,
        [Description("'batch' to process only one batch, 'session' to process every batch.")]
        string scope = "batch",
        [Description("Rating at or above which a present file counts as kept (defaults to the session value).")]
        int? keepRating = null,
        [Description("When true (default) only reports what would be deleted.")]
        bool dryRun = true,
        [Description("Allow deleting more files than the configured safety limit.")]
        bool allowBulk = false,
        CancellationToken cancellationToken = default)
    {
        var session = sessionService.GetRequired(sessionId);
        var effectiveKeepRating = keepRating ?? session.KeepRating;

        if (dryRun)
        {
            var plan = await sessionService.PlanDeleteAsync(
                    session.Id,
                    batchIndex,
                    scope,
                    effectiveKeepRating,
                    dryRun: true,
                    cancellationToken)
                .ConfigureAwait(false);

            return ToolSupport.Json(new
            {
                plan = new
                {
                    plan.SessionId,
                    plan.BatchIndex,
                    plan.Scope,
                    plan.DryRun,
                    plan.Candidates,
                    plan.ExceedsLimit,
                    willDelete = plan.WillDelete.Select(Path.GetFileName),
                    willDeletePaths = plan.WillDelete,
                    keptCount = plan.Kept.Count,
                    removedAlreadyCount = plan.RemovedAlready.Count
                },
                nextStep = "Ask the user to confirm, then call again with dryRun=false."
            });
        }

        var outcome = await sessionService.DeleteAsync(
                session.Id,
                batchIndex,
                scope,
                effectiveKeepRating,
                allowBulk,
                cancellationToken)
            .ConfigureAwait(false);

        return ToolSupport.Json(new
        {
            outcome,
            failures = outcome.Failures.Select(Path.GetFileName)
        });
    }

    [McpServerTool(
        Name = "imagecare_session_stats",
        Title = "Session statistics",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false)]
    [Description(
        "Returns running statistics for the session: kept/undecided/removed counts, rating " +
        "histogram, tag histogram, and a per-batch summary.")]
    public async Task<string> SessionStats(
        [Description("Session id. Omit to use the most recent session.")]
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var stats = await sessionService.GetStatsAsync(sessionId, cancellationToken)
            .ConfigureAwait(false);

        return ToolSupport.Json(stats);
    }

    private static SessionSummary ToSummary(ReviewSession session)
    {
        var current = session.FindBatch(null);
        return new SessionSummary(
            session.Id,
            session.Name,
            session.SourcePath,
            session.BatchSize,
            session.TotalFiles,
            session.Batches.Count,
            current?.Index ?? 0,
            current?.Items.Count ?? 0,
            session.Batches.Count(batch => batch.State == ReviewBatchState.Pending),
            session.CreatedUtc.ToString("O"));
    }
}
