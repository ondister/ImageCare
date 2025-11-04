using ImageCare.Core.Domain.Folders;

namespace ImageCare.Core.Services.FileSystemWatcherService;

public interface IFileSystemWatcherService
{
	public IObservable<FileModel> FileCreated { get; }

	public IObservable<FileModel> FileDeleted { get; }

	public IObservable<FileRenamedModel> FileRenamed { get; }

	public IObservable<DirectoryModel> DirectoryCreated { get; }

	public IObservable<DirectoryModel> DirectoryDeleted { get; }

	public IObservable<DirectoryRenamedModel> DirectoryRenamed { get; }

	public void StartWatching();

	public void StopWatching();

	public void StartWatchingDirectory(string directoryPath);
}