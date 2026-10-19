using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;

namespace ImageCare.UI.Avalonia.Localization;

public sealed class LocalizationManager : INotifyPropertyChanged
{
    private const string ResourceBaseName = "ImageCare.UI.Avalonia.Localization.Strings";
    private readonly ResourceManager _resourceManager = new(ResourceBaseName, typeof(LocalizationManager).Assembly);
    private CultureInfo _culture;

    private LocalizationManager()
    {
        SupportedLanguages = DiscoverSupportedLanguages();
        _culture = ResolveCulture(CultureInfo.CurrentUICulture.Name);
        ApplyThreadCulture(_culture);
    }

    public static LocalizationManager Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? CultureChanged;

    public IReadOnlyList<LanguageOption> SupportedLanguages { get; }

    public CultureInfo Culture => _culture;

    [IndexerName("Item")]
    public string this[string key] => _resourceManager.GetString(key, _culture) ?? key;

    public string Get(string key, params object?[] arguments)
    {
        return string.Format(_culture, this[key], arguments);
    }

    public void SetCulture(string? cultureName)
    {
        var culture = string.IsNullOrWhiteSpace(cultureName)
            ? _culture
            : ResolveCulture(cultureName);
        if (string.Equals(_culture.Name, culture.Name, StringComparison.OrdinalIgnoreCase))
        {
            ApplyThreadCulture(culture);
            return;
        }

        _culture = culture;
        ApplyThreadCulture(culture);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    private CultureInfo ResolveCulture(string? cultureName)
    {
        if (!string.IsNullOrWhiteSpace(cultureName))
        {
            try
            {
                var requested = CultureInfo.GetCultureInfo(cultureName);
                var exact = SupportedLanguages.FirstOrDefault(x =>
                    string.Equals(x.Name, requested.Name, StringComparison.OrdinalIgnoreCase));
                if (exact != null)
                {
                    return exact.Culture;
                }

                var neutral = SupportedLanguages.FirstOrDefault(x =>
                    string.Equals(x.Culture.TwoLetterISOLanguageName,
                                  requested.TwoLetterISOLanguageName,
                                  StringComparison.OrdinalIgnoreCase));
                if (neutral != null)
                {
                    return neutral.Culture;
                }
            }
            catch (CultureNotFoundException)
            {
                // Fall back to the neutral resource culture.
            }
        }

        return SupportedLanguages[0].Culture;
    }

    private static IReadOnlyList<LanguageOption> DiscoverSupportedLanguages()
    {
        var assembly = typeof(LocalizationManager).Assembly;
        var neutralName = assembly.GetCustomAttribute<NeutralResourcesLanguageAttribute>()?.CultureName ?? "en";
        var cultures = new Dictionary<string, CultureInfo>(StringComparer.OrdinalIgnoreCase);
        var neutralCulture = CultureInfo.GetCultureInfo(neutralName);
        cultures[neutralCulture.Name] = neutralCulture;

        var satelliteName = $"{assembly.GetName().Name}.resources.dll";
        foreach (var directory in Directory.EnumerateDirectories(AppContext.BaseDirectory))
        {
            var candidatePath = Path.Combine(directory, satelliteName);
            if (!File.Exists(candidatePath))
            {
                continue;
            }

            try
            {
                var culture = CultureInfo.GetCultureInfo(Path.GetFileName(directory));
                cultures[culture.Name] = culture;
            }
            catch (CultureNotFoundException)
            {
                // Ignore directories that are not culture names.
            }
        }

        return cultures.Values
                       .OrderBy(culture => culture.Name.Equals(neutralName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                       .ThenBy(culture => culture.NativeName, StringComparer.CurrentCultureIgnoreCase)
                       .Select(culture => new LanguageOption(culture))
                       .ToArray();
    }

    private static void ApplyThreadCulture(CultureInfo culture)
    {
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
