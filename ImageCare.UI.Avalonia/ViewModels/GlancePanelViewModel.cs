using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.FileSystemWatcherService;
using ImageCare.Core.Services.FolderClusterizationService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.FolderStatisticsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;
using ImageCare.UI.Avalonia.Controls;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using MapsterMapper;

using Prism.Dialogs;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class GlancePanelViewModel : ViewModelBase, IDialogAware
{
    private const int PreloadRowsCount = 10;
    private readonly IFolderStatisticsService _folderStatisticsService;
    private readonly IFolderClusterizationService _folderClusterizationService;
    private readonly IMediaPreviewService _imageService;
    private readonly IFolderService _folderService;
    private readonly IFileSystemWatcherService _fileSystemWatcherService;
    private readonly IMediaPreviewOperationsService _fileOperationsService;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly object _imagePathsLock = new();
    private Collections.SortedObservableCollection<FileModel> _imagePaths;

    private CompositeDisposable? _disposable;
    private CancellationTokenSource _currentScrollCancellation = new();
    private CancellationTokenSource _imageLoadCts;
    private string _title;
    private int _imagesPerLine = 3;

    private bool _filesLoading;
    private string _selectedFolderPath;
    private AspectRatioViewModel _selectedAspectRatio;
    private GlanceMediaPreviewViewModel? _selectedPreview;
    private bool _isScrollResetRequested;
    private double _panelRowHeight;

    public GlancePanelViewModel(IFolderStatisticsService folderStatisticsService,
                                IFolderClusterizationService folderClusterizationService,
                                IMediaPreviewService imageService,
                                IFolderService folderService,
                                IFileSystemWatcherService fileSystemWatcherService,
                                IMediaPreviewOperationsService fileOperationsService,
                                IMapper mapper,
                                ILogger logger,
                                SynchronizationContext synchronizationContext, DialogCloseListener requestClose)
    {
        _folderStatisticsService = folderStatisticsService;
        _folderClusterizationService = folderClusterizationService;
        _imageService = imageService;
        _folderService = folderService;
        _fileSystemWatcherService = fileSystemWatcherService;
        _fileOperationsService = fileOperationsService;
        _mapper = mapper;
        _logger = logger;
        _synchronizationContext = synchronizationContext;
        RequestClose = requestClose;

        _imageLoadCts = new CancellationTokenSource();
        ImagePreviews = new Collections.SortedObservableCollection<GlanceMediaPreviewViewModel>(new CreationDateTimeDescendingComparer());
        TimelineVm = new TimelineViewModel(_folderStatisticsService, _synchronizationContext);

        ZoomInCommand = CreateCommand(ZoomIn, CanZoomIn).ObservesProperty(() => ImagePreviews.Count);
        ZoomOutCommand = CreateCommand(ZoomOut, CanZoomOut).ObservesProperty(() => ImagePreviews.Count);

        AspectRatioViewModels = new List<AspectRatioViewModel>
        {
            new(AspectRatio.Ratio4x3, "4:3"),
            new(AspectRatio.Ratio16x9, "16:9"),
            new(AspectRatio.Ratio3x2, "3:2"),
            new(AspectRatio.Ratio1x1, "1:1")
        };
        SelectedAspectRatio = AspectRatioViewModels[0];
    }

    public AspectRatioViewModel SelectedAspectRatio
    {
        get => _selectedAspectRatio;
        set => SetProperty(ref _selectedAspectRatio, value);
    }

    public IList<AspectRatioViewModel> AspectRatioViewModels { get; }

    public ICommand ZoomInCommand { get; }

    public ICommand ZoomOutCommand { get; }

    public TimelineViewModel TimelineVm { get; }

    public bool FilesLoading
    {
        get => _filesLoading;
        set => SetProperty(ref _filesLoading, value);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public Collections.SortedObservableCollection<GlanceMediaPreviewViewModel> ImagePreviews { get; }

    public int ImagesPerLine
    {
        get => _imagesPerLine;
        set => SetProperty(ref _imagesPerLine, value);
    }

    public GlanceMediaPreviewViewModel? SelectedPreview
    {
        get => _selectedPreview;
        set
        {
            if (_selectedPreview != null)
            {
                _selectedPreview.Selected = false;
            }

            SetProperty(ref _selectedPreview, value);
        }
    }

    public double PanelRowHeight
    {
        get => _panelRowHeight;
        set => SetProperty(ref _panelRowHeight, value);
    }

    /// <inheritdoc />
    public DialogCloseListener RequestClose { get; }

    public string SelectedFolderPath
    {
        get => _selectedFolderPath;
        set
        {
            SetProperty(ref _selectedFolderPath, value);
            Title = _selectedFolderPath;
        }
    }

    // Used by VerticalScrollBehavior
    public bool IsScrollResetRequested
    {
        get => _isScrollResetRequested;
        set => SetProperty(ref _isScrollResetRequested, value);
    }

    /// <inheritdoc />
    public bool CanCloseDialog()
    {
        return true;
    }

    /// <inheritdoc />
    public void OnDialogClosed()
    {
        try
        {
            CancelImageLoading();
            ClearPreviewPanel();
            _disposable?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during separate image window cleanup");
        }
    }

    /// <inheritdoc />
    public void OnDialogOpened(IDialogParameters parameters)
    {
        try
        {
            _disposable = new CompositeDisposable
            {
                _fileSystemWatcherService.FileCreated.Subscribe(OnFileCreated, onError: OnObservableError),
                _fileSystemWatcherService.FileDeleted.Subscribe(OnFileDeleted, onError: OnObservableError),
                _fileSystemWatcherService.FileRenamed.Subscribe(OnFileRenamed, onError: OnObservableError),
                _folderClusterizationService.ClusterizationCompleted.Subscribe(OnClusterizationCompleted, onError: OnObservableError),
                TimelineVm.DateSelected.Subscribe(OnTimelineDateSelected, onError: OnObservableError)
            };

            _folderStatisticsService.Stop();
            _fileSystemWatcherService.StopWatching();

            _imageLoadCts.Cancel();
            _imageLoadCts.Dispose();
            _imageLoadCts = new CancellationTokenSource();

            if (parameters["selectedFolder"] is not SelectedDirectory selectedDirectory)
            {
                return;
            }

            ClearPreviewPanel();

            _ = LoadFolderAsync(selectedDirectory, _imageLoadCts.Token);

            SelectedFolderPath = selectedDirectory.Path;

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
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize separate image window");
        }
    }

    public async Task HandleScrollAsync(double offsetY, double viewportHeight)
    {
        if (ImagePreviews.Count == 0 || PanelRowHeight <= 0)
        {
            return;
        }

        await _currentScrollCancellation.CancelAsync();
        _currentScrollCancellation.Dispose();
        _currentScrollCancellation = new CancellationTokenSource();
        var token = _currentScrollCancellation.Token;

        try
        {
            var firstVisibleRow = (int)(offsetY / PanelRowHeight);
            var lastVisibleRow = (int)((offsetY + viewportHeight) / PanelRowHeight);

            firstVisibleRow = Math.Max(0, firstVisibleRow - 1);
            lastVisibleRow = Math.Min(GetTotalRows() - 1, lastVisibleRow + 1);

            var firstVisibleIndex = firstVisibleRow * ImagesPerLine;
            var lastVisibleIndex = Math.Min(
                ImagePreviews.Count - 1,
                (lastVisibleRow + 1) * ImagesPerLine - 1);

            for (var i = firstVisibleIndex; i <= lastVisibleIndex; i++)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (i >= 0 && i < ImagePreviews.Count && ImagePreviews[i].PreviewBitmap == null)
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
            _logger.Error(ex, "Error on scroll images");
        }
    }

    private void ZoomOut()
    {
        ImagesPerLine--;
    }

    private bool CanZoomOut()
    {
        if (ImagesPerLine <= 1)
        {
            return false;
        }

        return ImagePreviews.Count != 0;
    }

    private bool CanZoomIn()
    {
        if (ImagesPerLine >= 20)
        {
            return false;
        }

        return ImagePreviews.Count != 0;
    }

    private void ZoomIn()
    {
        ImagesPerLine++;
    }

    private void ClearPreviewPanel()
    {
        _imagePaths?.Clear();
        ImagePreviews.Clear();
        TimelineVm.Clear();
    }

    private async Task LoadFolderAsync(SelectedDirectory selectedFileSystemItem, CancellationToken token)
    {
        try
        {
            lock (_imagePathsLock)
            {
                FilesLoading = true;
                _imagePaths = new Collections.SortedObservableCollection<FileModel>(new FileModelCreationDateTimeDescendingComparer());
            }

            var files = await _folderService.GetFileModelAsync(selectedFileSystemItem, "*");

            await Task.Run(
                () =>
                {
                    _imagePaths.AddRange(files);
                    FilesLoading = false;
                    token.ThrowIfCancellationRequested();

                    LoadInitialImagesAsync(_imageLoadCts.Token);
                    _folderStatisticsService.StartAsync(selectedFileSystemItem.Path, token);
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

    private async void LoadInitialImagesAsync(CancellationToken token)
    {
        try
        {
            if (_imagePaths.Count == 0)
            {
                return;
            }

            var initialCount = Math.Min(ImagesPerLine * PreloadRowsCount, _imagePaths.Count);

            var firstChunk = true;
            foreach (var chunk in _imagePaths.Chunk(initialCount))
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (firstChunk)
                {
                    firstChunk = false;
                    await LoadChunkAsync(chunk);
                    for (var i = 0; i < initialCount; i++)
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        _ = LoadImageAsync(i, token);
                    }
                }
                else
                {
                    await LoadChunkAsync(chunk);
                }
            }

            _folderClusterizationService.StartAsync(SelectedFolderPath, token);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Unexpected exception during loading initial images: {ex.Message}");
        }
    }

    private async Task LoadChunkAsync(FileModel[] chunk)
    {
        var previews = new List<GlanceMediaPreviewViewModel>(chunk.Length);

        foreach (var fileModel in chunk)
        {
            var previewImage = await _imageService.GetMediaPreviewAsync(fileModel.FullName);
            var mediaPreviewViewModel = _mapper.Map<GlanceMediaPreviewViewModel>(previewImage);
            mediaPreviewViewModel.FileDate = fileModel.CreatedDateTime.Value;
            previews.Add(mediaPreviewViewModel);
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

    private void OnClusterizationCompleted(FileClustersStatistics statistics)
    {
        var count = ImagePreviews.Count;

        for (var index = 0; index < count; index++)
        {
            if (index >= ImagePreviews.Count)
            {
                continue;
            }

            var preview = ImagePreviews[index];
            if (preview.FrameColorCode.StartsWith("#"))
            {
                continue;
            }

            var cluster = statistics.GetClusterByFilePath(preview.Url);
            if (cluster != null)
            {
                preview.FrameColorCode = cluster.ColorCode;
            }
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
            var start = Math.Max(0, index - PreloadRowsCount / 2);
            var end = Math.Min(ImagePreviews.Count - 1, index + PreloadRowsCount / 2);

            await LoadImageAsync(index, token);
            SelectedPreview = targetPreview;

            var upRowsTask = Task.Run(
                async () =>
                {
                    for (var i = index - 1; i >= start; i--)
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        await LoadImageAsync(i, token);
                    }
                },
                token);
            var downRowsTask = Task.Run(
                async () =>
                {
                    for (var i = index; i <= end; i++)
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        await LoadImageAsync(i, token);
                    }
                },
                token);

            await Task.WhenAll(upRowsTask, downRowsTask);
        }
        catch (Exception ex)
        {
            _logger.Error($"Date selection failed: {dateTime}", ex);
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

    private void OnFileCreated(FileModel fileModel)
    {
        lock (_imagePathsLock)
        {
            _imagePaths.Add(fileModel);
        }

        CreateImagePreviewFromPathAsync(fileModel.FullName, true);
    }

    private void OnObservableError(Exception ex)
    {
        _logger.Error(ex, "Error in observable subscription");
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

    private async Task AddImagePreviewAsync(MediaPreview previewImage)
    {
        try
        {
            var mediaPreviewViewModel = _mapper.Map<GlanceMediaPreviewViewModel>(previewImage);

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

    private void RemoveImagePreviewByPath(string filePath)
    {
        try
        {
            var imagePreviewViewModel = ImagePreviews.FirstOrDefault(vm => vm.Url.Equals(filePath, StringComparison.OrdinalIgnoreCase));
            if (imagePreviewViewModel != null)
            {
                ImagePreviews.Remove(imagePreviewViewModel);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Unexpected exception during Creating image preview from path: {filePath}");
        }
    }

    private void CancelImageLoading()
    {
        try
        {
            _imageLoadCts.Cancel();
            _imageLoadCts.Dispose();
            _imageLoadCts = new CancellationTokenSource();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to cancel image loading");
        }
    }

    private int GetTotalRows()
    {
        if (ImagePreviews.Count == 0 || ImagesPerLine <= 0)
        {
            return 0;
        }

        return (int)Math.Ceiling((double)ImagePreviews.Count / ImagesPerLine);
    }
}