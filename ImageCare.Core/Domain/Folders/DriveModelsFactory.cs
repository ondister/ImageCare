namespace ImageCare.Core.Domain.Folders;

public sealed class DriveModelsFactory : IDriveModelsFactory
{
    private static readonly Dictionary<DriveType, Func<DriveInfo, DriveModel>> DriveModelFactoryMethods = new()
    {
        {
            DriveType.Fixed,
            drive => new FixedDriveModel(drive.Name, drive.RootDirectory.FullName)
            {
                RootDirectory = CreateRootDirectory(drive)
            }
        },
        {
            DriveType.Removable,
            drive => new RemovableDriveModel(
                drive.Name,
                drive.RootDirectory.FullName,
                drive.TotalSize,
                drive.AvailableFreeSpace)
            {
                RootDirectory = CreateRootDirectory(drive)
            }
        },
        {
            DriveType.Network,
            drive => new NetworkDriveModel(drive.Name, drive.RootDirectory.FullName)
            {
                RootDirectory = CreateRootDirectory(drive)
            }
        }
    };

    public DriveModel? CreateDriveModel(DriveInfo driveInfo)
    {
        try
        {
            if (DriveModelFactoryMethods.TryGetValue(driveInfo.DriveType, out var factory))
            {
                return factory(driveInfo);
            }

            return new DriveModel(driveInfo.Name, driveInfo.RootDirectory.FullName)
            {
                RootDirectory = CreateRootDirectory(driveInfo)
            };
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static DirectoryModel CreateRootDirectory(DriveInfo drive)
    {
        var root = drive.RootDirectory;
        var name = string.IsNullOrWhiteSpace(root.Name) ? drive.Name : root.Name;
        return new DirectoryModel(name, root.FullName);
    }
}
