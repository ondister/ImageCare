using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using ImageCare.Core.Domain;

namespace ImageCare.Core.Services.FolderService;

public sealed class LocalFileSystemFolderService : IFolderService, IDisposable
{
    private readonly Subject<SelectedDirectory> _selectedDirectorySubject;
    private readonly Subject<SelectedDirectory> _folderVisitingSubject;
    private readonly Subject<SelectedDirectory> _folderLeftSubject;

    private readonly ConcurrentDictionary<FileManagerPanel, DirectoryModel> _selectedDirectories = new();
    private readonly ConcurrentDictionary<(string, FileManagerPanel), SelectedDirectory> _visitingDirectoryModels = new();
    private readonly DriveModelsFactory _driveModelsFactory;

    public LocalFileSystemFolderService()
    {
        _selectedDirectorySubject = new Subject<SelectedDirectory>();
        _folderVisitingSubject = new Subject<SelectedDirectory>();
        _folderLeftSubject = new Subject<SelectedDirectory>();

        _driveModelsFactory = new DriveModelsFactory();
    }

    /// <inheritdoc />
    public IObservable<SelectedDirectory> FileSystemItemSelected => _selectedDirectorySubject.AsObservable();

    /// <inheritdoc />
    public IObservable<SelectedDirectory> FolderVisited => _folderVisitingSubject.AsObservable();

    /// <inheritdoc />
    public IObservable<SelectedDirectory> FolderLeft => _folderLeftSubject.AsObservable();

    /// <inheritdoc />
    public void Dispose()
    {
        _selectedDirectorySubject.Dispose();
        _folderVisitingSubject.Dispose();
        _folderLeftSubject.Dispose();
    }

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
        return await Task.Run(async () => await GetFileModelAsync(directoryModel.Path, searchPattern));
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

                       files.AddRange(directoryInfo.EnumerateFiles(searchPattern).Select(fileInfo => new FileModel(fileInfo.Name, fileInfo.FullName)));

                       return files;
                   });
    }

    /// <inheritdoc />
    public void SetSelectedDirectory(SelectedDirectory selectedDirectory)
    {
        _selectedDirectories.AddOrUpdate(selectedDirectory.FileManagerPanel, _ => selectedDirectory, (_, _) => selectedDirectory);
        _selectedDirectorySubject.OnNext(selectedDirectory);
    }

    /// <inheritdoc />
    public DirectoryModel? GetSelectedDirectory(FileManagerPanel fileManagerPanel)
    {
        return _selectedDirectories.GetValueOrDefault(fileManagerPanel);
    }

    /// <inheritdoc />
    public void AddVisitingFolder(DirectoryModel directoryModel, FileManagerPanel fileManagerPanel)
    {
        var visitingDirectory = new SelectedDirectory(directoryModel, fileManagerPanel);
        if (_visitingDirectoryModels.TryAdd((directoryModel.Path, fileManagerPanel), visitingDirectory))
        {
            _folderVisitingSubject.OnNext(visitingDirectory);
        }
    }

    /// <inheritdoc />
    public void RemoveVisitingFolder(DirectoryModel directoryModel, FileManagerPanel fileManagerPanel)
    {
        if (_visitingDirectoryModels.TryRemove((directoryModel.Path, fileManagerPanel), out var removedDirectoryModel))
        {
            _folderLeftSubject.OnNext(removedDirectoryModel);
        }
    }

    public async Task<DirectoryModel> GetCustomDirectoriesLevelAsync(DirectoryModel directoryModel, bool preview = false)
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

    private async Task<DirectoryModel> GetRootDirectoriesLevelAsync()
    {
        return await Task.Run(
                   async () =>
                   {
                       var rootModel = new DeviceModel(Environment.MachineName, "//");
                       var drives = DriveInfo.GetDrives();

                       foreach (var driveInfo in drives)
                       {
                           if (_driveModelsFactory.CreateDriveModel(driveInfo) is { } drive)
                           {
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
}