using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.MediaAnnotationsService;

using ImageCare.Mcp.Media;
using ImageCare.Mcp.Sessions;

using Serilog;

namespace ImageCare.Mcp.Tags;

public sealed record TagSuggestion(
    string Tag,
    double Score,
    IReadOnlyList<string> Reasons,
    int EvidenceCount);

public sealed record FolderTagProposal(
    string DirectoryPath,
    IReadOnlyList<TagSuggestion> Suggested,
    IReadOnlyList<string> Existing);

/// <summary>
/// Produces tag suggestions from evidence the user already created: rated/tagged files in the
/// same folder, tags inherited from <c>folder-tags.txt</c>, pinned tags, and — most importantly —
/// what the user kept earlier in the same review session. Over time this makes the assistant
/// reflect the user's own cataloguing habits instead of a generic model.
/// </summary>
public sealed class TagSuggestionService
{
    private const double SessionTimeWindowWeight = 6.0;
    private const double SessionKeptWeight = 4.0;
    private const double SameFolderWeight = 3.0;
    private const double FolderTagWeight = 2.0;
    private const double PinnedWeight = 1.0;

    private readonly IMediaAnnotationsService _annotationsService;
    private readonly IFolderTagApplicationService _folderTagApplicationService;
    private readonly IConfigurationService _configurationService;
    private readonly ReviewSessionService _sessionService;
    private readonly MediaLibrary _mediaLibrary;
    private readonly ILogger _logger;

    public TagSuggestionService(
        IMediaAnnotationsService annotationsService,
        IFolderTagApplicationService folderTagApplicationService,
        IConfigurationService configurationService,
        ReviewSessionService sessionService,
        MediaLibrary mediaLibrary,
        ILogger logger)
    {
        _annotationsService = annotationsService;
        _folderTagApplicationService = folderTagApplicationService;
        _configurationService = configurationService;
        _sessionService = sessionService;
        _mediaLibrary = mediaLibrary;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TagSuggestion>> SuggestForFilesAsync(
        IReadOnlyList<string> targetPaths,
        string? sessionId,
        int windowMinutes,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var scores = new Dictionary<string, Accumulator>(StringComparer.OrdinalIgnoreCase);

        void Add(string tag, double weight, string reason)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                return;
            }

            var normalized = MediaAnnotations.NormalizeTag(tag);
            if (normalized.Length == 0)
            {
                return;
            }

            if (!scores.TryGetValue(normalized, out var accumulator))
            {
                accumulator = new Accumulator(normalized);
                scores[normalized] = accumulator;
            }

            accumulator.Add(weight, reason);
        }

        var targets = targetPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (targets.Length == 0)
        {
            return [];
        }

        var targetTimes = targets
            .Select(path => (Path: path, Time: MediaLibrary.GetCaptureTime(path)))
            .Where(item => item.Time.HasValue)
            .ToArray();

        // 1. Session history: what did the user keep, especially near the same moment?
        ReviewSession? session = null;
        try
        {
            session = string.IsNullOrWhiteSpace(sessionId)
                ? _sessionService.GetLatestSession()
                : _sessionService.GetRequired(sessionId);
        }
        catch (InvalidOperationException)
        {
            // No session yet: other signals still apply.
        }

        if (session is not null)
        {
            var sessionPaths = session.Batches
                .SelectMany(batch => batch.Items)
                .Where(File.Exists)
                .ToArray();
            var statuses = await _sessionService
                .BuildStatusesAsync(sessionPaths, session.KeepRating, cancellationToken)
                .ConfigureAwait(false);
            var targetSet = targets.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var kept = statuses
                .Where(item => item.Status == "kept" && item.Tags.Count > 0)
                .Where(item => !targetSet.Contains(item.Path))
                .ToArray();

            foreach (var item in kept)
            {
                AddSessionSignals(item, targetTimes, windowMinutes, Add);
            }
        }

        // 2. Same-folder evidence, straight from the persistence layer (works even before a session).
        foreach (var folder in targets
                     .Select(Path.GetDirectoryName)
                     .Where(folder => !string.IsNullOrWhiteSpace(folder))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var directoryAnnotations = await GetDirectoryAnnotationsAsync(folder!, cancellationToken)
                .ConfigureAwait(false);
            var targetSet = targets.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var indexed in directoryAnnotations)
            {
                if (targetSet.Contains(indexed.MediaPath) || indexed.Annotations.Tags.Length == 0)
                {
                    continue;
                }

                foreach (var tag in indexed.Annotations.Tags)
                {
                    Add(tag, SameFolderWeight, $"used by {Path.GetFileName(indexed.MediaPath)} in the same folder");
                }
            }

            foreach (var tag in _folderTagApplicationService.GetEffectiveTags(folder!))
            {
                Add(tag, FolderTagWeight, "declared by folder-tags.txt");
            }
        }

        // 3. Pinned tags are the user's deliberate shortlist.
        foreach (var tag in _configurationService.Configuration.Value.PinnedTags)
        {
            Add(tag, PinnedWeight, "pinned tag");
        }

        // Targets already carry tags: never suggest what is present.
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            try
            {
                var annotations = await _annotationsService.GetAsync(target, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var tag in annotations.Tags)
                {
                    existing.Add(MediaAnnotations.NormalizeTag(tag));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.Debug(exception, "Could not read existing tags for {Path}", target);
            }
        }

        return scores.Values
            .Where(accumulator => !existing.Contains(accumulator.Tag))
            .Select(accumulator => accumulator.ToSuggestion())
            .OrderByDescending(suggestion => suggestion.Score)
            .ThenBy(suggestion => suggestion.Tag, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();
    }

    public async Task<FolderTagProposal> ProposeFolderTagsAsync(
        string directoryPath,
        string? sessionId,
        int limit,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
        var files = _mediaLibrary.EnumerateMedia(fullPath, recursive: false);
        var suggestions = await SuggestForFilesAsync(files, sessionId, 30, limit, cancellationToken)
            .ConfigureAwait(false);
        var existing = _folderTagApplicationService.GetEffectiveTags(fullPath);

        // The descriptor already covers these tags (own or inherited); proposing them again is noise.
        var existingSet = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = suggestions
            .Where(suggestion => !existingSet.Contains(suggestion.Tag))
            .ToArray();

        return new FolderTagProposal(fullPath, added, existing);
    }

    private void AddSessionSignals(
        SessionFileStatus item,
        IReadOnlyList<(string Path, DateTime? Time)> targetTimes,
        int windowMinutes,
        Action<string, double, string> add)
    {
        var keptTime = MediaLibrary.GetCaptureTime(item.Path);
        var nearestMinutes = double.MaxValue;
        if (keptTime.HasValue && targetTimes.Count > 0)
        {
            foreach (var target in targetTimes)
            {
                if (!target.Time.HasValue)
                {
                    continue;
                }

                var minutes = Math.Abs((target.Time.Value - keptTime.Value).TotalMinutes);
                if (minutes < nearestMinutes)
                {
                    nearestMinutes = minutes;
                }
            }
        }

        var timeBoost = nearestMinutes <= windowMinutes
            ? SessionTimeWindowWeight * (1.0 - nearestMinutes / Math.Max(windowMinutes, 1))
            : 0.0;

        foreach (var tag in item.Tags)
        {
            add(tag, SessionKeptWeight, "you kept files with this tag in this session");
            if (timeBoost > 0)
            {
                add(
                    tag,
                    timeBoost,
                    $"kept nearby frame within {Math.Round(nearestMinutes)} min ({Path.GetFileName(item.Path)})");
            }
        }
    }

    private async Task<IReadOnlyList<IndexedMediaAnnotations>> GetDirectoryAnnotationsAsync(
        string directoryPath,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _annotationsService.GetDirectoryAsync(directoryPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Debug(exception, "Annotation index not available for {Directory}", directoryPath);
            return [];
        }
    }

    private sealed class Accumulator(string tag)
    {
        private readonly List<string> _reasons = [];
        private double _score;
        private int _evidence;

        public string Tag { get; } = tag;

        public void Add(double weight, string reason)
        {
            _score += weight;
            _evidence++;
            if (!_reasons.Contains(reason, StringComparer.Ordinal))
            {
                _reasons.Add(reason);
            }
        }

        public TagSuggestion ToSuggestion() =>
            new(Tag, Math.Round(_score, 2), _reasons.Take(4).ToArray(), _evidence);
    }
}
