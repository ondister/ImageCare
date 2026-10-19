using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using MapsterMapper;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly IConfigurationService _configService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private readonly LocalizationManager _localization;

    private string _newExtension = string.Empty;
    private string _newApplicationPath = string.Empty;
    private FileApplicationAssociationViewModel? _selectedAssociation;
    private ApplicationChoiceViewModel? _selectedApplicationChoice;
    private LanguageOption? _selectedLanguage;
    private bool _restoreLastFoldersOnStartup;

    public SettingsViewModel(
        IConfigurationService configurationService,
        IFileDialogService fileDialogService,
        IMapper mapper,
        ILogger logger,
        LocalizationManager localization)
    {
        _configService = configurationService;
        _fileDialogService = fileDialogService;
        _mapper = mapper;
        _logger = logger;
        _localization = localization;

        SupportedExtensions = MediaFormat.GetSupportedExtensions()
            .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        AddAssociationCommand = CreateCommand(AddAssociation, CanAddAssociation, OnCommandException)
            .ObservesProperty(() => NewExtension)
            .ObservesProperty(() => SelectedApplicationChoice)
            .ObservesProperty(() => NewApplicationPath);
        RemoveAssociationCommand = CreateCommand(RemoveAssociation, CanRemoveAssociation, OnCommandException)
            .ObservesProperty(() => SelectedAssociation);
        ChooseApplicationCommand = CreateAsyncCommand(
                ChooseApplicationAsync,
                () => IsCustomApplicationSelected,
                OnCommandException)
            .ObservesProperty(() => IsCustomApplicationSelected);
        RestoreRecommendedApplicationsCommand = CreateCommand(
            RestoreRecommendedApplications,
            () => true,
            OnCommandException);

        _newExtension = SupportedExtensions.FirstOrDefault(extension =>
            extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase))
            ?? SupportedExtensions.FirstOrDefault()
            ?? string.Empty;
        RebuildApplicationChoices();
    }

    public string Title => _localization["Settings_Title"];

    public IReadOnlyList<LanguageOption> AvailableLanguages => _localization.SupportedLanguages;

    public IReadOnlyList<string> SupportedExtensions { get; }

    public ObservableCollection<ApplicationChoiceViewModel> ApplicationChoices { get; } = new();

    public ObservableCollection<FileApplicationAssociationViewModel> Associations { get; } = new();

    public bool RestoreLastFoldersOnStartup
    {
        get => _restoreLastFoldersOnStartup;
        set
        {
            if (!SetProperty(ref _restoreLastFoldersOnStartup, value))
            {
                return;
            }

            _configService.Configuration.Value.RestoreLastFoldersOnStartup = value;
            SaveConfiguration();
        }
    }

    public LanguageOption? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (!SetProperty(ref _selectedLanguage, value) || value == null)
            {
                return;
            }

            _localization.SetCulture(value.Name);
            _configService.Configuration.Value.UiCultureName = value.Name;
            SaveConfiguration();
            RebuildApplicationChoices();
            RaisePropertyChanged(nameof(Title));
        }
    }

    public string NewExtension
    {
        get => _newExtension;
        set
        {
            if (!SetProperty(ref _newExtension, value ?? string.Empty))
            {
                return;
            }

            RebuildApplicationChoices();
        }
    }

    public string NewApplicationPath
    {
        get => _newApplicationPath;
        private set => SetProperty(ref _newApplicationPath, value);
    }

    public ApplicationChoiceViewModel? SelectedApplicationChoice
    {
        get => _selectedApplicationChoice;
        set
        {
            if (!SetProperty(ref _selectedApplicationChoice, value))
            {
                return;
            }

            if (value?.IsCustom != true)
            {
                NewApplicationPath = string.Empty;
            }

            RaisePropertyChanged(nameof(IsCustomApplicationSelected));
        }
    }

    public bool IsCustomApplicationSelected => SelectedApplicationChoice?.IsCustom == true;

    public FileApplicationAssociationViewModel? SelectedAssociation
    {
        get => _selectedAssociation;
        set => SetProperty(ref _selectedAssociation, value);
    }

    public ICommand AddAssociationCommand { get; }

    public ICommand RemoveAssociationCommand { get; }

    public ICommand ChooseApplicationCommand { get; }

    public ICommand RestoreRecommendedApplicationsCommand { get; }

    public void Close()
    {
        try
        {
            Associations.Clear();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during SettingsViewModel disposal");
        }
    }

    public void Open()
    {
        try
        {
            _selectedLanguage = AvailableLanguages.FirstOrDefault(language =>
                string.Equals(language.Name, _localization.Culture.Name, StringComparison.OrdinalIgnoreCase));
            RaisePropertyChanged(nameof(SelectedLanguage));

            _restoreLastFoldersOnStartup = _configService.Configuration.Value.RestoreLastFoldersOnStartup;
            RaisePropertyChanged(nameof(RestoreLastFoldersOnStartup));

            LoadAssociations();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during dialog open");
        }
    }

    private void AddAssociation()
    {
        var choice = SelectedApplicationChoice;
        if (choice == null)
        {
            return;
        }

        var applicationPath = choice.IsCustom ? NewApplicationPath : choice.ApplicationPath;
        var name = choice.IsCustom
            ? Path.GetFileNameWithoutExtension(applicationPath)
            : choice.DisplayName;

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var associations = _configService.Configuration.Value.ApplicationAssociationPairs;
        if (associations.Any(association =>
                association.FileExtension.Equals(NewExtension, StringComparison.OrdinalIgnoreCase)
                && association.LaunchMode == choice.LaunchMode
                && association.ApplicationPath.Equals(applicationPath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        associations.Add(
            new FileApplicationAssociation
            {
                FileExtension = NewExtension,
                Name = name,
                ApplicationPath = applicationPath,
                LaunchMode = choice.LaunchMode
            });

        SaveConfiguration();
        NewApplicationPath = string.Empty;
        LoadAssociations();
    }

    private bool CanAddAssociation()
    {
        var choice = SelectedApplicationChoice;
        return !string.IsNullOrWhiteSpace(NewExtension)
            && choice != null
            && (!choice.IsCustom || !string.IsNullOrWhiteSpace(NewApplicationPath));
    }

    private void RemoveAssociation()
    {
        try
        {
            if (SelectedAssociation == null)
            {
                return;
            }

            var associationToRemove = _configService.Configuration.Value.ApplicationAssociationPairs
                .FirstOrDefault(association =>
                    association.FileExtension.Equals(SelectedAssociation.FileExtension, StringComparison.OrdinalIgnoreCase)
                    && association.Name.Equals(SelectedAssociation.Name, StringComparison.OrdinalIgnoreCase)
                    && association.LaunchMode == SelectedAssociation.LaunchMode
                    && association.ApplicationPath.Equals(
                        SelectedAssociation.ApplicationPath,
                        StringComparison.OrdinalIgnoreCase));

            if (associationToRemove == null)
            {
                return;
            }

            _configService.Configuration.Value.ApplicationAssociationPairs.Remove(associationToRemove);
            SaveConfiguration();
            LoadAssociations();
            SelectedAssociation = null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to remove file association");
        }
    }

    private bool CanRemoveAssociation()
    {
        return SelectedAssociation != null;
    }

    private async Task ChooseApplicationAsync(
        IProgress<int> progress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filePath = await _fileDialogService.ShowOpenApplicationDialogAsync(
                _localization["Settings_SelectApplication"]);

            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                NewApplicationPath = filePath;
            }
        }
        catch (OperationCanceledException)
        {
            // The application picker was cancelled.
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to select an external application");
        }
    }

    private void RestoreRecommendedApplications()
    {
        if (!DefaultFileApplicationAssociations.AddMissing(_configService.Configuration.Value))
        {
            return;
        }

        SaveConfiguration();
        LoadAssociations();
    }

    private void RebuildApplicationChoices()
    {
        var previousMode = SelectedApplicationChoice?.LaunchMode;
        var wasCustom = SelectedApplicationChoice?.IsCustom == true;

        ApplicationChoices.Clear();
        ApplicationChoices.Add(
            new ApplicationChoiceViewModel(
                _localization["Settings_SystemDefaultApplication"],
                ApplicationLaunchMode.SystemDefault));
        ApplicationChoices.Add(
            new ApplicationChoiceViewModel(
                OperatingSystem.IsMacOS()
                    ? _localization["Settings_ShowInFinder"]
                    : _localization["Settings_ShowInFileExplorer"],
                ApplicationLaunchMode.FileManager));

        if (OperatingSystem.IsMacOS())
        {
            var isVideo = NewExtension.Equals(".mp4", StringComparison.OrdinalIgnoreCase);
            ApplicationChoices.Add(
                isVideo
                    ? new ApplicationChoiceViewModel(
                        "QuickTime Player",
                        ApplicationLaunchMode.MacApplication,
                        "/System/Applications/QuickTime Player.app")
                    : new ApplicationChoiceViewModel(
                        "Preview",
                        ApplicationLaunchMode.MacApplication,
                        "/System/Applications/Preview.app"));
        }

        ApplicationChoices.Add(
            new ApplicationChoiceViewModel(
                _localization["Settings_CustomApplication"],
                OperatingSystem.IsMacOS()
                    ? ApplicationLaunchMode.MacApplication
                    : ApplicationLaunchMode.Executable,
                isCustom: true));

        SelectedApplicationChoice = ApplicationChoices.FirstOrDefault(choice =>
            choice.LaunchMode == previousMode && choice.IsCustom == wasCustom)
            ?? ApplicationChoices.FirstOrDefault();
    }

    private void LoadAssociations()
    {
        try
        {
            Associations.Clear();
            var orderedList = _configService.Configuration.Value.ApplicationAssociationPairs
                .OrderBy(pair => pair.FileExtension, StringComparer.OrdinalIgnoreCase)
                .ThenBy(pair => pair.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var association in orderedList)
            {
                try
                {
                    Associations.Add(_mapper.Map<FileApplicationAssociationViewModel>(association));
                }
                catch (Exception ex)
                {
                    _logger.Error(
                        ex,
                        "Failed to map association: {Extension} - {Name}",
                        association.FileExtension,
                        association.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load associations");
        }
    }

    private void SaveConfiguration()
    {
        try
        {
            _configService.SaveConfiguration();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save configuration");
        }
    }

    private void OnCommandException(Exception exception)
    {
        _logger.Error(exception, "Command execution failed");
    }
}
