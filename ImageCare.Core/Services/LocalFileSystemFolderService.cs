using ImageCare.Core.Domain;

namespace ImageCare.Core.Services;

public sealed class LocalFileSystemFolderService : IFolderService
{
    private static readonly Dictionary<DriveType, Func<DriveInfo, DriveModel>> _driveModelFactoryMethods = new()
    {
        { DriveType.Fixed, drive => new FixedDriveModel(drive.Name, drive.RootDirectory.FullName) { RootDirectory = new DirectoryModel(drive.RootDirectory.Name, drive.RootDirectory.FullName) } },
        { DriveType.Removable, drive => new RemovableDriveModel(drive.Name, drive.RootDirectory.FullName) { RootDirectory = new DirectoryModel(drive.RootDirectory.Name, drive.RootDirectory.FullName) } },
        { DriveType.Network, drive => new NetworkDriveModel(drive.Name, drive.RootDirectory.FullName) { RootDirectory = new DirectoryModel(drive.RootDirectory.Name, drive.RootDirectory.FullName) } }
    };

    /// <inheritdoc />
    public async Task<DirectoryModel> GetDirectoryModelAsync(DirectoryModel? directoryModel = null)
    {
        if (directoryModel == null)
        {
            return await GetRootDirectoriesLevelAsync();
        }

        return await GetCustomDirectoriesLevelAsync(directoryModel);
    }

    /// <inheritdoc />
    public async Task<DirectoryModel> GetDirectoryModelAsync(string directoryPath)
    {
        return await Task.Run(
                   async () =>
                   {
                       var directoryInfo = new DirectoryInfo(directoryPath);
                       if (!directoryInfo.Exists)
                       {
                           return new DirectoryModel("Invalid folder", string.Empty);
                       }

                       var directoryModel = new DirectoryModel(directoryInfo.Name, directoryInfo.FullName);

                       return await GetCustomDirectoriesLevelAsync(directoryModel);
                   });
    }

    /// <inheritdoc />
    public async Task<IEnumerable<FileModel>> GetFileModelAsync(DirectoryModel directoryModel, string searchPattern)
    {
        return await Task.Run(async () => { return await GetFileModelAsync(directoryModel.Path, searchPattern); });
    }

    public async Task<IEnumerable<FileModel>> GetFileModelAsync(string directoryPath, string searchPattern)
    {
        return await Task.Run(
                   () =>
                   {
                       var files = new List<FileModel>();
                       var directoryInfo = new DirectoryInfo(directoryPath);

                       if (!directoryInfo.Exists)
                       {
                           return files;
                       }

                       foreach (var fileInfo in directoryInfo.EnumerateFiles(searchPattern))
                       {
                           files.Add(new FileModel(fileInfo.Name, fileInfo.FullName));
                       }

                       return files;
                   });
    }

    private async Task<DirectoryModel> GetRootDirectoriesLevelAsync()
    {
        return await Task.Run(
                   async () =>
                   {
                       var rootModel = new DeviceModel(Environment.MachineName, "//");
                       var drives = DriveInfo.GetDrives();

                       foreach (var driveInfo in drives)
                       {
                           if (_driveModelFactoryMethods.TryGetValue(driveInfo.DriveType, out var factoryFunc))
                           {
                               var drive = factoryFunc.Invoke(driveInfo);
                               rootModel.AddDirectory(drive);
                               if (drive.RootDirectory == null)
                               {
                                   continue;
                               }

                               var firstDirectoryTier = await GetCustomDirectoriesLevelAsync(drive.RootDirectory, true);
                               drive.AddDirectories(firstDirectoryTier.DirectoryModels);
                           }
                       }

                       return rootModel;
                   });
    }

    private async Task<DirectoryModel> GetCustomDirectoriesLevelAsync(DirectoryModel directoryModel, bool preview = false, string searchPattern = "*")
    {
        return await Task.Run(
                   () =>
                   {
                       var rootDirectoryInfo = new DirectoryInfo(directoryModel.Path);
                       if (!rootDirectoryInfo.Exists || directoryModel.DirectoryModels.Any())
                       {
                           return directoryModel;
                       }

                       foreach (var directoryInfo in rootDirectoryInfo.EnumerateDirectories())
                       {
                           var directory = new DirectoryModel(directoryInfo.Name, directoryInfo.FullName);

                           // Add first subdirectory if possible.
                           var directoryModelInfo = new DirectoryInfo(directory.Path);

                           try
                           {
                               if (directoryModelInfo.EnumerateDirectories().FirstOrDefault() is { } subDirectoryInfo)
                               {
                                   directory.AddDirectory(new DirectoryModel(subDirectoryInfo.Name, subDirectoryInfo.FullName));
                               }

                               directoryModel.AddDirectory(directory);
                           }
                           catch (UnauthorizedAccessException)
                           {
                               // Ignored.
                           }

                           if (preview)
                           {
                               break;
                           }
                       }

                       return directoryModel;
                   });
    }
}