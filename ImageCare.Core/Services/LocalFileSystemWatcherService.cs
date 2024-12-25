using System.Management;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ImageCare.Core.Domain;

namespace ImageCare.Core.Services;

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

    public LocalFileSystemWatcherService()
    {
        _filesWatcher = new FileSystemWatcher { NotifyFilter = NotifyFilters.FileName };
        _directoriesWatcher = new FileSystemWatcher
        {
            NotifyFilter = NotifyFilters.DirectoryName,
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

    /// <inheritdoc />
    public void Dispose()
    {
        _filesWatcher.Dispose();
        _directoriesWatcher.Dispose();

        _fileCreatedSubject.Dispose();
        _fileDeletedSubject.Dispose();
        _fileRenamedSubject.Dispose();

        _directoryCreatedSubject.Dispose();
        _directoryDeletedSubject.Dispose();
        _directoryRenamedSubject.Dispose();
    }

    /// <inheritdoc />
    public void StartWatching()
    {
        _filesWatcher.EnableRaisingEvents = true;
        _directoriesWatcher.EnableRaisingEvents = true;
    }

    /// <inheritdoc />
    public void StopWatching()
    {
        _filesWatcher.EnableRaisingEvents = false;
        _directoriesWatcher.EnableRaisingEvents = false;
    }

    /// <inheritdoc />
    public void SetWatchingDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ArgumentException($"'{nameof(directoryPath)}' cannot be null or whitespace.", nameof(directoryPath));
        }

        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"{directoryPath} is not found.");
        }

        _filesWatcher.EnableRaisingEvents = false;
        _directoriesWatcher.EnableRaisingEvents = false;

        _filesWatcher.Path = directoryPath;
        _directoriesWatcher.Path = directoryPath;

        _filesWatcher.EnableRaisingEvents = true;
        _directoriesWatcher.EnableRaisingEvents = true;

    }


    private void CreateObservables()
    {
        Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
                      h => _filesWatcher.Created += h,
                      h => _filesWatcher.Created -= h)
                  .Select(e => new FileModel(e.EventArgs.Name, e.EventArgs.FullPath))
                  .Subscribe(_fileCreatedSubject);

        Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
                      h => _filesWatcher.Deleted += h,
                      h => _filesWatcher.Deleted -= h)
                  .Select(e => new FileModel(e.EventArgs.Name, e.EventArgs.FullPath))
                  .Subscribe(_fileDeletedSubject);

        Observable.FromEventPattern<RenamedEventHandler, RenamedEventArgs>(
                      h => _filesWatcher.Renamed += h,
                      h => _filesWatcher.Renamed -= h)
                  .Select(e => new FileRenamedModel(new FileModel(e.EventArgs.OldName, e.EventArgs.OldFullPath), new FileModel(e.EventArgs.Name, e.EventArgs.FullPath)))
                  .Subscribe(_fileRenamedSubject);

        Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
                      h => _directoriesWatcher.Created += h,
                      h => _directoriesWatcher.Created -= h)
                  .Select(e => new DirectoryModel(e.EventArgs.Name, e.EventArgs.FullPath))
                  .Subscribe(_directoryCreatedSubject);

        Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
                      h => _directoriesWatcher.Deleted += h,
                      h => _directoriesWatcher.Deleted -= h)
                  .Select(e => new DirectoryModel(e.EventArgs.Name, e.EventArgs.FullPath))
                  .Subscribe(_directoryDeletedSubject);

        Observable.FromEventPattern<RenamedEventHandler, RenamedEventArgs>(
                      h => _directoriesWatcher.Renamed += h,
                      h => _directoriesWatcher.Renamed -= h)
                  .Select(e => new DirectoryRenamedModel(new DirectoryModel(e.EventArgs.OldName, e.EventArgs.OldFullPath), new DirectoryModel(e.EventArgs.Name, e.EventArgs.FullPath)))
                  .Subscribe(_directoryRenamedSubject);
    }
}