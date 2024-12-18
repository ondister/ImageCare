using System.Threading;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using DryIoc;

using ImageCare.Core.Services;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.FileOperationsService;
using ImageCare.UI.Avalonia.Behaviors;
using ImageCare.UI.Avalonia.Views;

using Prism.DryIoc;
using Prism.Ioc;

namespace ImageCare.UI.Avalonia;

public class App : PrismApplication
{
    private IContainer _container;

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

    /// <inheritdoc />
    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry.Register<MainWindow>();

        containerRegistry.RegisterForNavigation<FoldersView>();
        containerRegistry.RegisterForNavigation<MainImageView>();
        containerRegistry.RegisterForNavigation<PreviewImageView>();

        containerRegistry.RegisterSingleton<IFolderService, LocalFileSystemFolderService>();
        containerRegistry.RegisterSingleton<IFileSystemImageService, FileSystemImageService>();
        containerRegistry.RegisterSingleton<IFileOperationsService, LocalFileSystemFileOperationsService>();
        containerRegistry.RegisterSingleton<IConfigurationService, JsonConfigurationService>();

        containerRegistry.RegisterInstance<SynchronizationContext>(SynchronizationContext.Current);

        containerRegistry.Register<IFileSystemWatcherService, LocalFileSystemWatcherService>();
        containerRegistry.Register<ImagePreviewDropHandler, ImagePreviewDropHandler>();

        _container = containerRegistry.GetContainer();
    }

    /// <inheritdoc />
    protected override AvaloniaObject CreateShell()
    {
        return Container.Resolve<MainWindow>();
    }

    protected override void OnInitialized() { }

    protected virtual void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        var configurationService = _container.Resolve<IConfigurationService>();
        configurationService.SaveConfiguration();
    }
}