using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using AutoMapper;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.FileSystemWatcherService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.FolderStatisticsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.Mvvm.Collections;
using ImageCare.UI.Avalonia.Behaviors;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Prism.Navigation.Regions;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class PreviewPanelViewModel : NavigatedViewModelBase
{
    // Desired size of item
    private const int MaxItemWidth = 316;
    private const int PreloadCount = 20;

    private readonly IMediaPreviewService _imageService;
    private readonly IFolderService _folderService;
    private readonly IFileSystemWatcherService _fileSystemWatcherService;
    private readonly IMediaPreviewOperationsService _fileOperationsService;
    private readonly IFolderStatisticsService _folderStatisticsService;
    private readonly INotificationService _notificationService;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;

    private readonly object _imagePathsLock = new();
    private MediaPreviewViewModel? _selectedPreview;
    private CompositeDisposable _disposable;

    private CancellationTokenSource _folderSelectedCancellationTokenSource;
    private SortedObservableCollection<FileModel> _imagePaths;
    private CancellationTokenSource _currentScrollCancellation = new();

    private bool _isScrollResetRequested;
    private bool _filesLoading;
    private FileClustersStatistics? _lastClustersStatistics;

    public PreviewPanelViewModel(IMediaPreviewService imageService,
                                 IFolderService folderService,
                                 IFileSystemWatcherService fileSystemWatcherService,
                                 IMediaPreviewOperationsService fileOperationsService,
                                 IFolderStatisticsService folderStatisticsService,
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
        _folderStatisticsService = folderStatisticsService;
        _notificationService = notificationService;
        _mapper = mapper;
        _logger = logger;
        _synchronizationContext = synchronizationContext;

        ImagePreviewDropHandler = imagePreviewDropHandler;
        CopyImagePreviewCommand = CreateAsyncCommand(CopyImagePreviewAsync);
        MoveImagePreviewCommand = CreateAsyncCommand(MoveImagePreviewAsync);
        DeleteImagePreviewCommand = CreateAsyncCommand(DeleteImagePreview);

        ImagePreviews = new SortedObservableCollection<MediaPreviewViewModel>(new CreationDateTimeDescendingComparer());

        _folderSelectedCancellationTokenSource = new CancellationTokenSource();

        TimelineVm = new TimelineViewModel(_folderStatisticsService, _synchronizationContext);
    }

    // Used by HorizontalScrollBehavior
    public bool IsScrollResetRequested
    {
        get => _isScrollResetRequested;
        set => SetProperty(ref _isScrollResetRequested, value);
    }

    public bool FilesLoading
    {
        get => _filesLoading;
        set => SetProperty(ref _filesLoading, value);
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

    public ICommand CopyImagePreviewCommand { get; }

    public ICommand MoveImagePreviewCommand { get; }

    public ICommand DeleteImagePreviewCommand { get; }

    /// <inheritdoc />
    public override void OnNavigatedTo(NavigationContext navigationContext)
    {
        FileManagerPanel = (FileManagerPanel)navigationContext.Parameters["panel"];

        _disposable = new CompositeDisposable
        {
            _fileSystemWatcherService.FileCreated.Subscribe(OnFileCreated),
            _fileSystemWatcherService.FileDeleted.Subscribe(OnFileDeleted),
            _fileSystemWatcherService.FileRenamed.Subscribe(OnFileRenamed),
            _folderService.FileSystemItemSelected.Subscribe(OnFolderSelected),
            _fileOperationsService.ImagePreviewSelected.Subscribe(OnImagePreviewSelected),
            _folderStatisticsService.ClusterizationCompleted.Subscribe(OnClusterizationCompleted),
            TimelineVm.DateSelected.Subscribe(OnTimelineDateSelected)
        };
    }

    /// <inheritdoc />
    public override void OnNavigatedFrom(NavigationContext navigationContext)
    {
        OnDispose();
    }

    /// <inheritdoc />
    /// <inheritdoc />
    protected override void OnDispose()
    {
        _disposable.Dispose();
        _folderSelectedCancellationTokenSource.Dispose();
        base.OnDispose();
    }

    internal async Task HandleScrollAsync(double horizontalOffset, double viewportWidth)
    {
        await _currentScrollCancellation.CancelAsync();
        _currentScrollCancellation.Dispose();
        _currentScrollCancellation = new CancellationTokenSource();
        var token = _currentScrollCancellation.Token;

        try
        {
            var firstVisibleIndex = (int)(horizontalOffset / MaxItemWidth);
            var lastVisibleIndex = (int)((horizontalOffset + viewportWidth) / MaxItemWidth);

            firstVisibleIndex = Math.Max(0, firstVisibleIndex);
            lastVisibleIndex = Math.Min(ImagePreviews.Count - 1, lastVisibleIndex);

            for (var i = Math.Max(0, firstVisibleIndex - PreloadCount);
                 i <= Math.Min(ImagePreviews.Count - 1, lastVisibleIndex + PreloadCount);
                 i++)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (ImagePreviews[i].PreviewBitmap == null)
                {
                   await LoadImageAsync(i, token);
                }
            }

        }
        catch (OperationCanceledException)
        {
            // Ignored
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Scroll handling error");
        }
    }

    private async Task CopyImagePreviewAsync()
    {
        try
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
        catch (Exception ex)
        {
            _logger.Error(ex, $"Error of image copying:{SelectedPreview}");
        }
    }

    private async Task MoveImagePreviewAsync()
    {
        try
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
        catch (Exception ex)
        {
            _logger.Error(ex, $"Error of image moving:{SelectedPreview}");
        }
    }

    private async Task DeleteImagePreview()
    {
        try
        {
            if (SelectedPreview == null)
            {
                return;
            }

            await SelectedPreview.RemoveImagePreviewAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Error of image removing:{SelectedPreview}");
        }
    }

    private async Task AddImagePreviewAsync(MediaPreview previewImage)
    {
        try
        {
            var mediaPreviewViewModel = _mapper.Map<MediaPreviewViewModel>(previewImage);

            var metadata = await _imageService.GetMediaMetadataAsync(previewImage);
            mediaPreviewViewModel.MetadataString = metadata.GetString();
            mediaPreviewViewModel.DateTimeString = metadata.CreationDateTime.ToString("dd.MM.yyyy HH:mm");
            mediaPreviewViewModel.Metadata = metadata;

            mediaPreviewViewModel.RotateAngle = mediaPreviewViewModel.Metadata.Orientation.ToRotationAngle();
            _ = mediaPreviewViewModel.LoadPreviewAsync(CancellationToken.None);

            _synchronizationContext.Send(d => { ImagePreviews.Add(mediaPreviewViewModel); }, null);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Error of adding image to preview: {previewImage.Url}");
        }
    }

    private void OnFolderSelected(SelectedDirectory selectedFileSystemItem)
    {
        if (selectedFileSystemItem.FileManagerPanel != FileManagerPanel)
        {
            return;
        }

        {
            _folderStatisticsService.Stop();

            _folderSelectedCancellationTokenSource.Cancel();
            _folderSelectedCancellationTokenSource.Dispose();
            _folderSelectedCancellationTokenSource = new CancellationTokenSource();

            _lastClustersStatistics = null;

            ClearPreviewPanel();

            if (selectedFileSystemItem.Path == string.Empty)
            {
                return;
            }

            _ = LoadFolderAsync(selectedFileSystemItem, _folderSelectedCancellationTokenSource.Token);

            SelectedFolderPath = selectedFileSystemItem.Path;

            if (!string.IsNullOrWhiteSpace(SelectedFolderPath))
            {
                try
                {
                    _fileSystemWatcherService.StartWatchingDirectory(SelectedFolderPath);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Unexpected exception during set watching directory {SelectedFolderPath}");
                }
            }
        }
    }

    private async Task LoadFolderAsync(SelectedDirectory selectedFileSystemItem, CancellationToken token)
    {
        try
        {
            lock (_imagePathsLock)
            {
                FilesLoading = true;
                _imagePaths = new SortedObservableCollection<FileModel>(new FileModelCreationDateTimeDescendingComparer());
            }

            var files = await _folderService.GetFileModelAsync(selectedFileSystemItem, "*");

            await Task.Run(
                () =>
                {
                    _imagePaths.AddRange(files);
                    FilesLoading = false;
                    token.ThrowIfCancellationRequested();

                    LoadInitialImagesAsync(_folderSelectedCancellationTokenSource.Token);
                    _folderStatisticsService.StartAsync(selectedFileSystemItem.Path, true, token);
                },
                token);
        }
        catch (OperationCanceledException)
        {
            // Ignored
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Failed on initial folder loading: {selectedFileSystemItem.Path}");
        }
        finally
        {
            FilesLoading = false;
        }
    }

    private void OnFileCreated(FileModel fileModel)
    {
        lock (_imagePathsLock)
        {
            _imagePaths.Add(fileModel);
        }

        CreateImagePreviewFromPathAsync(fileModel.FullName, true);
    }

    private void OnFileDeleted(FileModel fileModel)
    {
        try
        {
            lock (_imagePathsLock)
            {
                _imagePaths.Remove(fileModel);
            }

            RemoveImagePreviewByPath(fileModel.FullName);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Unexpected exception during handling of file deletion: {fileModel.FullName}");
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
                _imagePaths.Add(model.NewFileModel);
            }

            CreateImagePreviewFromPathAsync(model.NewFileModel.FullName, true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Unexpected exception during handling of file renaming: {model.OldFileModel.FullName}");
        }
    }

    private async Task CreateImagePreviewFromPathAsync(string filePath, bool loadToTimeline)
    {
        try
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

            await AddImagePreviewAsync(imagePreview);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Unexpected exception during Creating image preview from path: {filePath}");
        }
    }

    private void RemoveImagePreviewByPath(string filePath)
    {
        try
        {
            var imagePreviewViewModel = ImagePreviews.FirstOrDefault(vm => vm.Url.Equals(filePath, StringComparison.OrdinalIgnoreCase));
            if (imagePreviewViewModel != null)
            {
                var indexToRemove = ImagePreviews.IndexOf(imagePreviewViewModel);
                ImagePreviews.Remove(imagePreviewViewModel);

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
        catch (Exception ex)
        {
            _logger.Error(ex, $"Unexpected exception during Creating image preview from path: {filePath}");
        }
    }

    private void ClearPreviewPanel()
    {
        _imagePaths?.Clear();
        ImagePreviews.Clear();
        SelectedPreview = null;
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
        try
        {
            if (_imagePaths.Count == 0)
            {
                return;
            }

            var initialCount = Math.Min(PreloadCount * 2, _imagePaths.Count);

            var firstChunk = true;
            foreach (var chunk in _imagePaths.Chunk(initialCount))
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                await LoadChunkAsync(chunk);

                if (firstChunk)
                {
                    firstChunk = false;
                    for (var i = 0; i < initialCount; i++)
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        await LoadImageAsync(i, token);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Unexpected exception during loading initial images: {ex.Message}");
        }
    }

    private async Task LoadChunkAsync(FileModel[] chunk)
    {
        var previews = new List<MediaPreviewViewModel>(chunk.Length);

        foreach (var fileModel in chunk)
        {
            var previewImage = await _imageService.GetMediaPreviewAsync(fileModel.FullName);
            var mediaPreviewViewModel = _mapper.Map<MediaPreviewViewModel>(previewImage);
            mediaPreviewViewModel.FileDate = fileModel.CreatedDateTime.Value;
            previews.Add(mediaPreviewViewModel);

            var fileCluster = _lastClustersStatistics?.GetClusterByFilePath(mediaPreviewViewModel.Url);
            if (fileCluster != null)
            {
                mediaPreviewViewModel.FrameColorCode = fileCluster.ColorCode;
            }

        }

        _synchronizationContext.Send(d => { ImagePreviews.AddRange(previews); }, null);
    }

    private async Task LoadImageAsync(int indexInPreviews, CancellationToken token = default)
    {
        if (indexInPreviews < 0 || indexInPreviews >= ImagePreviews.Count)
        {
            return;
        }

        var previewVm = ImagePreviews[indexInPreviews];
        if (previewVm.PreviewBitmap != null)
        {
            return;
        }

        try
        {
            token.ThrowIfCancellationRequested();

            var imagePath = previewVm.Url;
            var mediaPreview = await _imageService.GetMediaPreviewAsync(imagePath);

            if (mediaPreview == null)
            {
                return;
            }


            var metadata = await _imageService.GetMediaMetadataAsync(mediaPreview);
            previewVm.MetadataString = metadata.GetString();
            previewVm.DateTimeString = metadata.CreationDateTime.ToString("dd.MM.yyyy HH:mm");
            previewVm.Metadata = metadata;
            previewVm.RotateAngle = metadata.Orientation.ToRotationAngle();

            await previewVm.LoadPreviewAsync(token);
        }
        catch (OperationCanceledException)
        {
            // Ignored
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to load image: {previewVm.Url}", ex);
        }
    }

    private async void OnTimelineDateSelected(DateTime dateTime)
    {
        try
        {
            var targetPreview = ImagePreviews.FirstOrDefault(p =>
                                                                 p.FileDate.Date == dateTime.Date);

            if (targetPreview == null)
            {
                return;
            }

            var index = ImagePreviews.IndexOf(targetPreview);

            await _currentScrollCancellation.CancelAsync();
            _currentScrollCancellation.Dispose();
            _currentScrollCancellation = new CancellationTokenSource();
            var token = _currentScrollCancellation.Token;

            // Load range nearby item
            var start = Math.Max(0, index - PreloadCount);
            var end = Math.Min(ImagePreviews.Count - 1, index + PreloadCount);

            await LoadImageAsync(index, token);
            SelectedPreview = targetPreview;

            var loadTasks = new List<Task>();
            for (var i = start; i <= end; i++)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                loadTasks.Add(LoadImageAsync(i, token));
            }

            await Task.WhenAll(loadTasks);
        }
        catch (Exception ex)
        {
            _logger.Error($"Date selection failed: {dateTime}", ex);
        }
    }

    private void OnClusterizationCompleted(FileClustersStatistics statistics)
    {
        _lastClustersStatistics = statistics;

        var count = ImagePreviews.Count;

        for (var index = 0; index < count; index++)
        {
            if (index >= ImagePreviews.Count)
            {
                continue;
            }

            var preview = ImagePreviews[index];
            var cluster = statistics.GetClusterByFilePath(preview.Url);
            if (cluster != null)
            {
                preview.FrameColorCode = cluster.ColorCode;

            }
        }
    }
}