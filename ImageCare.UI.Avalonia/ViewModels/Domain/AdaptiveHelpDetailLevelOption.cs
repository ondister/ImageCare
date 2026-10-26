using ImageCare.Core.Domain.Configuration;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

public sealed class AdaptiveHelpDetailLevelOption(string displayName, AdaptiveHelpDetailLevel level)
{
    public string DisplayName { get; } = displayName;

    public AdaptiveHelpDetailLevel Level { get; } = level;
}
