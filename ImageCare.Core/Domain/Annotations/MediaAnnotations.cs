using System.Collections.Immutable;
using System.Text;

namespace ImageCare.Core.Domain.Annotations;

public sealed record MediaAnnotations
{
    public static MediaAnnotations Empty { get; } = new(0, []);

    public MediaAnnotations(int rating, IEnumerable<string>? tags = null)
    {
        if (rating is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(rating), rating, "Rating must be between 0 and 5.");
        }

        Rating = rating;
        Tags = NormalizeTags(tags);
    }

    public int Rating { get; }

    public ImmutableArray<string> Tags { get; }

    public static string NormalizeTag(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        var result = new StringBuilder(tag.Length);
        var pendingSpace = false;
        foreach (var character in tag.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = result.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                result.Append(' ');
                pendingSpace = false;
            }

            result.Append(character);
        }

        return result.ToString().Normalize(NormalizationForm.FormC);
    }

    internal static string GetTagKey(string tag) => NormalizeTag(tag).ToUpperInvariant();

    private static readonly char[] TagTokenSeparators =
        [' ', '\t', '-', '_', '.', ',', ';', ':', '/', '\\', '(', ')', '[', ']', '"', '\'', '*', '#'];

    /// <summary>
    /// Matches a tag against typed text ignoring word order and separators, so that
    /// "щурка" finds "золотистая щурка". Every typed token must appear somewhere in the tag.
    /// </summary>
    public static bool MatchesTagQuery(string tag, string query)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(query);

        var normalizedTag = NormalizeTag(tag);
        if (normalizedTag.Length == 0)
        {
            return false;
        }

        var normalizedQuery = NormalizeTag(query);
        if (normalizedQuery.Length == 0)
        {
            return true;
        }

        if (normalizedTag.Contains(normalizedQuery, StringComparison.CurrentCultureIgnoreCase))
        {
            return true;
        }

        var tokens = normalizedQuery.Split(
            TagTokenSeparators,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        return tokens.All(token => normalizedTag.Contains(token, StringComparison.CurrentCultureIgnoreCase));
    }

    private static ImmutableArray<string> NormalizeTags(IEnumerable<string>? tags)
    {
        if (tags is null)
        {
            return [];
        }

        var uniqueTags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in tags)
        {
            if (value is null)
            {
                continue;
            }

            var normalized = NormalizeTag(value);
            if (normalized.Length > 0)
            {
                uniqueTags.TryAdd(normalized, normalized);
            }
        }

        return uniqueTags.Values
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value, StringComparer.Ordinal)
            .ToImmutableArray();
    }
}
