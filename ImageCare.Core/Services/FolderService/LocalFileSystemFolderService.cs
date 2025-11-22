using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Exceptions;
using ImageCare.Core.Services.FileSystemService;

namespace ImageCare.Core.Services.FolderService;

public sealed class LocalFileSystemFolderService : IFolderService, IDisposable
{
    private readonly Subject<SelectedDirectory> _selectedDirectorySubject;
    private readonly Subject<SelectedDirectory> _folderVisitingSubject;
    private readonly Subject<SelectedDirectory> _folderLeftSubject;
    private readonly IFileSystemService _fileSystemService;
    private readonly IDriveModelsFactory _driveModelsFactory;

    private readonly ConcurrentDictionary<FileManagerPanel, DirectoryModel> _selectedDirectories = new();
    private readonly ConcurrentDictionary<(string, FileManagerPanel), SelectedDirectory> _visitingDirectoryModels = new();
    private bool _disposed;

    public LocalFileSystemFolderService(IFileSystemService fileSystemService, IDriveModelsFactory driveModelsFactory)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _driveModelsFactory = driveModelsFactory ?? throw new ArgumentNullException(nameof(driveModelsFactory));

        _selectedDirectorySubject = new Subject<SelectedDirectory>();
        _folderVisitingSubject = new Subject<SelectedDirectory>();
        _folderLeftSubject = new Subject<SelectedDirectory>();
    }

    public IObservable<SelectedDirectory> FileSystemItemSelected => _selectedDirectorySubject.AsObservable();

    public IObservable<SelectedDirectory> FolderVisited => _folderVisitingSubject.AsObservable();

    public IObservable<SelectedDirectory> FolderLeft => _folderLeftSubject.AsObservable();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _selectedDirectorySubject.Dispose();
        _folderVisitingSubject.Dispose();
        _folderLeftSubject.Dispose();
        _disposed = true;
    }

    public async Task<DirectoryModel> GetDirectoryModelAsync(DirectoryModel? directoryModel = null)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LocalFileSystemFolderService));
        }

        return directoryModel == null
                   ? await GetRootDirectoriesLevelAsync()
                   : await GetCustomDirectoriesLevelAsync(directoryModel);
    }

    public async Task<DirectoryModel> GetDirectoryModelAsync(string directoryPath)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LocalFileSystemFolderService));
        }

        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ServiceException("Directory path cannot be null or empty");
        }

        if (!_fileSystemService.DirectoryExists(directoryPath))
        {
            return new DirectoryModel("Invalid folder", string.Empty);
        }

        var directoryModel = new DirectoryModel(Path.GetFileName(directoryPath), directoryPath);

        return await GetCustomDirectoriesLevelAsync(directoryModel);
    }

    public async Task<IEnumerable<FileModel>> GetFileModelAsync(DirectoryModel directoryModel, string searchPattern)
    {
        return await GetFileModelAsync(directoryModel.Path, searchPattern);
    }

    public async Task<IEnumerable<FileModel>> GetFileModelAsync(string directoryPath, string searchPattern)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LocalFileSystemFolderService));
        }

        return await Task.Run(() =>
        {
            if (!_fileSystemService.DirectoryExists(directoryPath))
            {
                return Enumerable.Empty<FileModel>();
            }

            var files = _fileSystemService.EnumerateFiles(directoryPath, searchPattern)
                                          .Select(file => CreateFileModel(file))
                                          .Where(f => f.CreatedDateTime.HasValue);

            return files;
        });
    }




    public void SetSelectedDirectory(SelectedDirectory selectedDirectory)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LocalFileSystemFolderService));
        }

        _selectedDirectories.AddOrUpdate(
            selectedDirectory.FileManagerPanel,
            selectedDirectory,
            (_, _) => selectedDirectory);
        _selectedDirectorySubject.OnNext(selectedDirectory);
    }

    public DirectoryModel? GetSelectedDirectory(FileManagerPanel fileManagerPanel)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LocalFileSystemFolderService));
        }

        return _selectedDirectories.GetValueOrDefault(fileManagerPanel);
    }

    public void AddVisitingFolder(DirectoryModel directoryModel, FileManagerPanel fileManagerPanel)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LocalFileSystemFolderService));
        }

        if (directoryModel is DeviceModel)
        {
            return;
        }

        var visitingDirectory = new SelectedDirectory(directoryModel, fileManagerPanel);
        if (_visitingDirectoryModels.TryAdd((directoryModel.Path, fileManagerPanel), visitingDirectory))
        {
            _folderVisitingSubject.OnNext(visitingDirectory);
        }
    }

    public void RemoveVisitingFolder(DirectoryModel directoryModel, FileManagerPanel fileManagerPanel)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LocalFileSystemFolderService));
        }

        if (_visitingDirectoryModels.TryRemove((directoryModel.Path, fileManagerPanel), out var removedDirectory))
        {
            _folderLeftSubject.OnNext(removedDirectory);
        }
    }

    public void RemoveFolder(DirectoryModel directoryModel)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LocalFileSystemFolderService));
        }

        if (directoryModel is DriveModel || directoryModel is DeviceModel)
        {
            return;
        }

        if (_fileSystemService.GetFiles(directoryModel.Path).Any())
        {
            return;
        }

        _fileSystemService.SafeDeleteDirectory(directoryModel.Path);
    }

    public DirectoryModel? CreateSubFolder(DirectoryModel directoryModel)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LocalFileSystemFolderService));
        }

        if (directoryModel is DeviceModel)
        {
            return null;
        }

        var fullName = CreateNewDirectoryFullName(directoryModel.Path);
        _fileSystemService.CreateDirectory(fullName);

        return new DirectoryModel(Path.GetFileName(fullName), fullName);
    }

    public async Task<DirectoryModel> GetCustomDirectoriesLevelAsync(DirectoryModel directoryModel, bool preview = false)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LocalFileSystemFolderService));
        }

        if (directoryModel.DirectoryModels.Any())
        {
            return directoryModel;
        }

        return await Task.Run(() =>
        {
            if (!_fileSystemService.DirectoryExists(directoryModel.Path))
            {
                return directoryModel;
            }

            var subDirectories = _fileSystemService.EnumerateDirectories(directoryModel.Path, "*");
            var directoriesToProcess = preview ? subDirectories.Take(1) : subDirectories;

            if (!preview)
            {
                var directories = directoriesToProcess
                                  .AsParallel()
                                  .WithDegreeOfParallelism(Environment.ProcessorCount)
                                  .Select(subDirectory =>
                                  {
                                      try
                                      {
                                          return ProcessDirectory(subDirectory, preview);
                                      }
                                      catch (UnauthorizedAccessException)
                                      {
                                          return null;
                                      }
                                  })
                                  .Where(dir => dir != null)
                                  .ToList();

                directoryModel.AddDirectories(directories);
            }
            else
            {
                foreach (var subDirectory in directoriesToProcess)
                {
                    try
                    {
                        var directory = ProcessDirectory(subDirectory, preview);
                        directoryModel.AddDirectory(directory);
                        break;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Ignored
                    }
                }
            }

            return directoryModel;
        });
    }

    private DirectoryModel ProcessDirectory(string subDirectory, bool preview)
    {
        var directoryName = Path.GetFileName(subDirectory);
        var directory = new DirectoryModel(directoryName, subDirectory);

        if (!preview)
        {
            var firstSubDir = _fileSystemService.EnumerateDirectories(subDirectory, "*").FirstOrDefault();
            if (firstSubDir != null)
            {
                directory.AddDirectory(new DirectoryModel(Path.GetFileName(firstSubDir), firstSubDir));
            }
        }

        directory.HasSupportedMedia = CheckHasSupportedMedia(subDirectory);

        return directory;
    }

    private bool CheckHasSupportedMedia(string directoryPath)
    {
        try
        {
            var supportedExtensions = MediaFormat.GetSupportedExtensions();
            var supportedExtensionsSet = new HashSet<string>(supportedExtensions, StringComparer.OrdinalIgnoreCase);

            var files = _fileSystemService.EnumerateFiles(directoryPath, "*.*");

            foreach (var file in files)
            {
                var extension = Path.GetExtension(file);
                if (supportedExtensionsSet.Contains(extension))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception _)
        {
            return false;
        }
    }

    private (MediaFormat mediaFormat, long count) GetMediaFormatCountFromFolder(string folderPath, string extension)
    {
        var mediaFormat = MediaFormat.GetMediaFormatByExtension(extension);
        if (mediaFormat != null && _fileSystemService.DirectoryExists(folderPath))
        {
            var files = _fileSystemService.GetFiles(folderPath, $"*{extension}");
            return (mediaFormat, files.Length);
        }

        return (MediaFormat.MediaFormatUnknown, 0);
    }

    private string CreateNewDirectoryFullName(string directoryPath)
    {
        const string initialName = "New Folder";
        var counter = 0;
        var finalName = initialName;

        while (_fileSystemService.EnumerateDirectories(directoryPath, finalName).Any())
        {
            counter++;
            finalName = $"{initialName}({counter})";
        }

        return Path.Combine(directoryPath, finalName);
    }

    private async Task<DirectoryModel> GetRootDirectoriesLevelAsync()
    {
        return await Task.Run(async () =>
        {
            var rootModel = new DeviceModel(Environment.MachineName, "//");
            var drives = DriveInfo.GetDrives();

            var driveTasks = drives
                             .Select(async driveInfo =>
                             {
                                 try
                                 {
                                     using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                                     return await ProcessDriveAsync(driveInfo, cts.Token);
                                 }
                                 catch (OperationCanceledException ex)
                                 {
                                     throw new ServiceException($"Drive processing timeout: {driveInfo.Name}", ex);
                                 }
                                 catch (Exception ex)
                                 {
                                     throw new ServiceException($"Failed to process drive: {driveInfo.Name}", ex);
                                 }
                             })
                             .ToList();

            var processedDrives = await Task.WhenAll(driveTasks);

            foreach (var drive in processedDrives)
            {
                if (drive != null)
                {
                    rootModel.AddDirectory(drive);
                }
            }

            return rootModel;
        });
    }

    private async Task<DriveModel?> ProcessDriveAsync(DriveInfo driveInfo, CancellationToken cancellationToken)
    {
        if (driveInfo.DriveType == DriveType.Network)
        {
            try
            {
                var isReady = await CheckDriveReadyWithTimeoutAsync(driveInfo, cancellationToken);
                if (!isReady)
                {
                    return null;
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        var drive = _driveModelsFactory.CreateDriveModel(driveInfo);
        if (drive?.RootDirectory == null)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var firstDirectoryTier = await GetCustomDirectoriesLevelAsync(drive.RootDirectory, true);
        drive.AddDirectories(firstDirectoryTier.DirectoryModels);

        return drive;
    }

    private async Task<bool> CheckDriveReadyWithTimeoutAsync(DriveInfo driveInfo, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(() => { return driveInfo.IsReady; }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool MatchesSearchPattern(string fileName, string searchPattern)
    {
        return searchPattern == "*.*" || fileName.EndsWith(searchPattern.TrimStart('*'), StringComparison.OrdinalIgnoreCase);
    }

    private FileModel CreateFileModel(string filePath)
    {
        var fileInfo = _fileSystemService.GetFileInfo(filePath);
        return new FileModel(fileInfo.Name, fileInfo.FullName, fileInfo.LastWriteTime);
    }
}