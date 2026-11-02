using System;
using System.Threading;
using System.Threading.Tasks;

using ImageCare.Core.Services.ConfigurationService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;

using Prism.Commands.Ex;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Settings;

public sealed class SafeDeletionSettingsViewModel : ViewModelBase
{
    private readonly IConfigurationService _configurationService;
    private readonly IFileDialogService _fileDialogService;
    private readonly ILogger _logger;
    private readonly LocalizationManager _localization;
    private readonly Action _saveConfiguration;
    private bool _enabled;
    private string _directoryPath = string.Empty;
    private int _maxFileCount = 100;

    public SafeDeletionSettingsViewModel(
        IConfigurationService configurationService,
        IFileDialogService fileDialogService,
        ILogger logger,
        LocalizationManager localization,
        Action saveConfiguration)
    {
        _configurationService = configurationService;
        _fileDialogService = fileDialogService;
        _logger = logger;
        _localization = localization;
        _saveConfiguration = saveConfiguration;
        ChooseDirectoryCommand = CreateAsyncCommand(ChooseDirectoryAsync, OnCommandException);
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (!SetProperty(ref _enabled, value))
            {
                return;
            }

            _configurationService.Configuration.Value.SafeDeletionEnabled = value;
            _saveConfiguration();
        }
    }

    public string DirectoryPath
    {
        get => _directoryPath;
        set
        {
            var normalizedValue = value ?? string.Empty;
            if (!SetProperty(ref _directoryPath, normalizedValue))
            {
                return;
            }

            _configurationService.Configuration.Value.SafeDeletionDirectoryPath = normalizedValue;
            _saveConfiguration();
        }
    }

    public int MaxFileCount
    {
        get => _maxFileCount;
        set
        {
            var normalizedValue = Math.Clamp(value, 1, 10_000);
            if (!SetProperty(ref _maxFileCount, normalizedValue))
            {
                return;
            }

            _configurationService.Configuration.Value.SafeDeletionMaxFileCount = normalizedValue;
            _saveConfiguration();
        }
    }

    public AsyncDelegateCommand ChooseDirectoryCommand { get; }

    public void Open()
    {
        var configuration = _configurationService.Configuration.Value;

        _enabled = configuration.SafeDeletionEnabled;
        RaisePropertyChanged(nameof(Enabled));

        _directoryPath = configuration.SafeDeletionDirectoryPath ?? string.Empty;
        RaisePropertyChanged(nameof(DirectoryPath));

        _maxFileCount = Math.Clamp(configuration.SafeDeletionMaxFileCount, 1, 10_000);
        RaisePropertyChanged(nameof(MaxFileCount));
    }

    private async Task ChooseDirectoryAsync(
        IProgress<int> progress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var directoryPath = await _fileDialogService.ShowOpenFolderDialogAsync(
                _localization["Settings_SelectSafeDeletionFolder"]);
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                DirectoryPath = directoryPath;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to select the safe deletion directory");
        }
    }

    private void OnCommandException(Exception exception) =>
        _logger.Error(exception, "Command execution failed");
}
