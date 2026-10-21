using ImageCare.Core.Domain.Folders;

namespace ImageCare.Core.Services.FileSystemWatcherService;

public interface IFileSystemWatcherService
{
    IObservable<FileModel> FileCreated { get; }

    IObservable<FileModel> FileChanged { get; }

    IObservable<FileModel> FileDeleted { get; }

    IObservable<FileRenamedModel> FileRenamed { get; }

    IObservable<DirectoryModel> DirectoryCreated { get; }

    IObservable<DirectoryModel> DirectoryDeleted { get; }

    IObservable<DirectoryRenamedModel> DirectoryRenamed { get; }

    IObservable<string> RescanRequested { get; }

    void StartWatching();

    void StopWatching();

    void StartWatchingDirectory(string directoryPath);
}
