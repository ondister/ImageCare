namespace ImageCare.Core.Services.DrivesWatcherService;

public interface IManagementEventWatcher : IDisposable
{
	IObservable<string> DeviceArrived { get; }

	IObservable<string> DeviceRemoved { get; }

	void Start();

	void Stop();
}