namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal class NotificationViewModel
{
    public NotificationViewModel(string title, string? description)
    {
        Title = title;
        Description = description;
    }

    public string Title { get; }

    public string? Description { get; }
}