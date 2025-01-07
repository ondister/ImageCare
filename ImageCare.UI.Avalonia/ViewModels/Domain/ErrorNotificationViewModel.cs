namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal sealed class ErrorNotificationViewModel : NotificationViewModel
{
    /// <inheritdoc />
    public ErrorNotificationViewModel(string title, string? description)
        : base(title, description) { }
}