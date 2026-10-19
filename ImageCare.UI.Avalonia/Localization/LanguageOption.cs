using System.Globalization;

namespace ImageCare.UI.Avalonia.Localization;

public sealed class LanguageOption
{
    public LanguageOption(CultureInfo culture)
    {
        Culture = culture;
    }

    public CultureInfo Culture { get; }

    public string Name => Culture.Name;

    public string DisplayName => Culture.NativeName;

    public override string ToString() => DisplayName;
}
