namespace ImageCare.Core.Services.NotificationService;

public interface INotificationService
{
    IObservable<Notification> NotificationReceived { get; }

    void SendNotification(Notification notification);
}