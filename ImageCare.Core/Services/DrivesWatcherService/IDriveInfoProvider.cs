namespace ImageCare.Core.Services.DrivesWatcherService;

public interface IDriveInfoProvider
{
	IEnumerable<DriveInfo> GetDrives();

	DriveInfo? GetDriveByName(string driveName);
}