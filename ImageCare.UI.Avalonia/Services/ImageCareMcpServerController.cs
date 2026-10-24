using System;
using System.Threading;
using System.Threading.Tasks;

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;

using ImageCare.Mcp;
using ImageCare.Mcp.Ui;

using Prism.Navigation.Regions;

using Serilog;

namespace ImageCare.UI.Avalonia.Services;

public sealed class ImageCareMcpServerController : System.IAsyncDisposable
{
    private readonly IConfigurationService _configurationService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IMediaPreviewService _mediaPreviewService;
    private readonly IMediaAnnotationsService _annotationsService;
    private readonly IMediaPreviewOperationsService _previewOperations;
    private readonly IFolderTagApplicationService _folderTagApplicationService;
    private readonly IFolderTagsStore _folderTagsStore;
    private readonly IFolderService _folderService;
    private readonly IRegionManager _regionManager;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ImageCareMcpHost? _host;

    public ImageCareMcpServerController(
        IConfigurationService configurationService,
        IFileSystemService fileSystemService,
        IMediaPreviewService mediaPreviewService,
        IMediaAnnotationsService annotationsService,
        IMediaPreviewOperationsService previewOperations,
        IFolderTagApplicationService folderTagApplicationService,
        IFolderTagsStore folderTagsStore,
        IFolderService folderService,
        IRegionManager regionManager,
        ILogger logger,
        SynchronizationContext synchronizationContext)
    {
        _configurationService = configurationService;
        _fileSystemService = fileSystemService;
        _mediaPreviewService = mediaPreviewService;
        _annotationsService = annotationsService;
        _previewOperations = previewOperations;
        _folderTagApplicationService = folderTagApplicationService;
        _folderTagsStore = folderTagsStore;
        _folderService = folderService;
        _regionManager = regionManager;
        _logger = logger;
        _synchronizationContext = synchronizationContext;
    }

    public bool IsRunning => _host is not null;

    public string? EndpointUrl { get; private set; }

    /// <summary>Last start failure, kept so the settings page can explain why the server is down.</summary>
    public string? LastError { get; private set; }

    public async Task StartIfEnabledAsync(CancellationToken cancellationToken = default)
    {
        var configuration = _configurationService.Configuration.Value;
        if (!configuration.McpServerEnabled)
        {
            return;
        }

        await StartAsync(configuration.McpServerPort, configuration.McpServerAccessToken, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task StartAsync(int? port = null, string? accessToken = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_host is not null)
            {
                return;
            }

            await _annotationsService.InitializeAsync(cancellationToken).ConfigureAwait(false);

            var configuration = _configurationService.Configuration.Value;
            var options = new ImageCareMcpOptions
            {
                Port = port ?? configuration.McpServerPort,
                ListenHost = "127.0.0.1",
                AccessToken = string.IsNullOrWhiteSpace(accessToken)
                    ? (string.IsNullOrWhiteSpace(configuration.McpServerAccessToken)
                        ? null
                        : configuration.McpServerAccessToken)
                    : accessToken
            };

            var services = new ImageCareMcpServices
            {
                FileSystemService = _fileSystemService,
                MediaPreviewService = _mediaPreviewService,
                AnnotationsService = _annotationsService,
                PreviewOperations = _previewOperations,
                FolderTagApplicationService = _folderTagApplicationService,
                FolderTagsStore = _folderTagsStore,
                ConfigurationService = _configurationService,
                Logger = _logger,
                UiHost = new AvaloniaMcpUiHost(
                    _folderService,
                    _regionManager,
                    _previewOperations,
                    _synchronizationContext)
            };

            _host = await ImageCareMcpHost.StartAsync(services, options, cancellationToken).ConfigureAwait(false);
            EndpointUrl = _host.EndpointUrl;
            LastError = null;
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            _logger.Error(exception, "Failed to start ImageCare MCP server");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_host is null)
            {
                return;
            }

            await _host.DisposeAsync().ConfigureAwait(false);
            _host = null;
            EndpointUrl = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async System.Threading.Tasks.ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _gate.Dispose();
    }
}
