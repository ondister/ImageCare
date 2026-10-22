namespace ImageCare.Core.Domain.Annotations;

/// <summary>
/// Resolves the effective folder-tag set for a directory using variant B: start at the
/// directory and walk upwards, collecting the tags declared by every ancestor's
/// <c>folder-tags.txt</c> descriptor until the walk reaches <paramref name="boundaryPath"/>
/// (inclusive) or the file-system root. Missing and empty descriptors are skipped, so the
/// result is the union of all inherited descriptors, ordered nearest-first.
/// </summary>
public static class FolderTagsInheritance
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    /// <summary>
    /// Walks from <paramref name="directoryPath"/> upwards, calling
    /// <paramref name="readDescriptor"/> for each directory and merging every non-empty tag
    /// list it returns. Directories above <paramref name="boundaryPath"/> are not visited.
    /// </summary>
    public static IReadOnlyList<string> Resolve(
        string directoryPath,
        Func<string, IReadOnlyList<string>?> readDescriptor,
        string? boundaryPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(readDescriptor);

        var boundary = string.IsNullOrWhiteSpace(boundaryPath)
            ? null
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(boundaryPath));
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
        var inherited = new List<string>();

        while (true)
        {
            var tags = readDescriptor(current);
            if (tags is { Count: > 0 })
            {
                // Variant B: nearest directory first, then its ancestors.
                inherited.AddRange(tags);
            }

            if (boundary is not null && PathComparer.Equals(current, boundary))
            {
                break;
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || PathComparer.Equals(parent, current))
            {
                break;
            }

            current = Path.TrimEndingDirectorySeparator(parent);
        }

        return MediaAnnotations.NormalizeTagsOrdered(inherited);
    }
}
