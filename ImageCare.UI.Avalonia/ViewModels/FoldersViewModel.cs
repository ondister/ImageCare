using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using AutoMapper;

using CommunityToolkit.Mvvm.Input;

using ImageCare.Core.Domain;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.DrivesWatcherService;
using ImageCare.Core.Services.FileSystemWatcherService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Mvvm.Collections;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Prism.Commands;
using Prism.Regions;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class FoldersViewModel : NavigatedViewModelBase
{
    private readonly IFolderService _folderService;
    private readonly IMultiSourcesFileSystemWatcherService _multiSourcesFileSystemWatcherService;
    private readonly IDrivesWatcherService _drivesWatcherService;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;
    private DirectoryViewModel? _selectedFileSystemItem;
    private CompositeDisposable _compositeDisposable;
    private DirectoryModel? _createdSubFolder;

    public FoldersViewModel(IFolderService folderService,
                            IMultiSourcesFileSystemWatcherService multiSourcesFileSystemWatcherService,
                            IDrivesWatcherService drivesWatcherService,
                            IMapper mapper,
                            ILogger logger,
                            SynchronizationContext synchronizationContext)
    {
        _folderService = folderService;
        _multiSourcesFileSystemWatcherService = multiSourcesFileSystemWatcherService;
        _drivesWatcherService = drivesWatcherService;
        _mapper = mapper;
        _logger = logger;
        _synchronizationContext = synchronizationContext;

        OnViewLoadedCommand = new AsyncRelayCommand(OnViewLoaded);
        DeleteFolderCommand = new DelegateCommand(DeleteFolder);
        CreateFolderCommand = new DelegateCommand(CreateFolder);

        FileSystemItemViewModels = new SortedObservableCollection<DirectoryViewModel>();
    }

    public SortedObservableCollection<DirectoryViewModel> FileSystemItemViewModels { get; }

    public ICommand OnViewLoadedCommand { get; }

    public ICommand CreateFolderCommand { get; }

    public ICommand DeleteFolderCommand { get; }

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
            _drivesWatcherService.DriveUnmounted.Subscribe(OnDriveUnmounted),
            _folderService.FolderVisited.Where(folder => folder.FileManagerPanel == FileManagerPanel).Subscribe(OnFolderVisited),
            _folderService.FolderLeft.Where(folder => folder.FileManagerPanel == FileManagerPanel).Subscribe(OnFolderLeft),
            _multiSourcesFileSystemWatcherService.DirectoryCreated.DistinctUntilChanged(folder => folder.Path).ObserveOn(_synchronizationContext).Subscribe(OnDirectoryCreated),
            _multiSourcesFileSystemWatcherService.DirectoryDeleted.DistinctUntilChanged(folder => folder.Path).ObserveOn(_synchronizationContext).Subscribe(OnDirectoryRemoved),
            _multiSourcesFileSystemWatcherService.DirectoryRenamed.DistinctUntilChanged(folder => folder.NewDirectoryModel.Path).ObserveOn(_synchronizationContext).Subscribe(OnDirectoryRenamed)
        };

        _drivesWatcherService.StartWatching();
        _multiSourcesFileSystemWatcherService.StartWatching();
    }

    /// <inheritdoc />
    public override void OnNavigatedFrom(NavigationContext navigationContext)
    {
        _compositeDisposable.Dispose();
        _drivesWatcherService.StopWatching();

        _multiSourcesFileSystemWatcherService.StopWatching();
        _multiSourcesFileSystemWatcherService.ClearWatchers();
    }

    private void OnDirectoryCreated(DirectoryModel directoryModel)
    {
        var parent = directoryModel.GetParent();
        if (parent == null)
        {
            return;
        }

        foreach (var directoryViewModel in FileSystemItemViewModels)
        {
            if (directoryViewModel.FindChildByPathRecursively(parent.Path) is { } parentVieModel)
            {
                if (!parentVieModel.ChildFileSystemItems.Any(d => d.Path.Equals(directoryModel.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    var createdViewModel = _mapper.Map<DirectoryViewModel>(directoryModel);
                    parentVieModel.ChildFileSystemItems.InsertItem(createdViewModel);

                    if (createdViewModel.Path.Equals(_createdSubFolder?.Path, StringComparison.OrdinalIgnoreCase))
                    {
                        createdViewModel.IsEditing = true;
                    }

                }

                return;
            }
        }
    }

    private void OnDirectoryRemoved(DirectoryModel directoryModel)
    {
        var parent = directoryModel.GetParent();
        if (parent == null)
        {
            return;
        }

        foreach (var directoryViewModel in FileSystemItemViewModels)
        {
            if (directoryViewModel.FindChildByPathRecursively(parent.Path) is { } parentVieModel)
            {
                if (parentVieModel.ChildFileSystemItems.FirstOrDefault(d => d.Path.Equals(directoryModel.Path, StringComparison.OrdinalIgnoreCase)) is { } directoryViewModelForDelete)
                {
                    if (directoryViewModelForDelete.Path == SelectedFileSystemItem?.Path)
                    {
                        SelectedFileSystemItem = null;
                    }

                    parentVieModel.ChildFileSystemItems.Remove(directoryViewModelForDelete);
                    if (parentVieModel.IsExpanded && !parentVieModel.ChildFileSystemItems.Any())
                    {
                        parentVieModel.IsExpanded = false;
                    }
                }

                return;
            }
        }
    }

    private void OnDirectoryRenamed(DirectoryRenamedModel directoryRenamedModel)
    {
        var parent = directoryRenamedModel.OldDirectoryModel.GetParent();
        if (parent == null)
        {
            return;
        }

        foreach (var directoryViewModel in FileSystemItemViewModels)
        {
            if (directoryViewModel.FindChildByPathRecursively(parent.Path) is { } parentVieModel)
            {
                if (parentVieModel.ChildFileSystemItems.FirstOrDefault(d => d.Path.Equals(directoryRenamedModel.OldDirectoryModel.Path, StringComparison.OrdinalIgnoreCase)) is { } directoryViewModelForRename)
                {
                    directoryViewModelForRename.UpdateDirectory(directoryRenamedModel.NewDirectoryModel);
                    if (directoryRenamedModel.OldDirectoryModel.Path == SelectedFileSystemItem?.Path)
                    {
                        SelectedFileSystemItem = null;
                        SelectedFileSystemItem = directoryViewModelForRename;
                    }
                }

                return;
            }
        }
    }

    private async Task OnViewLoaded()
    {
        try
        {
            var root = await _folderService.GetDirectoryModelAsync();

            var rootViewModel = _mapper.Map<DirectoryViewModel>(root);
            rootViewModel.SetFileManagerPanel(FileManagerPanel);
            rootViewModel.IsExpanded = true;

            FileSystemItemViewModels.InsertItem(rootViewModel);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Unexpected exception during root folders loading");
        }
    }

    private void OnDriveUnmounted(string driveName)
    {
        var root = FileSystemItemViewModels.FirstOrDefault(d => d is DeviceViewModel);

        var driveForRemove = root?.ChildFileSystemItems.FirstOrDefault(d => d.Name != null && d.Name.Equals(driveName, StringComparison.OrdinalIgnoreCase));

        if (driveForRemove != null)
        {
            // Reset selected drive to empty fo clearing preview panel
            if (SelectedFileSystemItem != null && SelectedFileSystemItem.Path.StartsWith(driveName, StringComparison.OrdinalIgnoreCase))
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

    private void OnFolderVisited(DirectoryModel directoryModel)
    {
        _multiSourcesFileSystemWatcherService.StartWatchingDirectory(directoryModel.Path);
    }

    private void OnFolderLeft(DirectoryModel directoryModel)
    {
        _multiSourcesFileSystemWatcherService.StopWatchingDirectory(directoryModel.Path);
    }

    private void CreateFolder()
    {
        if (SelectedFileSystemItem == null)
        {
            return;
        }

        _createdSubFolder = _folderService.CreateSubFolder(_mapper.Map<DirectoryModel>(SelectedFileSystemItem));
    }

    private void DeleteFolder()
    {
        if (SelectedFileSystemItem != null && !SelectedFileSystemItem.HasSupportedMedia)
        {
            _folderService.RemoveFolder(_mapper.Map<DirectoryModel>(SelectedFileSystemItem));
        }
    }
}