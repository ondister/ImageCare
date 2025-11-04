using System.Management;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace ImageCare.Core.Services.DrivesWatcherService;

#pragma warning disable CA1416
public sealed class WindowsManagementEventWatcher : IManagementEventWatcher
{
	private readonly ManagementEventWatcher _watcher;
	private readonly Subject<string> _deviceArrivedSubject;
	private readonly Subject<string> _deviceRemovedSubject;
	private bool _disposed;

	public WindowsManagementEventWatcher()
	{
		_deviceArrivedSubject = new Subject<string>();
		_deviceRemovedSubject = new Subject<string>();

		_watcher = new ManagementEventWatcher();

		var query = new WqlEventQuery("SELECT * FROM Win32_VolumeChangeEvent WHERE EventType = 2 OR EventType = 3");
		_watcher.Query = query;

		_watcher.EventArrived += OnEventArrived;
	}

	public IObservable<string> DeviceArrived => _deviceArrivedSubject.AsObservable();

	public IObservable<string> DeviceRemoved => _deviceRemovedSubject.AsObservable();

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_watcher?.Stop();
		_watcher?.Dispose();
		_deviceArrivedSubject?.Dispose();
		_deviceRemovedSubject?.Dispose();
		_disposed = true;
	}

	public void Start()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(WindowsManagementEventWatcher));
		}

		_watcher.Start();
	}

	public void Stop()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(WindowsManagementEventWatcher));
		}

		_watcher.Stop();
	}

	private void OnEventArrived(object sender, EventArrivedEventArgs e)
	{
		var driveNameProperty = e.NewEvent.Properties["DriveName"];
		var eventTypeProperty = e.NewEvent.Properties["EventType"];

		if (driveNameProperty.Value is not string driveName || eventTypeProperty.Value is not ushort eventType)
		{
			return;
		}

		switch (eventType)
		{
			case 2: // Win32 VolumeChangeEvent DeviceArrival
				_deviceArrivedSubject.OnNext(driveName);
				break;
			case 3: // Win32 VolumeChangeEvent DeviceRemoval  
				_deviceRemovedSubject.OnNext(driveName);
				break;
		}
	}
}