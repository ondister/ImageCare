using ImageCare.Core.Domain;

namespace ImageCare.Core.Services.FileSystemWatcherService;

public interface IFileSystemWatcherService
{
    public IObservable<FileModel> FileCreated { get; }

    public IObservable<FileModel> FileDeleted { get; }

    public IObservable<FileRenamedModel> FileRenamed { get; }

    /// <inheritdoc />
    public IObservable<DirectoryModel> DirectoryCreated { get; }

    /// <inheritdoc />
    public IObservable<DirectoryModel> DirectoryDeleted { get; }

    /// <inheritdoc />
    public IObservable<DirectoryRenamedModel> DirectoryRenamed { get; }

    public void StartWatching();

    public void StopWatching();

    public void StartWatchingDirectory(string directoryPath);
}