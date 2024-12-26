namespace ImageCare.Core.Services.FileSystemWatcherService;

public interface IMultiSourcesFileSystemWatcherService : IFileSystemWatcherService
{
    void StopWatchingDirectory(string directoryPath);

    void ClearWatchers();
}