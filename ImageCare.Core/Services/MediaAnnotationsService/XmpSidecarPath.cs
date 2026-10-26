namespace ImageCare.Core.Services.MediaAnnotationsService;

/// <summary>
/// Resolves external XMP sidecars without platform-specific path assumptions.
/// </summary>
public static class XmpSidecarPath
{
    private static readonly string[] ProprietaryRawExtensions = [".nef", ".arw", ".cr3", ".dng"];
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    /// <summary>
    /// Returns the external XMP sidecar path, or <see langword="null"/> when XMP is embedded.
    /// </summary>
    public static string? GetExternalSidecarPath(string mediaPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);

        var extension = Path.GetExtension(mediaPath);
        if (IsJpegExtension(extension))
        {
            return null;
        }

        return UsesBaseNameSidecar(mediaPath)
            ? Path.ChangeExtension(mediaPath, ".xmp")
            : mediaPath + ".xmp";
    }

    /// <summary>
    /// Returns the external sidecar only when it exists according to the supplied file-system check.
    /// </summary>
    public static string? GetExistingExternalSidecarPath(
        string mediaPath,
        Func<string, bool>? fileExists = null)
    {
        EnsureUnambiguousRawOwner(mediaPath);
        var sidecarPath = GetExternalSidecarPath(mediaPath);
        return sidecarPath is not null && (fileExists ?? File.Exists)(sidecarPath)
            ? sidecarPath
            : null;
    }

    /// <summary>
    /// Resolves the sidecar destination while preserving the source media's sidecar naming convention.
    /// </summary>
    public static string? GetDestinationExternalSidecarPath(
        string sourceMediaPath,
        string destinationMediaPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceMediaPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationMediaPath);

        var sourceExtension = Path.GetExtension(sourceMediaPath);
        if (IsJpegExtension(sourceExtension))
        {
            return null;
        }

        return IsProprietaryRawExtension(sourceExtension)
            ? Path.ChangeExtension(destinationMediaPath, ".xmp")
            : destinationMediaPath + ".xmp";
    }

    public static bool UsesEmbeddedXmp(string mediaPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        return IsJpegExtension(Path.GetExtension(mediaPath));
    }

    public static bool UsesBaseNameSidecar(string mediaPath) =>
        IsProprietaryRawExtension(Path.GetExtension(mediaPath));

    public static void EnsureUnambiguousRawOwner(string mediaPath)
    {
        var owners = GetExistingRawOwners(mediaPath);
        if (owners.Count > 1)
        {
            throw new RawSidecarAmbiguityException(GetExternalSidecarPath(mediaPath)!, owners);
        }
    }

    public static bool HasAnotherRawOwner(string mediaPath)
    {
        if (!UsesBaseNameSidecar(mediaPath))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(mediaPath);
        return GetExistingRawOwners(fullPath).Any(owner => !PathComparer.Equals(owner, fullPath));
    }

    public static IReadOnlyList<string> GetExistingRawOwners(string mediaPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        if (!UsesBaseNameSidecar(mediaPath))
        {
            return [];
        }

        var fullPath = Path.GetFullPath(mediaPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        var baseName = Path.GetFileNameWithoutExtension(fullPath);
        try
        {
            return Directory.EnumerateFiles(directory)
                .Where(path => IsProprietaryRawExtension(Path.GetExtension(path)))
                .Where(path => Path.GetFileNameWithoutExtension(path)
                    .Equals(baseName, StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFullPath)
                .Distinct(PathComparer)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Cannot determine RAW sidecar ownership for '{fullPath}'.", exception);
        }
    }

    private static bool IsProprietaryRawExtension(string extension) =>
        ProprietaryRawExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

    private static bool IsJpegExtension(string extension) =>
        extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
}
