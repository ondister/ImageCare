using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using AutoMapper;

using CommunityToolkit.Mvvm.Input;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.FileOperationsService;
using ImageCare.Core.Services.FileSystemImageService;
using ImageCare.Core.Services.FileSystemWatcherService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.Mvvm.Collections;
using ImageCare.UI.Avalonia.Behaviors;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Prism.Regions;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class PreviewPanelViewModel : NavigatedViewModelBase, IDisposable
{
	private const int maxItemWidth = 300;

	private readonly IFileSystemImageService _imageService;
	private readonly IFolderService _folderService;
	private readonly IFileSystemWatcherService _fileSystemWatcherService;
	private readonly IFileOperationsService _fileOperationsService;
	private readonly INotificationService _notificationService;
	private readonly IMapper _mapper;
	private readonly ILogger _logger;
	private readonly SynchronizationContext _synchronizationContext;
	private readonly int _preloadCount = 20;
	private readonly SemaphoreSlim _loadSemaphore = new(1, 1);

	private readonly object _imagePathsLock = new();
	private MediaPreviewViewModel? _selectedPreview;
	private CompositeDisposable _fileSystemWatcherCompositeDisposable;

	private CancellationTokenSource _folderSelectedCancellationTokenSource;
	private string? _statistics;
	private SortedObservableCollection<FileModel> _imagePaths;
	private CancellationTokenSource _currentScrollCancellation = new();

	public PreviewPanelViewModel(IFileSystemImageService imageService,
	                             IFolderService folderService,
	                             IFileSystemWatcherService fileSystemWatcherService,
	                             IFileOperationsService fileOperationsService,
	                             INotificationService notificationService,
	                             IMapper mapper,
	                             ILogger logger,
	                             ImagePreviewDropHandler imagePreviewDropHandler,
	                             SynchronizationContext synchronizationContext)
	{
		_imageService = imageService;
		_folderService = folderService;
		_fileSystemWatcherService = fileSystemWatcherService;
		_fileOperationsService = fileOperationsService;
		_notificationService = notificationService;
		_mapper = mapper;
		_logger = logger;
		_synchronizationContext = synchronizationContext;

		ImagePreviewDropHandler = imagePreviewDropHandler;
		CopyImagePreviewCommand = new AsyncRelayCommand(CopyImagePreviewAsync);
		MoveImagePreviewCommand = new AsyncRelayCommand(MoveImagePreviewAsync);
		DeleteImagePreviewCommand = new AsyncRelayCommand(DeleteImagePreview);

		ImagePreviews = new SortedObservableCollection<MediaPreviewViewModel>(new CreationDateTimeDescendingComparer());

		_folderSelectedCancellationTokenSource = new CancellationTokenSource();

		TimelineVm = new TimelineViewModel(_synchronizationContext);
	}

	public SortedObservableCollection<MediaPreviewViewModel> ImagePreviews { get; }

	public ImagePreviewDropHandler ImagePreviewDropHandler { get; }

	public FileManagerPanel FileManagerPanel { get; private set; }

	public MediaPreviewViewModel? SelectedPreview
	{
		get => _selectedPreview;
		set
		{
			if (_selectedPreview != null)
			{
				_selectedPreview.Selected = false;
			}

			SetProperty(ref _selectedPreview, value);
			if (_selectedPreview != null)
			{
				_selectedPreview.Selected = true;

				_fileOperationsService.SetSelectedPreview(new SelectedMediaPreview(_mapper.Map<MediaPreview>(_selectedPreview), FileManagerPanel));
			}
		}
	}

	public string SelectedFolderPath { get; set; }

	public TimelineViewModel TimelineVm { get; }

	public string? Statistics
	{
		get => _statistics;
		set => SetProperty(ref _statistics, value);
	}

	public ICommand CopyImagePreviewCommand { get; }

	public ICommand MoveImagePreviewCommand { get; }

	public ICommand DeleteImagePreviewCommand { get; }

	/// <inheritdoc />
	public void Dispose()
	{
		_fileSystemWatcherCompositeDisposable.Dispose();
		_folderSelectedCancellationTokenSource.Dispose();
	}

	/// <inheritdoc />
	public override void OnNavigatedTo(NavigationContext navigationContext)
	{
		FileManagerPanel = (FileManagerPanel)navigationContext.Parameters["panel"];

		_fileSystemWatcherCompositeDisposable = new CompositeDisposable
		{
			_fileSystemWatcherService.FileCreated.Subscribe(OnFileCreated),
			_fileSystemWatcherService.FileDeleted.Subscribe(OnFileDeleted),
			_fileSystemWatcherService.FileRenamed.Subscribe(OnFileRenamed),
			_folderService.FileSystemItemSelected.Subscribe(OnFolderSelected),
			_fileOperationsService.ImagePreviewSelected.Subscribe(OnImagePreviewSelected),
			TimelineVm.DateSelected.Subscribe(OnTimelineDateSelected)
		};
	}

	/// <inheritdoc />
	public override void OnNavigatedFrom(NavigationContext navigationContext)
	{
		_fileSystemWatcherCompositeDisposable.Dispose();
	}

	internal async Task HandleScroll(double horizontalOffset, double viewportWidth)
	{
		_currentScrollCancellation.Cancel();
		_currentScrollCancellation.Dispose();
		_currentScrollCancellation = new CancellationTokenSource();

		var token = _currentScrollCancellation.Token;

		try
		{
			List<FileModel> currentImagePaths;
			lock (_imagePathsLock)
			{
				currentImagePaths = new List<FileModel>(_imagePaths);
			}

			var firstVisibleIndex = (int)(horizontalOffset / maxItemWidth);
			var lastVisibleIndex = (int)((horizontalOffset + viewportWidth) / maxItemWidth);

			for (var i = Math.Max(0, firstVisibleIndex - _preloadCount);
			     i <= Math.Min(currentImagePaths.Count - 1, lastVisibleIndex + _preloadCount);
			     i++)
			{
				if (token.IsCancellationRequested)
				{
					return;
				}

				if (ImagePreviews.Count <= i || ImagePreviews[i].PreviewBitmap == null)
				{
					await LoadImageAsync(i, false, token);
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Ignored
		}
	}

	private async Task CopyImagePreviewAsync()
	{
		if (SelectedPreview == null)
		{
			return;
		}

		var targetDirectory = _folderService.GetSelectedDirectory(FileManagerPanel == FileManagerPanel.Left ? FileManagerPanel.Right : FileManagerPanel.Left);

		if (targetDirectory == null)
		{
			return;
		}

		var notificationTitle = $"{SelectedPreview.Url} => {targetDirectory.Path}";
		_notificationService.SendNotification(new Notification(notificationTitle, string.Empty));
		var progress = new Progress<OperationInfo>();

		progress.ProgressChanged += (o, info) => { _notificationService.SendNotification(new Notification(notificationTitle, info.Percentage.ToString("F1"))); };

		var result = await _fileOperationsService.CopyImagePreviewToDirectoryAsync(
			             _mapper.Map<MediaPreview>(SelectedPreview),
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
		if (SelectedPreview == null)
		{
			return;
		}

		var targetDirectory = _folderService.GetSelectedDirectory(FileManagerPanel == FileManagerPanel.Left ? FileManagerPanel.Right : FileManagerPanel.Left);

		if (targetDirectory == null)
		{
			return;
		}

		var notificationTitle = $"{SelectedPreview.Url} => {targetDirectory.Path}";
		_notificationService.SendNotification(new Notification(notificationTitle, string.Empty));
		var progress = new Progress<OperationInfo>();

		progress.ProgressChanged += (o, info) => { _notificationService.SendNotification(new Notification(notificationTitle, info.Percentage.ToString("F1"))); };

		var result = await _fileOperationsService.MoveImagePreviewToDirectoryAsync(
			             _mapper.Map<MediaPreview>(SelectedPreview),
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

	private async Task DeleteImagePreview()
	{
		if (SelectedPreview == null)
		{
			return;
		}

		await SelectedPreview.RemoveImagePreviewAsync();
	}

	private async Task AddImagePreviewAsync(MediaPreview previewImage, bool loadToTimeline)
	{
		var mediaPreviewViewModel = _mapper.Map<MediaPreviewViewModel>(previewImage);

		var metadata = await _imageService.GetMediaMetadataAsync(previewImage);
		mediaPreviewViewModel.MetadataString = metadata.GetString();
		mediaPreviewViewModel.DateTimeString = metadata.CreationDateTime.ToString("dd.MM.yyyy HH:mm");
		mediaPreviewViewModel.Metadata = metadata;

		mediaPreviewViewModel.RotateAngle = mediaPreviewViewModel.Metadata.Orientation.ToRotationAngle();

		if (loadToTimeline)
		{
			TimelineVm.AddFile(new FileModel(previewImage.Title, previewImage.Url, metadata.CreationDateTime));
		}

		_synchronizationContext.Send(d => { ImagePreviews.InsertItem(mediaPreviewViewModel); }, null);
	}

	private void OnFileCreated(FileModel fileModel)
	{
		try
		{
			lock (_imagePathsLock)
			{
				_imagePaths.InsertItem(fileModel);
			}

			LoadFolderStatisticsAsync(SelectedFolderPath);
			CreateImagePreviewFromPathAsync(fileModel.FullName, true);
		}
		catch (Exception exception)
		{
			_logger.Error(exception, $"Unexpected exception during handling of file creation: {fileModel.FullName}");
		}
	}

	private void OnFileDeleted(FileModel fileModel)
	{
		try
		{
			lock (_imagePathsLock)
			{
				_imagePaths.Remove(fileModel);
			}

			LoadFolderStatisticsAsync(SelectedFolderPath);
			RemoveImagePreviewByPath(fileModel.FullName);
		}
		catch (Exception exception)
		{
			_logger.Error(exception, $"Unexpected exception during handling of file deletion: {fileModel.FullName}");
		}
	}

	private void OnFileRenamed(FileRenamedModel model)
	{
		try
		{
			lock (_imagePathsLock)
			{
				_imagePaths.Remove(model.OldFileModel);
			}

			RemoveImagePreviewByPath(model.OldFileModel.FullName);

			lock (_imagePathsLock)
			{
				_imagePaths.InsertItem(model.NewFileModel);
			}

			CreateImagePreviewFromPathAsync(model.NewFileModel.FullName, true);
		}
		catch (Exception exception)
		{
			_logger.Error(exception, $"Unexpected exception during handling of file renaming: {model.OldFileModel.FullName}");
		}
	}

	private async Task CreateImagePreviewFromPathAsync(string filePath, bool loadToTimeline)
	{
		var imagePreview = await _imageService.GetMediaPreviewAsync(filePath);

		if (imagePreview == null)
		{
			return;
		}

		if (ImagePreviews.Any(p => p.Url.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
		{
			return;
		}

		await AddImagePreviewAsync(imagePreview, loadToTimeline);
	}

	private void RemoveImagePreviewByPath(string filePath)
	{
		var imagePreviewViewModel = ImagePreviews.FirstOrDefault(vm => vm.Url.Equals(filePath, StringComparison.OrdinalIgnoreCase));
		if (imagePreviewViewModel != null)
		{
			var indexToRemove = ImagePreviews.IndexOf(imagePreviewViewModel);
			ImagePreviews.Remove(imagePreviewViewModel);

			TimelineVm.RemoveFile(new FileModel(imagePreviewViewModel.Title, imagePreviewViewModel.Url, imagePreviewViewModel.Metadata.CreationDateTime));

			if (ImagePreviews.Count != 0 && ImagePreviews.Count > indexToRemove)
			{
				_synchronizationContext.Post(d => { SelectedPreview = ImagePreviews[indexToRemove]; }, null);
			}

			if (ImagePreviews.Count == 0)
			{
				_synchronizationContext.Post(d => { SelectedPreview = null; }, null);
				_fileOperationsService.SetSelectedPreview(new SelectedMediaPreview(MediaPreview.Empty, FileManagerPanel));
			}
		}
	}

	private void OnFolderSelected(SelectedDirectory selectedFileSystemItem)
	{
		if (selectedFileSystemItem.FileManagerPanel == FileManagerPanel)
		{
			_folderSelectedCancellationTokenSource.Cancel();
			_folderSelectedCancellationTokenSource.Dispose();
			_folderSelectedCancellationTokenSource = new CancellationTokenSource();

			ClearPreviewPanel();

			if (selectedFileSystemItem.Path == string.Empty)
			{
				return;
			}

			_folderService.GetFileModelAsync(selectedFileSystemItem, "*")
			              .ContinueWith(
				              task =>
				              {
					              lock (_imagePathsLock)
					              {
						              _imagePaths = new SortedObservableCollection<FileModel>(task.Result, new FileModelCreationDateTimeDescendingComparer());
					              }

					              LoadInitialImagesAsync(_folderSelectedCancellationTokenSource.Token);
					              LoadTimelineDataAsync(_folderSelectedCancellationTokenSource.Token);
				              },
				              TaskScheduler.FromCurrentSynchronizationContext());

			_ = LoadFolderStatisticsAsync(selectedFileSystemItem.Path);

			SelectedFolderPath = selectedFileSystemItem.Path;

			if (!string.IsNullOrWhiteSpace(SelectedFolderPath))
			{
				try
				{
					_fileSystemWatcherService.StartWatchingDirectory(SelectedFolderPath);
				}
				catch (Exception exception)
				{
					_logger.Error(exception, $"Unexpected exception during set watching directory {SelectedFolderPath}");
				}
			}
		}
	}

	private async Task LoadFolderStatisticsAsync(string folderPath)
	{
		try
		{
			var statistics = await _folderService.GetFolderStatisticsAsync(folderPath);

			Statistics = statistics.ToString();
		}
		catch (Exception exception)
		{
			_logger.Error(exception, $"Error of getting statistics for {folderPath}");
		}
	}

	private void ClearPreviewPanel()
	{
		_imagePaths?.Clear();
		ImagePreviews.Clear();
		SelectedPreview = null;
		Statistics = string.Empty;
		TimelineVm.Clear();
	}

	private void OnImagePreviewSelected(SelectedMediaPreview selectedImagePreview)
	{
		if (selectedImagePreview.FileManagerPanel != FileManagerPanel)
		{
			SelectedPreview = null;
		}
	}

	private async void LoadInitialImagesAsync(CancellationToken token)
	{
		var initialCount = Math.Min(_preloadCount * 2, _imagePaths.Count);

		for (var i = 0; i < initialCount; i++)
		{
			if (token.IsCancellationRequested)
			{
				return;
			}

			await LoadImageAsync(i, token: token);
		}
	}

	private async Task LoadImageAsync(int index, bool loadToTimeLine = false, CancellationToken token = default)
	{
		FileModel imagePath;
		lock (_imagePathsLock)
		{
			if (index >= _imagePaths.Count || index < 0)
			{
				return;
			}

			imagePath = _imagePaths[index];
		}

		if (ImagePreviews.Count > index && ImagePreviews[index].PreviewBitmap != null)
		{
			return;
		}

		await _loadSemaphore.WaitAsync(token);

		try
		{
			token.ThrowIfCancellationRequested();

			var mediaPreview = await _imageService.GetMediaPreviewAsync(imagePath.FullName);

			if (mediaPreview != null)
			{
				await AddImagePreviewAsync(mediaPreview, loadToTimeLine);
			}
		}
		catch (OperationCanceledException)
		{
			// Ignored
		}
		catch (Exception ex)
		{
			_logger.Warning($"File {imagePath} is not supported");
		}
		finally
		{
			_loadSemaphore.Release();
		}
	}

	private async void OnTimelineDateSelected(DateTime dateTime)
	{
		try
		{
			var imagePathIndexWithDate = _imagePaths.FirstOrDefault(p => p.CreatedDateTime.Value.Date == dateTime.Date);

			if (imagePathIndexWithDate != null)
			{
				var index = _imagePaths.IndexOf(imagePathIndexWithDate);

				_currentScrollCancellation.Cancel();
				_currentScrollCancellation.Dispose();
				_currentScrollCancellation = new CancellationTokenSource();

				var token = _currentScrollCancellation.Token;
				await LoadImageAsync(index, false, token);
				var imageToSelect = ImagePreviews.FirstOrDefault(p => p.Metadata.CreationDateTime.Date == dateTime.Date);
				if (imageToSelect != null)
				{
					SelectedPreview = imageToSelect;
				}
			}
		}
		catch (Exception exception)
		{
			_logger.Error(exception, "Error on timeline date selected changed");
		}
	}

	private async Task LoadTimelineDataAsync(CancellationToken token)
	{
		// Создаём потокобезопасную копию коллекции
		List<FileModel> imagePathsCopy;
		lock (_imagePathsLock)
		{
			imagePathsCopy = _imagePaths.Select(x => new FileModel(x.Name, x.FullName,x.CreatedDateTime)).ToList();
		}

		foreach (var imagePath in imagePathsCopy)
		{
			if (token.IsCancellationRequested)
				return;

			try
			{
				var preview = await _imageService.GetMediaPreviewAsync(imagePath.FullName);
				if (preview == null)
				{
					continue;
				}
				

				var creationDate = await _imageService.GetCreationDateTime(preview);
				var fileModel = new FileModel(imagePath.Name, imagePath.FullName, creationDate);

					TimelineVm.AddFile(fileModel);
			}
			catch (OperationCanceledException)
			{
				return; 
			}
			catch (Exception exception)
			{
				// Логирование ошибки
				_logger.Error(exception, $"Failed to load {imagePath.FullName}: {exception.Message}");
			}
		}
	}
}