using ImageCare.UI.Avalonia.Controls;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal class AspectRatioViewModel
{
    public AspectRatioViewModel(AspectRatio aspectRatio, string name)
    {
        AspectRatio = aspectRatio;
        Name = name;
    }

    public AspectRatio AspectRatio { get; }

    public string Name { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return Name;
    }
}