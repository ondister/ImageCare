namespace ImageCare.Core.Services.NotificationService;

public sealed class ErrorNotification : Notification
{
    /// <inheritdoc />
    public ErrorNotification(string title, string? description)
        : base(title, description) { }
}