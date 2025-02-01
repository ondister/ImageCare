using System.Linq;

using AutoMapper;

using ImageCare.Core.Domain;
using ImageCare.Core.Services.FileOperationsService;
using ImageCare.Core.Services.FileSystemImageService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.Modules.Logging.Mapping;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Prism.Ioc;

using Serilog;

namespace ImageCare.UI.Avalonia.Mapping;

internal sealed class ApplicationMapper
{
    private readonly Mapper _mapper;

    public ApplicationMapper(IContainerProvider serviceLocator)
    {
        var config = new MapperConfiguration(
            cfg =>
            {
                cfg.AddProfile(new LoggerMapper());

                cfg.CreateMap<DirectoryModel, DirectoryViewModel>()
                   .ForMember(dst => dst.ChildFileSystemItems, opt => opt.Ignore())
                   .ForMember(dst => dst.IsExpanded, opt => opt.Ignore())
                   .ForMember(dst => dst.IsLoaded, opt => opt.Ignore())
                   .ForMember(dst => dst.HasSupportedMedia, opt => opt.Ignore())
                   .ForMember(dst => dst.FileManagerPanel, opt => opt.Ignore())
                   .ForMember(dst => dst.IsEditing, opt => opt.Ignore())
                   .ForMember(dst => dst.EditableName, opt => opt.Ignore())
                   .ConstructUsing(src => new DirectoryViewModel(src.Name, src.Path, src.DirectoryModels.Select(m => _mapper.Map(m, m.GetType(), typeof(DirectoryViewModel)) as DirectoryViewModel), serviceLocator.Resolve<IFolderService>(), _mapper, serviceLocator.Resolve<ILogger>())
                   {
                       HasSupportedMedia = src.HasSupportedMedia
                   });

                cfg.CreateMap<DriveModel, DriveViewModel>()
                   .IncludeBase<DirectoryModel, DirectoryViewModel>()
                   .ConstructUsing(src => new DriveViewModel(src.Name, src.Path, src.DirectoryModels.Select(m => _mapper.Map(m, m.GetType(), typeof(DirectoryViewModel)) as DirectoryViewModel), serviceLocator.Resolve<IFolderService>(), _mapper, serviceLocator.Resolve<ILogger>()));

                cfg.CreateMap<DeviceModel, DeviceViewModel>()
                   .IncludeBase<DriveModel, DriveViewModel>()
                   .ConstructUsing(src => new DeviceViewModel(src.Name, src.Path, src.DirectoryModels.Select(m => _mapper.Map(m, m.GetType(), typeof(DirectoryViewModel)) as DirectoryViewModel), serviceLocator.Resolve<IFolderService>(), _mapper, serviceLocator.Resolve<ILogger>()));

                cfg.CreateMap<FixedDriveModel, FixedDriveViewModel>()
                   .IncludeBase<DriveModel, DriveViewModel>()
                   .ConstructUsing(src => new FixedDriveViewModel(src.Name, src.Path, src.DirectoryModels.Select(m => _mapper.Map(m, m.GetType(), typeof(DirectoryViewModel)) as DirectoryViewModel), serviceLocator.Resolve<IFolderService>(), _mapper, serviceLocator.Resolve<ILogger>()));

                cfg.CreateMap<NetworkDriveModel, NetworkDriveViewModel>()
                   .IncludeBase<DriveModel, DriveViewModel>()
                   .ConstructUsing(src => new NetworkDriveViewModel(src.Name, src.Path, src.DirectoryModels.Select(m => _mapper.Map(m, m.GetType(), typeof(DirectoryViewModel)) as DirectoryViewModel), serviceLocator.Resolve<IFolderService>(), _mapper, serviceLocator.Resolve<ILogger>()));

                cfg.CreateMap<RemovableDriveModel, RemovableDriveViewModel>()
                   .IncludeBase<DriveModel, DriveViewModel>()
                   .ConstructUsing(src => new RemovableDriveViewModel(src.Name, src.Path, src.DirectoryModels.Select(m => _mapper.Map(m, m.GetType(), typeof(DirectoryViewModel)) as DirectoryViewModel), serviceLocator.Resolve<IFolderService>(), _mapper, serviceLocator.Resolve<ILogger>()));

                cfg.CreateMap<DirectoryViewModel, DirectoryModel>()
                   .ForMember(dst => dst.DirectoryModels, opt => opt.Ignore())
                   .ConstructUsing(src => new DirectoryModel(src.Name, src.Path))
                   .AfterMap((viewModel, model) => { model.AddDirectories(viewModel.ChildFileSystemItems.Select(d => _mapper.Map(d, d.GetType(), typeof(DirectoryModel)) as DirectoryModel)); });

                cfg.CreateMap<DriveViewModel, DriveModel>()
                   .IncludeBase<DirectoryViewModel, DirectoryModel>()
                   .ForMember(dst => dst.DirectoryModels, opt => opt.Ignore())
                   .ForMember(dst => dst.RootDirectory, opt => opt.Ignore())
                   .AfterMap(
                       (viewModel, model) =>
                       {
                           model.AddDirectories(viewModel.ChildFileSystemItems.Select(d => _mapper.Map(d, d.GetType(), typeof(DirectoryModel)) as DirectoryModel));
                           model.RootDirectory = new DirectoryModel(viewModel.Name, viewModel.Path);
                       });

                cfg.CreateMap<DeviceViewModel, DeviceModel>()
                   .IncludeBase<DriveViewModel, DriveModel>()
                   .ForMember(dst => dst.DirectoryModels, opt => opt.Ignore())
                   .ForMember(dst => dst.RootDirectory, opt => opt.Ignore())
                   .AfterMap(
                       (viewModel, model) =>
                       {
                           model.AddDirectories(viewModel.ChildFileSystemItems.Select(d => _mapper.Map(d, d.GetType(), typeof(DirectoryModel)) as DirectoryModel));
                           model.RootDirectory = new DirectoryModel(viewModel.Name, viewModel.Path);
                       });

                cfg.CreateMap<FixedDriveViewModel, FixedDriveModel>()
                   .IncludeBase<DriveViewModel, DriveModel>()
                   .ForMember(dst => dst.DirectoryModels, opt => opt.Ignore())
                   .ForMember(dst => dst.RootDirectory, opt => opt.Ignore())
                   .AfterMap(
                       (viewModel, model) =>
                       {
                           model.AddDirectories(viewModel.ChildFileSystemItems.Select(d => _mapper.Map(d, d.GetType(), typeof(DirectoryModel)) as DirectoryModel));
                           model.RootDirectory = new DirectoryModel(viewModel.Name, viewModel.Path);
                       });

                cfg.CreateMap<NetworkDriveViewModel, NetworkDriveModel>()
                   .IncludeBase<DriveViewModel, DriveModel>()
                   .ForMember(dst => dst.DirectoryModels, opt => opt.Ignore())
                   .ForMember(dst => dst.RootDirectory, opt => opt.Ignore())
                   .AfterMap(
                       (viewModel, model) =>
                       {
                           model.AddDirectories(viewModel.ChildFileSystemItems.Select(d => _mapper.Map(d, d.GetType(), typeof(DirectoryModel)) as DirectoryModel));
                           model.RootDirectory = new DirectoryModel(viewModel.Name, viewModel.Path);
                       });

                cfg.CreateMap<RemovableDriveViewModel, RemovableDriveModel>()
                   .IncludeBase<DriveViewModel, DriveModel>()
                   .ForMember(dst => dst.DirectoryModels, opt => opt.Ignore())
                   .ForMember(dst => dst.RootDirectory, opt => opt.Ignore())
                   .AfterMap(
                       (viewModel, model) =>
                       {
                           model.AddDirectories(viewModel.ChildFileSystemItems.Select(d => _mapper.Map(d, d.GetType(), typeof(DirectoryModel)) as DirectoryModel));
                           model.RootDirectory = new DirectoryModel(viewModel.Name, viewModel.Path);
                       });

                cfg.CreateMap<MediaPreviewViewModel, MediaPreview>()
                   .ConstructUsing(src => new MediaPreview(src.Title, src.Url, src.MediaFormat, src.MaxImageHeight));

                cfg.CreateMap<MediaPreview, MediaPreviewViewModel>()
                   .ForMember(dst => dst.PreviewBitmap, opt => opt.Ignore())
                   .ForMember(dst => dst.RemoveImagePreviewCommand, opt => opt.Ignore())
                   .ForMember(dst => dst.Selected, opt => opt.Ignore())
                   .ForMember(dst => dst.IsLoading, opt => opt.Ignore())
                   .ForMember(dst => dst.Metadata, opt => opt.Ignore())
                   .ForMember(dst => dst.MetadataString, opt => opt.Ignore())
                   .ForMember(dst => dst.DateTimeString, opt => opt.Ignore())
                   .ForMember(dst => dst.RotateAngle, opt => opt.Ignore())
                   .ConstructUsing(
                       src => new MediaPreviewViewModel(
                           src.Title,
                           src.Url,
                           src.MediaFormat,
                           src.MaxImageHeight,
                           serviceLocator.Resolve<FileSystemImageService>(),
                           serviceLocator.Resolve<IFileOperationsService>(),
                           serviceLocator.Resolve<INotificationService>(),
                           _mapper,
                           serviceLocator.Resolve<ILogger>()));

                cfg.CreateMap<Notification, NotificationViewModel>();
                cfg.CreateMap<SuccessNotification, SuccessNotificationViewModel>()
                   .IncludeBase<Notification, NotificationViewModel>();
                cfg.CreateMap<ErrorNotification, ErrorNotificationViewModel>()
                   .IncludeBase<Notification, NotificationViewModel>();
            });

        config.AssertConfigurationIsValid();

        _mapper = new Mapper(config);
    }

    public IMapper GetMapper()
    {
        return _mapper;
    }
}