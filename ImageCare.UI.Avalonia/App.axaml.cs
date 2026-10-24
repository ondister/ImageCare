using System;
using System.IO;
using System.Threading;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DryIoc;
using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Media;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.DrivesWatcherService;
using ImageCare.Core.Services.FileAssociationsService;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.FileSystemWatcherService;
using ImageCare.Core.Services.FolderClusterizationService;
using ImageCare.Core.Services.FolderHistoryService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.FolderStatisticsService;
using ImageCare.Core.Services.FolderTagService;
using ImageCare.Core.Services.LogEventService;
using ImageCare.Core.Services.MediaAnnotationsIndex;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.Core.Services.ProcessService;
using ImageCare.UI.Avalonia.Behaviors;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Mapping;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels;
using ImageCare.UI.Avalonia.Views;
using Mapster;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using Prism.DryIoc;
using Prism.Ioc;
using Serilog;
using Serilog.Core;
using Serilog.Exceptions;
using Serilog.Exceptions.Core;
using Serilog.Extensions.Logging;
using ChildWindow = ImageCare.UI.Avalonia.Views.ChildWindow;
using ILogger = Serilog.ILogger;

namespace ImageCare.UI.Avalonia;

public class App : PrismApplication
{
    private SplashWindow? _splashWindow;
    private MediaAnnotationsCoordinator? _mediaAnnotationsCoordinator;
    private ImageCareMcpServerController? _mcpServerController;

    protected override Rules CreateContainerRules()
    {
        return base.CreateContainerRules()
                   .WithUseInterpretation();
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        base.Initialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownRequested += OnShutdownRequested;
            _splashWindow = new SplashWindow();
            _splashWindow.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    protected override AvaloniaObject CreateShell()
    {
        var configurationService = Container.Resolve<IConfigurationService>();
        var configuration = configurationService.Configuration.Value;
        if (DefaultFileApplicationAssociations.EnsureInitialized(configuration))
        {
            configurationService.SaveConfiguration();
        }

        LocalizationManager.Instance.SetCulture(configuration.UiCultureName);

        var mainWindow = Container.Resolve<MainWindow>();
        _mediaAnnotationsCoordinator = Container.Resolve<MediaAnnotationsCoordinator>();
        _mediaAnnotationsCoordinator.Start();

        _mcpServerController = Container.Resolve<ImageCareMcpServerController>();
        _ = System.Threading.Tasks.Task.Run(() => SafeStartMcpAsync());

        mainWindow.Opened += OnMainWindowOpened;
        return mainWindow;
    }

    private async System.Threading.Tasks.Task SafeStartMcpAsync()
    {
        try
        {
            if (_mcpServerController is not null)
            {
                await _mcpServerController.StartIfEnabledAsync().ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            Log.Logger.Warning(exception, "ImageCare MCP server failed to start");
        }
    }

    private void OnMainWindowOpened(object? sender, EventArgs e)
    {
        if (sender is MainWindow mainWindow)
        {
            mainWindow.Opened -= OnMainWindowOpened;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                _splashWindow?.Close();
                _splashWindow = null;
            },
            DispatcherPriority.Background);
    }

    protected virtual async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        var configurationService = Container.Resolve<IConfigurationService>();
        configurationService.SaveConfiguration();
        _mediaAnnotationsCoordinator?.Dispose();
        _mediaAnnotationsCoordinator = null;
        try
        {
            if (_mcpServerController is not null)
            {
                await _mcpServerController.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Best-effort shutdown of the local MCP server.
        }

        _mcpServerController = null;
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterInstance(LocalizationManager.Instance);

        var logService = new SinkLogEventService();
        containerRegistry.RegisterInstance<ILogEventService>(logService);
        containerRegistry.RegisterInstance<ILogNotificationService>(logService);

        var serilogLogger = CreateLogger(logService);
        Log.Logger = serilogLogger;
        containerRegistry.RegisterInstance<ILogger>(serilogLogger);

        containerRegistry.RegisterDialog<LogViewerView>("logViewer");

        var loggerFactory = new SerilogLoggerFactory(serilogLogger);
        containerRegistry.RegisterSingleton<ILoggerFactory>(_ => loggerFactory);

        containerRegistry.Register<MainWindow>();
        containerRegistry.RegisterSingleton<MainWindowViewModel>();
        containerRegistry.Register<MainWindowTitleRightView>();
        containerRegistry.Register<MetadataView>();
        containerRegistry.Register<MainImageView>();
        containerRegistry.RegisterSingleton<MainImageViewModel>();
        containerRegistry.RegisterSingleton<MainVideoViewModel>();

        containerRegistry.RegisterForNavigation<FoldersView>();
        containerRegistry.RegisterForNavigation<MainImageView>();
        containerRegistry.RegisterForNavigation<MainVideoView>();
        containerRegistry.RegisterForNavigation<PreviewPanelView>();
        containerRegistry.RegisterForNavigation<BottomBarView>();

        containerRegistry.RegisterSingleton<IDriveModelsFactory, DriveModelsFactory>();
        RegisterPlatformTypes(containerRegistry);

        containerRegistry.RegisterSingleton<IFolderService, LocalFileSystemFolderService>();
        containerRegistry.RegisterSingleton<IFolderHistoryService, LocalFolderHistoryService>();
        containerRegistry.RegisterSingleton<IMediaPreviewOperationsService, WindowsMediaPreviewOperationsService>();

        containerRegistry.RegisterSingleton<IConfigurationFileSource, WindowsConfigurationFileSource>();
        containerRegistry.RegisterSingleton<IConfigurationService, JsonConfigurationService>();
        containerRegistry.RegisterSingleton<IMediaAnnotationsStorageService, XmpMediaAnnotationsStorageService>();
        containerRegistry.RegisterSingleton<IMediaAnnotationsIndex, SqliteMediaAnnotationsIndex>();
        containerRegistry.RegisterSingleton<IMediaAnnotationsService, MediaAnnotationsService>();
        containerRegistry.RegisterSingleton<MediaAnnotationsCoordinator>();
        containerRegistry.RegisterSingleton<IFolderTagsStore, FolderTagsStore>();
        containerRegistry.RegisterSingleton<FolderTagApplicationLog>(_ => new FolderTagApplicationLog(GetLogDirectory()));
        containerRegistry.RegisterSingleton<IFolderTagApplicationService, FolderTagApplicationService>();

        containerRegistry.RegisterSingleton<INotificationService, LocalNotificationService>();
        containerRegistry.RegisterSingleton<IFileAssociationsService, ConfigurationFileAssociationsService>();
        containerRegistry.RegisterSingleton<IClipboardService, ClipboardService>();
        containerRegistry.RegisterSingleton<IFileDialogService, AvaloniaFileDialogService>();
        containerRegistry.RegisterSingleton<RenameFileWindowService>();
        containerRegistry.RegisterSingleton<FileTransferCoordinator>();
        containerRegistry.RegisterSingleton<FileTransferOperationState>();
        containerRegistry.RegisterSingleton<TrayAnalysisProgressService>();
        containerRegistry.RegisterSingleton<ImageViewerWindowService>();
        containerRegistry.RegisterSingleton<SettingsWindowService>();
        containerRegistry.RegisterSingleton<MediaSearchWindowService>();

        containerRegistry.RegisterSingleton<TypeAdapterConfig>(() =>
        {
            var config = new TypeAdapterConfig();
            new RegisterMapper(Container).Register(config);
            return config;
        });
        containerRegistry.RegisterSingleton<IMapper, ServiceMapper>();

        containerRegistry.RegisterInstance(SynchronizationContext.Current);

        containerRegistry.Register<IFileSystemWatcherService, LocalFileSystemWatcherService>();
        containerRegistry.Register<IMultiSourcesFileSystemWatcherService, MultiSourcesLocalFileSystemWatcherService>();
        containerRegistry.Register<IFolderStatisticsService, FileWatcherFolderStatisticsService>();
        containerRegistry.Register<IFolderClusterizationService, FastFolderClusterizationService>();
        containerRegistry.Register<ImagePreviewDropHandler, ImagePreviewDropHandler>();

        containerRegistry.RegisterSingleton<ImageCareMcpServerController>();

        containerRegistry.RegisterDialogWindow<ChildWindow>("childWindow");
        containerRegistry.RegisterDialog<GlancePanelView, GlancePanelViewModel>("glanceViewer");
    }

    private static void RegisterPlatformTypes(IContainerRegistry containerRegistry)
    {
        if (OperatingSystem.IsWindows())
        {
            containerRegistry.RegisterSingleton<IFileSystemService, WindowsFileSystemService>();
            containerRegistry.RegisterSingleton<IProcessService, WindowsProcessService>();
            containerRegistry.RegisterSingleton<IManagementEventWatcher, WindowsManagementEventWatcher>();
            containerRegistry.RegisterSingleton<IDriveInfoProvider, SystemDriveInfoProvider>();
            containerRegistry.RegisterSingleton<IDrivesWatcherService, WindowsDrivesWatcherService>();
            containerRegistry.RegisterSingleton<IMediaPreviewService, CommonMediaPreviewService>();
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            containerRegistry.RegisterSingleton<IFileSystemService, MacOsFileSystemService>();
            containerRegistry.RegisterSingleton<IProcessService, SystemProcessService>();
            containerRegistry.RegisterSingleton<IDriveInfoProvider, MacOsDriveInfoProvider>();
            containerRegistry.RegisterSingleton<IDrivesWatcherService, StaticDrivesWatcherService>();
            containerRegistry.RegisterSingleton<IMediaPreviewService>(
                _ => new CommonMediaPreviewService(MediaPreviewCapabilities.Full));
            return;
        }

        throw new PlatformNotSupportedException("ImageCare currently supports Windows and macOS only.");
    }

    private static string GetLogDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageCare",
            "Logs");

    private static Logger CreateLogger(ILogEventSink logEventSink)
    {
        var options = new DestructuringOptionsBuilder()
            .WithDefaultDestructurers();
        var logDirectory = GetLogDirectory();
        Directory.CreateDirectory(logDirectory);

        return new LoggerConfiguration()
               .MinimumLevel.Warning()
               .Enrich.FromLogContext()
               .Enrich.WithExceptionDetails(options)
               .WriteTo.File(
                   Path.Combine(logDirectory, "Errors.log"),
                   rollingInterval: RollingInterval.Day,
                   outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message} {NewLine}{Exception}")
               .WriteTo.Sink(logEventSink)
               .CreateLogger();
    }
}
