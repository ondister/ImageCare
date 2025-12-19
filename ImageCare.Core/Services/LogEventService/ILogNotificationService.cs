namespace ImageCare.Core.Services.LogEventService;

public interface ILogNotificationService
{
	public IObservable<int> ErrorsCountUpdated { get; }

	public IObservable<int> WarningsCountUpdated { get; }

	public int GetErrorsCount();

	public int GetWarningsCount();
}