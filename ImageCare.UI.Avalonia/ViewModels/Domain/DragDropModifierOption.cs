using ImageCare.Core.Domain.Configuration;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

public sealed class DragDropModifierOption(string displayName, DragDropModifierKey modifier)
{
    public string DisplayName { get; } = displayName;

    public DragDropModifierKey Modifier { get; } = modifier;
}
