using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace ImageCare.Core.Services.NotificationService;

public sealed class LocalNotificationService : INotificationService, IDisposable
{
    private readonly Subject<Notification> _notificationSubject;

    public LocalNotificationService()
    {
        _notificationSubject = new Subject<Notification>();
    }

    /// <inheritdoc />
    public IObservable<Notification> NotificationReceived => _notificationSubject.AsObservable();

    /// <inheritdoc />
    public void Dispose()
    {
        _notificationSubject.Dispose();
    }

    /// <inheritdoc />
    public void SendNotification(Notification notification)
    {
        _notificationSubject.OnNext(notification);
    }
}