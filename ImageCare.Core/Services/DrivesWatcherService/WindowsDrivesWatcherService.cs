using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.FolderService;

namespace ImageCare.Core.Services.DrivesWatcherService;

public sealed class WindowsDrivesWatcherService : IDrivesWatcherService, IDisposable
{
    private static readonly TimeSpan FreeSpaceProbeTimeout = TimeSpan.FromSeconds(3);

    private readonly IFolderService _folderService;
    private readonly IManagementEventWatcher _watcher;
    private readonly IDriveInfoProvider _driveInfoProvider;
    private readonly IDriveModelsFactory _driveModelsFactory;
    private readonly Subject<DriveModel> _driveMountedSubject;
    private readonly Subject<string> _driveUnmountedSubject;
    private readonly Subject<AvailableFreeSpaceInfo> _driveFreeSpaceSubject;
    private readonly CompositeDisposable _subscriptions;
    private readonly object _watcherLock = new();

    private CancellationTokenSource? _cancellationTokenSource;
    private int _watcherClients;
    private bool _disposed;

    public WindowsDrivesWatcherService(IFolderService folderService,
                                       IManagementEventWatcher watcher,
                                       IDriveInfoProvider driveInfoProvider,
                                       IDriveModelsFactory driveModelsFactory)
    {
        _folderService = folderService;
        _watcher = watcher;
        _driveInfoProvider = driveInfoProvider;
        _driveModelsFactory = driveModelsFactory;

        _driveMountedSubject = new Subject<DriveModel>();
        _driveUnmountedSubject = new Subject<string>();
        _driveFreeSpaceSubject = new Subject<AvailableFreeSpaceInfo>();

        _subscriptions = new CompositeDisposable
        {
            _watcher.DeviceArrived
                    .Select(driveName => Observable.FromAsync(() => OnDeviceArrival(driveName)))
                    .Concat()
                    .Subscribe(),
            _watcher.DeviceRemoved.Subscribe(OnDeviceRemoval)
        };
    }

    public IObservable<DriveModel> DriveMounted => _driveMountedSubject.AsObservable();

    public IObservable<string> DriveUnmounted => _driveUnmountedSubject.AsObservable();

    public IObservable<AvailableFreeSpaceInfo> DriveAvailableFreeSpaceChanged => _driveFreeSpaceSubject.AsObservable();

    public void Dispose()
    {
        lock (_watcherLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _watcherClients = 0;
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }

        _subscriptions.Dispose();
        _watcher.Dispose();
        _driveMountedSubject.Dispose();
        _driveUnmountedSubject.Dispose();
        _driveFreeSpaceSubject.Dispose();
    }

    public void StartWatching()
    {
        lock (_watcherLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _watcherClients++;
            if (_watcherClients > 1)
            {
                return;
            }

            try
            {
                _watcher.Start();
                _cancellationTokenSource = new CancellationTokenSource();
                var token = _cancellationTokenSource.Token;
                _ = Task.Run(() => StartWatchingFreeSpaceAsync(token), token);
            }
            catch
            {
                _watcherClients = 0;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                throw;
            }
        }
    }

    public void StopWatching()
    {
        lock (_watcherLock)
        {
            if (_disposed || _watcherClients == 0)
            {
                return;
            }

            _watcherClients--;
            if (_watcherClients > 0)
            {
                return;
            }

            _watcher.Stop();
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private async Task OnDeviceArrival(string driveName)
    {
        try
        {
            var driveInfo = _driveInfoProvider.GetDriveByName(driveName);
            if (driveInfo == null)
            {
                return;
            }

            var driveModel = _driveModelsFactory.CreateDriveModel(driveInfo);
            if (driveModel == null)
            {
                return;
            }

            var directories = await _folderService.GetCustomDirectoriesLevelAsync(driveModel, true);
            driveModel.AddDirectories(directories.DirectoryModels);
            _driveMountedSubject.OnNext(driveModel);
        }
        catch
        {
            // A disconnected or inaccessible drive must not terminate the watcher stream.
        }
    }

    private void OnDeviceRemoval(string driveName)
    {
        var driveInfo = _driveInfoProvider.GetDriveByName(driveName);
        if (driveInfo == null)
        {
            _driveUnmountedSubject.OnNext($"{driveName}\\");
        }
    }

    private async Task StartWatchingFreeSpaceAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var probeTask = Task.Run(() =>
                {
                    try
                    {
                        return _driveInfoProvider.GetDrives()
                            .Where(drive => drive is { DriveType: DriveType.Removable, IsReady: true })
                            .Select(drive => new AvailableFreeSpaceInfo(
                                drive.RootDirectory.FullName,
                                drive.AvailableFreeSpace))
                            .ToArray();
                    }
                    catch
                    {
                        return Array.Empty<AvailableFreeSpaceInfo>();
                    }
                });

                var freeSpaceEntries = await probeTask.WaitAsync(FreeSpaceProbeTimeout, token);
                foreach (var freeSpace in freeSpaceEntries)
                {
                    _driveFreeSpaceSubject.OnNext(freeSpace);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (TimeoutException)
            {
                // Do not start more probes while a Windows DriveInfo call is blocked.
                break;
            }
            catch
            {
                // A transient drive failure is retried on the next interval.
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
