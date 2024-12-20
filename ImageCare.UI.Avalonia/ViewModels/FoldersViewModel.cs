using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using System.Windows.Input;

using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

using ImageCare.Core.Domain;
using ImageCare.Core.Services;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Mvvm;
using ImageCare.Mvvm.Collections;
using ImageCare.UI.Avalonia.Messages;
using ImageCare.UI.Avalonia.ViewModels.Domain;
using Prism.Regions;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class FoldersViewModel : ViewModelBase
{
    public static readonly Dictionary<Type, Func<string, string, IEnumerable<FileSystemItemViewModel>, IFolderService, FileSystemItemViewModel>> FileSystemItemsFactories = new()
    {
        { typeof(FileModel), (name, path, children, folderService) => new FileViewModel(name, path, children, folderService) },
        { typeof(DirectoryModel), (name, path, children, folderService) => new DirectoryViewModel(name, path, children, folderService) },
        { typeof(DeviceModel), (name, path, children, folderService) => new DeviceViewModel(name, path, children, folderService) },
        { typeof(FixedDriveModel), (name, path, children, folderService) => new FixedDriveViewModel(name, path, children, folderService) },
        { typeof(RemovableDriveModel), (name, path, children, folderService) => new RemovableDriveViewModel(name, path, children, folderService) },
        { typeof(NetworkDriveModel), (name, path, children, folderService) => new NetworkDriveViewModel(name, path, children, folderService) }
    };

    private readonly IFolderService _folderService;
    private readonly IConfigurationService _configurationService;
    private readonly ILogger _logger;
    private DirectoryViewModel? _selectedFileSystemItem;

    public FoldersViewModel(IFolderService folderService, 
                            IConfigurationService configurationService,
                            ILogger logger)
    {
        _folderService = folderService;
        _configurationService = configurationService;
        _logger = logger;
        OnViewLoadedCommand = new AsyncRelayCommand(OnViewLoaded);

        FileSystemItemViewModels = new SortedObservableCollection<FileSystemItemViewModel>();
    }

    public SortedObservableCollection<FileSystemItemViewModel> FileSystemItemViewModels { get; }

    public ICommand OnViewLoadedCommand { get; }

    public DirectoryViewModel? SelectedFileSystemItem
    {
        get => _selectedFileSystemItem;
        set
        {
            if (SetProperty(ref _selectedFileSystemItem, value) && _selectedFileSystemItem != null)
            {
                WeakReferenceMessenger.Default.Send(new FolderSelectedMessage(new DirectoryModel(_selectedFileSystemItem.Name, _selectedFileSystemItem.Path), Mode));
                _logger.Warning(_selectedFileSystemItem.Path);
            }
        }
    }

    public string Mode { get; private set; }

    /// <inheritdoc />
    public override void OnNavigatedTo(NavigationContext navigationContext)
    {
        Mode = (string)navigationContext.Parameters["mode"];
    }

    internal static IEnumerable<FileSystemItemViewModel> GetFileSystemItemChildren(IEnumerable<DirectoryModel> directoryModels,
                                                                                   IEnumerable<FileModel> fileModels, IFolderService folderService)
    {
        var children = new List<FileSystemItemViewModel>();

        foreach (var directoryModel in directoryModels)
        {
            if (!FileSystemItemsFactories.TryGetValue(directoryModel.GetType(), out var factoryFunc))
            {
                continue;
            }

            var directoryViewModel = factoryFunc.Invoke(directoryModel.Name, directoryModel.Path, GetFileSystemItemChildren(directoryModel.DirectoryModels, directoryModel.FileModels, folderService), folderService);
            children.Add(directoryViewModel);
        }

        return children;
    }

    private async Task OnViewLoaded()
    {
        var root = await _folderService.GetDirectoryModelAsync();

        if (FileSystemItemsFactories.TryGetValue(root.GetType(), out var factoryFunc))
        {
            var rootViewModel = factoryFunc.Invoke(root.Name, root.Path, GetFileSystemItemChildren(root.DirectoryModels, root.FileModels, _folderService), _folderService);

            FileSystemItemViewModels.InsertItem(rootViewModel);
        }
    }
}