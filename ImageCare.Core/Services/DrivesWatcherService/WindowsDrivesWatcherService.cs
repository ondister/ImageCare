using System.Management;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.FolderService;

namespace ImageCare.Core.Services.DrivesWatcherService;

public sealed class WindowsDrivesWatcherService : IDrivesWatcherService, IDisposable
{
    private readonly IFolderService _folderService;
    private readonly ManagementEventWatcher _watcher;
    private readonly DriveModelsFactory _driveModelsFactory;

    private readonly Subject<DriveModel> _driveMountedSubject;
    private readonly Subject<string> _driveUnmountedSubject;

    public WindowsDrivesWatcherService(IFolderService folderService)
    {
        _folderService = folderService;
        _driveModelsFactory = new DriveModelsFactory();

        _driveMountedSubject = new Subject<DriveModel>();
        _driveUnmountedSubject = new Subject<string>();

        _watcher = new ManagementEventWatcher();
        var query = new WqlEventQuery("SELECT * FROM Win32_VolumeChangeEvent WHERE EventType = 2 OR EventType = 3");
        _watcher.EventArrived += OnEventArrived;
        _watcher.Query = query;
    }

    /// <inheritdoc />
    public IObservable<DriveModel> DriveMounted => _driveMountedSubject.AsObservable();

    /// <inheritdoc />
    public IObservable<string> DriveUnmounted => _driveUnmountedSubject.AsObservable();

    /// <inheritdoc />
    public void Dispose()
    {
        _watcher.Stop();
        _watcher.Dispose();

        _driveMountedSubject.Dispose();
        _driveUnmountedSubject.Dispose();
    }

    /// <inheritdoc />
    public void StartWatching()
    {
        _watcher.Start();
    }

    /// <inheritdoc />
    public void StopWatching()
    {
        _watcher.Stop();
    }

    private void OnEventArrived(object sender, EventArrivedEventArgs eventArgs)
    {
        var driveNameProperty = eventArgs.NewEvent.Properties["DriveName"];
        var eventTypeProperty = eventArgs.NewEvent.Properties["EventType"];

        var driveName = (string)driveNameProperty.Value;
        var eventType = (DriveEventType)eventTypeProperty.Value;

        switch (eventType)
        {
            case DriveEventType.DeviceArrival:
                HandleDeviceArrival(driveName);
                break;
            case DriveEventType.DeviceRemoval:
                HandleDeviceRemoval(driveName);
                break;
        }
    }

    private void HandleDeviceArrival(string driveName)
    {
        var drivesInfo = DriveInfo.GetDrives();

        var affectedDriveInfo = drivesInfo.FirstOrDefault(drive => drive.Name.StartsWith(driveName, StringComparison.OrdinalIgnoreCase));

        if (affectedDriveInfo != null)
        {
            if (_driveModelsFactory.CreateDriveModel(affectedDriveInfo) is { } drive)
            {
                _folderService.GetCustomDirectoriesLevelAsync(drive, true)
                              .ContinueWith(
                                  task =>
                                  {
                                      drive.AddDirectories(task.Result.DirectoryModels);

                                      _driveMountedSubject.OnNext(drive);
                                  });
            }
        }
    }

    private void HandleDeviceRemoval(string driveName)
    {
        var drivesInfo = DriveInfo.GetDrives();

        var affectedDriveInfo = drivesInfo.FirstOrDefault(drive => drive.Name.StartsWith(driveName, StringComparison.OrdinalIgnoreCase));

        if (affectedDriveInfo == null)
        {
            _driveUnmountedSubject.OnNext($"{driveName}\\");
        }
    }
}