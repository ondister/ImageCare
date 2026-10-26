using ImageCare.Core.Domain.MediaFormats;

namespace ImageCare.Core.Domain.Configuration;

public static class DefaultFileApplicationAssociations
{
    public const int CurrentVersion = 2;

    public static bool EnsureInitialized(Configuration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.ApplicationAssociationsDefaultsVersion >= CurrentVersion)
        {
            return false;
        }

        AddMissing(configuration);
        configuration.ApplicationAssociationsDefaultsVersion = CurrentVersion;
        return true;
    }

    public static bool AddMissing(Configuration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var changed = false;
        foreach (var extension in MediaFormat.GetSupportedExtensions())
        {
            if (OperatingSystem.IsMacOS())
            {
                changed |= AddIfMissing(configuration, extension, "Finder", ApplicationLaunchMode.FileManager);

                if (string.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase))
                {
                    changed |= AddIfMissing(
                        configuration,
                        extension,
                        "QuickTime Player",
                        ApplicationLaunchMode.MacApplication,
                        "/System/Applications/QuickTime Player.app");
                }
                else
                {
                    changed |= AddIfMissing(
                        configuration,
                        extension,
                        "Preview",
                        ApplicationLaunchMode.MacApplication,
                        "/System/Applications/Preview.app");
                }
            }
            else if (OperatingSystem.IsWindows())
            {
                changed |= AddIfMissing(configuration, extension, "Default application", ApplicationLaunchMode.SystemDefault);
                changed |= AddIfMissing(configuration, extension, "File Explorer", ApplicationLaunchMode.FileManager);
            }
        }

        return changed;
    }

    private static bool AddIfMissing(
        Configuration configuration,
        string extension,
        string name,
        ApplicationLaunchMode launchMode,
        string applicationPath = "")
    {
        var exists = configuration.ApplicationAssociationPairs.Any(association =>
            association.FileExtension.Equals(extension, StringComparison.OrdinalIgnoreCase)
            && association.LaunchMode == launchMode
            && association.ApplicationPath.Equals(applicationPath, StringComparison.OrdinalIgnoreCase));

        if (exists)
        {
            return false;
        }

        configuration.ApplicationAssociationPairs.Add(
            new FileApplicationAssociation
            {
                FileExtension = extension,
                Name = name,
                LaunchMode = launchMode,
                ApplicationPath = applicationPath
            });
        return true;
    }
}
