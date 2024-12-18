using System.Threading;

using Avalonia;
using Avalonia.Markup.Xaml;

using CommunityToolkit.Mvvm.Messaging;

using ImageCare.Core.Services;
using ImageCare.Core.Services.FileOperationsService;
using ImageCare.UI.Avalonia.Behaviors;
using ImageCare.UI.Avalonia.Views;

using Prism.DryIoc;
using Prism.Ioc;

namespace ImageCare.UI.Avalonia;

public class App : PrismApplication
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        base.Initialize();
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
        containerRegistry.RegisterInstance<SynchronizationContext>(SynchronizationContext.Current);

        containerRegistry.Register<IFileSystemWatcherService, LocalFileSystemWatcherService>();
        containerRegistry.Register<ImagePreviewDropHandler, ImagePreviewDropHandler>();


    }

    /// <inheritdoc />
    protected override AvaloniaObject CreateShell()
    {
        return Container.Resolve<MainWindow>();
    }

    protected override void OnInitialized()
    {
    }
}