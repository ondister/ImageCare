using System.Threading;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using ImageCare.Core.Services;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.DrivesWatcherService;
using ImageCare.Core.Services.FileAssociationsService;
using ImageCare.Core.Services.FileOperationsService;
using ImageCare.Core.Services.FileSystemImageService;
using ImageCare.Core.Services.FileSystemWatcherService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.Modules.Logging;
using ImageCare.UI.Avalonia.Behaviors;
using ImageCare.UI.Avalonia.Controls;
using ImageCare.UI.Avalonia.Mapping;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.Views;
using ImageCare.UI.Common.Desktop.Views;

using Prism.DryIoc;
using Prism.Ioc;
using Prism.Modularity;

namespace ImageCare.UI.Avalonia;

public class App : PrismApplication
{
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
        }

        base.OnFrameworkInitializationCompleted();
    }

    protected override void ConfigureModuleCatalog(IModuleCatalog moduleCatalog)
    {
        base.ConfigureModuleCatalog(moduleCatalog);

        moduleCatalog.AddModule<LoggerModule>();
    }

    /// <inheritdoc />
    protected override AvaloniaObject CreateShell()
    {
        return Container.Resolve<MainWindow>();
    }

    protected virtual void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        var configurationService = Container.Resolve<IConfigurationService>();
        configurationService.SaveConfiguration();
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry.Register<MainWindow>();
        containerRegistry.Register<MainWindowTitleRightView>();
        containerRegistry.Register<MetadataView>();

        containerRegistry.RegisterForNavigation<FoldersView>();
        containerRegistry.RegisterForNavigation<MainImageView>();
        containerRegistry.RegisterForNavigation<MainVideoView>();
        containerRegistry.RegisterForNavigation<PreviewPanelView>();
        containerRegistry.RegisterForNavigation<BottomBarView>();

        containerRegistry.RegisterSingleton<IFolderService, LocalFileSystemFolderService>();
        containerRegistry.RegisterSingleton<IFileSystemImageService, FileSystemImageService>();
        containerRegistry.RegisterSingleton<IFileOperationsService, LocalFileSystemFileOperationsService>();
        containerRegistry.RegisterSingleton<IDrivesWatcherService, WindowsDrivesWatcherService>();
        containerRegistry.RegisterSingleton<IConfigurationService, JsonConfigurationService>();
        containerRegistry.RegisterSingleton<INotificationService, LocalNotificationService>();
        containerRegistry.RegisterSingleton<IFileAssociationsService, ConfigurationFileAssociationsService>();
        containerRegistry.RegisterSingleton<IClipboardService>(provider =>
        {
	        var topLevel = TopLevel.GetTopLevel(provider.Resolve<MainWindow>());
	        return new ClipboardService(topLevel);
        });

        containerRegistry.RegisterInstance(new ApplicationMapper(Container).GetMapper());
        containerRegistry.RegisterInstance(SynchronizationContext.Current);
        containerRegistry.RegisterSingleton<MapControlMediator>();

        containerRegistry.Register<IFileSystemWatcherService, LocalFileSystemWatcherService>();
        containerRegistry.Register<IMultiSourcesFileSystemWatcherService, MultiSourcesLocalFileSystemWatcherService>();
        containerRegistry.Register<ImagePreviewDropHandler, ImagePreviewDropHandler>();

        containerRegistry.RegisterDialogWindow<ChildWindow>("childWindow");
    }
}