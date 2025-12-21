using System.Linq;

using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Logs;
using ImageCare.Core.Domain.Notification;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.FileAssociationsService;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.UI.Avalonia.ViewModels.Domain;
using ImageCare.UI.Avalonia.ViewModels.Domain.Logs;

using Mapster;

using MapsterMapper;

using Prism.Ioc;

using Serilog;

namespace ImageCare.UI.Avalonia.Mapping;

public class RegisterMapper : IRegister
{
    private readonly IContainerProvider _serviceLocator;

    public RegisterMapper(IContainerProvider serviceLocator)
    {
        _serviceLocator = serviceLocator;
    }

    /// <inheritdoc />
    public void Register(TypeAdapterConfig cfg)
    {
        MapLogs(cfg);

        MapNotification(cfg);

        MapConfiguration(cfg);

        MapDirectory(cfg);

        MapMediaPreview(cfg);
    }

    private void MapMediaPreview(TypeAdapterConfig cfg)
    {
        cfg.NewConfig<MediaPreviewViewModel, MediaPreview>()
           .ConstructUsing(src => new MediaPreview(src.Title, src.Url, src.MediaFormat, src.MaxImageHeight));

        cfg.NewConfig<MediaPreview, MediaPreviewViewModel>()
           .Include<MediaPreview, GlanceMediaPreviewViewModel>()
           .Ignore(dst => dst.PreviewBitmap)
           .Ignore(dst => dst.RemoveImagePreviewCommand)
           .Ignore(dst => dst.Selected)
           .Ignore(dst => dst.IsLoading)
           .Ignore(dst => dst.Metadata)
           .Ignore(dst => dst.MetadataString)
           .Ignore(dst => dst.DateTimeString)
           .Ignore(dst => dst.RotateAngle)
           .Ignore(dst => dst.OpenWithViewModels)
           .Ignore(dst => dst.UseOpenWith)
           .Ignore(dst => dst.HasLocation)
           .Ignore(dst => dst.FileDate)
           .Ignore(dst => dst.FrameColorCode)
           .ConstructUsing(src => new MediaPreviewViewModel(
                               src.Title,
                               src.Url,
                               src.MediaFormat,
                               src.MaxImageHeight,
                               _serviceLocator.Resolve<IMediaPreviewService>(),
                               _serviceLocator.Resolve<IMediaPreviewOperationsService>(),
                               _serviceLocator.Resolve<INotificationService>(),
                               _serviceLocator.Resolve<IFileAssociationsService>(),
                               _serviceLocator.Resolve<IMapper>(),
                               _serviceLocator.Resolve<ILogger>()));

        cfg.NewConfig<MediaPreview, GlanceMediaPreviewViewModel>()
           .Ignore(dst => dst.PreviewBitmap)
           .Ignore(dst => dst.RemoveImagePreviewCommand)
           .Ignore(dst => dst.Selected)
           .Ignore(dst => dst.IsLoading)
           .Ignore(dst => dst.Metadata)
           .Ignore(dst => dst.MetadataString)
           .Ignore(dst => dst.DateTimeString)
           .Ignore(dst => dst.RotateAngle)
           .Ignore(dst => dst.OpenWithViewModels)
           .Ignore(dst => dst.UseOpenWith)
           .Ignore(dst => dst.HasLocation)
           .Ignore(dst => dst.FileDate)
           .Ignore(dst => dst.FrameColorCode)
           .ConstructUsing(src => new GlanceMediaPreviewViewModel(
                               src.Title,
                               src.Url,
                               src.MediaFormat,
                               src.MaxImageHeight,
                               _serviceLocator.Resolve<IMediaPreviewService>(),
                               _serviceLocator.Resolve<IMediaPreviewOperationsService>(),
                               _serviceLocator.Resolve<INotificationService>(),
                               _serviceLocator.Resolve<IFileAssociationsService>(),
                               _serviceLocator.Resolve<IMapper>(),
                               _serviceLocator.Resolve<ILogger>()));
    }

    private void MapDirectory(TypeAdapterConfig cfg)
    {
        cfg.NewConfig<DirectoryModel, DirectoryViewModel>()
           .MapWith(src => MapPolymorphicChild(src))
           .Map(dest => dest.HasSupportedMedia, src => src.HasSupportedMedia)
           .Ignore(dest => dest.IsExpanded)
           .Ignore(dest => dest.IsLoaded)
           .Ignore(dest => dest.FileManagerPanel)
           .Ignore(dest => dest.IsEditing)
           .Ignore(dest => dest.EditableName)
           .Ignore(dest => dest.LoadState)
           .Ignore(dest => dest.IsSelected);

        cfg.NewConfig<DirectoryViewModel, DirectoryModel>()
           .MapWith(src => MapViewModelToModelPolymorphic(src))
           .Ignore(dest => dest.DirectoryModels)
           .AfterMapping((src, dest) =>
           {
               if (src.ChildFileSystemItems?.Any() != true)
               {
                   return;
               }

               var children = src.ChildFileSystemItems
                                 .Select(MapViewModelToModelPolymorphic)
                                 .ToList();
               dest.AddDirectories(children);
           });
    }

    private DirectoryViewModel MapPolymorphicChild(DirectoryModel child)
    {
        return child switch
        {
            DeviceModel device => MapDeviceViewModel(device),
            SpecialDirectoryModel special => MapSpecialDirectoryViewModel(special),
            FixedDriveModel fixedDrive => MapFixedDriveViewModel(fixedDrive),
            NetworkDriveModel network => MapNetworkDriveViewModel(network),
            RemovableDriveModel removable => MapRemovableDriveViewModel(removable),
            DriveModel drive => MapDriveViewModel(drive),
            _ => MapDirectoryViewModel(child)
        };
    }

    private RemovableDriveViewModel MapRemovableDriveViewModel(RemovableDriveModel src)
    {
        var mapper = _serviceLocator.Resolve<IMapper>();
        var folderService = _serviceLocator.Resolve<IFolderService>();
        var fileSystemService = _serviceLocator.Resolve<IFileSystemService>();
        var logger = _serviceLocator.Resolve<ILogger>();

        var children = src.DirectoryModels
                          .Select(MapPolymorphicChild)
                          .ToList();

        return new RemovableDriveViewModel(
            src.Name,
            src.Path,
            src.TotalSize,
            src.AvailableFreeSpace,
            children,
            folderService,
            fileSystemService,
            mapper,
            logger);
    }

    private NetworkDriveViewModel MapNetworkDriveViewModel(NetworkDriveModel src)
    {
        var mapper = _serviceLocator.Resolve<IMapper>();
        var folderService = _serviceLocator.Resolve<IFolderService>();
        var fileSystemService = _serviceLocator.Resolve<IFileSystemService>();
        var logger = _serviceLocator.Resolve<ILogger>();

        var children = src.DirectoryModels
                          .Select(MapPolymorphicChild)
                          .ToList();

        return new NetworkDriveViewModel(
            src.Name,
            src.Path,
            children,
            folderService,
            fileSystemService,
            mapper,
            logger);
    }

    private FixedDriveViewModel MapFixedDriveViewModel(FixedDriveModel src)
    {
        var mapper = _serviceLocator.Resolve<IMapper>();
        var folderService = _serviceLocator.Resolve<IFolderService>();
        var fileSystemService = _serviceLocator.Resolve<IFileSystemService>();
        var logger = _serviceLocator.Resolve<ILogger>();

        var children = src.DirectoryModels
                          .Select(MapPolymorphicChild)
                          .ToList();

        return new FixedDriveViewModel(
            src.Name,
            src.Path,
            children,
            folderService,
            fileSystemService,
            mapper,
            logger);
    }

    private DeviceViewModel MapDeviceViewModel(DeviceModel src)
    {
        var mapper = _serviceLocator.Resolve<IMapper>();
        var folderService = _serviceLocator.Resolve<IFolderService>();
        var fileSystemService = _serviceLocator.Resolve<IFileSystemService>();
        var logger = _serviceLocator.Resolve<ILogger>();

        var children = src.DirectoryModels
                          .Select(MapPolymorphicChild)
                          .ToList();

        return new DeviceViewModel(
            src.Name,
            src.Path,
            children,
            folderService,
            fileSystemService,
            mapper,
            logger);
    }

    private SpecialDirectoryViewModel MapSpecialDirectoryViewModel(SpecialDirectoryModel src)
    {
        var mapper = _serviceLocator.Resolve<IMapper>();
        var folderService = _serviceLocator.Resolve<IFolderService>();
        var fileSystemService = _serviceLocator.Resolve<IFileSystemService>();
        var logger = _serviceLocator.Resolve<ILogger>();

        var children = src.DirectoryModels
                          .Select(MapPolymorphicChild)
                          .ToList();

        return new SpecialDirectoryViewModel(
            src.Name,
            src.Path,
            children,
            folderService,
            fileSystemService,
            mapper,
            logger);
    }

    private DriveViewModel MapDriveViewModel(DriveModel src)
    {
        var mapper = _serviceLocator.Resolve<IMapper>();
        var folderService = _serviceLocator.Resolve<IFolderService>();
        var fileSystemService = _serviceLocator.Resolve<IFileSystemService>();
        var logger = _serviceLocator.Resolve<ILogger>();

        var children = src.DirectoryModels
                          .Select(MapPolymorphicChild)
                          .ToList();

        return new DriveViewModel(
            src.Name,
            src.Path,
            children,
            folderService,
            fileSystemService,
            mapper,
            logger);
    }

    private DirectoryViewModel MapDirectoryViewModel(DirectoryModel src)
    {
        var mapper = _serviceLocator.Resolve<IMapper>();
        var folderService = _serviceLocator.Resolve<IFolderService>();
        var fileSystemService = _serviceLocator.Resolve<IFileSystemService>();
        var logger = _serviceLocator.Resolve<ILogger>();

        var children = src.DirectoryModels
                          .Select(MapPolymorphicChild)
                          .ToList();

        return new DirectoryViewModel(
            src.Name,
            src.Path,
            children,
            folderService,
            fileSystemService,
            mapper,
            logger) { HasSupportedMedia = src.HasSupportedMedia };
    }

    private DirectoryModel MapViewModelToModelPolymorphic(DirectoryViewModel viewModel)
    {
        return viewModel switch
        {
            DeviceViewModel device => MapViewModelToDeviceModel(device),
            SpecialDirectoryViewModel special => MapViewModelToSpecialDirectoryModel(special),
            FixedDriveViewModel fixedDrive => MapViewModelToFixedDriveModel(fixedDrive),
            NetworkDriveViewModel network => MapViewModelToNetworkDriveModel(network),
            RemovableDriveViewModel removable => MapViewModelToRemovableDriveModel(removable),
            DriveViewModel drive => MapViewModelToDriveModel(drive),
            _ => MapViewModelToDirectoryModel(viewModel)
        };
    }

    private DeviceModel MapViewModelToDeviceModel(DeviceViewModel viewModel)
    {
        var model = new DeviceModel(viewModel.Name, viewModel.Path);

        if (viewModel.ChildFileSystemItems?.Any() == true)
        {
            var children = viewModel.ChildFileSystemItems
                                    .Select(MapViewModelToModelPolymorphic)
                                    .ToList();
            model.AddDirectories(children);
        }

        return model;
    }

    private SpecialDirectoryModel MapViewModelToSpecialDirectoryModel(SpecialDirectoryViewModel viewModel)
    {
        var model = new SpecialDirectoryModel(viewModel.Name, viewModel.Path);

        if (viewModel.ChildFileSystemItems?.Any() == true)
        {
            var children = viewModel.ChildFileSystemItems
                                    .Select(MapViewModelToModelPolymorphic)
                                    .ToList();
            model.AddDirectories(children);
        }

        return model;
    }

    private FixedDriveModel MapViewModelToFixedDriveModel(FixedDriveViewModel viewModel)
    {
        var model = new FixedDriveModel(viewModel.Name, viewModel.Path);

        if (viewModel.ChildFileSystemItems?.Any() == true)
        {
            var children = viewModel.ChildFileSystemItems
                                    .Select(MapViewModelToModelPolymorphic)
                                    .ToList();
            model.AddDirectories(children);
        }

        return model;
    }

    private NetworkDriveModel MapViewModelToNetworkDriveModel(NetworkDriveViewModel viewModel)
    {
        var model = new NetworkDriveModel(viewModel.Name, viewModel.Path);

        if (viewModel.ChildFileSystemItems?.Any() == true)
        {
            var children = viewModel.ChildFileSystemItems
                                    .Select(MapViewModelToModelPolymorphic)
                                    .ToList();
            model.AddDirectories(children);
        }

        return model;
    }

    private RemovableDriveModel MapViewModelToRemovableDriveModel(RemovableDriveViewModel viewModel)
    {
        var model = new RemovableDriveModel(
            viewModel.Name,
            viewModel.Path,
            viewModel.TotalSize,
            viewModel.AvailableFreeSpace);

        if (viewModel.ChildFileSystemItems?.Any() == true)
        {
            var children = viewModel.ChildFileSystemItems
                                    .Select(MapViewModelToModelPolymorphic)
                                    .ToList();
            model.AddDirectories(children);
        }

        return model;
    }

    private DriveModel MapViewModelToDriveModel(DriveViewModel viewModel)
    {
        var model = new DriveModel(viewModel.Name, viewModel.Path);

        if (viewModel.ChildFileSystemItems?.Any() == true)
        {
            var children = viewModel.ChildFileSystemItems
                                    .Select(MapViewModelToModelPolymorphic)
                                    .ToList();
            model.AddDirectories(children);
        }

        return model;
    }

    private DirectoryModel MapViewModelToDirectoryModel(DirectoryViewModel viewModel)
    {
        var model = new DirectoryModel(viewModel.Name, viewModel.Path);

        if (viewModel.ChildFileSystemItems?.Any() == true)
        {
            var children = viewModel.ChildFileSystemItems
                                    .Select(MapViewModelToModelPolymorphic)
                                    .ToList();
            model.AddDirectories(children);
        }

        model.HasSupportedMedia = viewModel.HasSupportedMedia;

        return model;
    }

    private static void MapConfiguration(TypeAdapterConfig cfg)
    {
        cfg.NewConfig<FileApplicationAssociation, FileApplicationAssociationViewModel>();
    }

    private static void MapNotification(TypeAdapterConfig cfg)
    {
        cfg.NewConfig<Notification, NotificationViewModel>();
        cfg.NewConfig<SuccessNotification, SuccessNotificationViewModel>()
           .Inherits<Notification, NotificationViewModel>();
        cfg.NewConfig<ErrorNotification, ErrorNotificationViewModel>()
           .Inherits<Notification, NotificationViewModel>();
    }

    private static void MapLogs(TypeAdapterConfig cfg)
    {
        cfg.NewConfig<LogMessage, ErrorLogMessageViewModel>();
        cfg.NewConfig<LogMessage, WarningLogMessageViewModel>();
    }
}