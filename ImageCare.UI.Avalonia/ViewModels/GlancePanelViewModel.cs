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

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Media;
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
    private readonly LocalizationManager _localization;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly object _imagePathsLock = new();
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
    private double _panelRowHeight;
    private bool _hasDeletedFiles;
    private bool _isRestoringAll;

    public GlancePanelViewModel(IFolderStatisticsService folderStatisticsService,
                                IFolderClusterizationService folderClusterizationService,
                                IMediaPreviewService imageService,
                                IFolderService folderService,
                                IFileSystemWatcherService fileSystemWatcherService,
                                IMediaPreviewOperationsService fileOperationsService,
                                IConfigurationService configurationService,
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
        _localization = localization;
        _mapper = mapper;
        _logger = logger;
        _synchronizationContext = synchronizationContext;
        RequestClose = requestClose;

        _imageLoadCts = new CancellationTokenSource();
        ImagePreviews = new SortedObservableCollection<GlanceMediaPreviewViewModel>(new CreationDateTimeDescendingComparer());
        DeletedFiles = new ObservableCollection<DeletedMediaFileViewModel>();
        TimelineVm = new TimelineViewModel(_folderStatisticsService, configurationService, _synchronizationContext);

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
                _imagePaths = new SortedObservableCollection<FileModel>(new FileModelCreationDateTimeDescendingComparer());
            }

            var files = await _folderService.GetFileModelAsync(selectedFileSystemItem, "*");

            token.ThrowIfCancellationRequested();
            var pathComparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            var distinctFiles = files
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
                    await LoadChunkAsync(chunk, token);
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
                    await LoadChunkAsync(chunk, token);
                }
            }

            _folderClusterizationService.StartAsync(SelectedFolderPath, token);
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
                var previewImage = await _imageService.GetMediaPreviewAsync(fileModel.FullName).ConfigureAwait(false);
                if (previewImage == null)
                {
                    return;
                }

                var metadata = await _imageService.GetMediaMetadataAsync(previewImage).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                var mediaPreviewViewModel = _mapper.Map<GlanceMediaPreviewViewModel>(previewImage);
                mediaPreviewViewModel.FileDate = metadata.CreationDateTime;
                mediaPreviewViewModel.MetadataString = metadata.GetString();
                mediaPreviewViewModel.DateTimeString = metadata.CreationDateTime.ToString("dd.MM.yyyy HH:mm");
                mediaPreviewViewModel.Metadata = metadata;
                mediaPreviewViewModel.RotateAngle = metadata.Orientation.ToRotationAngle();
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
            RemoveFilePath(model.OldFileModel.FullName);
            RemoveImagePreviewByPath(model.OldFileModel.FullName);
            AddFilePath(model.NewFileModel);
            _ = CreateImagePreviewFromPathAsync(model.NewFileModel.FullName, true);
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
        DeletedFiles.Clear();
        foreach (var deletedFile in deletedFiles)
        {
            DeletedFiles.Add(new DeletedMediaFileViewModel(deletedFile, RestoreDeletedFileAsync));
        }

        HasDeletedFiles = DeletedFiles.Count > 0;
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