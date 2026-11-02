using System;
using System.Threading;

using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Settings;

using MapsterMapper;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly IConfigurationService _configurationService;
    private readonly ILogger _logger;
    private readonly LocalizationManager _localization;

    public SettingsViewModel(
        IConfigurationService configurationService,
        IFileDialogService fileDialogService,
        IMediaAnnotationsService annotationsService,
        ImageCareMcpServerController mcpServerController,
        SynchronizationContext synchronizationContext,
        IMapper mapper,
        ILogger logger,
        LocalizationManager localization)
    {
        _configurationService = configurationService;
        _logger = logger;
        _localization = localization;

        Associations = new FileAssociationsViewModel(
            configurationService,
            fileDialogService,
            mapper,
            logger,
            localization,
            SaveConfiguration);
        Annotations = new AnnotationsSettingsViewModel(
            configurationService,
            fileDialogService,
            annotationsService,
            synchronizationContext,
            logger,
            localization,
            SaveConfiguration);
        General = new SettingsGeneralViewModel(
            configurationService,
            localization,
            SaveConfiguration,
            OnCultureChanged);
        SafeDeletion = new SafeDeletionSettingsViewModel(
            configurationService,
            fileDialogService,
            logger,
            localization,
            SaveConfiguration);
        McpServer = new McpServerSettingsViewModel(
            configurationService,
            mcpServerController,
            synchronizationContext,
            logger,
            localization,
            SaveConfiguration);
    }

    public string Title => _localization["Settings_Title"];

    public SettingsGeneralViewModel General { get; }

    public FileAssociationsViewModel Associations { get; }

    public AnnotationsSettingsViewModel Annotations { get; }

    public SafeDeletionSettingsViewModel SafeDeletion { get; }

    public McpServerSettingsViewModel McpServer { get; }

    public void Open()
    {
        try
        {
            General.Open();
            SafeDeletion.Open();
            McpServer.Open();
            Associations.Open();
            Annotations.Open();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Error during dialog open");
        }
    }

    public void Close()
    {
        try
        {
            Associations.Close();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Error while closing file association settings");
        }

        try
        {
            Annotations.Close();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Error while closing annotation settings");
        }
    }

    private void OnCultureChanged()
    {
        Associations.RefreshCulture();
        RaisePropertyChanged(nameof(Title));
    }

    private void SaveConfiguration()
    {
        try
        {
            _configurationService.SaveConfiguration();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save configuration");
        }
    }
}
