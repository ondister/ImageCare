namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal sealed class TagDescriptionViewModel
{
    public TagDescriptionViewModel(string title, string description)
    {
        Title = title;
        Description = description;
    }

    public string Title { get; }

    public string Description { get; }
}