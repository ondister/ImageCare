using System.Linq;

using AutoMapper;

using ImageCare.Core.Domain;
using ImageCare.Core.Services.FileOperationsService;
using ImageCare.Core.Services.FileSystemImageService;
using ImageCare.Core.Services.FolderService;
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
                   .ForMember(dst => dst.FileManagerPanel, opt => opt.Ignore())
                   .ConstructUsing(src => new DirectoryViewModel(src.Name, src.Path, src.DirectoryModels.Select(m => _mapper.Map(m, m.GetType(), typeof(DirectoryViewModel)) as DirectoryViewModel), serviceLocator.Resolve<IFolderService>(), _mapper, serviceLocator.Resolve<ILogger>()));

                cfg.CreateMap<DriveModel, DriveViewModel>()
                   .IncludeBase<DirectoryModel, DirectoryViewModel>()
                   .ForMember(dst => dst.ChildFileSystemItems, opt => opt.Ignore())
                   .ForMember(dst => dst.IsExpanded, opt => opt.Ignore())
                   .ForMember(dst => dst.IsLoaded, opt => opt.Ignore())
                   .ForMember(dst => dst.FileManagerPanel, opt => opt.Ignore())
                   .ConstructUsing(src => new DriveViewModel(src.Name, src.Path, src.DirectoryModels.Select(m => _mapper.Map(m, m.GetType(), typeof(DirectoryViewModel)) as DirectoryViewModel), serviceLocator.Resolve<IFolderService>(), _mapper, serviceLocator.Resolve<ILogger>()));

                cfg.CreateMap<DeviceModel, DeviceViewModel>()
                   .IncludeBase<DriveModel, DriveViewModel>()
                   .ForMember(dst => dst.ChildFileSystemItems, opt => opt.Ignore())
                   .ForMember(dst => dst.IsExpanded, opt => opt.Ignore())
                   .ForMember(dst => dst.IsLoaded, opt => opt.Ignore())
                   .ForMember(dst => dst.FileManagerPanel, opt => opt.Ignore())
                   .ConstructUsing(src => new DeviceViewModel(src.Name, src.Path, src.DirectoryModels.Select(m => _mapper.Map(m, m.GetType(), typeof(DirectoryViewModel)) as DirectoryViewModel), serviceLocator.Resolve<IFolderService>(), _mapper, serviceLocator.Resolve<ILogger>()));

                cfg.CreateMap<FixedDriveModel, FixedDriveViewModel>()
                   .IncludeBase<DriveModel, DriveViewModel>()
                   .ForMember(dst => dst.ChildFileSystemItems, opt => opt.Ignore())
                   .ForMember(dst => dst.IsExpanded, opt => opt.Ignore())
                   .ForMember(dst => dst.IsLoaded, opt => opt.Ignore())
                   .ForMember(dst => dst.FileManagerPanel, opt => opt.Ignore())
                   .ConstructUsing(src => new FixedDriveViewModel(src.Name, src.Path, src.DirectoryModels.Select(m => _mapper.Map(m, m.GetType(), typeof(DirectoryViewModel)) as DirectoryViewModel), serviceLocator.Resolve<IFolderService>(), _mapper, serviceLocator.Resolve<ILogger>()));

                cfg.CreateMap<NetworkDriveModel, NetworkDriveViewModel>()
                   .IncludeBase<DriveModel, DriveViewModel>()
                   .ForMember(dst => dst.ChildFileSystemItems, opt => opt.Ignore())
                   .ForMember(dst => dst.IsExpanded, opt => opt.Ignore())
                   .ForMember(dst => dst.IsLoaded, opt => opt.Ignore())
                   .ForMember(dst => dst.FileManagerPanel, opt => opt.Ignore())
                   .ConstructUsing(src => new NetworkDriveViewModel(src.Name, src.Path, src.DirectoryModels.Select(m => _mapper.Map(m, m.GetType(), typeof(DirectoryViewModel)) as DirectoryViewModel), serviceLocator.Resolve<IFolderService>(), _mapper, serviceLocator.Resolve<ILogger>()));

                cfg.CreateMap<RemovableDriveModel, RemovableDriveViewModel>()
                   .IncludeBase<DriveModel, DriveViewModel>()
                   .ForMember(dst => dst.ChildFileSystemItems, opt => opt.Ignore())
                   .ForMember(dst => dst.IsExpanded, opt => opt.Ignore())
                   .ForMember(dst => dst.IsLoaded, opt => opt.Ignore())
                   .ForMember(dst => dst.FileManagerPanel, opt => opt.Ignore())
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

                cfg.CreateMap<ImagePreviewViewModel, ImagePreview>()
                   .ConstructUsing(src => new ImagePreview(src.Title, src.Url, src.MediaFormat));

                cfg.CreateMap<ImagePreview, ImagePreviewViewModel>()
                   .ForMember(dst => dst.PreviewBitmap, opt => opt.Ignore())
                   .ForMember(dst => dst.RemoveImagePreviewCommand, opt => opt.Ignore())
                   .ForMember(dst => dst.Selected, opt => opt.Ignore())
                   .ForMember(dst => dst.IsLoading, opt => opt.Ignore())
                   .ConstructUsing(src => new ImagePreviewViewModel(src.Title, src.Url, src.MediaFormat, serviceLocator.Resolve<FileSystemImageService>(), serviceLocator.Resolve<IFileOperationsService>(), _mapper, serviceLocator.Resolve<ILogger>()));
            });

        config.AssertConfigurationIsValid();

        _mapper = new Mapper(config);
    }

    public IMapper GetMapper()
    {
        return _mapper;
    }
}