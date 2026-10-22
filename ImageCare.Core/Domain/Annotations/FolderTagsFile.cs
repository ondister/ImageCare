using System.Text;

namespace ImageCare.Core.Domain.Annotations;

/// <summary>
/// Parses and serializes a <c>folder-tags.txt</c> descriptor.
/// Format: one tag per line, blank lines ignored, lines starting with '#' are comments.
/// There are no separators between tags, so a tag may safely contain commas.
/// </summary>
public static class FolderTagsFile
{
    public const string FileName = "folder-tags.txt";

    public static IReadOnlyList<string> Parse(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return [];
        }

        var tags = new List<string>();
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var tag = MediaAnnotations.NormalizeTag(line);
            if (tag.Length > 0)
            {
                tags.Add(tag);
            }
        }

        return MediaAnnotations.NormalizeTagsOrdered(tags);
    }

    public static string Serialize(IEnumerable<string>? tags)
    {
        var normalized = MediaAnnotations.NormalizeTagsOrdered(tags ?? []);
        if (normalized.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var tag in normalized)
        {
            builder.Append(tag).Append('\n');
        }

        return builder.ToString();
    }
}
