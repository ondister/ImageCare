using System.Threading;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using ImageCare.Core.Services;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.FileOperationsService;
using ImageCare.Modules.Logging;
using ImageCare.Mvvm;
using ImageCare.Mvvm.Views;
using ImageCare.UI.Avalonia.Behaviors;
using ImageCare.UI.Avalonia.Views;

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

    protected override void OnInitialized() { }

    protected virtual void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        var configurationService = Container.Resolve<IConfigurationService>();
        configurationService.SaveConfiguration();
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry.Register<MainWindow>();

        containerRegistry.RegisterForNavigation<FoldersView>();
        containerRegistry.RegisterForNavigation<MainImageView>();
        containerRegistry.RegisterForNavigation<PreviewImageView>();
        containerRegistry.RegisterForNavigation<BottomBarView>();

        containerRegistry.RegisterSingleton<IFolderService, LocalFileSystemFolderService>();
        containerRegistry.RegisterSingleton<IFileSystemImageService, FileSystemImageService>();
        containerRegistry.RegisterSingleton<IFileOperationsService, LocalFileSystemFileOperationsService>();
        containerRegistry.RegisterSingleton<IConfigurationService, JsonConfigurationService>();

        containerRegistry.RegisterInstance<SynchronizationContext>(SynchronizationContext.Current);

        containerRegistry.Register<IFileSystemWatcherService, LocalFileSystemWatcherService>();
        containerRegistry.Register<ImagePreviewDropHandler, ImagePreviewDropHandler>();

        containerRegistry.RegisterDialogWindow<ChildWindow>("childWindow");
    }
}