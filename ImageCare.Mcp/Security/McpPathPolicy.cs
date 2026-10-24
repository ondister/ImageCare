using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Services.ConfigurationService;

namespace ImageCare.Mcp.Security;

/// <summary>
/// Restricts file-system access to locations the user has explicitly opened up to the
/// application: configured search roots, removable drives, and folders referenced by a
/// review session. Everything else is rejected so a model cannot rummage through the disk.
/// </summary>
public sealed class McpPathPolicy
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly IConfigurationService _configurationService;

    public McpPathPolicy(IConfigurationService configurationService)
    {
        _configurationService = configurationService;
    }

    public bool IsAllowed(string path, IEnumerable<string> additionalRoots)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        string normalized;
        try
        {
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        foreach (var root in EnumerateRoots(additionalRoots))
        {
            if (IsSameOrDescendant(normalized, root))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsAllowedForAnyRoot(string path, IEnumerable<string> additionalRoots)
    {
        return IsAllowed(path, additionalRoots);
    }

    private IEnumerable<string> EnumerateRoots(IEnumerable<string> additionalRoots)
    {
        var configuration = _configurationService.Configuration.Value;

        foreach (var root in EnumerateSafe(configuration.MediaSearchRootPaths))
        {
            yield return root;
        }

        foreach (var root in EnumerateSafe(additionalRoots))
        {
            yield return root;
        }

        foreach (var drive in EnumerateRemovableDrives())
        {
            yield return drive;
        }
    }

    private static IEnumerable<string> EnumerateSafe(IEnumerable<string>? paths)
    {
        if (paths is null)
        {
            yield break;
        }

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string normalized;
            try
            {
                normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            yield return normalized;
        }
    }

    private static IEnumerable<string> EnumerateRemovableDrives()
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var drive in drives)
        {
            string? root = null;
            try
            {
                if (drive.DriveType != DriveType.Removable || !drive.IsReady)
                {
                    continue;
                }

                root = Path.TrimEndingDirectorySeparator(drive.RootDirectory.FullName);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (root is not null)
            {
                yield return root;
            }
        }
    }

    private static bool IsSameOrDescendant(string candidate, string root)
    {
        if (PathComparer.Equals(candidate, root))
        {
            return true;
        }

        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
    }
}
