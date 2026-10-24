using System.ComponentModel;

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.Mcp.Sessions;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace ImageCare.Mcp.Tools;

public sealed record TagApplyFileResult(
    string Path,
    bool Changed,
    IReadOnlyList<string> Added,
    string? Error);

[McpServerToolType]
public sealed class TagTools(
    Tags.TagSuggestionService suggestionService,
    IMediaAnnotationsService annotationsService,
    IFolderTagsStore folderTagsStore,
    IFolderTagApplicationService folderTagApplicationService,
    ReviewSessionService sessionService,
    McpPathGuard pathGuard)
{
    [McpServerTool(
        Name = "imagecare_tags_suggest",
        Title = "Suggest tags",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false)]
    [Description(
        "Suggests tags for files based on evidence from the user's own library and the current " +
        "session: tags the user kept on nearby frames, tags used in the same folder, folder-tags.txt " +
        "and pinned tags. Each suggestion includes a score and the reason it was proposed.")]
    public async Task<string> TagsSuggest(
        [Description("Absolute paths to files. Omit to use undecided files of the current batch.")]
        string[]? paths = null,
        [Description("Session id used as evidence source. Omit to use the most recent session.")]
        string? sessionId = null,
        [Description("On sessions, weight tags from kept frames captured within this many minutes.")]
        int windowMinutes = 30,
        [Description("Maximum number of suggestions (1-50).")]
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var targets = ResolveTargets(paths, sessionId);
        if (targets.Count == 0)
        {
            throw new McpException("No files to suggest tags for.");
        }

        var suggestions = await suggestionService
            .SuggestForFilesAsync(
                targets,
                sessionId,
                Math.Clamp(windowMinutes, 0, 720),
                Math.Clamp(limit, 1, 50),
                cancellationToken)
            .ConfigureAwait(false);

        return ToolSupport.Json(new
        {
            files = targets.Select(Path.GetFileName),
            suggestions,
            note = suggestions.Count == 0
                ? "Not enough evidence yet. Rate or tag a few files in this folder, then ask again."
                : "Ask the user before applying any tag."
        });
    }

    [McpServerTool(
        Name = "imagecare_tags_apply",
        Title = "Apply tags to files",
        ReadOnly = false,
        Idempotent = true,
        Destructive = false)]
    [Description(
        "Merges tags into the XMP metadata of the given files (RAW sidecars and JPEG embedded " +
        "XMP are handled by ImageCare). Never removes existing tags. dryRun defaults to true.")]
    public async Task<string> TagsApply(
        [Description("Absolute paths to media files.")]
        string[] paths,
        [Description("Tags to add.")]
        string[] tags,
        [Description("When true (default) only reports what would be written.")]
        bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        var normalizedTags = NormalizeTags(tags);
        if (normalizedTags.Count == 0)
        {
            throw new McpException("At least one non-empty tag is required.");
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

        var results = new List<TagApplyFileResult>(targets.Length);
        foreach (var path in targets)
        {
            if (!File.Exists(path))
            {
                results.Add(new TagApplyFileResult(path, false, [], "file not found"));
                continue;
            }

            try
            {
                var snapshot = await annotationsService.GetSnapshotAsync(path, cancellationToken)
                    .ConfigureAwait(false);
                var existingKeys = snapshot.Annotations.Tags
                    .Select(tag => tag.ToUpperInvariant())
                    .ToHashSet(StringComparer.Ordinal);
                var added = normalizedTags
                    .Where(tag => !existingKeys.Contains(tag.ToUpperInvariant()))
                    .ToArray();

                if (added.Length == 0)
                {
                    results.Add(new TagApplyFileResult(path, false, [], null));
                    continue;
                }

                if (!dryRun)
                {
                    var merged = new MediaAnnotations(
                        snapshot.Annotations.Rating,
                        snapshot.Annotations.Tags.Concat(added));
                    await annotationsService
                        .SaveAsync(path, merged, snapshot.FileState, cancellationToken)
                        .ConfigureAwait(false);
                }

                results.Add(new TagApplyFileResult(path, true, added, null));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                results.Add(new TagApplyFileResult(path, false, [], exception.Message));
            }
        }

        return ToolSupport.Json(new
        {
            dryRun,
            tags = normalizedTags,
            changed = results.Count(result => result.Changed),
            unchanged = results.Count(result => !result.Changed && result.Error is null),
            failed = results.Count(result => result.Error is not null),
            results = results.Select(result => new
            {
                file = Path.GetFileName(result.Path),
                path = result.Path,
                result.Changed,
                result.Added,
                result.Error
            })
        });
    }

    [McpServerTool(
        Name = "imagecare_folder_tags_get",
        Title = "Read folder tags",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false)]
    [Description(
        "Returns the folder-tags.txt descriptor of a folder plus the effective inherited tag set.")]
    public string FolderTagsGet(
        [Description("Absolute path to a directory.")]
        string directoryPath)
    {
        var path = pathGuard.Require(directoryPath, nameof(directoryPath));
        if (!Directory.Exists(path))
        {
            throw new McpException($"Directory not found: {path}");
        }

        var descriptorPath = folderTagsStore.GetDescriptorPath(path);
        return ToolSupport.Json(new
        {
            directory = path,
            descriptorPath,
            hasDescriptor = folderTagsStore.HasDescriptor(path),
            ownTags = folderTagsStore.Read(path),
            effectiveTags = folderTagApplicationService.GetEffectiveTags(path)
        });
    }

    [McpServerTool(
        Name = "imagecare_folder_tags_set",
        Title = "Write folder tags",
        ReadOnly = false,
        Idempotent = true,
        Destructive = false)]
    [Description(
        "Writes folder-tags.txt for a folder. New media copied or moved into the folder inherits " +
        "these tags. dryRun defaults to true.")]
    public async Task<string> FolderTagsSet(
        [Description("Absolute path to a directory.")]
        string directoryPath,
        [Description("Complete tag list for the descriptor. An empty array removes the descriptor.")]
        string[] tags,
        [Description("When true (default) only reports what would be written.")]
        bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        var path = pathGuard.Require(directoryPath, nameof(directoryPath));
        if (!Directory.Exists(path))
        {
            throw new McpException($"Directory not found: {path}");
        }

        var normalized = NormalizeTags(tags);
        var descriptorPath = folderTagsStore.GetDescriptorPath(path);
        if (!dryRun)
        {
            await folderTagsStore.WriteAsync(path, normalized, cancellationToken).ConfigureAwait(false);
        }

        return ToolSupport.Json(new
        {
            directory = path,
            descriptorPath,
            dryRun,
            tags = normalized,
            effectiveTags = folderTagApplicationService.GetEffectiveTags(path)
        });
    }

    [McpServerTool(
        Name = "imagecare_folder_tags_propose",
        Title = "Propose folder tags",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false)]
    [Description(
        "Proposes tags for a folder's folder-tags.txt using the tags the user assigned in that " +
        "folder and kept in the current session. Nothing is written; ask the user first, then " +
        "call imagecare_folder_tags_set.")]
    public async Task<string> FolderTagsPropose(
        [Description("Absolute path to a directory. Omit to use the current session source folder.")]
        string? directoryPath = null,
        [Description("Session id used as evidence source. Omit to use the most recent session.")]
        string? sessionId = null,
        [Description("Maximum number of suggestions (1-20).")]
        int limit = 8,
        CancellationToken cancellationToken = default)
    {
        var path = ResolveFolder(directoryPath, sessionId);
        var proposal = await suggestionService
            .ProposeFolderTagsAsync(path, sessionId, Math.Clamp(limit, 1, 20), cancellationToken)
            .ConfigureAwait(false);

        return ToolSupport.Json(new
        {
            directory = proposal.DirectoryPath,
            existing = proposal.Existing,
            suggested = proposal.Suggested,
            note = "Review with the user before calling imagecare_folder_tags_set."
        });
    }

    private string ResolveFolder(string? directoryPath, string? sessionId)
    {
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            return pathGuard.Require(directoryPath, nameof(directoryPath));
        }

        return sessionService.GetRequired(sessionId).SourcePath;
    }

    private IReadOnlyList<string> ResolveTargets(string[]? paths, string? sessionId)
    {
        if (paths is { Length: > 0 })
        {
            return paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => pathGuard.Require(path, nameof(paths)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var session = sessionService.GetRequired(sessionId);
        var batch = session.FindBatch(null);
        return batch?.Items.Where(File.Exists).ToArray() ?? [];
    }

    private static IReadOnlyList<string> NormalizeTags(IEnumerable<string>? tags)
    {
        if (tags is null)
        {
            return [];
        }

        var unique = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
        {
            if (tag is null)
            {
                continue;
            }

            var normalized = MediaAnnotations.NormalizeTag(tag);
            if (normalized.Length > 0)
            {
                unique.TryAdd(normalized, normalized);
            }
        }

        return unique.Values.ToArray();
    }
}
