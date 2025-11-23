using System.Reactive.Linq;
using System.Reactive.Subjects;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Exceptions;

namespace ImageCare.Core.Services.FileSystemWatcherService;

public sealed class LocalFileSystemWatcherService : IFileSystemWatcherService, IDisposable
{
	private readonly FileSystemWatcher _filesWatcher;
	private readonly FileSystemWatcher _directoriesWatcher;

	private readonly Subject<FileModel> _fileCreatedSubject;
	private readonly Subject<FileModel> _fileDeletedSubject;
	private readonly Subject<FileRenamedModel> _fileRenamedSubject;

	private readonly Subject<DirectoryModel> _directoryCreatedSubject;
	private readonly Subject<DirectoryModel> _directoryDeletedSubject;
	private readonly Subject<DirectoryRenamedModel> _directoryRenamedSubject;

	private bool _disposed;

	public LocalFileSystemWatcherService()
	{
		_filesWatcher = new FileSystemWatcher
		{
			NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
		};

		_directoriesWatcher = new FileSystemWatcher
		{
			NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
			IncludeSubdirectories = true
		};

		_fileCreatedSubject = new Subject<FileModel>();
		_fileDeletedSubject = new Subject<FileModel>();
		_fileRenamedSubject = new Subject<FileRenamedModel>();

		_directoryCreatedSubject = new Subject<DirectoryModel>();
		_directoryDeletedSubject = new Subject<DirectoryModel>();
		_directoryRenamedSubject = new Subject<DirectoryRenamedModel>();

		CreateObservables();
	}

	public IObservable<FileModel> FileCreated => _fileCreatedSubject.AsObservable();

	public IObservable<FileModel> FileDeleted => _fileDeletedSubject.AsObservable();

	public IObservable<FileRenamedModel> FileRenamed => _fileRenamedSubject.AsObservable();

	public IObservable<DirectoryModel> DirectoryCreated => _directoryCreatedSubject.AsObservable();

	public IObservable<DirectoryModel> DirectoryDeleted => _directoryDeletedSubject.AsObservable();

	public IObservable<DirectoryRenamedModel> DirectoryRenamed => _directoryRenamedSubject.AsObservable();

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_filesWatcher.Dispose();
		_directoriesWatcher.Dispose();

		_fileCreatedSubject.Dispose();
		_fileDeletedSubject.Dispose();
		_fileRenamedSubject.Dispose();

		_directoryCreatedSubject.Dispose();
		_directoryDeletedSubject.Dispose();
		_directoryRenamedSubject.Dispose();

		_disposed = true;
	}

	public void StartWatching()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(LocalFileSystemWatcherService));
		}

		_filesWatcher.EnableRaisingEvents = true;
		_directoriesWatcher.EnableRaisingEvents = true;
	}

	public void StopWatching()
	{
		if (_disposed)
		{
			return;
		}

		_filesWatcher.EnableRaisingEvents = false;
		_directoriesWatcher.EnableRaisingEvents = false;
	}

	public void StartWatchingDirectory(string directoryPath)
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(LocalFileSystemWatcherService));
		}

		if (string.IsNullOrWhiteSpace(directoryPath))
		{
			throw new ServiceException($"'{nameof(directoryPath)}' cannot be null or whitespace.");
		}

		if (!Directory.Exists(directoryPath))
		{
			throw new ServiceException($"{directoryPath} is not found.");
		}

		try
		{
			_filesWatcher.EnableRaisingEvents = false;
			_directoriesWatcher.EnableRaisingEvents = false;

			_filesWatcher.Path = directoryPath;
			_directoriesWatcher.Path = directoryPath;

			_filesWatcher.EnableRaisingEvents = true;
			_directoriesWatcher.EnableRaisingEvents = true;
		}
		catch (Exception ex)
		{
			throw new ServiceException($"Failed to start watching directory: {directoryPath}", ex);
		}
	}

	private void CreateObservables()
	{
		SubscribeToFileEvents();
		SubscribeToDirectoryEvents();
	}

	private void SubscribeToFileEvents()
	{
		Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
			          h => _filesWatcher.Created += h,
			          h => _filesWatcher.Created -= h)
                  .Select(e => CreateFileModel(e.EventArgs))
                  .GroupBy(file => file.FullName)
                  .SelectMany(group => group.Throttle(TimeSpan.FromSeconds(1))) // Ignore duble event for large files
                  .Subscribe(_fileCreatedSubject);

        Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
			          h => _filesWatcher.Deleted += h,
			          h => _filesWatcher.Deleted -= h)
		          .Select(e => CreateFileModel(e.EventArgs))
		          .Throttle(TimeSpan.FromMilliseconds(100))
		          .Subscribe(_fileDeletedSubject);

		Observable.FromEventPattern<RenamedEventHandler, RenamedEventArgs>(
			          h => _filesWatcher.Renamed += h,
			          h => _filesWatcher.Renamed -= h)
		          .Select(e => new FileRenamedModel(
			                  CreateFileModel(e.EventArgs, e.EventArgs.OldName, e.EventArgs.OldFullPath),
			                  CreateFileModel(e.EventArgs)))
		          .Subscribe(_fileRenamedSubject);
	}

	private void SubscribeToDirectoryEvents()
	{
		Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
			          h => _directoriesWatcher.Created += h,
			          h => _directoriesWatcher.Created -= h)
		          .Select(e => CreateDirectoryModel(e.EventArgs.FullPath))
		          .Throttle(TimeSpan.FromMilliseconds(100))
		          .Subscribe(_directoryCreatedSubject);

		Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
			          h => _directoriesWatcher.Deleted += h,
			          h => _directoriesWatcher.Deleted -= h)
		          .Select(e => CreateDirectoryModel(e.EventArgs.FullPath))
		          .Throttle(TimeSpan.FromMilliseconds(100))
		          .Subscribe(_directoryDeletedSubject);

		Observable.FromEventPattern<RenamedEventHandler, RenamedEventArgs>(
			          h => _directoriesWatcher.Renamed += h,
			          h => _directoriesWatcher.Renamed -= h)
		          .Select(e => new DirectoryRenamedModel(
			                  CreateDirectoryModel(e.EventArgs.OldFullPath),
			                  CreateDirectoryModel(e.EventArgs.FullPath)))
		          .Subscribe(_directoryRenamedSubject);
	}

	private static FileModel CreateFileModel(FileSystemEventArgs args, string? name = null, string? fullPath = null)
	{
		return new FileModel(name ?? args.Name, fullPath ?? args.FullPath, null);
	}

	private static DirectoryModel CreateDirectoryModel(string path)
	{
		var directoryInfo = new DirectoryInfo(path);
		return new DirectoryModel(directoryInfo.Name, directoryInfo.FullName);
	}
}