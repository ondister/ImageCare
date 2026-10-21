using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Exceptions;

namespace ImageCare.Core.Services.FileSystemWatcherService;

public sealed class MultiSourcesLocalFileSystemWatcherService : IMultiSourcesFileSystemWatcherService, IDisposable
{
    private readonly Subject<FileModel> _fileCreatedSubject = new();
    private readonly Subject<FileModel> _fileChangedSubject = new();
    private readonly Subject<FileModel> _fileDeletedSubject = new();
    private readonly Subject<FileRenamedModel> _fileRenamedSubject = new();
    private readonly Subject<DirectoryModel> _directoryCreatedSubject = new();
    private readonly Subject<DirectoryModel> _directoryDeletedSubject = new();
    private readonly Subject<DirectoryRenamedModel> _directoryRenamedSubject = new();
    private readonly Subject<string> _rescanRequestedSubject = new();
    private readonly Dictionary<string, WatchRegistration> _registrations;
    private readonly object _syncRoot = new();
    private bool _disposed;

    public MultiSourcesLocalFileSystemWatcherService()
    {
        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        _registrations = new Dictionary<string, WatchRegistration>(pathComparer);
    }

    public IObservable<FileModel> FileCreated => _fileCreatedSubject.AsObservable();

    public IObservable<FileModel> FileChanged => _fileChangedSubject.AsObservable();

    public IObservable<FileModel> FileDeleted => _fileDeletedSubject.AsObservable();

    public IObservable<FileRenamedModel> FileRenamed => _fileRenamedSubject.AsObservable();

    public IObservable<DirectoryModel> DirectoryCreated => _directoryCreatedSubject.AsObservable();

    public IObservable<DirectoryModel> DirectoryDeleted => _directoryDeletedSubject.AsObservable();

    public IObservable<DirectoryRenamedModel> DirectoryRenamed => _directoryRenamedSubject.AsObservable();

    public IObservable<string> RescanRequested => _rescanRequestedSubject.AsObservable();

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            DisposeRegistrations();
            _fileCreatedSubject.Dispose();
            _fileChangedSubject.Dispose();
            _fileDeletedSubject.Dispose();
            _fileRenamedSubject.Dispose();
            _directoryCreatedSubject.Dispose();
            _directoryDeletedSubject.Dispose();
            _directoryRenamedSubject.Dispose();
            _rescanRequestedSubject.Dispose();
            _disposed = true;
        }
    }

    public void StartWatching()
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            foreach (var registration in _registrations.Values)
            {
                registration.Service.StartWatching();
            }
        }
    }

    public void StopWatching()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var registration in _registrations.Values)
            {
                registration.Service.StopWatching();
            }
        }
    }

    public void StartWatchingDirectory(string directoryPath)
    {
        var normalizedPath = NormalizeExistingDirectoryPath(directoryPath);

        lock (_syncRoot)
        {
            ThrowIfDisposed();

            if (_registrations.TryGetValue(normalizedPath, out var existingRegistration))
            {
                existingRegistration.ReferenceCount++;
                return;
            }

            LocalFileSystemWatcherService? service = null;
            CompositeDisposable? subscriptions = null;
            try
            {
                service = new LocalFileSystemWatcherService();
                subscriptions = new CompositeDisposable
                {
                    service.FileCreated.Subscribe(_fileCreatedSubject),
                    service.FileChanged.Subscribe(_fileChangedSubject),
                    service.FileDeleted.Subscribe(_fileDeletedSubject),
                    service.FileRenamed.Subscribe(_fileRenamedSubject),
                    service.DirectoryCreated.Subscribe(_directoryCreatedSubject),
                    service.DirectoryDeleted.Subscribe(_directoryDeletedSubject),
                    service.DirectoryRenamed.Subscribe(_directoryRenamedSubject),
                    service.RescanRequested.Subscribe(_ => _rescanRequestedSubject.OnNext(normalizedPath))
                };

                service.StartWatchingDirectory(normalizedPath);
                _registrations.Add(normalizedPath, new WatchRegistration(service, subscriptions));
            }
            catch (Exception ex)
            {
                subscriptions?.Dispose();
                service?.Dispose();
                throw new ServiceException($"Failed to start watching directory: {normalizedPath}", ex);
            }
        }
    }

    public void StopWatchingDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ServiceException($"'{nameof(directoryPath)}' cannot be null or whitespace.");
        }

        string normalizedPath;
        try
        {
            normalizedPath = NormalizePath(directoryPath);
        }
        catch (Exception ex)
        {
            throw new ServiceException($"Invalid directory path: {directoryPath}", ex);
        }

        lock (_syncRoot)
        {
            ThrowIfDisposed();
            if (!_registrations.TryGetValue(normalizedPath, out var registration))
            {
                return;
            }

            registration.ReferenceCount--;
            if (registration.ReferenceCount > 0)
            {
                return;
            }

            _registrations.Remove(normalizedPath);
            registration.Dispose();
        }
    }

    public void ClearWatchers()
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            DisposeRegistrations();
        }
    }

    private void DisposeRegistrations()
    {
        foreach (var registration in _registrations.Values)
        {
            registration.Dispose();
        }

        _registrations.Clear();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MultiSourcesLocalFileSystemWatcherService));
        }
    }

    private static string NormalizeExistingDirectoryPath(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ServiceException($"'{nameof(directoryPath)}' cannot be null or whitespace.");
        }

        string normalizedPath;
        try
        {
            normalizedPath = NormalizePath(directoryPath);
        }
        catch (Exception ex)
        {
            throw new ServiceException($"Invalid directory path: {directoryPath}", ex);
        }

        if (!Directory.Exists(normalizedPath))
        {
            throw new ServiceException($"{normalizedPath} is not found.");
        }

        return normalizedPath;
    }

    private static string NormalizePath(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private sealed class WatchRegistration(
        LocalFileSystemWatcherService service,
        CompositeDisposable subscriptions) : IDisposable
    {
        public LocalFileSystemWatcherService Service { get; } = service;

        public int ReferenceCount { get; set; } = 1;

        public void Dispose()
        {
            subscriptions.Dispose();
            Service.StopWatching();
            Service.Dispose();
        }
    }
}
