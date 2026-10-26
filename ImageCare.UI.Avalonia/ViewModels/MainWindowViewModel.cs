using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using Avalonia.Input;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.FolderHistoryService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.Views;

using Prism.Commands.Ex;
using Prism.Navigation;
using Prism.Navigation.Regions;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class MainWindowViewModel : ViewModelBase
{
	private readonly IRegionManager _regionManager;
	private readonly IMediaPreviewOperationsService _fileOperationsService;
	private readonly IFolderService _folderService;
    private readonly IFolderHistoryService _folderHistoryService;
    private readonly FileTransferCoordinator _fileTransferCoordinator;
    private readonly FileTransferOperationState _fileTransferOperationState;
    private readonly MediaSelectionCoordinator _selectionCoordinator;
	private readonly SynchronizationContext _synchronizationContext;
	private readonly ILogger _logger;
    private readonly MainImageViewModel _mainImageViewModel;
    private readonly MainVideoViewModel _mainVideoViewModel;
	private CompositeDisposable? _subscriptions;
    private bool _isImageViewVisible = true;
    private bool _isVideoViewVisible;
    private bool _isInitialized;

	public MainWindowViewModel(IRegionManager regionManager,
							   IMediaPreviewOperationsService fileOperationsService,
							   IFolderService folderService,
							   IFolderHistoryService folderHistoryService,
                               FileTransferCoordinator fileTransferCoordinator,
                               FileTransferOperationState fileTransferOperationState,
                               MediaSelectionCoordinator selectionCoordinator,
                               MainImageViewModel mainImageViewModel,
                               MainVideoViewModel mainVideoViewModel,
							   SynchronizationContext synchronizationContext,
							   ILogger logger)
	{
		_regionManager = regionManager;
		_fileOperationsService = fileOperationsService;
		_folderService = folderService;
        _folderHistoryService = folderHistoryService;
        _fileTransferCoordinator = fileTransferCoordinator;
        _fileTransferOperationState = fileTransferOperationState;
        _selectionCoordinator = selectionCoordinator;
        _mainImageViewModel = mainImageViewModel;
        _mainVideoViewModel = mainVideoViewModel;
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

    public bool RenameFileOnTransfer
    {
        get => _fileTransferCoordinator.RenameEnabled;
        set
        {
            if (_fileTransferCoordinator.RenameEnabled == value)
            {
                return;
            }

            _fileTransferCoordinator.RenameEnabled = value;
            RaisePropertyChanged();
        }
    }

    public void HandleKeyDown(KeyEventArgs args) =>
        _fileTransferOperationState.HandleKeyDown(args.Key, args.KeyModifiers);

    public void HandleKeyUp(KeyEventArgs args) =>
        _fileTransferOperationState.HandleKeyUp(args.Key, args.KeyModifiers);

    public void ClearWindowFileTransferModifiers() =>
        _fileTransferOperationState.ClearWindowModifiers();

    public void ResetFileTransferModifiers() =>
        _fileTransferOperationState.ResetModifiers();

    public MainImageViewModel MainImageViewModel => _mainImageViewModel;

    public MainVideoViewModel MainVideoViewModel => _mainVideoViewModel;

    public bool IsImageViewVisible
    {
        get => _isImageViewVisible;
        private set => SetProperty(ref _isImageViewVisible, value);
    }

    public bool IsVideoViewVisible
    {
        get => _isVideoViewVisible;
        private set => SetProperty(ref _isVideoViewVisible, value);
    }

	private async Task CopyImagePreviewAsync()
	{
		try
		{
			if (!CanDoPreviewOperation())
			{
				return;
			}

			await _selectionCoordinator.CopyAsync();
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Error copying selected image previews");
		}
	}

	private async Task MoveImagePreviewAsync()
	{
		try
		{
			if (!CanDoPreviewOperation())
			{
				return;
			}

			await _selectionCoordinator.MoveAsync();
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Error moving selected image previews");
		}
	}

	private bool CanDoPreviewOperation()
	{
		try
		{
			if (!_selectionCoordinator.CanCopyOrMove)
			{
				return false;
			}

			return _selectionCoordinator.FileManagerPanel switch
			{
				FileManagerPanel.Left => _folderService.GetSelectedDirectory(FileManagerPanel.Right) != null,
				FileManagerPanel.Right => _folderService.GetSelectedDirectory(FileManagerPanel.Left) != null,
				_ => false
			};
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
            switch (preview.MediaFormat.MediaType)
            {
                case MediaType.Video:
                    _mainImageViewModel.Deactivate();
                    IsImageViewVisible = false;
                    IsVideoViewVisible = true;
                    _mainVideoViewModel.Show(preview);
                    break;
                case MediaType.Image:
                    _mainVideoViewModel.Deactivate();
                    IsVideoViewVisible = false;
                    IsImageViewVisible = true;
                    _mainImageViewModel.Show(preview);
                    break;
            }

			NotifyFileOperationCommandsCanExecuteChanged();
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Error handling image preview selection for {Url}", preview?.Url);
		}
	}

	private void OnMediaSelectionChanged(object? sender, EventArgs args)
	{
		NotifyFileOperationCommandsCanExecuteChanged();
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
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;

		try
		{
			_folderHistoryService.LoadHistory();

			_regionManager.RequestNavigate(RegionNames.SourceFoldersRegion, "FoldersView", OnNavigationResult, new NavigationParameters { { "panel", FileManagerPanel.Left } });
			_regionManager.RequestNavigate(RegionNames.TargetFoldersRegion, "FoldersView", new NavigationParameters { { "panel", FileManagerPanel.Right } });
			_regionManager.RequestNavigate(RegionNames.SourcePreviewImageRegion, "PreviewPanelView", OnNavigationResult, new NavigationParameters { { "panel", FileManagerPanel.Left } });
			_regionManager.RequestNavigate(RegionNames.TargetPreviewImageRegion, "PreviewPanelView", OnNavigationResult, new NavigationParameters { { "panel", FileManagerPanel.Right } });
			_regionManager.RequestNavigate(RegionNames.BottomBarRegion, "BottomBarView");

			_selectionCoordinator.SelectionChanged += OnMediaSelectionChanged;
			_subscriptions = new CompositeDisposable
			{
				Disposable.Create(() => _selectionCoordinator.SelectionChanged -= OnMediaSelectionChanged),
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
            _isInitialized = false;
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
		var innerException = result.Exception?.InnerException;
		if (innerException == null)
		{
			return;
		}

		try
		{
			var regionName = result.Context?.NavigationService?.Region?.Name ?? string.Empty;
			_logger.Error(innerException, "Navigation failed for region {Region}", regionName);

			throw innerException;
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