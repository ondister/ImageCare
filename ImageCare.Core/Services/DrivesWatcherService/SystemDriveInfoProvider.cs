namespace ImageCare.Core.Services.DrivesWatcherService;

public sealed class SystemDriveInfoProvider : IDriveInfoProvider
{
	public IEnumerable<DriveInfo> GetDrives()
	{
		return DriveInfo.GetDrives();
	}

	public DriveInfo? GetDriveByName(string driveName)
	{
		return DriveInfo.GetDrives()
		                .FirstOrDefault(d =>
			                                d.Name.StartsWith(driveName, StringComparison.OrdinalIgnoreCase));
	}
}