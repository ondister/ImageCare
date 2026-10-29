using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
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
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.FolderClusterizationService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;
using ImageCare.UI.Avalonia.Behaviors;
using ImageCare.UI.Avalonia.Collections;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using MapsterMapper;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Preview;

/// <summary>
/// Owns the horizontal preview strip: the sorted card collection, lazy bitmap loading,
/// unloading of off-screen cards, and sort direction.
/// </summary>
internal sealed class PreviewImageStripViewModel : ViewModelBase
{
    // Desired size of item
    private const int MaxItemWidth = 286;

    /// <summary>Number of cards kept loaded on either side of the viewport.</summary>
    public const int PreloadCount = 20;

    private readonly IMediaPreviewService _imageService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IMediaPreviewOperationsService _fileOperationsService;
    private readonly IConfigurationService _configurationService;
    private readonly PreviewSelectionViewModel _selection;
    private readonly PreviewAnnotationCache _annotationCache;
    private readonly ImagePreviewDropHandler _imagePreviewDropHandler;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly Func<CancellationToken> _folderToken;

    private readonly object _loadedPreviewsLock = new();
    private readonly HashSet<MediaPreviewViewModel> _loadedPreviews = [];
    private readonly LatestOperationCancellation _scrollCancellation = new();

    private bool _isScrollResetRequested;
    private bool _filesLoading;
    private MediaSortDirection _sortDirection = MediaSortDirection.NewestFirst;
    private string _selectedFolderPath = string.Empty;
    private int _indexInView;
    private string? _navigationTargetPath;
    private bool _isPreviewAnalysisComplete;

    public PreviewImageStripViewModel(
        IMediaPreviewService imageService,
        IFileSystemService fileSystemService,
        IMediaPreviewOperationsService fileOperationsService,
        IConfigurationService configurationService,
        PreviewSelectionViewModel selection,
        PreviewAnnotationCache annotationCache,
        ImagePreviewDropHandler imagePreviewDropHandler,
        IMapper mapper,
        ILogger logger,
        SynchronizationContext synchronizationContext,
        Func<CancellationToken> folderToken)
    {
        _imageService = imageService;
        _fileSystemService = fileSystemService;
        _fileOperationsService = fileOperationsService;
        _configurationService = configurationService;
        _selection = selection;
        _annotationCache = annotationCache;
        _imagePreviewDropHandler = imagePreviewDropHandler;
        _mapper = mapper;
        _logger = logger;
        _synchronizationContext = synchronizationContext;
        _folderToken = folderToken;

        ImagePreviews = new SortedObservableCollection<MediaPreviewViewModel>(
            new CreationDateTimeComparer(_sortDirection));
        ImagePreviews.CollectionChanged += OnImagePreviewsChanged;
        _selection.PrimarySelectionChanged += OnPrimarySelectionChanged;

        ToggleSortDirectionCommand = CreateCommand(ToggleSortDirection);
    }

    public event EventHandler<MediaSortDirection>? SortDirectionChanged;

    public SortedObservableCollection<MediaPreviewViewModel> ImagePreviews { get; }

    public ImagePreviewDropHandler ImagePreviewDropHandler => _imagePreviewDropHandler;

    public FileManagerPanel FileManagerPanel { get; set; }

    // Used by HorizontalScrollBehavior
    public bool IsScrollResetRequested
    {
        get => _isScrollResetRequested;
        set => SetProperty(ref _isScrollResetRequested, value);
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

    public bool IsSortAscending => _sortDirection == MediaSortDirection.OldestFirst;

    public MediaSortDirection SortDirection => _sortDirection;

    public bool CanChangeSort => !FilesLoading &&
                                 ImagePreviews.Count > 0 &&
                                 !string.IsNullOrWhiteSpace(SelectedFolderPath);

    /// <summary>Re-evaluates whether sorting can be changed (for example after an analysis pass).</summary>
    public void RefreshSortState() => RaisePropertyChanged(nameof(CanChangeSort));

    public int IndexInView
    {
        get => _indexInView;
        set
        {
            _indexInView = value;
            RaisePropertyChanged(nameof(IndexInView));
        }
    }

    public ICommand ToggleSortDirectionCommand { get; }

    public MediaPreviewViewModel? SelectedPreview => _selection.SelectedPreview;

    public IReadOnlyList<MediaPreviewViewModel> SelectedPreviews => _selection.SelectedPreviews;

    public void UpdateSelection(
        IReadOnlyList<MediaPreviewViewModel> selectedPreviews,
        MediaPreviewViewModel? primary = null) =>
        _selection.UpdateSelection(selectedPreviews, primary);

    public void ApplySavedSortDirection()
    {
        var configuration = _configurationService.Configuration.Value;
        var direction = FileManagerPanel switch
        {
            FileManagerPanel.Left => configuration.LeftPreviewSortDirection,
            FileManagerPanel.Right => configuration.RightPreviewSortDirection,
            _ => MediaSortDirection.NewestFirst
        };
        ApplySortDirection(direction, save: false);
    }

    public async Task HandleScrollAsync(double horizontalOffset, double viewportWidth)
    {
        try
        {
            using var operation = _scrollCancellation.Begin();
            var token = operation.Token;
            var totalContentCount = ImagePreviews.Count * MaxItemWidth;

            var firstVisibleIndex = (int)(horizontalOffset / MaxItemWidth);
            var lastVisibleIndex = (int)((horizontalOffset + viewportWidth) / MaxItemWidth);

            const int epsilon = 2;

            firstVisibleIndex = Math.Max(0, firstVisibleIndex - epsilon);
            lastVisibleIndex = Math.Min(ImagePreviews.Count - 1, lastVisibleIndex + epsilon);

            var loadStartIndex = Math.Max(0, firstVisibleIndex - PreloadCount);
            var loadEndIndex = Math.Min(ImagePreviews.Count - 1, lastVisibleIndex + PreloadCount);

            var scrollToEndThreshold = viewportWidth * 0.9;
            if (horizontalOffset + viewportWidth >= totalContentCount - scrollToEndThreshold)
            {
                loadEndIndex = ImagePreviews.Count - 1;
                loadStartIndex = Math.Max(0, ImagePreviews.Count - 20);
            }

            for (var i = loadStartIndex; i <= loadEndIndex; i++)
            {
                token.ThrowIfCancellationRequested();

                if (ImagePreviews[i].PreviewBitmap == null)
                {
                    await LoadImageAsync(i, token);
                }
            }

            token.ThrowIfCancellationRequested();
            UnloadPreviewsOutsideRange(
                Math.Max(0, loadStartIndex - PreloadCount),
                Math.Min(ImagePreviews.Count - 1, loadEndIndex + PreloadCount));
        }
        catch (OperationCanceledException)
        {
            // Expected when a newer scroll request supersedes this one.
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Scroll handling error");
        }
    }

    public async Task AppendPreviewsAsync(IReadOnlyList<FileModel> files, CancellationToken token)
    {
        var previews = new ConcurrentBag<MediaPreviewViewModel>();

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

                var mediaPreviewViewModel = _mapper.Map<MediaPreviewViewModel>(previewImage);
                _annotationCache.ApplyCached(mediaPreviewViewModel);
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
                    ImagePreviews.AddRange(newPreviews);
                    TrySelectNavigationTarget(finalize: false);
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

    public async Task AddImagePreviewAsync(MediaPreview previewImage)
    {
        try
        {
            var mediaPreviewViewModel = _mapper.Map<MediaPreviewViewModel>(previewImage);
            _annotationCache.ApplyCached(mediaPreviewViewModel);

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
                    if (!_fileSystemService.FileExists(mediaPreviewViewModel.Url) ||
                        ImagePreviews.Any(existing =>
                            PreviewPathUtility.PathsEqual(existing.Url, mediaPreviewViewModel.Url)))
                    {
                        return;
                    }

                    ImagePreviews.Add(mediaPreviewViewModel);
                    added = true;
                    var index = ImagePreviews.IndexOf(mediaPreviewViewModel);
                    IndexInView = -1;
                    IndexInView = index;
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

    public async Task CreateImagePreviewFromPathAsync(string filePath)
    {
        try
        {
            if (!MediaFormat.IsSupportedExtension(Path.GetExtension(filePath)) ||
                !_fileSystemService.FileExists(filePath))
            {
                return;
            }

            var imagePreview = await _imageService.GetMediaPreviewAsync(filePath);

            if (imagePreview == null || !_fileSystemService.FileExists(filePath))
            {
                return;
            }

            if (ImagePreviews.Any(preview =>
                    preview.Url.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            await AddImagePreviewAsync(imagePreview);
            if (imagePreview.MediaFormat.SupportsAnnotations)
            {
                _ = _annotationCache.RefreshChangedAsync(filePath);
            }
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
            // Guard against spurious watcher events: an atomic replace (temporary file moved
            // over the original, as happens when XMP is embedded into a JPEG) can surface as a
            // Deleted notification even though the media file still exists. Removing the
            // preview in that case would make the image vanish from the panel.
            if (_fileSystemService.FileExists(filePath))
            {
                return;
            }

            var imagePreviewViewModel = ImagePreviews.FirstOrDefault(preview =>
                PreviewPathUtility.PathsEqual(preview.Url, filePath));
            if (imagePreviewViewModel == null)
            {
                return;
            }

            var indexToRemove = ImagePreviews.IndexOf(imagePreviewViewModel);
            var wasSelected = ReferenceEquals(_selection.SelectedPreview, imagePreviewViewModel);
            imagePreviewViewModel.UnloadPreview();
            lock (_loadedPreviewsLock)
            {
                _loadedPreviews.Remove(imagePreviewViewModel);
            }

            ImagePreviews.Remove(imagePreviewViewModel);

            if (wasSelected)
            {
                _selection.SelectedPreview = ImagePreviews.Count == 0
                    ? null
                    : ImagePreviews[Math.Min(indexToRemove, ImagePreviews.Count - 1)];
            }

            if (ImagePreviews.Count == 0 &&
                _fileOperationsService.GetLastSelectedMediaPreview() is SelectedMediaPreview current &&
                current.FileManagerPanel == FileManagerPanel)
            {
                _fileOperationsService.SetSelectedPreview(
                    new SelectedMediaPreview(MediaPreview.Empty, FileManagerPanel));
            }

            var indexToLoad = indexToRemove + PreloadCount;
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

    public void ClearPreviews()
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
        _selection.SelectedPreview = null;
    }

    public void CompleteInitialLoad()
    {
        _isPreviewAnalysisComplete = true;
        RaisePropertyChanged(nameof(CanChangeSort));
        _synchronizationContext.Post(_ => TrySelectNavigationTarget(finalize: true), null);
    }

    public void NavigateToPreview(SelectedMediaPreview requestedPreview)
    {
        if (requestedPreview.FileManagerPanel != FileManagerPanel ||
            string.IsNullOrWhiteSpace(requestedPreview.Url))
        {
            return;
        }

        _navigationTargetPath = requestedPreview.Url;
        var targetDirectory = Path.GetDirectoryName(requestedPreview.Url);
        if (!string.IsNullOrWhiteSpace(targetDirectory) &&
            !string.IsNullOrWhiteSpace(SelectedFolderPath) &&
            PreviewPathUtility.PathsEqual(targetDirectory, SelectedFolderPath))
        {
            TrySelectNavigationTarget(_isPreviewAnalysisComplete);
        }
    }

    public void HandleExternalPreviewSelected(SelectedMediaPreview selectedImagePreview)
    {
        if (selectedImagePreview.FileManagerPanel != FileManagerPanel)
        {
            _selection.SelectedPreview = null;
        }
    }

    public async Task HandleTimelineDateSelectedAsync(DateTime dateTime)
    {
        try
        {
            using var operation = _scrollCancellation.Begin();
            var token = operation.Token;
            var targetPreview = ImagePreviews.FirstOrDefault(preview =>
                preview.FileDate.Date == dateTime.Date);

            if (targetPreview == null)
            {
                return;
            }

            var index = ImagePreviews.IndexOf(targetPreview);

            // Load range nearby item
            var start = Math.Max(0, index - PreloadCount / 2);
            var end = Math.Min(ImagePreviews.Count - 1, index + PreloadCount / 2);

            await LoadImageAsync(index, token);
            _selection.SelectedPreview = targetPreview;

            var leftLineTask = Task.Run(
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
            var rightLineTask = Task.Run(
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

            await Task.WhenAll(leftLineTask, rightLineTask);
        }
        catch (OperationCanceledException)
        {
            // Expected when scrolling or a newer timeline selection supersedes this request.
        }
        catch (Exception ex)
        {
            _logger.Error($"Date selection failed: {dateTime}", ex);
        }
    }

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

    public void CancelPendingOperations()
    {
        _scrollCancellation.Cancel();
        _navigationTargetPath = null;
    }

    /// <summary>
    /// Drops a pending navigation target when the user selects a different folder.
    /// </summary>
    public void ClearNavigationTargetOutside(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(_navigationTargetPath))
        {
            return;
        }

        var navigationDirectory = Path.GetDirectoryName(_navigationTargetPath);
        if (!string.IsNullOrWhiteSpace(navigationDirectory) &&
            !PreviewPathUtility.PathsEqual(navigationDirectory, folderPath))
        {
            _navigationTargetPath = null;
        }
    }

    private void OnPrimarySelectionChanged(object? sender, EventArgs args) =>
        RaisePropertyChanged(nameof(SelectedPreview));

    private void OnImagePreviewsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(CanChangeSort));
        if (_selection.SelectedPreviews.Count == 0)
        {
            return;
        }

        var remaining = _selection.SelectedPreviews.Where(ImagePreviews.Contains).ToArray();
        if (remaining.Length == _selection.SelectedPreviews.Count)
        {
            return;
        }

        var primary = _selection.SelectedPreview is { } selected && remaining.Contains(selected)
            ? selected
            : remaining.LastOrDefault();
        _selection.UpdateSelection(remaining, primary);
    }

    private void ToggleSortDirection()
    {
        if (!CanChangeSort)
        {
            return;
        }

        var direction = _sortDirection == MediaSortDirection.NewestFirst
            ? MediaSortDirection.OldestFirst
            : MediaSortDirection.NewestFirst;
        ApplySortDirection(direction, save: true);
    }

    private void ApplySortDirection(MediaSortDirection direction, bool save)
    {
        _sortDirection = direction;
        ImagePreviews.SetComparer(new CreationDateTimeComparer(direction));
        SortDirectionChanged?.Invoke(this, direction);

        RaisePropertyChanged(nameof(IsSortAscending));
        IsScrollResetRequested = true;

        if (!save)
        {
            return;
        }

        var configuration = _configurationService.Configuration.Value;
        if (FileManagerPanel == FileManagerPanel.Left)
        {
            configuration.LeftPreviewSortDirection = direction;
        }
        else if (FileManagerPanel == FileManagerPanel.Right)
        {
            configuration.RightPreviewSortDirection = direction;
        }

        _configurationService.SaveConfiguration();
    }

    private void TrySelectNavigationTarget(bool finalize)
    {
        if (string.IsNullOrWhiteSpace(_navigationTargetPath))
        {
            return;
        }

        var target = ImagePreviews.FirstOrDefault(preview =>
            PreviewPathUtility.PathsEqual(preview.Url, _navigationTargetPath));
        if (target != null)
        {
            var index = ImagePreviews.IndexOf(target);
            if (!ReferenceEquals(_selection.SelectedPreview, target))
            {
                _selection.SelectedPreview = target;
                _ = LoadImageAsync(index, _folderToken());
            }

            IndexInView = -1;
            IndexInView = index;
        }

        if (finalize)
        {
            _navigationTargetPath = null;
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
}
