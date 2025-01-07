namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal sealed class SuccessNotificationViewModel : NotificationViewModel
{
    /// <inheritdoc />
    public SuccessNotificationViewModel(string title, string? description)
        : base(title, description) { }
}