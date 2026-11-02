using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Prism.Commands.Ex;

namespace ImageCare.UI.Avalonia.ViewModels.Settings;

public sealed class SettingsGeneralViewModel : ViewModelBase
{
    private readonly IConfigurationService _configurationService;
    private readonly LocalizationManager _localization;
    private readonly Action _saveConfiguration;
    private readonly Action _cultureChanged;
    private LanguageOption? _selectedLanguage;
    private DragDropModifierOption? _selectedDragDropModifier;
    private bool _copyOnDragDropByDefault;
    private bool _folderTagsEnabled;
    private bool _folderTagApplicationLogEnabled;
    private bool _restoreLastFoldersOnStartup;
    private bool _adaptiveHelpEnabled;
    private bool _adaptiveHelpReturningUserEnabled;
    private bool _adaptiveHelpDifficultyAssistanceEnabled;
    private bool _adaptiveHelpNewFeaturesEnabled;
    private AdaptiveHelpDetailLevelOption? _selectedAdaptiveHelpDetailLevel;

    public SettingsGeneralViewModel(
        IConfigurationService configurationService,
        LocalizationManager localization,
        Action saveConfiguration,
        Action cultureChanged)
    {
        _configurationService = configurationService;
        _localization = localization;
        _saveConfiguration = saveConfiguration;
        _cultureChanged = cultureChanged;

        ResetAdaptiveHelpProgressCommand = CreateCommand(ResetAdaptiveHelpProgress);
        ClearAdaptiveHelpHistoryCommand = CreateCommand(ClearAdaptiveHelpHistory);
        RebuildDragDropModifierOptions(DragDropModifierKey.Control);
        RebuildAdaptiveHelpDetailLevelOptions(AdaptiveHelpDetailLevel.Standard);
    }

    public IReadOnlyList<LanguageOption> AvailableLanguages => _localization.SupportedLanguages;

    public ObservableCollection<DragDropModifierOption> DragDropModifierOptions { get; } = new();

    public ObservableCollection<AdaptiveHelpDetailLevelOption> AdaptiveHelpDetailLevelOptions { get; } = new();

    public bool RestoreLastFoldersOnStartup
    {
        get => _restoreLastFoldersOnStartup;
        set => SetAndSave(
            ref _restoreLastFoldersOnStartup,
            value,
            static (configuration, updatedValue) => configuration.RestoreLastFoldersOnStartup = updatedValue);
    }

    public bool AdaptiveHelpEnabled
    {
        get => _adaptiveHelpEnabled;
        set => SetAndSave(
            ref _adaptiveHelpEnabled,
            value,
            static (configuration, updatedValue) => configuration.AdaptiveHelpEnabled = updatedValue);
    }

    public bool AdaptiveHelpReturningUserEnabled
    {
        get => _adaptiveHelpReturningUserEnabled;
        set => SetAndSave(
            ref _adaptiveHelpReturningUserEnabled,
            value,
            static (configuration, updatedValue) => configuration.AdaptiveHelpReturningUserEnabled = updatedValue);
    }

    public bool AdaptiveHelpDifficultyAssistanceEnabled
    {
        get => _adaptiveHelpDifficultyAssistanceEnabled;
        set => SetAndSave(
            ref _adaptiveHelpDifficultyAssistanceEnabled,
            value,
            static (configuration, updatedValue) => configuration.AdaptiveHelpDifficultyAssistanceEnabled = updatedValue);
    }

    public bool AdaptiveHelpNewFeaturesEnabled
    {
        get => _adaptiveHelpNewFeaturesEnabled;
        set => SetAndSave(
            ref _adaptiveHelpNewFeaturesEnabled,
            value,
            static (configuration, updatedValue) => configuration.AdaptiveHelpNewFeaturesEnabled = updatedValue);
    }

    public AdaptiveHelpDetailLevelOption? SelectedAdaptiveHelpDetailLevel
    {
        get => _selectedAdaptiveHelpDetailLevel;
        set
        {
            if (value == null || _selectedAdaptiveHelpDetailLevel?.Level == value.Level)
            {
                return;
            }

            SetProperty(ref _selectedAdaptiveHelpDetailLevel, value);
            _configurationService.Configuration.Value.AdaptiveHelpDetailLevel = value.Level;
            _saveConfiguration();
        }
    }

    public DragDropModifierOption? SelectedDragDropModifier
    {
        get => _selectedDragDropModifier;
        set
        {
            if (value == null || _selectedDragDropModifier?.Modifier == value.Modifier)
            {
                return;
            }

            SetProperty(ref _selectedDragDropModifier, value);
            _configurationService.Configuration.Value.DragDropModifier = value.Modifier;
            _saveConfiguration();
        }
    }

    public bool CopyOnDragDropByDefault
    {
        get => _copyOnDragDropByDefault;
        set => SetAndSave(
            ref _copyOnDragDropByDefault,
            value,
            static (configuration, updatedValue) => configuration.CopyOnDragDropByDefault = updatedValue);
    }

    public bool FolderTagsEnabled
    {
        get => _folderTagsEnabled;
        set => SetAndSave(
            ref _folderTagsEnabled,
            value,
            static (configuration, updatedValue) => configuration.FolderTagsEnabled = updatedValue);
    }

    public bool FolderTagApplicationLogEnabled
    {
        get => _folderTagApplicationLogEnabled;
        set => SetAndSave(
            ref _folderTagApplicationLogEnabled,
            value,
            static (configuration, updatedValue) => configuration.FolderTagApplicationLogEnabled = updatedValue);
    }

    public LanguageOption? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value == null
                || string.Equals(_selectedLanguage?.Name, value.Name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            SetProperty(ref _selectedLanguage, value);
            _localization.SetCulture(value.Name);
            _configurationService.Configuration.Value.UiCultureName = value.Name;
            _saveConfiguration();
            RebuildDragDropModifierOptions(_configurationService.Configuration.Value.DragDropModifier);
            RebuildAdaptiveHelpDetailLevelOptions(_configurationService.Configuration.Value.AdaptiveHelpDetailLevel);
            _cultureChanged();
        }
    }

    public DelegateCommand ResetAdaptiveHelpProgressCommand { get; }

    public DelegateCommand ClearAdaptiveHelpHistoryCommand { get; }

    public void Open()
    {
        var configuration = _configurationService.Configuration.Value;

        _selectedLanguage = AvailableLanguages.FirstOrDefault(language =>
            string.Equals(language.Name, _localization.Culture.Name, StringComparison.OrdinalIgnoreCase));
        RaisePropertyChanged(nameof(SelectedLanguage));

        _restoreLastFoldersOnStartup = configuration.RestoreLastFoldersOnStartup;
        RaisePropertyChanged(nameof(RestoreLastFoldersOnStartup));

        _adaptiveHelpEnabled = configuration.AdaptiveHelpEnabled;
        RaisePropertyChanged(nameof(AdaptiveHelpEnabled));

        _adaptiveHelpReturningUserEnabled = configuration.AdaptiveHelpReturningUserEnabled;
        RaisePropertyChanged(nameof(AdaptiveHelpReturningUserEnabled));

        _adaptiveHelpDifficultyAssistanceEnabled = configuration.AdaptiveHelpDifficultyAssistanceEnabled;
        RaisePropertyChanged(nameof(AdaptiveHelpDifficultyAssistanceEnabled));

        _adaptiveHelpNewFeaturesEnabled = configuration.AdaptiveHelpNewFeaturesEnabled;
        RaisePropertyChanged(nameof(AdaptiveHelpNewFeaturesEnabled));

        RebuildAdaptiveHelpDetailLevelOptions(configuration.AdaptiveHelpDetailLevel);
        RebuildDragDropModifierOptions(configuration.DragDropModifier);

        _copyOnDragDropByDefault = configuration.CopyOnDragDropByDefault;
        RaisePropertyChanged(nameof(CopyOnDragDropByDefault));

        _folderTagsEnabled = configuration.FolderTagsEnabled;
        RaisePropertyChanged(nameof(FolderTagsEnabled));

        _folderTagApplicationLogEnabled = configuration.FolderTagApplicationLogEnabled;
        RaisePropertyChanged(nameof(FolderTagApplicationLogEnabled));
    }

    private void ResetAdaptiveHelpProgress()
    {
        var configuration = _configurationService.Configuration.Value;
        configuration.AdaptiveHelpCompletedHintIds ??= new List<string>();
        configuration.AdaptiveHelpCompletedHintIds.Clear();
        configuration.AdaptiveHelpDismissedHintIds ??= new List<string>();
        configuration.AdaptiveHelpDismissedHintIds.Clear();
        configuration.AdaptiveHelpProgress ??= new List<AdaptiveHelpProgressEntry>();
        configuration.AdaptiveHelpProgress.Clear();
        configuration.AdaptiveHelpUserGoal = AdaptiveHelpUserGoal.Unknown;
        _saveConfiguration();
    }

    private void ClearAdaptiveHelpHistory()
    {
        var configuration = _configurationService.Configuration.Value;
        configuration.AdaptiveHelpSessionHistoryUtc ??= new List<DateTimeOffset>();
        configuration.AdaptiveHelpSessionHistoryUtc.Clear();
        configuration.AdaptiveHelpLastSessionUtc = null;
        configuration.AdaptiveHelpLastShownUtc = null;
        _saveConfiguration();
    }

    private void RebuildAdaptiveHelpDetailLevelOptions(AdaptiveHelpDetailLevel selectedLevel)
    {
        AdaptiveHelpDetailLevelOptions.Clear();
        AdaptiveHelpDetailLevelOptions.Add(
            new AdaptiveHelpDetailLevelOption(
                _localization["Settings_HelpLevelMinimal"],
                AdaptiveHelpDetailLevel.Minimal));
        AdaptiveHelpDetailLevelOptions.Add(
            new AdaptiveHelpDetailLevelOption(
                _localization["Settings_HelpLevelStandard"],
                AdaptiveHelpDetailLevel.Standard));
        AdaptiveHelpDetailLevelOptions.Add(
            new AdaptiveHelpDetailLevelOption(
                _localization["Settings_HelpLevelDetailed"],
                AdaptiveHelpDetailLevel.Detailed));

        _selectedAdaptiveHelpDetailLevel = AdaptiveHelpDetailLevelOptions.First(option =>
            option.Level == selectedLevel);
        RaisePropertyChanged(nameof(SelectedAdaptiveHelpDetailLevel));
    }

    private void RebuildDragDropModifierOptions(DragDropModifierKey selectedModifier)
    {
        DragDropModifierOptions.Clear();
        DragDropModifierOptions.Add(
            new DragDropModifierOption(_localization["Settings_ModifierControl"], DragDropModifierKey.Control));
        DragDropModifierOptions.Add(
            new DragDropModifierOption(_localization["Settings_ModifierAlt"], DragDropModifierKey.Alt));
        DragDropModifierOptions.Add(
            new DragDropModifierOption(_localization["Settings_ModifierShift"], DragDropModifierKey.Shift));
        DragDropModifierOptions.Add(
            new DragDropModifierOption(_localization["Settings_ModifierMeta"], DragDropModifierKey.Meta));

        _selectedDragDropModifier = DragDropModifierOptions.FirstOrDefault(option =>
            option.Modifier == selectedModifier)
            ?? DragDropModifierOptions[0];
        RaisePropertyChanged(nameof(SelectedDragDropModifier));
    }

    private void SetAndSave(
        ref bool field,
        bool value,
        Action<Configuration, bool> updateConfiguration)
    {
        if (!SetProperty(ref field, value))
        {
            return;
        }

        updateConfiguration(_configurationService.Configuration.Value, value);
        _saveConfiguration();
    }
}
