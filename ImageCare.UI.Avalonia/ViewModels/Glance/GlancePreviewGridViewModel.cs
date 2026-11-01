using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.FolderClusterizationService;
using ImageCare.Core.Services.MediaPreviewService;
using ImageCare.UI.Avalonia.Collections;
using ImageCare.UI.Avalonia.Controls;
using ImageCare.UI.Avalonia.ViewModels.Domain;
using ImageCare.UI.Avalonia.ViewModels.Preview;

using MapsterMapper;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Glance;

/// <summary>
/// Owns the virtualized media grid of the folder overview: the sorted preview collection,
/// row projection, adaptive preview loading, zoom/aspect ratio, scrolling and sorting.
/// </summary>
internal sealed class GlancePreviewGridViewModel : ViewModelBase
{
    private const int PreloadRowsCount = 10;

    private readonly IMediaPreviewService _imageService;
    private readonly IConfigurationService _configurationService;
    private readonly GlanceSelectionViewModel _selection;
    private readonly GlanceAnnotationCache _annotationCache;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly object _loadedPreviewsLock = new();
    private readonly HashSet<MediaPreviewViewModel> _loadedPreviews = [];
    private readonly LatestOperationCancellation _scrollCancellation = new();

    private CancellationTokenSource _imageLoadCts = new();
    private string _selectedFolderPath = string.Empty;
    private bool _filesLoading;
    private int _imagesPerLine = 3;
    private AspectRatioViewModel _selectedAspectRatio;
    private bool _isScrollResetRequested;
    private int _scrollTargetRowIndex = -1;
    private double _panelRowHeight;
    private MediaSortDirection _sortDirection;
    private IReadOnlyList<IReadOnlyList<GlanceMediaPreviewViewModel>> _previewRows = [];
    private bool _previewRowsRebuildPending;
    private int _lastVisibleStartIndex;
    private int _lastVisibleEndIndex = -1;

    public GlancePreviewGridViewModel(
        IMediaPreviewService imageService,
        IConfigurationService configurationService,
        GlanceSelectionViewModel selection,
        GlanceAnnotationCache annotationCache,
        IMapper mapper,
        ILogger logger,
        SynchronizationContext synchronizationContext)
    {
        _imageService = imageService;
        _configurationService = configurationService;
        _selection = selection;
        _annotationCache = annotationCache;
        _mapper = mapper;
        _logger = logger;
        _synchronizationContext = synchronizationContext;

        _sortDirection = configurationService.Configuration.Value.FolderOverviewSortDirection;
        ImagePreviews = new SortedObservableCollection<GlanceMediaPreviewViewModel>(
            new CreationDateTimeComparer(_sortDirection));
        ImagePreviews.CollectionChanged += (_, _) => SchedulePreviewRowsRebuild();

        ToggleSortDirectionCommand = CreateCommand(ToggleSortDirection);
        ZoomInCommand = CreateCommand(ZoomIn, CanZoomIn).ObservesProperty(() => ImagePreviews.Count);
        ZoomOutCommand = CreateCommand(ZoomOut, CanZoomOut).ObservesProperty(() => ImagePreviews.Count);

        AspectRatioViewModels = new List<AspectRatioViewModel>
        {
            new(AspectRatio.Ratio4x3, "4:3"),
            new(AspectRatio.Ratio16x9, "16:9"),
            new(AspectRatio.Ratio3x2, "3:2"),
            new(AspectRatio.Ratio1x1, "1:1")
        };
        _selectedAspectRatio = AspectRatioViewModels[0];
    }

    public event EventHandler<MediaSortDirection>? SortDirectionChanged;

    public SortedObservableCollection<GlanceMediaPreviewViewModel> ImagePreviews { get; }

    public IReadOnlyList<IReadOnlyList<GlanceMediaPreviewViewModel>> PreviewRows
    {
        get => _previewRows;
        private set => SetProperty(ref _previewRows, value);
    }

    public ICommand ToggleSortDirectionCommand { get; }

    public ICommand ZoomInCommand { get; }

    public ICommand ZoomOutCommand { get; }

    public ICommand SelectPreviewCommand => _selection.SelectPreviewCommand;

    public IList<AspectRatioViewModel> AspectRatioViewModels { get; }

    public AspectRatioViewModel SelectedAspectRatio
    {
        get => _selectedAspectRatio;
        set => SetProperty(ref _selectedAspectRatio, value);
    }

    public bool IsSortAscending => _sortDirection == MediaSortDirection.OldestFirst;

    public MediaSortDirection SortDirection => _sortDirection;

    public bool CanChangeSort => !FilesLoading &&
                                 ImagePreviews.Count > 0 &&
                                 !string.IsNullOrWhiteSpace(SelectedFolderPath);

    public string SelectedFolderPath
    {
        get => _selectedFolderPath;
        set
        {
            if (SetProperty(ref _selectedFolderPath, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(CanChangeSort));
            }
        }
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

    public int ImagesPerLine
    {
        get => _imagesPerLine;
        set
        {
            if (SetProperty(ref _imagesPerLine, value))
            {
                RebuildPreviewRows();
                ReloadVisiblePreviewsForCurrentScale();
            }
        }
    }

    public double PanelRowHeight
    {
        get => _panelRowHeight;
        set => SetProperty(ref _panelRowHeight, value);
    }

    // Used by VerticalScrollBehavior.
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

    public CancellationToken ImageLoadToken => _imageLoadCts.Token;

    public async Task HandleScrollAsync(double offsetY, double viewportHeight)
    {
        if (ImagePreviews.Count == 0 || PanelRowHeight <= 0)
        {
            return;
        }

        try
        {
            using var operation = _scrollCancellation.Begin();
            var token = operation.Token;
            var firstVisibleRow = (int)(offsetY / PanelRowHeight);
            var lastVisibleRow = (int)((offsetY + viewportHeight) / PanelRowHeight);

            firstVisibleRow = Math.Max(0, firstVisibleRow - 1);
            lastVisibleRow = Math.Min(GetTotalRows() - 1, lastVisibleRow + 1);

            var firstVisibleIndex = firstVisibleRow * ImagesPerLine;
            var lastVisibleIndex = Math.Min(
                ImagePreviews.Count - 1,
                (lastVisibleRow + 1) * ImagesPerLine - 1);

            _lastVisibleStartIndex = firstVisibleIndex;
            _lastVisibleEndIndex = lastVisibleIndex;
            var loadTasks = new List<Task>();
            var targetWidth = GetOverviewPreviewWidth();
            for (var i = firstVisibleIndex; i <= lastVisibleIndex; i++)
            {
                token.ThrowIfCancellationRequested();
                if (i >= 0 && i < ImagePreviews.Count &&
                    ImagePreviews[i].LoadedPreviewWidth < targetWidth)
                {
                    loadTasks.Add(LoadImageAsync(i, token));
                }
            }

            await Task.WhenAll(loadTasks);
            token.ThrowIfCancellationRequested();
            UnloadPreviewsOutsideRange(
                Math.Max(0, firstVisibleIndex - ImagesPerLine * 2),
                Math.Min(ImagePreviews.Count - 1, lastVisibleIndex + ImagesPerLine * 2));
        }
        catch (OperationCanceledException)
        {
            // Expected when a newer scroll request supersedes this one.
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error on scroll images");
        }
    }

    public async Task AppendPreviewsAsync(IReadOnlyList<FileModel> files, CancellationToken token)
    {
        var previews = new ConcurrentBag<GlanceMediaPreviewViewModel>();

        await Parallel.ForEachAsync(
            files,
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
                    var existingPaths = ImagePreviews
                        .Select(existing => existing.Url)
                        .ToHashSet(PreviewPathUtility.PathComparer);
                    var newPreviews = previews
                        .Where(preview => existingPaths.Add(preview.Url))
                        .ToArray();
                    foreach (var preview in newPreviews)
                    {
                        _annotationCache.ApplyCached(preview);
                    }

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

    public async Task LoadImageAsync(int indexInPreviews, CancellationToken token = default)
    {
        if (indexInPreviews < 0 || indexInPreviews >= ImagePreviews.Count)
        {
            return;
        }

        var previewVm = ImagePreviews[indexInPreviews];
        var targetWidth = GetOverviewPreviewWidth();
        if (previewVm.PreviewBitmap != null && previewVm.LoadedPreviewWidth >= targetWidth)
        {
            return;
        }

        try
        {
            token.ThrowIfCancellationRequested();

            if (previewVm.Metadata == null)
            {
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
            }

            await previewVm.LoadPreviewAsync(
                targetWidth,
                targetWidth > 512 ? MediaPreviewSize.Large : MediaPreviewSize.Medium,
                token);
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

    public async Task CreateImagePreviewFromPathAsync(string filePath)
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

            if (ImagePreviews.Any(preview => PreviewPathUtility.PathsEqual(preview.Url, filePath)))
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

    public void RemoveImagePreviewByPath(string filePath)
    {
        try
        {
            var imagePreviewViewModel = ImagePreviews.FirstOrDefault(vm =>
                PreviewPathUtility.PathsEqual(vm.Url, filePath));
            if (imagePreviewViewModel == null)
            {
                return;
            }

            var indexToRemove = ImagePreviews.IndexOf(imagePreviewViewModel);
            imagePreviewViewModel.UnloadPreview();
            lock (_loadedPreviewsLock)
            {
                _loadedPreviews.Remove(imagePreviewViewModel);
            }

            ImagePreviews.Remove(imagePreviewViewModel);
            _selection.RemoveMissingSelections();

            var indexToLoad = indexToRemove + PreloadRowsCount * ImagesPerLine;
            if (ImagePreviews.Count > indexToLoad)
            {
                _ = LoadImageAsync(indexToLoad);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Unexpected exception during removing image preview: {filePath}");
        }
    }

    public void Clear()
    {
        foreach (var preview in ImagePreviews)
        {
            preview.UnloadPreview();
        }

        lock (_loadedPreviewsLock)
        {
            _loadedPreviews.Clear();
        }

        ImagePreviews.Clear();
    }

    public void CancelImageLoading()
    {
        try
        {
            _scrollCancellation.Cancel();
            _imageLoadCts.Cancel();
            _imageLoadCts.Dispose();
            _imageLoadCts = new CancellationTokenSource();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to cancel image loading");
        }
    }

    public void ResetScrollToRow(int rowIndex)
    {
        IsScrollResetRequested = true;
        ScrollTargetRowIndex = -1;
        ScrollTargetRowIndex = rowIndex;
    }

    public void RefreshSortState() => RaisePropertyChanged(nameof(CanChangeSort));

    public void ApplyClusterColors(FileClustersStatistics statistics)
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
                _ =>
                {
                    if (ImagePreviews.All(existing =>
                            !PreviewPathUtility.PathsEqual(existing.Url, mediaPreviewViewModel.Url)))
                    {
                        _annotationCache.ApplyCached(mediaPreviewViewModel);
                        ImagePreviews.Add(mediaPreviewViewModel);
                        added = true;
                    }
                },
                null);

            if (added)
            {
                var targetWidth = GetOverviewPreviewWidth();
                await mediaPreviewViewModel.LoadPreviewAsync(
                    targetWidth,
                    targetWidth > 512 ? MediaPreviewSize.Large : MediaPreviewSize.Medium,
                    CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Error of adding image to preview: {previewImage.Url}");
        }
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
        SortDirectionChanged?.Invoke(this, _sortDirection);

        _configurationService.Configuration.Value.FolderOverviewSortDirection = _sortDirection;
        _configurationService.SaveConfiguration();
        RaisePropertyChanged(nameof(IsSortAscending));
        IsScrollResetRequested = true;
    }

    private void ZoomIn() => ImagesPerLine++;

    private void ZoomOut() => ImagesPerLine--;

    private bool CanZoomIn()
    {
        if (ImagesPerLine >= 20)
        {
            return false;
        }

        return ImagePreviews.Count != 0;
    }

    private bool CanZoomOut()
    {
        if (ImagesPerLine <= 1)
        {
            return false;
        }

        return ImagePreviews.Count != 0;
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

    private void SchedulePreviewRowsRebuild()
    {
        if (_previewRowsRebuildPending)
        {
            return;
        }

        _previewRowsRebuildPending = true;
        _synchronizationContext.Post(
            _ =>
            {
                _previewRowsRebuildPending = false;
                RebuildPreviewRows();
            },
            null);
    }

    private void ReloadVisiblePreviewsForCurrentScale()
    {
        if (ImagePreviews.Count == 0)
        {
            return;
        }

        var first = Math.Clamp(_lastVisibleStartIndex, 0, ImagePreviews.Count - 1);
        var fallbackEnd = Math.Min(ImagePreviews.Count - 1, ImagesPerLine * PreloadRowsCount - 1);
        var last = _lastVisibleEndIndex < first
            ? fallbackEnd
            : Math.Min(_lastVisibleEndIndex, ImagePreviews.Count - 1);
        for (var index = first; index <= last; index++)
        {
            if (ImagePreviews[index].LoadedPreviewWidth < GetOverviewPreviewWidth())
            {
                _ = LoadImageAsync(index, _imageLoadCts.Token);
            }
        }
    }

    private int GetOverviewPreviewWidth() => ImagesPerLine switch
    {
        <= 1 => 1_536,
        <= 3 => 1_024,
        <= 6 => 768,
        _ => 512
    };

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

    private int GetTotalRows()
    {
        if (ImagePreviews.Count == 0 || ImagesPerLine <= 0)
        {
            return 0;
        }

        return (int)Math.Ceiling((double)ImagePreviews.Count / ImagesPerLine);
    }
}
