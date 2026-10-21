using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Domain.Notification;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.FileSystemWatcherService;
using ImageCare.Core.Services.FolderClusterizationService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.FolderStatisticsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.UI.Avalonia.Collections;
using ImageCare.UI.Avalonia.Controls;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;
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
    private readonly INotificationService _notificationService;
    private readonly IConfigurationService _configurationService;
    private readonly LocalizationManager _localization;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly object _imagePathsLock = new();
    private readonly object _loadedPreviewsLock = new();
    private readonly HashSet<MediaPreviewViewModel> _loadedPreviews = [];
    private SortedObservableCollection<FileModel> _imagePaths;

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
    private int _scrollTargetRowIndex = -1;
    private double _panelRowHeight;
    private bool _hasDeletedFiles;
    private bool _isRestoringAll;
    private bool _isPreviewAnalysisComplete;
    private bool _isStatisticsAnalysisComplete;
    private MediaSortDirection _sortDirection;
    private IReadOnlyList<IReadOnlyList<GlanceMediaPreviewViewModel>> _previewRows = [];

    public GlancePanelViewModel(IFolderStatisticsService folderStatisticsService,
                                IFolderClusterizationService folderClusterizationService,
                                IMediaPreviewService imageService,
                                IFolderService folderService,
                                IFileSystemWatcherService fileSystemWatcherService,
                                IMediaPreviewOperationsService fileOperationsService,
                                IConfigurationService configurationService,
                                TrayAnalysisProgressService trayAnalysisProgressService,
                                INotificationService notificationService,
                                LocalizationManager localization,
                                IMapper mapper,
                                ILogger logger,
                                SynchronizationContext synchronizationContext,
                                DialogCloseListener requestClose)
    {
        _folderStatisticsService = folderStatisticsService;
        _folderClusterizationService = folderClusterizationService;
        _imageService = imageService;
        _folderService = folderService;
        _fileSystemWatcherService = fileSystemWatcherService;
        _fileOperationsService = fileOperationsService;
        _notificationService = notificationService;
        _configurationService = configurationService;
        _localization = localization;
        _mapper = mapper;
        _logger = logger;
        _synchronizationContext = synchronizationContext;
        RequestClose = requestClose;

        _imageLoadCts = new CancellationTokenSource();
        _sortDirection = configurationService.Configuration.Value.FolderOverviewSortDirection;
        ImagePreviews = new SortedObservableCollection<GlanceMediaPreviewViewModel>(
            new CreationDateTimeComparer(_sortDirection));
        ImagePreviews.CollectionChanged += (_, _) => RebuildPreviewRows();
        DeletedFiles = new ObservableCollection<DeletedMediaFileViewModel>();
        TimelineVm = new TimelineViewModel(
            _folderStatisticsService,
            configurationService,
            trayAnalysisProgressService,
            _synchronizationContext);

        ToggleSortDirectionCommand = CreateCommand(ToggleSortDirection);
        SelectPreviewCommand = CreateCommand<GlanceMediaPreviewViewModel>(preview => SelectedPreview = preview);
        ZoomInCommand = CreateCommand(ZoomIn, CanZoomIn).ObservesProperty(() => ImagePreviews.Count);
        ZoomOutCommand = CreateCommand(ZoomOut, CanZoomOut).ObservesProperty(() => ImagePreviews.Count);
        RestoreAllCommand = CreateAsyncCommand(RestoreAllAsync, () => HasDeletedFiles && !IsRestoringAll)
            .ObservesProperty(() => HasDeletedFiles)
            .ObservesProperty(() => IsRestoringAll);

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

    public ICommand RestoreAllCommand { get; }

    public ICommand ToggleSortDirectionCommand { get; }

    public ICommand SelectPreviewCommand { get; }

    public bool IsSortAscending => _sortDirection == MediaSortDirection.OldestFirst;

    public bool CanChangeSort => !FilesLoading &&
                                 ImagePreviews.Count > 0 &&
                                 !string.IsNullOrWhiteSpace(SelectedFolderPath);

    public TimelineViewModel TimelineVm { get; }

    public ObservableCollection<DeletedMediaFileViewModel> DeletedFiles { get; }

    public bool HasDeletedFiles
    {
        get => _hasDeletedFiles;
        private set => SetProperty(ref _hasDeletedFiles, value);
    }

    public bool IsRestoringAll
    {
        get => _isRestoringAll;
        private set => SetProperty(ref _isRestoringAll, value);
    }

    public bool FilesLoading
    {
        get => _filesLoading;
        set
        {
            if (SetProperty(ref _filesLoading, value))
            {
                RaisePropertyChanged(nameof(CanChangeSort));
            }
        }
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public SortedObservableCollection<GlanceMediaPreviewViewModel> ImagePreviews { get; }

    public IReadOnlyList<IReadOnlyList<GlanceMediaPreviewViewModel>> PreviewRows
    {
        get => _previewRows;
        private set => SetProperty(ref _previewRows, value);
    }

    public int ImagesPerLine
    {
        get => _imagesPerLine;
        set
        {
            if (SetProperty(ref _imagesPerLine, value))
            {
                RebuildPreviewRows();
            }
        }
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
            if (_selectedPreview != null)
            {
                _selectedPreview.Selected = true;
            }
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
            Title = _localization.Get("Overview_Title", _selectedFolderPath);
        }
    }

    // Used by VerticalScrollBehavior
    public bool IsScrollResetRequested
    {
        get => _isScrollResetRequested;
        set => SetProperty(ref _isScrollResetRequested, value);
    }

    // Used by ScrollToIndexBehavior. The index addresses PreviewRows, not individual previews.
    public int ScrollTargetRowIndex
    {
        get => _scrollTargetRowIndex;
        private set => SetProperty(ref _scrollTargetRowIndex, value);
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
            _folderStatisticsService.Stop();
            _fileSystemWatcherService.StopWatching();
            ClearPreviewPanel();
            ClearDeletedFiles();
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
            _fileSystemWatcherService.FileCreated
                                         .Buffer(TimeSpan.FromMilliseconds(150))
                                         .Where(files => files.Count > 0)
                                         .ObserveOn(_synchronizationContext)
                                         .Subscribe(OnFilesCreated, onError: OnObservableError),
                _fileSystemWatcherService.FileDeleted
                                         .Buffer(TimeSpan.FromMilliseconds(150))
                                         .Where(files => files.Count > 0)
                                         .ObserveOn(_synchronizationContext)
                                         .Subscribe(OnFilesDeleted, onError: OnObservableError),
                _fileSystemWatcherService.FileRenamed
                                         .ObserveOn(_synchronizationContext)
                                         .Subscribe(OnFileRenamed, onError: OnObservableError),
                _fileOperationsService.FileOperationCompleted
                                      .ObserveOn(_synchronizationContext)
                                      .Subscribe(OnFileOperationCompleted, onError: OnObservableError),
                _fileOperationsService.DeletedFilesChanged
                                      .ObserveOn(_synchronizationContext)
                                      .Subscribe(RefreshDeletedFiles, onError: OnObservableError),
                _folderClusterizationService.ClusterizationCompleted
                                            .ObserveOn(_synchronizationContext)
                                            .Subscribe(OnClusterizationCompleted, onError: OnObservableError),
                _folderStatisticsService.ScanProgress
                                        .ObserveOn(_synchronizationContext)
                                        .Subscribe(OnScanProgressChanged, onError: OnObservableError),
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

            UnloadPreviewsOutsideRange(
                Math.Max(0, firstVisibleIndex - ImagesPerLine * 2),
                Math.Min(ImagePreviews.Count - 1, lastVisibleIndex + ImagesPerLine * 2));
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

    private void UnloadPreviewsOutsideRange(int startIndex, int endIndex)
    {
        HashSet<MediaPreviewViewModel> retained = [];
        for (var index = startIndex; index <= endIndex; index++)
        {
            retained.Add(ImagePreviews[index]);
        }

        MediaPreviewViewModel[] toUnload;
        lock (_loadedPreviewsLock)
        {
            toUnload = _loadedPreviews.Where(preview => !retained.Contains(preview)).ToArray();
            foreach (var preview in toUnload)
            {
                _loadedPreviews.Remove(preview);
            }
        }

        foreach (var preview in toUnload)
        {
            preview.UnloadPreview();
        }
    }

    private void RebuildPreviewRows()
    {
        var itemsPerRow = Math.Max(1, ImagesPerLine);
        var rows = new IReadOnlyList<GlanceMediaPreviewViewModel>[
            (ImagePreviews.Count + itemsPerRow - 1) / itemsPerRow];
        for (var row = 0; row < rows.Length; row++)
        {
            var start = row * itemsPerRow;
            var count = Math.Min(itemsPerRow, ImagePreviews.Count - start);
            var items = new GlanceMediaPreviewViewModel[count];
            for (var column = 0; column < count; column++)
            {
                items[column] = ImagePreviews[start + column];
            }

            rows[row] = items;
        }

        PreviewRows = rows;
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

    private void ToggleSortDirection()
    {
        if (!CanChangeSort)
        {
            return;
        }

        _sortDirection = _sortDirection == MediaSortDirection.NewestFirst
            ? MediaSortDirection.OldestFirst
            : MediaSortDirection.NewestFirst;
        ImagePreviews.SetComparer(new CreationDateTimeComparer(_sortDirection));
        lock (_imagePathsLock)
        {
            _imagePaths?.SetComparer(new FileModelCreationDateTimeComparer(_sortDirection));
        }

        _configurationService.Configuration.Value.FolderOverviewSortDirection = _sortDirection;
        _configurationService.SaveConfiguration();
        RaisePropertyChanged(nameof(IsSortAscending));
        IsScrollResetRequested = true;
    }

    private void OnScanProgressChanged(ScanProgress progress)
    {
        _isStatisticsAnalysisComplete = progress.Status == ScanStatus.InitialScanCompleted;
        RaisePropertyChanged(nameof(CanChangeSort));
    }

    private void ResetAnalysisState()
    {
        _isPreviewAnalysisComplete = false;
        _isStatisticsAnalysisComplete = false;
        RaisePropertyChanged(nameof(CanChangeSort));
    }

    private void ClearPreviewPanel()
    {
        ResetAnalysisState();
        _imagePaths?.Clear();
        foreach (var preview in ImagePreviews)
        {
            preview.UnloadPreview();
        }

        lock (_loadedPreviewsLock)
        {
            _loadedPreviews.Clear();
        }

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
                _imagePaths = new SortedObservableCollection<FileModel>(
                    new FileModelCreationDateTimeComparer(_sortDirection));
            }

            var files = await _folderService.GetFileModelAsync(selectedFileSystemItem, "*");

            token.ThrowIfCancellationRequested();
            var pathComparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            var distinctFiles = files
                .Where(file => MediaFormat.IsSupportedExtension(System.IO.Path.GetExtension(file.FullName)))
                .GroupBy(file => file.FullName, pathComparer)
                .Select(group => group.First());
            lock (_imagePathsLock)
            {
                _imagePaths.AddRange(distinctFiles);
            }

            FilesLoading = false;
            _ = LoadInitialImagesAsync(_imageLoadCts.Token);
            _ = _folderStatisticsService.StartAsync(selectedFileSystemItem.Path, token);
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

    private async Task LoadInitialImagesAsync(CancellationToken token)
    {
        try
        {
            if (_imagePaths.Count == 0)
            {
                _isPreviewAnalysisComplete = true;
                RaisePropertyChanged(nameof(CanChangeSort));
                return;
            }

            var initialCount = Math.Min(ImagesPerLine * PreloadRowsCount, _imagePaths.Count);
            FileModel[] pathSnapshot;
            lock (_imagePathsLock)
            {
                pathSnapshot = _imagePaths.ToArray();
            }

            var batchSize = Math.Max(1, initialCount);
            for (var offset = 0; offset < pathSnapshot.Length;)
            {
                token.ThrowIfCancellationRequested();
                var count = Math.Min(batchSize, pathSnapshot.Length - offset);
                var chunk = new FileModel[count];
                Array.Copy(pathSnapshot, offset, chunk, 0, count);
                await LoadChunkAsync(chunk, token);

                if (offset == 0)
                {
                    for (var i = 0; i < count; i++)
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        _ = LoadImageAsync(i, token);
                    }
                }

                offset += count;
                batchSize = Math.Min(batchSize * 2, 4_096);
            }

            if (_imagePaths.Count <= 5_000)
            {
                _folderClusterizationService.StartAsync(SelectedFolderPath, token);
            }

            _isPreviewAnalysisComplete = true;
            RaisePropertyChanged(nameof(CanChangeSort));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Unexpected exception during loading initial images: {ex.Message}");
        }
    }

    private async Task LoadChunkAsync(FileModel[] chunk, CancellationToken token)
    {
        var previews = new ConcurrentBag<GlanceMediaPreviewViewModel>();

        await Parallel.ForEachAsync(
            chunk,
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token },
            async (fileModel, cancellationToken) =>
            {
                var previewImage = await _imageService.GetMediaPreviewAsync(
                    fileModel,
                    cancellationToken).ConfigureAwait(false);
                if (previewImage == null)
                {
                    return;
                }

                var mediaPreviewViewModel = _mapper.Map<GlanceMediaPreviewViewModel>(previewImage);
                mediaPreviewViewModel.FileDate = fileModel.CreatedDateTime ?? DateTime.MinValue;
                mediaPreviewViewModel.DateTimeString = mediaPreviewViewModel.FileDate.ToString("dd.MM.yyyy HH:mm");
                previews.Add(mediaPreviewViewModel);
            }).ConfigureAwait(false);

        token.ThrowIfCancellationRequested();
        var collectionUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _synchronizationContext.Post(
            _ =>
            {
                try
                {
                    var pathComparer = OperatingSystem.IsWindows()
                        ? StringComparer.OrdinalIgnoreCase
                        : StringComparer.Ordinal;
                    var existingPaths = ImagePreviews
                        .Select(existing => existing.Url)
                        .ToHashSet(pathComparer);
                    var newPreviews = previews
                        .Where(preview => existingPaths.Add(preview.Url))
                        .ToArray();
                    ImagePreviews.AddRange(newPreviews);
                }
                finally
                {
                    collectionUpdate.SetResult();
                }
            },
            null);
        await collectionUpdate.Task.WaitAsync(token);
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
            var mediaPreview = await _imageService.GetMediaPreviewAsync(imagePath, token);

            if (mediaPreview == null)
            {
                return;
            }

            var metadata = await _imageService.GetMediaMetadataAsync(mediaPreview, token);
            previewVm.MetadataString = metadata.GetString();
            previewVm.DateTimeString = metadata.CreationDateTime.ToString("dd.MM.yyyy HH:mm");
            previewVm.Metadata = metadata;
            previewVm.RotateAngle = metadata.Orientation.ToRotationAngle();

            await previewVm.LoadPreviewAsync(token);
            if (previewVm.PreviewBitmap != null)
            {
                lock (_loadedPreviewsLock)
                {
                    _loadedPreviews.Add(previewVm);
                }
            }
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
            var representativeFilePath = TimelineVm.GetRepresentativeFilePath(dateTime);
            GlanceMediaPreviewViewModel? targetPreview = null;
            if (!string.IsNullOrWhiteSpace(representativeFilePath))
            {
                targetPreview = ImagePreviews.FirstOrDefault(
                    preview => PathsEqual(preview.Url, representativeFilePath));
                if (targetPreview == null)
                {
                    await CreateImagePreviewFromPathAsync(representativeFilePath, false);
                    targetPreview = ImagePreviews.FirstOrDefault(
                        preview => PathsEqual(preview.Url, representativeFilePath));
                }
            }

            targetPreview ??= ImagePreviews.FirstOrDefault(
                preview => preview.FileDate.Date == dateTime.Date);
            if (targetPreview == null)
            {
                _logger.Warning("No overview image found for timeline date {Date}", dateTime.Date);
                return;
            }

            var index = ImagePreviews.IndexOf(targetPreview);
            SelectedPreview = targetPreview;
            IsScrollResetRequested = true;
            ScrollTargetRowIndex = -1;
            ScrollTargetRowIndex = index / Math.Max(1, ImagesPerLine);

            await LoadImageAsync(index, _imageLoadCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected when the overview closes or switches folders.
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Date selection failed: {Date}", dateTime);
        }
    }

    private void OnFileRenamed(FileRenamedModel model)
    {
        try
        {
            var oldPath = model.OldFileModel.FullName;
            var newPath = model.NewFileModel.FullName;
            if (MediaFormat.IsSupportedExtension(System.IO.Path.GetExtension(oldPath)))
            {
                RemoveFilePath(oldPath);
                RemoveImagePreviewByPath(oldPath);
            }

            if (MediaFormat.IsSupportedExtension(System.IO.Path.GetExtension(newPath)))
            {
                AddFilePath(model.NewFileModel);
                _ = CreateImagePreviewFromPathAsync(newPath, true);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unexpected exception during handling of file renaming: {Path}", model.OldFileModel.FullName);
        }
    }

    private void OnFilesDeleted(IList<FileModel> files)
    {
        try
        {
            var pathComparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            var deletedPaths = files.Select(file => file.FullName).ToHashSet(pathComparer);

            lock (_imagePathsLock)
            {
                _imagePaths.RemoveWhere(file => deletedPaths.Contains(file.FullName));
            }

            var selectedWasDeleted = SelectedPreview != null && deletedPaths.Contains(SelectedPreview.Url);
            var removedPreviews = ImagePreviews.Where(preview => deletedPaths.Contains(preview.Url)).ToArray();
            foreach (var preview in removedPreviews)
            {
                preview.UnloadPreview();
            }

            lock (_loadedPreviewsLock)
            {
                _loadedPreviews.ExceptWith(removedPreviews);
            }

            ImagePreviews.RemoveWhere(preview => deletedPaths.Contains(preview.Url));
            if (selectedWasDeleted)
            {
                SelectedPreview = ImagePreviews.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unexpected exception during batch file deletion handling");
        }
    }

    private void OnFilesCreated(IList<FileModel> files)
    {
        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var newFiles = files
            .Where(file => MediaFormat.IsSupportedExtension(System.IO.Path.GetExtension(file.FullName)))
            .Where(file => PathsEqual(System.IO.Path.GetDirectoryName(file.FullName), SelectedFolderPath))
            .GroupBy(file => file.FullName, pathComparer)
            .Select(group => group.Last())
            .ToArray();

        lock (_imagePathsLock)
        {
            var existingPaths = _imagePaths.Select(file => file.FullName).ToHashSet(pathComparer);
            _imagePaths.AddRange(newFiles.Where(file => existingPaths.Add(file.FullName)));
        }

        _ = ProcessCreatedFilesAsync(newFiles);
    }

    private async Task ProcessCreatedFilesAsync(IReadOnlyList<FileModel> files)
    {
        await Parallel.ForEachAsync(
            files,
            new ParallelOptions { MaxDegreeOfParallelism = 4 },
            async (file, _) => await CreateImagePreviewFromPathAsync(file.FullName, true));
    }

    private void OnFileOperationCompleted(MediaFileOperationCompleted operation)
    {
        if (operation.Kind is MediaFileOperationKind.Move or MediaFileOperationKind.Delete &&
            PathsEqual(System.IO.Path.GetDirectoryName(operation.SourcePath), SelectedFolderPath))
        {
            RemoveFilePath(operation.SourcePath);
            RemoveImagePreviewByPath(operation.SourcePath);
        }

        if (operation.Kind is MediaFileOperationKind.Copy or MediaFileOperationKind.Move or MediaFileOperationKind.Restore &&
            operation.DestinationPath is { } destinationPath &&
            MediaFormat.IsSupportedExtension(System.IO.Path.GetExtension(destinationPath)) &&
            PathsEqual(System.IO.Path.GetDirectoryName(destinationPath), SelectedFolderPath))
        {
            AddFilePath(new FileModel(System.IO.Path.GetFileName(destinationPath), destinationPath, null));
            _ = CreateImagePreviewFromPathAsync(destinationPath, true);
        }
    }

    private void AddFilePath(FileModel fileModel)
    {
        lock (_imagePathsLock)
        {
            if (_imagePaths.All(existing => !PathsEqual(existing.FullName, fileModel.FullName)))
            {
                _imagePaths.Add(fileModel);
            }
        }
    }

    private void RemoveFilePath(string filePath)
    {
        lock (_imagePathsLock)
        {
            var existing = _imagePaths.FirstOrDefault(file => PathsEqual(file.FullName, filePath));
            if (existing != null)
            {
                _imagePaths.Remove(existing);
            }
        }
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (left == null || right == null)
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            System.IO.Path.TrimEndingDirectorySeparator(left),
            System.IO.Path.TrimEndingDirectorySeparator(right),
            comparison);
    }

    private void OnObservableError(Exception ex)
    {
        _logger.Error(ex, "Error in observable subscription");
    }

    private async Task CreateImagePreviewFromPathAsync(string filePath, bool loadToTimeline)
    {
        try
        {
            if (!MediaFormat.IsSupportedExtension(System.IO.Path.GetExtension(filePath)))
            {
                return;
            }

            var imagePreview = await _imageService.GetMediaPreviewAsync(filePath);

            if (imagePreview == null)
            {
                return;
            }

            if (ImagePreviews.Any(preview => PathsEqual(preview.Url, filePath)))
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
            mediaPreviewViewModel.FileDate = metadata.CreationDateTime;
            mediaPreviewViewModel.MetadataString = metadata.GetString();
            mediaPreviewViewModel.DateTimeString = metadata.CreationDateTime.ToString("dd.MM.yyyy HH:mm");
            mediaPreviewViewModel.Metadata = metadata;
            mediaPreviewViewModel.RotateAngle = metadata.Orientation.ToRotationAngle();

            var added = false;
            _synchronizationContext.Send(
                d =>
                {
                    if (ImagePreviews.All(existing => !PathsEqual(existing.Url, mediaPreviewViewModel.Url)))
                    {
                        ImagePreviews.Add(mediaPreviewViewModel);
                        added = true;
                    }
                },
                null);

            if (added)
            {
                await mediaPreviewViewModel.LoadPreviewAsync(CancellationToken.None);
            }
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
            var imagePreviewViewModel = ImagePreviews.FirstOrDefault(vm => PathsEqual(vm.Url, filePath));
            if (imagePreviewViewModel != null)
            {
                var indexToRemove = ImagePreviews.IndexOf(imagePreviewViewModel);
                imagePreviewViewModel.UnloadPreview();
                lock (_loadedPreviewsLock)
                {
                    _loadedPreviews.Remove(imagePreviewViewModel);
                }

                ImagePreviews.Remove(imagePreviewViewModel);

                var indexToLoad = indexToRemove + PreloadRowsCount * ImagesPerLine;
                if (ImagePreviews.Count > indexToLoad)
                {
                    _ = LoadImageAsync(indexToLoad, _currentScrollCancellation.Token);
                }
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

    private void RefreshDeletedFiles(IReadOnlyList<DeletedMediaFile> deletedFiles)
    {
        ClearDeletedFiles();
        foreach (var deletedFile in deletedFiles)
        {
            var viewModel = new DeletedMediaFileViewModel(
                deletedFile,
                RestoreDeletedFileAsync,
                _imageService,
                _logger);
            DeletedFiles.Add(viewModel);
            _ = viewModel.LoadPreviewAsync();
        }

        HasDeletedFiles = DeletedFiles.Count > 0;
    }

    private void ClearDeletedFiles()
    {
        foreach (var deletedFile in DeletedFiles)
        {
            deletedFile.Dispose();
        }

        DeletedFiles.Clear();
        HasDeletedFiles = false;
    }

    private async Task RestoreDeletedFileAsync(DeletedMediaFile deletedFile)
    {
        try
        {
            var result = await _fileOperationsService.RestoreDeletedFileAsync(deletedFile.BufferPath);
            if (result == OperationResult.Success)
            {
                _notificationService.SendNotification(
                    new SuccessNotification(_localization["Delete_Restored"], System.IO.Path.GetFileName(deletedFile.OriginalPath)));
            }
            else
            {
                _notificationService.SendNotification(
                    new ErrorNotification(_localization["Delete_RestoreFailed"], _localization["Delete_RestoreConflict"]));
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to restore deleted file {Path}", deletedFile.OriginalPath);
            _notificationService.SendNotification(
                new ErrorNotification(_localization["Delete_RestoreFailed"], ex.Message));
        }
    }

    private async Task RestoreAllAsync()
    {
        try
        {
            IsRestoringAll = true;
            foreach (var deletedFile in _fileOperationsService.GetDeletedFiles().ToArray())
            {
                await RestoreDeletedFileAsync(deletedFile);
            }
        }
        finally
        {
            IsRestoringAll = false;
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