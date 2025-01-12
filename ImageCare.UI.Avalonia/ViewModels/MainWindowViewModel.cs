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
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Services.FileOperationsService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.Mvvm;
using ImageCare.UI.Avalonia.Views;

using Prism.Regions;

using ReactiveUI;

namespace ImageCare.UI.Avalonia.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly IRegionManager _regionManager;
    private readonly IFileOperationsService _fileOperationsService;
    private readonly IFolderService _folderService;
    private readonly INotificationService _notificationService;
    private readonly IMapper _mapper;
    private readonly SynchronizationContext _synchronizationContext;
    private CompositeDisposable _subscriptions;
    private SelectedMediaPreview? _currentSelectedPreview;

    public MainWindowViewModel(IRegionManager regionManager,
                               IFileOperationsService fileOperationsService,
                               IFolderService folderService,
                               INotificationService notificationService,
                               IMapper mapper,
                               SynchronizationContext synchronizationContext)
    {
        _regionManager = regionManager;
        _fileOperationsService = fileOperationsService;
        _folderService = folderService;
        _notificationService = notificationService;
        _mapper = mapper;
        _synchronizationContext = synchronizationContext;

        OnViewLoadedCommand = new RelayCommand(OnViewLoaded);
        OnViewUnloadedCommand = new RelayCommand(OnViewUnloaded);

        CopySelectedPreviewCommand = new AsyncRelayCommand(CopyImagePreviewAsync, CanDoPreviewOperation);
        MoveSelectedPreviewCommand = new AsyncRelayCommand(MoveImagePreviewAsync, CanDoPreviewOperation);
    }

    public ICommand OnViewLoadedCommand { get; }

    public ICommand OnViewUnloadedCommand { get; }

    public ICommand CopySelectedPreviewCommand { get; }

    public ICommand MoveSelectedPreviewCommand { get; }

    private async Task CopyImagePreviewAsync()
    {
        if (_currentSelectedPreview == null)
        {
            return;
        }

        var targetDirectory = _folderService.GetSelectedDirectory(_currentSelectedPreview.FileManagerPanel == FileManagerPanel.Left ? FileManagerPanel.Right : FileManagerPanel.Left);

        if (targetDirectory == null)
        {
            return;
        }

        var notificationTitle = $"{_currentSelectedPreview.Url} => {targetDirectory.Path}";
        _notificationService.SendNotification(new Notification(notificationTitle, string.Empty));
        var progress = new Progress<OperationInfo>();

        progress.ProgressChanged += (o, info) => { _notificationService.SendNotification(new Notification(notificationTitle, info.Percentage.ToString("F1"))); };

        var result = await _fileOperationsService.CopyImagePreviewToDirectoryAsync(
                         _mapper.Map<MediaPreview>(_currentSelectedPreview),
                         targetDirectory.Path,
                         progress);

        switch (result)
        {
            case OperationResult.Success:
                _notificationService.SendNotification(new SuccessNotification(notificationTitle, ""));
                break;
            case OperationResult.Failed:
                _notificationService.SendNotification(new ErrorNotification(notificationTitle, ""));
                break;
        }
    }

    private async Task MoveImagePreviewAsync()
    {
        if (_currentSelectedPreview == null)
        {
            return;
        }

        var targetDirectory = _folderService.GetSelectedDirectory(_currentSelectedPreview.FileManagerPanel == FileManagerPanel.Left ? FileManagerPanel.Right : FileManagerPanel.Left);

        if (targetDirectory == null)
        {
            return;
        }

        var notificationTitle = $"{_currentSelectedPreview.Url} => {targetDirectory.Path}";
        _notificationService.SendNotification(new Notification(notificationTitle, string.Empty));
        var progress = new Progress<OperationInfo>();

        progress.ProgressChanged += (o, info) => { _notificationService.SendNotification(new Notification(notificationTitle, info.Percentage.ToString("F1"))); };

        var result = await _fileOperationsService.MoveImagePreviewToDirectoryAsync(
                         _mapper.Map<MediaPreview>(_currentSelectedPreview),
                         targetDirectory.Path,
                         progress);

        switch (result)
        {
            case OperationResult.Success:
                _notificationService.SendNotification(new SuccessNotification(notificationTitle, ""));
                break;
            case OperationResult.Failed:
                _notificationService.SendNotification(new ErrorNotification(notificationTitle, ""));
                break;
        }
    }

    private bool CanDoPreviewOperation()
    {
        if (_currentSelectedPreview?.Url != string.Empty)
        {
            switch (_currentSelectedPreview?.FileManagerPanel)
            {
                case FileManagerPanel.Left when _folderService.GetSelectedDirectory(FileManagerPanel.Right) != null:
                case FileManagerPanel.Right when _folderService.GetSelectedDirectory(FileManagerPanel.Left) != null:
                    return true;
            }
        }

        return false;
    }

    private void OnViewLoaded()
    {
        _regionManager.RequestNavigate(RegionNames.SourceFoldersRegion, "FoldersView", OnNavigationResult, new NavigationParameters { { "panel", FileManagerPanel.Left } });
        _regionManager.RequestNavigate(RegionNames.TargetFoldersRegion, "FoldersView", new NavigationParameters { { "panel", FileManagerPanel.Right } });
        _regionManager.RequestNavigate(RegionNames.MainImageViewRegion, "MainImageView");
        _regionManager.RequestNavigate(RegionNames.SourcePreviewImageRegion, "PreviewPanelView", new NavigationParameters { { "panel", FileManagerPanel.Left } });
        _regionManager.RequestNavigate(RegionNames.TargetPreviewImageRegion, "PreviewPanelView", new NavigationParameters { { "panel", FileManagerPanel.Right } });
        _regionManager.RequestNavigate(RegionNames.BottomBarRegion, "BottomBarView");

        _subscriptions = new CompositeDisposable
        {
            _fileOperationsService.ImagePreviewSelected
                                  .ObserveOn(_synchronizationContext)
                                  .Subscribe(OnImagePreviewSelected),
            _folderService.FileSystemItemSelected
                          .ObserveOn(_synchronizationContext)
                          .Subscribe(OnFolderSelected)
        };
    }

    private void OnImagePreviewSelected(SelectedMediaPreview preview)
    {
        if (preview.MediaFormat.MediaType == MediaType.Video && !_regionManager.Regions[RegionNames.MainImageViewRegion].ActiveViews.Any(v => v is MainVideoView))
        {
            _regionManager.RequestNavigate(RegionNames.MainImageViewRegion, "MainVideoView", new NavigationParameters { { "imagePreview", preview } });
        }

        if (preview.MediaFormat.MediaType == MediaType.Image && !_regionManager.Regions[RegionNames.MainImageViewRegion].ActiveViews.Any(v => v is MainImageView))
        {
            _regionManager.RequestNavigate(RegionNames.MainImageViewRegion, "MainImageView", new NavigationParameters { { "imagePreview", preview } });
        }

        _currentSelectedPreview = preview;
        NotifyFileOperationCommandsCanExecuteChanged();
    }

    private void OnFolderSelected(SelectedDirectory directory)
    {
        NotifyFileOperationCommandsCanExecuteChanged();
    }

    private void NotifyFileOperationCommandsCanExecuteChanged()
    {
        (CopySelectedPreviewCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (MoveSelectedPreviewCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
    }

    private void OnViewUnloaded()
    {
        _subscriptions?.Dispose();
    }

    private void OnNavigationResult(NavigationResult result)
    {
        if (result.Error?.InnerException != null)
        {
            throw result.Error.InnerException;
        }
    }
}