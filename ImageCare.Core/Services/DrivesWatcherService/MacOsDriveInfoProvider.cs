namespace ImageCare.Core.Services.DrivesWatcherService;

public sealed class MacOsDriveInfoProvider : IDriveInfoProvider
{
    private const string VolumesRoot = "/Volumes/";

    public IEnumerable<DriveInfo> GetDrives()
    {
        return DriveInfo.GetDrives()
                        .Where(IsUserVisibleVolume)
                        .Where(IsReady)
                        .ToArray();
    }

    public DriveInfo? GetDriveByName(string driveName)
    {
        var normalizedName = Normalize(driveName);
        return GetDrives().FirstOrDefault(drive =>
            string.Equals(Normalize(drive.RootDirectory.FullName), normalizedName, StringComparison.Ordinal));
    }

    private static bool IsUserVisibleVolume(DriveInfo drive)
    {
        var path = Normalize(drive.RootDirectory.FullName);
        return path == Path.DirectorySeparatorChar.ToString()
            || path.StartsWith(VolumesRoot, StringComparison.Ordinal);
    }

    private static bool IsReady(DriveInfo drive)
    {
        try
        {
            return drive.IsReady;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string Normalize(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Path.TrimEndingDirectorySeparator(fullPath);
    }
}
