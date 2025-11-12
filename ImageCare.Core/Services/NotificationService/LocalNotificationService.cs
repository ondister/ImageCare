using System.Reactive.Linq;
using System.Reactive.Subjects;

using ImageCare.Core.Exceptions;

namespace ImageCare.Core.Services.NotificationService;

public sealed class LocalNotificationService : INotificationService, IDisposable
{
	private readonly Subject<Notification> _notificationSubject;
	private volatile bool _isDisposed;

	public LocalNotificationService()
	{
		_notificationSubject = new Subject<Notification>();
	}

	public IObservable<Notification> NotificationReceived => _notificationSubject.AsObservable();

	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}

		_notificationSubject.OnCompleted();
		_notificationSubject.Dispose();
		_isDisposed = true;
	}

	public void SendNotification(Notification notification)
	{
		if (_isDisposed)
		{
			throw new ServiceException("Notification service is disposed");
		}

		if (notification is null)
		{
			throw new ServiceException("Notification cannot be null");
		}

		if (string.IsNullOrWhiteSpace(notification.Title))
		{
			throw new ServiceException("Notification title cannot be null or empty");
		}

		_notificationSubject.OnNext(notification);
	}
}