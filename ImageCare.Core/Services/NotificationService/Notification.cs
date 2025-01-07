namespace ImageCare.Core.Services.NotificationService;

public class Notification
{
    public Notification(string title, string? description)
    {
        Title = title;
        Description = description;
    }

    public string Title { get; }

    public string? Description { get; }
}