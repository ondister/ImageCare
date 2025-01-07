namespace ImageCare.Core.Services.NotificationService;

public sealed class SuccessNotification : Notification
{
    /// <inheritdoc />
    public SuccessNotification(string title, string? description)
        : base(title, description) { }
}