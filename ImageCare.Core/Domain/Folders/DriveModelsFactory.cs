namespace ImageCare.Core.Domain.Folders;

internal sealed class DriveModelsFactory
{
    private static readonly Dictionary<DriveType, Func<DriveInfo, DriveModel>> _driveModelFactoryMethods = new()
    {
        { DriveType.Fixed, drive => new FixedDriveModel(drive.Name, drive.RootDirectory.FullName) { RootDirectory = new DirectoryModel(drive.RootDirectory.Name, drive.RootDirectory.FullName) } },
        { DriveType.Removable, drive => new RemovableDriveModel(drive.Name, drive.RootDirectory.FullName,drive.TotalSize,drive.AvailableFreeSpace) { RootDirectory = new DirectoryModel(drive.RootDirectory.Name, drive.RootDirectory.FullName) } },
        { DriveType.Network, drive => new NetworkDriveModel(drive.Name, drive.RootDirectory.FullName) { RootDirectory = new DirectoryModel(drive.RootDirectory.Name, drive.RootDirectory.FullName) } }
    };

    public DriveModel? CreateDriveModel(DriveInfo driveInfo)
    {
        return !_driveModelFactoryMethods.TryGetValue(driveInfo.DriveType, out var factoryFunc) ? null : factoryFunc.Invoke(driveInfo);
    }
}