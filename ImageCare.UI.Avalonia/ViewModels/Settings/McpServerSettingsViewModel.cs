using System;
using System.Threading;
using System.Threading.Tasks;

using ImageCare.Core.Services.ConfigurationService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Settings;

public sealed class McpServerSettingsViewModel : ViewModelBase
{
    private readonly IConfigurationService _configurationService;
    private readonly IImageCareMcpServerController _controller;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly ILogger _logger;
    private readonly LocalizationManager _localization;
    private readonly Action _saveConfiguration;
    private readonly object _applyGate = new();
    private bool _enabled;
    private int _port = 5277;
    private string _status = string.Empty;
    private long _desiredStateVersion;
    private bool _applyWorkerRunning;
    private Task _pendingApply = Task.CompletedTask;

    public McpServerSettingsViewModel(
        IConfigurationService configurationService,
        IImageCareMcpServerController controller,
        SynchronizationContext synchronizationContext,
        ILogger logger,
        LocalizationManager localization,
        Action saveConfiguration)
    {
        _configurationService = configurationService;
        _controller = controller;
        _synchronizationContext = synchronizationContext;
        _logger = logger;
        _localization = localization;
        _saveConfiguration = saveConfiguration;
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

            _configurationService.Configuration.Value.McpServerEnabled = value;
            _saveConfiguration();
            QueueApplyDesiredState();
        }
    }

    public int Port
    {
        get => _port;
        set
        {
            var normalizedValue = Math.Clamp(value, 1024, 65535);
            if (!SetProperty(ref _port, normalizedValue))
            {
                return;
            }

            _configurationService.Configuration.Value.McpServerPort = normalizedValue;
            _saveConfiguration();
            if (_enabled)
            {
                QueueApplyDesiredState();
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public void Open()
    {
        var configuration = _configurationService.Configuration.Value;

        _enabled = configuration.McpServerEnabled;
        RaisePropertyChanged(nameof(Enabled));

        _port = Math.Clamp(configuration.McpServerPort, 1024, 65535);
        RaisePropertyChanged(nameof(Port));

        RefreshStatus();
    }

    internal Task WaitForPendingOperationsAsync()
    {
        lock (_applyGate)
        {
            return _pendingApply;
        }
    }

    private void QueueApplyDesiredState()
    {
        TaskCompletionSource? completion = null;
        lock (_applyGate)
        {
            _desiredStateVersion++;
            if (_applyWorkerRunning)
            {
                return;
            }

            _applyWorkerRunning = true;
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingApply = completion.Task;
        }

        _ = ApplyDesiredStateLoopAsync(completion);
    }

    private async Task ApplyDesiredStateLoopAsync(TaskCompletionSource completion)
    {
        try
        {
            while (true)
            {
                bool enabled;
                int port;
                long version;
                lock (_applyGate)
                {
                    enabled = _enabled;
                    port = _port;
                    version = _desiredStateVersion;
                }

                await ApplyStateAsync(enabled, port).ConfigureAwait(false);

                lock (_applyGate)
                {
                    if (version != _desiredStateVersion)
                    {
                        continue;
                    }

                    _applyWorkerRunning = false;
                    break;
                }
            }
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    private async Task ApplyStateAsync(bool enabled, int port)
    {
        try
        {
            if (enabled)
            {
                if (_controller.IsRunning)
                {
                    await _controller.StopAsync().ConfigureAwait(false);
                }

                await _controller.StartAsync(port).ConfigureAwait(false);
            }
            else
            {
                await _controller.StopAsync().ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to apply MCP server settings");
        }
        finally
        {
            _synchronizationContext.Post(_ => RefreshStatus(), null);
        }
    }

    private void RefreshStatus()
    {
        if (!_enabled)
        {
            Status = _localization["Settings_McpServerStatusStopped"];
            return;
        }

        if (_controller.IsRunning && !string.IsNullOrWhiteSpace(_controller.EndpointUrl))
        {
            Status = _localization.Get("Settings_McpServerStatusRunning", _controller.EndpointUrl);
            return;
        }

        Status = string.IsNullOrWhiteSpace(_controller.LastError)
            ? _localization["Settings_McpServerStatusStarting"]
            : _localization.Get("Settings_McpServerStatusFailed", _controller.LastError);
    }
}
