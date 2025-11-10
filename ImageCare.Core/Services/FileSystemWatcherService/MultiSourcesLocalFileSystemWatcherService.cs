using System.Collections.Concurrent;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Exceptions;

namespace ImageCare.Core.Services.FileSystemWatcherService;

public sealed class MultiSourcesLocalFileSystemWatcherService : IMultiSourcesFileSystemWatcherService, IDisposable
{
	private readonly Subject<FileModel> _fileCreatedSubject;
	private readonly Subject<FileModel> _fileDeletedSubject;
	private readonly Subject<FileRenamedModel> _fileRenamedSubject;

	private readonly Subject<DirectoryModel> _directoryCreatedSubject;
	private readonly Subject<DirectoryModel> _directoryDeletedSubject;
	private readonly Subject<DirectoryRenamedModel> _directoryRenamedSubject;

	private readonly ConcurrentDictionary<string, LocalFileSystemWatcherService> _services = new();
	private readonly ConcurrentDictionary<string, CompositeDisposable> _subscriptions = new();
	private bool _disposed;

	public MultiSourcesLocalFileSystemWatcherService()
	{
		_fileCreatedSubject = new Subject<FileModel>();
		_fileDeletedSubject = new Subject<FileModel>();
		_fileRenamedSubject = new Subject<FileRenamedModel>();

		_directoryCreatedSubject = new Subject<DirectoryModel>();
		_directoryDeletedSubject = new Subject<DirectoryModel>();
		_directoryRenamedSubject = new Subject<DirectoryRenamedModel>();
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

		_fileCreatedSubject.Dispose();
		_fileDeletedSubject.Dispose();
		_fileRenamedSubject.Dispose();

		_directoryCreatedSubject.Dispose();
		_directoryDeletedSubject.Dispose();
		_directoryRenamedSubject.Dispose();

		foreach (var subscription in _subscriptions.Values)
		{
			subscription.Dispose();
		}

		_subscriptions.Clear();

		foreach (var service in _services.Values)
		{
			service.Dispose();
		}

		_services.Clear();

		_disposed = true;
	}

	public void StartWatching()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(MultiSourcesLocalFileSystemWatcherService));
		}

		foreach (var service in _services.Values)
		{
			service.StartWatching();
		}
	}

	public void StopWatching()
	{
		if (_disposed)
		{
			return;
		}

		foreach (var service in _services.Values)
		{
			service.StopWatching();
		}
	}

	public void StartWatchingDirectory(string directoryPath)
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(MultiSourcesLocalFileSystemWatcherService));
		}

		if (string.IsNullOrWhiteSpace(directoryPath))
		{
			throw new ServiceException($"'{nameof(directoryPath)}' cannot be null or whitespace.");
		}

		if (!Directory.Exists(directoryPath))
		{
			throw new ServiceException($"{directoryPath} is not found.");
		}

		if (_services.ContainsKey(directoryPath))
		{
			return;
		}

		var parentDirectoryAlreadyWatched = _services.Keys.Any(path =>
			                                                       directoryPath.StartsWith(path, StringComparison.OrdinalIgnoreCase));

		if (parentDirectoryAlreadyWatched)
		{
			return;
		}

		try
		{
			var service = new LocalFileSystemWatcherService();
			if (_services.TryAdd(directoryPath, service))
			{
				var compositeDisposable = new CompositeDisposable
				{
					service.FileCreated.Subscribe(_fileCreatedSubject),
					service.FileDeleted.Subscribe(_fileDeletedSubject),
					service.FileRenamed.Subscribe(_fileRenamedSubject),
					service.DirectoryCreated.Subscribe(_directoryCreatedSubject),
					service.DirectoryDeleted.Subscribe(_directoryDeletedSubject),
					service.DirectoryRenamed.Subscribe(_directoryRenamedSubject)
				};

				_subscriptions.TryAdd(directoryPath, compositeDisposable);
				service.StartWatchingDirectory(directoryPath);
			}
			else
			{
				service.Dispose();
			}
		}
		catch (Exception ex)
		{
			throw new ServiceException($"Failed to start watching directory: {directoryPath}", ex);
		}
	}

	public void StopWatchingDirectory(string directoryPath)
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(MultiSourcesLocalFileSystemWatcherService));
		}

		if (string.IsNullOrWhiteSpace(directoryPath))
		{
			throw new ServiceException($"'{nameof(directoryPath)}' cannot be null or whitespace.");
		}

		if (_services.TryRemove(directoryPath, out var service))
		{
			if (_subscriptions.TryRemove(directoryPath, out var disposable))
			{
				disposable.Dispose();
			}

			service.StopWatching();
			service.Dispose();
		}
	}

	public void ClearWatchers()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(MultiSourcesLocalFileSystemWatcherService));
		}

		foreach (var service in _services.Values)
		{
			service.Dispose();
		}

		_services.Clear();

		foreach (var subscription in _subscriptions.Values)
		{
			subscription.Dispose();
		}

		_subscriptions.Clear();
	}
}