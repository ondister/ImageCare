using System.Collections.Concurrent;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using ImageCare.Core.Domain.Folders;

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

    public MultiSourcesLocalFileSystemWatcherService()
    {
        _fileCreatedSubject = new Subject<FileModel>();
        _fileDeletedSubject = new Subject<FileModel>();
        _fileRenamedSubject = new Subject<FileRenamedModel>();

        _directoryCreatedSubject = new Subject<DirectoryModel>();
        _directoryDeletedSubject = new Subject<DirectoryModel>();
        _directoryRenamedSubject = new Subject<DirectoryRenamedModel>();
    }

    /// <inheritdoc />
    public IObservable<FileModel> FileCreated => _fileCreatedSubject.AsObservable();

    /// <inheritdoc />
    public IObservable<FileModel> FileDeleted => _fileDeletedSubject.AsObservable();

    /// <inheritdoc />
    public IObservable<FileRenamedModel> FileRenamed => _fileRenamedSubject.AsObservable();

    /// <inheritdoc />
    public IObservable<DirectoryModel> DirectoryCreated => _directoryCreatedSubject.AsObservable();

    /// <inheritdoc />
    public IObservable<DirectoryModel> DirectoryDeleted => _directoryDeletedSubject.AsObservable();

    /// <inheritdoc />
    public IObservable<DirectoryRenamedModel> DirectoryRenamed => _directoryRenamedSubject.AsObservable();

    public void Dispose()
    {
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
    }

    /// <inheritdoc />
    public void StartWatching()
    {
        foreach (var localFileSystemWatcherService in _services.Values)
        {
            localFileSystemWatcherService.StartWatching();
        }
    }

    /// <inheritdoc />
    public void StopWatching()
    {
        foreach (var localFileSystemWatcherService in _services.Values)
        {
            localFileSystemWatcherService.StopWatching();
        }
    }

    /// <inheritdoc />
    public void StartWatchingDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException(directoryPath);
        }

        if (_services.ContainsKey(directoryPath))
        {
            return;
        }

        if (_services.Keys.Any(path=>directoryPath.StartsWith(path,StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var service = new LocalFileSystemWatcherService();
        if (_services.TryAdd(directoryPath, service))
        {
            var compositeDisposable = new CompositeDisposable
            {
                service.FileCreated.Subscribe(_fileCreatedSubject),
                service.FileDeleted.Subscribe(_fileDeletedSubject),
                service.FileRenamed.Subscribe(_fileRenamedSubject),

                service.DirectoryCreated.DistinctUntilChanged(d=>d.Path).Subscribe(_directoryCreatedSubject),
                service.DirectoryDeleted.DistinctUntilChanged(d=>d.Path).Subscribe(_directoryDeletedSubject),
                service.DirectoryRenamed.DistinctUntilChanged(d=>d.NewDirectoryModel).Subscribe(_directoryRenamedSubject)
            };

            _subscriptions.TryAdd(directoryPath, compositeDisposable);
            service.StartWatchingDirectory(directoryPath);
        }
        else
        {
            service.Dispose();
        }
    }

    /// <inheritdoc />
    public void StopWatchingDirectory(string directoryPath)
    {
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

    /// <inheritdoc />
    public void ClearWatchers()
    {
        foreach (var service in _services.Values)
        {
            service.Dispose();
        }

        _services.Clear();
    }
}