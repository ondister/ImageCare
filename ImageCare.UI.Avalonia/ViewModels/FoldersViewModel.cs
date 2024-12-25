using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

using AutoMapper;

using CommunityToolkit.Mvvm.Input;

using ImageCare.Core.Domain;
using ImageCare.Core.Services;
using ImageCare.Core.Services.DrivesWatcherService;
using ImageCare.Mvvm;
using ImageCare.Mvvm.Collections;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Prism.Regions;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class FoldersViewModel : ViewModelBase
{
    private readonly IFolderService _folderService;
    private readonly IFileSystemWatcherService _fileSystemWatcherService;
    private readonly IDrivesWatcherService _drivesWatcherService;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private DirectoryViewModel? _selectedFileSystemItem;
    private CompositeDisposable _compositeDisposable;

    public FoldersViewModel(IFolderService folderService,
                            IFileSystemWatcherService fileSystemWatcherService,
                            IDrivesWatcherService drivesWatcherService,
                            IMapper mapper,
                            ILogger logger)
    {
        _folderService = folderService;
        _fileSystemWatcherService = fileSystemWatcherService;
        _drivesWatcherService = drivesWatcherService;
        _mapper = mapper;
        _logger = logger;

        OnViewLoadedCommand = new AsyncRelayCommand(OnViewLoaded);

        FileSystemItemViewModels = new SortedObservableCollection<DirectoryViewModel>();
    }

    public SortedObservableCollection<DirectoryViewModel> FileSystemItemViewModels { get; }

    public ICommand OnViewLoadedCommand { get; }

    public DirectoryViewModel? SelectedFileSystemItem
    {
        get => _selectedFileSystemItem;
        set
        {
            if (SetProperty(ref _selectedFileSystemItem, value) && _selectedFileSystemItem != null)
            {
                _folderService.SetSelectedDirectory(new SelectedDirectory(_selectedFileSystemItem.Name, _selectedFileSystemItem.Path, FileManagerPanel));
            }

        }
    }

    public FileManagerPanel FileManagerPanel { get; private set; }

    /// <inheritdoc />
    public override void OnNavigatedTo(NavigationContext navigationContext)
    {
        FileManagerPanel = (FileManagerPanel)navigationContext.Parameters["panel"];

        _compositeDisposable = new CompositeDisposable
        {
            _drivesWatcherService.DriveMounted.Subscribe(OnDriveMounted),
            _drivesWatcherService.DriveUnmounted.Subscribe(OnDriveUnmounted)
        };

        _drivesWatcherService.StartWatching();
    }

    /// <inheritdoc />
    public override void OnNavigatedFrom(NavigationContext navigationContext)
    {
        _compositeDisposable.Dispose();
        _drivesWatcherService.StopWatching();
    }

    private void OnDriveUnmounted(string driveName)
    {
        var root = FileSystemItemViewModels.FirstOrDefault(d => d is DeviceViewModel);

        var driveForRemove = root?.ChildFileSystemItems.FirstOrDefault(d => d.Name != null && d.Name.Equals(driveName, StringComparison.OrdinalIgnoreCase));

        if (driveForRemove != null)
        {
            // Reset selected drive to empty fo clearing preview panel
            if (SelectedFileSystemItem!=null && SelectedFileSystemItem.Path.StartsWith(driveName,StringComparison.OrdinalIgnoreCase))
            {
                _folderService.SetSelectedDirectory(new SelectedDirectory(DirectoryModel.Empty, FileManagerPanel));
            }

            root?.ChildFileSystemItems.Remove(driveForRemove);
        }
    }

    private void OnDriveMounted(DriveModel model)
    {
        var root = FileSystemItemViewModels.FirstOrDefault(d => d is DeviceViewModel);
        root?.ChildFileSystemItems.InsertItem(_mapper.Map<DriveViewModel>(model));
    }

    private async Task OnViewLoaded()
    {
        try
        {
            var root = await _folderService.GetDirectoryModelAsync();

            var rootViewModel = _mapper.Map<DirectoryViewModel>(root);
            rootViewModel.IsExpanded = true;

            FileSystemItemViewModels.InsertItem(rootViewModel);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Unexpected exception during root folders loading");
        }
    }
}