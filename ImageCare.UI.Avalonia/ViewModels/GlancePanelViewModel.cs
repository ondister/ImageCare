using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.FileSystemWatcherService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.FolderStatisticsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;
using ImageCare.Mvvm;
using ImageCare.Mvvm.Collections;
using ImageCare.UI.Avalonia.ViewModels.Domain;
using Prism.Dialogs;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using AutoMapper;

using ImageCare.Core.Domain.Media;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class GlancePanelViewModel : ViewModelBase, IDialogAware
{
    private readonly IFolderStatisticsService _folderStatisticsService;
    private readonly IMediaPreviewService _imageService;
    private readonly IFolderService _folderService;
    private readonly IFileSystemWatcherService _fileSystemWatcherService;
    private readonly IMediaPreviewOperationsService _fileOperationsService;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;
    private FileClustersStatistics? _lastClustersStatistics;
    private SortedObservableCollection<FileModel> _imagePaths;
    private readonly object _imagePathsLock = new();

    private CompositeDisposable? _disposable;
    private bool _isDisposed;
    private CancellationTokenSource _imageLoadCts;
    private string _title;
    private int _imagesPerLine = 5;
    
    private bool _filesLoading;
    private string _selectedFolderPath;

    public GlancePanelViewModel(IFolderStatisticsService folderStatisticsService,
                                IMediaPreviewService imageService,
                                IFolderService folderService,
                                IFileSystemWatcherService fileSystemWatcherService,
                                IMediaPreviewOperationsService fileOperationsService,
                                IMapper mapper,
                                ILogger logger,
                                SynchronizationContext synchronizationContext)
    {
        _folderStatisticsService = folderStatisticsService;
        _imageService = imageService;
        _folderService = folderService;
        _fileSystemWatcherService = fileSystemWatcherService;
        _fileOperationsService = fileOperationsService;
        _mapper = mapper;
        _logger = logger;
        _synchronizationContext = synchronizationContext;




        _imageLoadCts = new CancellationTokenSource();
        ImagePreviews = new SortedObservableCollection<GlanceMediaPreviewViewModel>(new CreationDateTimeDescendingComparer());
        TimelineVm = new TimelineViewModel(_folderStatisticsService, _synchronizationContext);

        ZoomInCommand = CreateCommand(ZoomIn, CanZoomIn).ObservesProperty(() => ImagePreviews.Count);
        ZoomOutCommand = CreateCommand(ZoomOut, CanZoomOut).ObservesProperty(() => ImagePreviews.Count);
    }

    private void ZoomOut()
    {
        ImagesPerLine--;
    }

    private bool CanZoomOut()
    {
        if (ImagesPerLine <=2)
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

    public SortedObservableCollection<GlanceMediaPreviewViewModel> ImagePreviews { get; }

    public int ImagesPerLine
    {
        get => _imagesPerLine;
        set => SetProperty(ref _imagesPerLine, value);
    }

    /// <inheritdoc />
    public DialogCloseListener RequestClose { get; }

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
        ThrowIfDisposed();

        try
        {
            _disposable = new CompositeDisposable
            {
                _fileSystemWatcherService.FileCreated.Subscribe(OnFileCreated, onError: OnObservableError),
                _fileSystemWatcherService.FileDeleted.Subscribe(OnFileDeleted, onError: OnObservableError),
                _fileSystemWatcherService.FileRenamed.Subscribe(OnFileRenamed, onError: OnObservableError),
                _folderStatisticsService.ClusterizationCompleted.Subscribe(OnClusterizationCompleted, onError: OnObservableError),
                TimelineVm.DateSelected.Subscribe(OnTimelineDateSelected, onError: OnObservableError),
                TimelineVm.StatisticsClick.Subscribe(OnStatisticsClick, onError: OnObservableError)
            };

            _folderStatisticsService.Stop();
            _fileSystemWatcherService.StopWatching();

            _imageLoadCts.Cancel();
            _imageLoadCts.Dispose();
            _imageLoadCts = new CancellationTokenSource();

            _lastClustersStatistics = null;

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

    public string SelectedFolderPath
    {
        get => _selectedFolderPath;
        set => SetProperty(ref _selectedFolderPath, value);
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
                _imagePaths = new SortedObservableCollection<FileModel>(new FileModelCreationDateTimeDescendingComparer());
            }

            var files = await _folderService.GetFileModelAsync(selectedFileSystemItem, "*");

            await Task.Run(
                () =>
                {
                    _imagePaths.AddRange(files);
                    FilesLoading = false;
                    token.ThrowIfCancellationRequested();

                    LoadInitialImagesAsync(_imageLoadCts.Token);
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

    protected override void OnDispose()
    {
        if (!_isDisposed)
        {
            try
            {
                _disposable?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during MainImageViewModel disposal");
            }

            _isDisposed = true;
        }

        base.OnDispose();
    }

    private async void LoadInitialImagesAsync(CancellationToken token)
    {
        try
        {
            if (_imagePaths.Count == 0)
            {
                return;
            }

            var initialCount = Math.Min(40 * 2, _imagePaths.Count);

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


    private void OnClusterizationCompleted(FileClustersStatistics statistics)
    {
        throw new NotImplementedException();
    }

    private void OnStatisticsClick(Unit unit)
    {
        throw new NotImplementedException();
    }

    private void OnTimelineDateSelected(DateTime time)
    {
        throw new NotImplementedException();
    }

    private void OnFileRenamed(FileRenamedModel model)
    {
        throw new NotImplementedException();
    }

    private void OnFileDeleted(FileModel model)
    {
        throw new NotImplementedException();
    }

    private void OnFileCreated(FileModel model)
    {
        throw new NotImplementedException();
    }

    private void OnObservableError(Exception ex)
    {
        _logger.Error(ex, "Error in observable subscription");
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(MainImageViewModel));
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
}