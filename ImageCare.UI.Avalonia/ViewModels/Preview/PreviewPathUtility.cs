using System;
using System.IO;

namespace ImageCare.UI.Avalonia.ViewModels.Preview;

/// <summary>
/// Path helpers shared by the preview-panel facets. File-system paths are compared
/// case-insensitively on Windows and case-sensitively elsewhere.
/// </summary>
internal static class PreviewPathUtility
{
    public static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static bool PathsEqual(string? left, string? right)
    {
        if (left == null || right == null)
        {
            return false;
        }

        return string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            PathComparison);
    }

    public static bool IsXmpPath(string path) =>
        Path.GetExtension(path).Equals(".xmp", StringComparison.OrdinalIgnoreCase);
}
