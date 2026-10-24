using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;

using ImageCare.Mcp.Media;

using Serilog;

namespace ImageCare.Mcp.Sessions;

public sealed record SessionFileStatus(
    string Path,
    bool Present,
    int Rating,
    IReadOnlyList<string> Tags,
    string Status);

public sealed record SessionSyncTotals(
    int Total,
    int Present,
    int Kept,
    int Undecided,
    int Removed);

public sealed record SessionSync(
    string SessionId,
    int BatchIndex,
    IReadOnlyList<SessionFileStatus> Items,
    SessionSyncTotals Totals);

public sealed record SessionTagCount(string Tag, int Count);

public sealed record SessionBatchSummary(
    int Index,
    int Total,
    int Present,
    int Kept,
    int Undecided,
    int Removed,
    string State);

public sealed record SessionStats(
    string SessionId,
    string Name,
    string SourcePath,
    int TotalFiles,
    int Batches,
    int CompletedBatches,
    int CurrentBatchIndex,
    int Kept,
    int Undecided,
    int Removed,
    int DeletedByAgent,
    IReadOnlyDictionary<int, int> RatingHistogram,
    IReadOnlyList<SessionTagCount> Tags,
    IReadOnlyList<SessionBatchSummary> BatchSummaries);

public sealed record DeletePlan(
    string SessionId,
    int BatchIndex,
    string Scope,
    bool DryRun,
    int Candidates,
    bool ExceedsLimit,
    IReadOnlyList<string> WillDelete,
    IReadOnlyList<string> Kept,
    IReadOnlyList<string> RemovedAlready);

public sealed record DeleteOutcome(
    string SessionId,
    int BatchIndex,
    int Requested,
    int Deleted,
    int Failed,
    IReadOnlyList<string> Failures);

/// <summary>
/// Owns the review-session lifecycle: creation, batching, synchronization with what the user
/// did in the UI, and safe deletion of everything that was not kept.
/// </summary>
public sealed class ReviewSessionService
{
    private readonly ReviewSessionStore _store;
    private readonly MediaLibrary _mediaLibrary;
    private readonly IMediaAnnotationsService _annotationsService;
    private readonly IMediaPreviewOperationsService _previewOperations;
    private readonly ImageCareMcpOptions _options;
    private readonly ILogger _logger;

    public ReviewSessionService(
        ReviewSessionStore store,
        MediaLibrary mediaLibrary,
        IMediaAnnotationsService annotationsService,
        IMediaPreviewOperationsService previewOperations,
        ImageCareMcpOptions options,
        ILogger logger)
    {
        _store = store;
        _mediaLibrary = mediaLibrary;
        _annotationsService = annotationsService;
        _previewOperations = previewOperations;
        _options = options;
        _logger = logger;
    }

    public async Task<ReviewSession> CreateAsync(
        string sourcePath,
        int batchSize,
        bool recursive,
        int keepRating,
        bool force,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath));
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Source folder not found: {fullPath}");
        }

        if (!force)
        {
            var existing = GetLatestSession();
            if (existing is not null
                && PathsEqual(existing.SourcePath, fullPath)
                && existing.Batches.Any(batch => batch.State != ReviewBatchState.Completed))
            {
                return existing;
            }
        }

        var files = _mediaLibrary.EnumerateMedia(fullPath, recursive);
        var batches = _mediaLibrary.PackBatches(files, batchSize);
        var now = DateTime.UtcNow;
        var session = new ReviewSession
        {
            Id = BuildSessionId(fullPath),
            Name = Path.GetFileName(fullPath) is { Length: > 0 } name ? name : fullPath,
            SourcePath = fullPath,
            Recursive = recursive,
            BatchSize = batchSize,
            KeepRating = keepRating,
            CreatedUtc = now,
            UpdatedUtc = now,
            CurrentBatchIndex = batches.Count > 0 ? 1 : 0,
            Batches = batches
                .Select((items, index) => new ReviewBatch
                {
                    Index = index + 1,
                    Items = items.ToList(),
                    State = index == 0 ? ReviewBatchState.Active : ReviewBatchState.Pending,
                    StartedUtc = index == 0 ? now : null
                })
                .ToList()
        };

        cancellationToken.ThrowIfCancellationRequested();
        _store.Save(session);
        _logger.Information(
            "Created MCP review session {SessionId} for {Path}: {Files} files in {Batches} batches",
            session.Id,
            session.SourcePath,
            session.TotalFiles,
            session.Batches.Count);
        return session;
    }

    public ReviewSession? GetLatestSession() => _store.TryLoadLatest();

    public ReviewSession GetRequired(string? sessionId)
    {
        var session = string.IsNullOrWhiteSpace(sessionId)
            ? _store.TryLoadLatest()
            : _store.TryLoad(sessionId);

        return session ?? throw new InvalidOperationException(
            "No review session exists yet. Call imagecare_session_create first.");
    }

    public ReviewSession Advance(string? sessionId)
    {
        var session = GetRequired(sessionId);
        var current = session.FindBatch(null);
        if (current is not null)
        {
            current.State = ReviewBatchState.Completed;
            current.CompletedUtc = DateTime.UtcNow;
        }

        var next = session.Batches
            .Where(batch => batch.State != ReviewBatchState.Completed)
            .OrderBy(batch => batch.Index)
            .FirstOrDefault();

        if (next is not null)
        {
            next.State = ReviewBatchState.Active;
            next.StartedUtc ??= DateTime.UtcNow;
            session.CurrentBatchIndex = next.Index;
        }

        _store.Save(session);
        return session;
    }

    public async Task<SessionSync> SyncAsync(
        string? sessionId,
        int? batchIndex,
        CancellationToken cancellationToken)
    {
        var session = GetRequired(sessionId);
        var batch = session.FindBatch(batchIndex)
                    ?? throw new InvalidOperationException("The requested batch does not exist in this session.");

        var statuses = await BuildStatusesAsync(batch.Items, session.KeepRating, cancellationToken)
            .ConfigureAwait(false);

        return new SessionSync(
            session.Id,
            batch.Index,
            statuses,
            new SessionSyncTotals(
                statuses.Count,
                statuses.Count(item => item.Present),
                statuses.Count(item => item.Status == "kept"),
                statuses.Count(item => item.Status == "undecided"),
                statuses.Count(item => item.Status == "removed")));
    }

    public async Task<SessionStats> GetStatsAsync(string? sessionId, CancellationToken cancellationToken)
    {
        var session = GetRequired(sessionId);
        var allStatuses = new List<SessionFileStatus>();
        var summaries = new List<SessionBatchSummary>();

        foreach (var batch in session.Batches.OrderBy(batch => batch.Index))
        {
            var statuses = await BuildStatusesAsync(batch.Items, session.KeepRating, cancellationToken)
                .ConfigureAwait(false);
            allStatuses.AddRange(statuses);

            var kept = statuses.Count(item => item.Status == "kept");
            var undecided = statuses.Count(item => item.Status == "undecided");
            var removed = statuses.Count(item => item.Status == "removed");
            summaries.Add(new SessionBatchSummary(
                batch.Index,
                statuses.Count,
                statuses.Count(item => item.Present),
                kept,
                undecided,
                removed,
                batch.State.ToString().ToLowerInvariant()));
        }

        var ratings = allStatuses
            .Where(item => item.Present)
            .GroupBy(item => item.Rating)
            .OrderBy(group => group.Key)
            .ToDictionary(group => group.Key, group => group.Count());

        var tags = allStatuses
            .SelectMany(item => item.Tags)
            .GroupBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SessionTagCount(group.Key, group.Count()))
            .ToArray();

        return new SessionStats(
            session.Id,
            session.Name,
            session.SourcePath,
            session.TotalFiles,
            session.Batches.Count,
            session.Batches.Count(batch => batch.State == ReviewBatchState.Completed),
            session.CurrentBatchIndex,
            allStatuses.Count(item => item.Status == "kept"),
            allStatuses.Count(item => item.Status == "undecided"),
            allStatuses.Count(item => item.Status == "removed"),
            session.DeletedByAgent,
            ratings,
            tags,
            summaries);
    }

    public async Task<DeletePlan> PlanDeleteAsync(
        string? sessionId,
        int? batchIndex,
        string scope,
        int keepRating,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var session = GetRequired(sessionId);
        var normalizedScope = NormalizeScope(scope);
        var (resolvedBatchIndex, paths) = ResolveDeleteScope(session, batchIndex, normalizedScope);
        var statuses = await BuildStatusesAsync(paths, keepRating, cancellationToken).ConfigureAwait(false);

        var candidates = statuses
            .Where(item => item.Present && item.Status == "undecided")
            .Select(item => item.Path)
            .ToArray();
        var kept = statuses
            .Where(item => item.Status == "kept")
            .Select(item => item.Path)
            .ToArray();
        var removed = statuses
            .Where(item => item.Status == "removed")
            .Select(item => item.Path)
            .ToArray();

        return new DeletePlan(
            session.Id,
            resolvedBatchIndex,
            normalizedScope,
            dryRun,
            candidates.Length,
            candidates.Length > _options.MaxFilesPerDelete,
            candidates,
            kept,
            removed);
    }

    public async Task<DeleteOutcome> DeleteAsync(
        string? sessionId,
        int? batchIndex,
        string scope,
        int keepRating,
        bool allowBulk,
        CancellationToken cancellationToken)
    {
        var plan = await PlanDeleteAsync(
                sessionId,
                batchIndex,
                scope,
                keepRating,
                dryRun: false,
                cancellationToken)
            .ConfigureAwait(false);

        if (plan.ExceedsLimit && !allowBulk)
        {
            throw new InvalidOperationException(
                $"Refusing to delete {plan.Candidates} files; the configured limit is " +
                $"{_options.MaxFilesPerDelete}. Re-run with allowBulk=true after verifying the dry run.");
        }

        var failures = new List<string>();
        var deleted = 0;
        foreach (var path in plan.WillDelete)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await _previewOperations
                    .DeleteImagePreviewAsync(new MediaPreview(
                        Path.GetFileName(path),
                        path,
                        ResolveFormat(path),
                        maxImageHeight: 0))
                    .ConfigureAwait(false);

                if (result == OperationResult.Success)
                {
                    deleted++;
                }
                else
                {
                    failures.Add(path);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.Warning(exception, "MCP delete failed for {Path}", path);
                failures.Add(path);
            }
        }

        var session = GetRequired(sessionId);
        session.DeletedByAgent += deleted;
        _store.Save(session);

        _logger.Information(
            "MCP delete session {SessionId} batch {BatchIndex}: deleted {Deleted}, failed {Failed}",
            plan.SessionId,
            plan.BatchIndex,
            deleted,
            failures.Count);

        return new DeleteOutcome(plan.SessionId, plan.BatchIndex, plan.WillDelete.Count, deleted, failures.Count, failures);
    }

    public IReadOnlyList<string> GetSessionRoots()
    {
        var roots = new List<string>();
        foreach (var id in _store.ListSessionIds())
        {
            var session = _store.TryLoad(id);
            if (session is not null)
            {
                roots.Add(session.SourcePath);
            }
        }

        return roots;
    }

    public async Task<IReadOnlyList<SessionFileStatus>> BuildStatusesAsync(
        IReadOnlyList<string> paths,
        int keepRating,
        CancellationToken cancellationToken)
    {
        var result = new SessionFileStatus?[paths.Count];
        var indexByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < paths.Count; index++)
        {
            indexByPath[paths[index]] = index;
        }

        await Parallel.ForEachAsync(
                paths,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = 8,
                    CancellationToken = cancellationToken
                },
                async (path, token) =>
                {
                    var status = await BuildStatusAsync(path, keepRating, token).ConfigureAwait(false);
                    if (indexByPath.TryGetValue(path, out var index))
                    {
                        result[index] = status;
                    }
                })
            .ConfigureAwait(false);

        return result
            .Where(status => status is not null)
            .Select(status => status!)
            .ToArray();
    }

    private async Task<SessionFileStatus> BuildStatusAsync(
        string path,
        int keepRating,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new SessionFileStatus(path, Present: false, 0, [], "removed");
        }

        var annotations = MediaAnnotations.Empty;
        try
        {
            annotations = await _annotationsService.GetAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Warning(exception, "Failed to read annotations for {Path}", path);
        }

        var ratingCountsAsKeep = keepRating > 0 && annotations.Rating >= keepRating;
        var status = ratingCountsAsKeep || annotations.Tags.Length > 0
            ? "kept"
            : "undecided";

        return new SessionFileStatus(
            path,
            Present: true,
            annotations.Rating,
            annotations.Tags.ToArray(),
            status);
    }

    private static (int BatchIndex, IReadOnlyList<string> Paths) ResolveDeleteScope(
        ReviewSession session,
        int? batchIndex,
        string scope)
    {
        if (scope == "session")
        {
            return (
                session.FindBatch(batchIndex)?.Index ?? session.CurrentBatchIndex,
                session.Batches.SelectMany(batch => batch.Items).ToArray());
        }

        var batch = session.FindBatch(batchIndex)
                    ?? throw new InvalidOperationException("The requested batch does not exist in this session.");
        return (batch.Index, batch.Items);
    }

    private static string NormalizeScope(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return "batch";
        }

        var normalized = scope.Trim().ToLowerInvariant();
        return normalized is "batch" or "session"
            ? normalized
            : throw new ArgumentException("scope must be 'batch' or 'session'.", nameof(scope));
    }

    public static MediaFormat ResolveFormat(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => MediaFormat.MediaFormatJpg,
            ".cr3" => MediaFormat.MediaFormatCr3,
            ".arw" => MediaFormat.MediaFormatArw,
            ".nef" => MediaFormat.MediaFormatNef,
            ".mp4" => MediaFormat.MediaFormatMp4,
            _ => MediaFormat.MediaFormatUnknown
        };
    }

    private static string BuildSessionId(string sourcePath)
    {
        var slug = new string(Path.GetFileName(sourcePath)
            .Where(char.IsLetterOrDigit)
            .Take(24)
            .ToArray());
        if (slug.Length == 0)
        {
            slug = "session";
        }

        return $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{slug}";
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
