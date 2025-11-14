using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.DrivesWatcherService;
using ImageCare.Core.Services.FileAssociationsService;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.FileSystemWatcherService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.Core.Services.ProcessService;
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
using Serilog;
using System.Threading;

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
		moduleCatalog.AddModule<LoggerModule>();

		base.ConfigureModuleCatalog(moduleCatalog);
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
		containerRegistry.RegisterSingleton<ILogger>(provider => Log.Logger);

		containerRegistry.Register<MainWindow>();
		containerRegistry.Register<MainWindowTitleRightView>();
		containerRegistry.Register<MetadataView>();

		containerRegistry.RegisterForNavigation<FoldersView>();
		containerRegistry.RegisterForNavigation<MainImageView>();
		containerRegistry.RegisterForNavigation<MainVideoView>();
		containerRegistry.RegisterForNavigation<PreviewPanelView>();
		containerRegistry.RegisterForNavigation<BottomBarView>();

		containerRegistry.RegisterSingleton<IFileSystemService, WindowsFileSystemService>();
		containerRegistry.RegisterSingleton<IFolderService, LocalFileSystemFolderService>();

		containerRegistry.RegisterSingleton<IMediaPreviewService, CommonMediaPreviewService>();

		containerRegistry.RegisterSingleton<IProcessService, WindowsProcessService>();
		containerRegistry.RegisterSingleton<IMediaPreviewOperationsService, WindowsMediaPreviewOperationsService>();

		containerRegistry.RegisterSingleton<IManagementEventWatcher, WindowsManagementEventWatcher>();
		containerRegistry.RegisterSingleton<IDriveInfoProvider, SystemDriveInfoProvider>();
		containerRegistry.RegisterSingleton<IDriveModelsFactory, DriveModelsFactory>();
		containerRegistry.RegisterSingleton<IDrivesWatcherService, WindowsDrivesWatcherService>();

		containerRegistry.RegisterSingleton<IConfigurationFileSource, WindowsConfigurationFileSource>();
		containerRegistry.RegisterSingleton<IConfigurationService, JsonConfigurationService>();

		containerRegistry.RegisterSingleton<INotificationService, LocalNotificationService>();
		containerRegistry.RegisterSingleton<IFileAssociationsService, ConfigurationFileAssociationsService>();
		containerRegistry.RegisterSingleton<IClipboardService>(provider =>
		{
			var topLevel = TopLevel.GetTopLevel(provider.Resolve<MainWindow>());
			return new ClipboardService(topLevel);
		});
		containerRegistry.RegisterSingleton<IFileDialogService, AvaloniaFileDialogService>();

		containerRegistry.RegisterInstance(new ApplicationMapper(Container).GetMapper());
		containerRegistry.RegisterInstance(SynchronizationContext.Current);
		containerRegistry.RegisterSingleton<MapControlMediator>();

		containerRegistry.Register<IFileSystemWatcherService, LocalFileSystemWatcherService>();
		containerRegistry.Register<IMultiSourcesFileSystemWatcherService, MultiSourcesLocalFileSystemWatcherService>();
		containerRegistry.Register<ImagePreviewDropHandler, ImagePreviewDropHandler>();

		containerRegistry.RegisterDialogWindow<ChildWindow>("childWindow");
		containerRegistry.RegisterDialog<SettingsView>("settingsViewer");
	}
}