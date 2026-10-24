using System.ComponentModel;

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Services.MediaAnnotationsService;

using ModelContextProtocol.Server;

namespace ImageCare.Mcp.Tools;

[McpServerToolType]
public sealed class SearchTools(IMediaAnnotationsService annotationsService)
{
    [McpServerTool(
        Name = "imagecare_search",
        Title = "Search the annotation index",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false)]
    [Description(
        "Searches the indexed media library by rating, tags and free text over file paths. " +
        "Use it to find precedents (for example other photos already tagged with a species) " +
        "before suggesting tags or folder tags.")]
    public async Task<string> Search(
        [Description("Free text matched against the file path.")]
        string? text = null,
        [Description("Restrict results to this absolute directory or its descendants.")]
        string? rootPath = null,
        [Description("Minimum rating (0-5).")]
        int? minimumRating = null,
        [Description("Maximum rating (0-5).")]
        int? maximumRating = null,
        [Description("Tags that must be present.")]
        string[]? tags = null,
        [Description("When true, every tag is required; otherwise any tag matches.")]
        bool matchAllTags = true,
        [Description("Number of results to skip.")]
        int offset = 0,
        [Description("Maximum number of results (1-500).")]
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var query = new MediaAnnotationsSearchQuery(
            Text: string.IsNullOrWhiteSpace(text) ? null : text,
            RootPath: string.IsNullOrWhiteSpace(rootPath) ? null : Path.GetFullPath(rootPath),
            MinimumRating: minimumRating,
            MaximumRating: maximumRating,
            Tags: tags is { Length: > 0 }
                ? tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).ToArray()
                : null,
            MatchAllTags: matchAllTags,
            Offset: Math.Max(0, offset),
            Limit: Math.Clamp(limit, 1, 500));

        var results = await annotationsService.SearchAsync(query, cancellationToken)
            .ConfigureAwait(false);

        return ToolSupport.Json(new
        {
            count = results.Count,
            results = results.Select(item => new
            {
                path = item.MediaPath,
                name = Path.GetFileName(item.MediaPath),
                rating = item.Annotations.Rating,
                tags = item.Annotations.Tags,
                indexedUtc = item.IndexedUtc.ToString("O")
            })
        });
    }

    [McpServerTool(
        Name = "imagecare_tags_all",
        Title = "List known tags",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false)]
    [Description("Returns every tag known to the annotation index, sorted alphabetically.")]
    public async Task<string> AllTags(CancellationToken cancellationToken = default)
    {
        var tags = await annotationsService.GetAllTagsAsync(cancellationToken).ConfigureAwait(false);
        return ToolSupport.Json(new { count = tags.Count, tags });
    }
}
