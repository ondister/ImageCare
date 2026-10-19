using ImageCare.Core.Domain.Configuration;

namespace ImageCare.Core.Domain;

public sealed class FileApplicationInfo
{
    public FileApplicationInfo(string name, string applicationPath)
        : this(name, applicationPath, ApplicationLaunchMode.Executable)
    {
    }

    public FileApplicationInfo(string name, string applicationPath, ApplicationLaunchMode launchMode)
    {
        Name = name;
        ApplicationPath = applicationPath;
        LaunchMode = launchMode;
    }

    public string Name { get; }

    public string ApplicationPath { get; }

    public ApplicationLaunchMode LaunchMode { get; }
}

public sealed class FileApplicationInfoComparer : IEqualityComparer<FileApplicationInfo>
{
    public static readonly FileApplicationInfoComparer Instance = new();

    public bool Equals(FileApplicationInfo? x, FileApplicationInfo? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x is null || y is null)
        {
            return false;
        }

        return x.LaunchMode == y.LaunchMode
            && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.ApplicationPath, y.ApplicationPath, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode(FileApplicationInfo obj)
    {
        return HashCode.Combine(
            obj.Name?.ToLowerInvariant(),
            obj.ApplicationPath?.ToLowerInvariant(),
            obj.LaunchMode);
    }
}
