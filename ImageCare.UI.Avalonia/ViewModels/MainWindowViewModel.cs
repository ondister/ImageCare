using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using AutoMapper;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.FolderHistoryService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.UI.Avalonia.Views;

using Prism.Commands.Ex;
using Prism.Navigation;
using Prism.Navigation.Regions;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
	private readonly IRegionManager _regionManager;
	private readonly IMediaPreviewOperationsService _fileOperationsService;
	private readonly IFolderService _folderService;
	private readonly INotificationService _notificationService;
    private readonly IFolderHistoryService _folderHistoryService;
    private readonly IMapper _mapper;
	private readonly SynchronizationContext _synchronizationContext;
	private readonly ILogger _logger;
	private CompositeDisposable _subscriptions;
	private SelectedMediaPreview? _currentSelectedPreview;

	public MainWindowViewModel(IRegionManager regionManager,
							   IMediaPreviewOperationsService fileOperationsService,
							   IFolderService folderService,
							   INotificationService notificationService,
							   IFolderHistoryService folderHistoryService,
							   IMapper mapper,
							   SynchronizationContext synchronizationContext,
							   ILogger logger)
	{
		_regionManager = regionManager;
		_fileOperationsService = fileOperationsService;
		_folderService = folderService;
		_notificationService = notificationService;
        _folderHistoryService = folderHistoryService;
        _mapper = mapper;
		_synchronizationContext = synchronizationContext;
		_logger = logger;

		OnViewLoadedCommand =  CreateCommand(OnViewLoaded);
		OnViewUnloadedCommand =  CreateCommand(OnViewUnloaded);

		CopySelectedPreviewCommand = CreateAsyncCommand(CopyImagePreviewAsync, CanDoPreviewOperation);
		MoveSelectedPreviewCommand = CreateAsyncCommand(MoveImagePreviewAsync, CanDoPreviewOperation);
	}

	public ICommand OnViewLoadedCommand { get; }

	public ICommand OnViewUnloadedCommand { get; }

	public ICommand CopySelectedPreviewCommand { get; }

	public ICommand MoveSelectedPreviewCommand { get; }

	private async Task CopyImagePreviewAsync()
	{
		try
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
					_logger.Error("Failed to copy image preview from {Source} to {Target}", _currentSelectedPreview.Url, targetDirectory.Path);
					break;
			}
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Error copying image preview from {Source}", _currentSelectedPreview?.Url);
		}
	}

	private async Task MoveImagePreviewAsync()
	{
		try
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
					_logger.Error("Failed to move image preview from {Source} to {Target}", _currentSelectedPreview.Url, targetDirectory.Path);
					break;
			}
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Error moving image preview from {Source}", _currentSelectedPreview?.Url);
		}
	}

	private bool CanDoPreviewOperation()
	{
		try
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
		catch (Exception ex)
		{
			_logger.Error(ex, "Error checking if preview operation can be performed");
			return false;
		}
	}

	private void OnImagePreviewSelected(SelectedMediaPreview preview)
	{
		try
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
		catch (Exception ex)
		{
			_logger.Error(ex, "Error handling image preview selection for {Url}", preview?.Url);
		}
	}

	private void OnFolderSelected(SelectedDirectory directory)
	{
		try
		{
			NotifyFileOperationCommandsCanExecuteChanged();
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Error handling folder selection for {Path}", directory?.Path);
		}
	}

	private void NotifyFileOperationCommandsCanExecuteChanged()
	{
		try
		{
			(CopySelectedPreviewCommand as AsyncDelegateCommand)?.RaiseCanExecuteChanged();
			(MoveSelectedPreviewCommand as AsyncDelegateCommand)?.RaiseCanExecuteChanged();
        }
		catch (Exception ex)
		{
			_logger.Error(ex, "Error notifying commands can execute changed");
		}
	}

	private void OnViewLoaded()
	{
		try
		{
			_folderHistoryService.LoadHistory();

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
									  .Subscribe(OnImagePreviewSelected, OnObservableError),
				_folderService.FileSystemItemSelected
							  .ObserveOn(_synchronizationContext)
							  .Subscribe(OnFolderSelected, OnObservableError)
			};
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Error during MainWindowViewModel initialization");
		}
	}

	private void OnViewUnloaded()
	{
		try
		{
            _folderHistoryService.SaveHistory();
			_subscriptions?.Dispose();
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Error during MainWindowViewModel cleanup");
		}
	}

	private void OnNavigationResult(NavigationResult result)
	{
		try
		{
			if (result.Exception?.InnerException != null)
			{
				_logger.Error(result.Exception?.InnerException, "Navigation failed for region {Region}", result.Context.NavigationService.Region.Name);

				throw result.Exception?.InnerException;
			}
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Error handling navigation result");
		}
	}

	private void OnObservableError(Exception exception)
	{
		_logger.Error(exception, "Error in observable sequence");
	}
}