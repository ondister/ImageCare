using ImageCare.Core.Domain.Configuration;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

public sealed class ApplicationChoiceViewModel
{
    public ApplicationChoiceViewModel(
        string displayName,
        ApplicationLaunchMode launchMode,
        string applicationPath = "",
        bool isCustom = false)
    {
        DisplayName = displayName;
        LaunchMode = launchMode;
        ApplicationPath = applicationPath;
        IsCustom = isCustom;
    }

    public string DisplayName { get; }

    public ApplicationLaunchMode LaunchMode { get; }

    public string ApplicationPath { get; }

    public bool IsCustom { get; }
}
