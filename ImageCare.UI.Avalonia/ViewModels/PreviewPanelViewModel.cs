using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Domain.Notification;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.FileSystemWatcherService;
using ImageCare.Core.Services.FolderClusterizationService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.FolderStatisticsService;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.UI.Avalonia.Behaviors;
using ImageCare.UI.Avalonia.Collections;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using MapsterMapper;

using Prism.Dialogs;
using Prism.Navigation.Regions;

using Serilog;

using Notification = ImageCare.Core.Domain.Notification.Notification;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class PreviewPanelViewModel : NavigatedViewModelBase
{
    // Desired size of item
    private const int MaxItemWidth = 286;
    private const int PreloadCount = 20;

    private readonly IMediaPreviewService _imageService;
    private readonly IFolderService _folderService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IFileSystemWatcherService _fileSystemWatcherService;
    private readonly IMediaPreviewOperationsService _fileOperationsService;
    private readonly FileTransferCoordinator _fileTransferCoordinator;
    private readonly IFolderStatisticsService _folderStatisticsService;
    private readonly IFolderClusterizationService _folderClusterizationService;
    private readonly INotificationService _notificationService;
    private readonly IConfigurationService _configurationService;
    private readonly IMediaAnnotationsService _annotationsService;
    private readonly LocalizationManager _localization;
    private readonly IDialogService _dialogService;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;

    private readonly object _imagePathsLock = new();
    private readonly object _loadedPreviewsLock = new();
    private readonly HashSet<MediaPreviewViewModel> _loadedPreviews = [];
    private MediaPreviewViewModel? _selectedPreview;
    private CompositeDisposable? _disposable;

    private CancellationTokenSource _folderSelectedCancellationTokenSource;
    private CancellationTokenSource? _selectedAnnotationCancellation;
    private SortedObservableCollection<FileModel> _imagePaths = new();
    private readonly LatestOperationCancellation _scrollCancellation = new();

    private bool _isScrollResetRequested;
    private bool _filesLoading;
    private bool _isPreviewAnalysisComplete;
    private bool _isStatisticsAnalysisComplete;
    private MediaSortDirection _sortDirection = MediaSortDirection.NewestFirst;
    private SelectedDirectory? _selectedDirectory;
    private int _indexInView;
    private Dictionary<string, IndexedMediaAnnotations> _annotationsByPath = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private int _selectedRating;
    private string _newTag = string.Empty;
    private string[] _allAvailableAnnotationTags = [];
    private MediaPreviewViewModel? _annotationPreview;
    private string _annotationEditorStatus = string.Empty;
    private bool _isSavingAnnotations;
    private bool _isLoadingSelectedAnnotations;
    private string? _navigationTargetPath;

    public PreviewPanelViewModel(IMediaPreviewService imageService,
                                 IFolderService folderService,
                                 IFileSystemService fileSystemService,
                                 IFileSystemWatcherService fileSystemWatcherService,
                                 IMediaPreviewOperationsService fileOperationsService,
                                 FileTransferCoordinator fileTransferCoordinator,
                                 IFolderStatisticsService folderStatisticsService,
                                 IFolderClusterizationService folderClusterizationService,
                                 INotificationService notificationService,
                                 IConfigurationService configurationService,
                                 IMediaAnnotationsService annotationsService,
                                 LocalizationManager localization,
                                 TrayAnalysisProgressService trayAnalysisProgressService,
                                 IDialogService dialogService,
                                 IMapper mapper,
                                 ILogger logger,
                                 ImagePreviewDropHandler imagePreviewDropHandler,
                                 SynchronizationContext synchronizationContext)
    {
        _imageService = imageService;
        _folderService = folderService;
        _fileSystemService = fileSystemService;
        _fileSystemWatcherService = fileSystemWatcherService;
        _fileOperationsService = fileOperationsService;
        _fileTransferCoordinator = fileTransferCoordinator;
        _folderStatisticsService = folderStatisticsService;
        _folderClusterizationService = folderClusterizationService;
        _notificationService = notificationService;
        _configurationService = configurationService;
        _annotationsService = annotationsService;
        _localization = localization;
        _dialogService = dialogService;
        _mapper = mapper;
        _logger = logger;
        _synchronizationContext = synchronizationContext;

        ImagePreviewDropHandler = imagePreviewDropHandler;
        CopyImagePreviewCommand = CreateAsyncCommand(CopyImagePreviewAsync);
        MoveImagePreviewCommand = CreateAsyncCommand(MoveImagePreviewAsync);
        DeleteImagePreviewCommand = CreateAsyncCommand(DeleteImagePreview);
        ToggleSortDirectionCommand = CreateCommand(ToggleSortDirection);
        SaveRatingCommand = CreateAsyncCommand(SaveRatingAsync, () => CanEditAnnotations)
            .ObservesProperty(() => CanEditAnnotations);
        AddTagCommand = CreateAsyncCommand(AddTagAsync, () => CanEditAnnotations && !string.IsNullOrWhiteSpace(MediaAnnotations.NormalizeTag(NewTag)))
            .ObservesProperty(() => CanEditAnnotations)
            .ObservesProperty(() => NewTag);
        RemoveTagCommand = CreateAsyncCommand<string>(RemoveTagAsync);

        ImagePreviews = new SortedObservableCollection<MediaPreviewViewModel>(
            new CreationDateTimeComparer(_sortDirection));

        _folderSelectedCancellationTokenSource = new CancellationTokenSource();

        TimelineVm = new TimelineViewModel(
            _folderStatisticsService,
            configurationService,
            trayAnalysisProgressService,
            _synchronizationContext);
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
        set
        {
            if (SetProperty(ref _filesLoading, value))
            {
                RaisePropertyChanged(nameof(CanChangeSort));
            }
        }
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
            _selectedAnnotationCancellation?.Cancel();
            SetSelectedAnnotationLoading(false);
            UpdateAnnotationEditor();
            RaisePropertyChanged(nameof(CanEditAnnotations));
            RaisePropertyChanged(nameof(HasAnnotatableSelection));
            if (_selectedPreview != null)
            {
                _selectedPreview.Selected = true;

                _fileOperationsService.SetSelectedPreview(new SelectedMediaPreview(_mapper.Map<MediaPreview>(_selectedPreview), FileManagerPanel));
                if (_selectedPreview.MediaFormat.SupportsAnnotations)
                {
                    var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        _folderSelectedCancellationTokenSource.Token);
                    _selectedAnnotationCancellation = cancellation;
                    SetSelectedAnnotationLoading(true);
                    _ = LoadSelectedAnnotationSnapshotAsync(_selectedPreview, cancellation);
                }
            }
        }
    }

    public string SelectedFolderPath { get; set; } = string.Empty;

    public TimelineViewModel TimelineVm { get; }

    public ICommand CopyImagePreviewCommand { get; }

    public ICommand MoveImagePreviewCommand { get; }

    public ICommand DeleteImagePreviewCommand { get; }

    public ICommand ToggleSortDirectionCommand { get; }

    public int SelectedRating
    {
        get => _selectedRating;
        set
        {
            var clamped = Math.Clamp(value, 0, 5);
            if (!SetProperty(ref _selectedRating, clamped))
            {
                return;
            }

            var preview = SelectedPreview;
            if (preview != null &&
                ReferenceEquals(preview, _annotationPreview) &&
                preview.MediaFormat.SupportsAnnotations &&
                preview.AnnotationSnapshot != null &&
                !IsSavingAnnotations &&
                !_isLoadingSelectedAnnotations)
            {
                _ = SaveSelectedAnnotationsAsync(clamped, CurrentTags, CancellationToken.None);
            }
        }
    }

    public string NewTag
    {
        get => _newTag;
        set
        {
            if (SetProperty(ref _newTag, value ?? string.Empty))
            {
                _synchronizationContext.Post(_ => ApplyAvailableAnnotationTagFilter(), null);
            }
        }
    }

    public bool HasCurrentTags => CurrentTags.Count > 0;

    /// <summary>
    /// Adds whatever is typed in the tag field. Used when the user presses Enter or clicks the add button.
    /// </summary>
    public void AddTagInput()
    {
        var normalized = MediaAnnotations.NormalizeTag(NewTag);
        if (normalized.Length == 0)
        {
            return;
        }

        _ = AddTagValueAsync(normalized);
    }

    public void RemoveTag(string tag) => _ = RemoveTagAsync(tag);

    public void AddExistingTag(string? tag)
    {
        var normalized = MediaAnnotations.NormalizeTag(tag ?? string.Empty);
        if (normalized.Length == 0)
        {
            return;
        }

        _ = AddExistingTagCoreAsync(normalized);
    }

    private async Task AddExistingTagCoreAsync(string normalized)
    {
        await AddTagValueAsync(normalized);
        NewTag = string.Empty;
    }

    private async Task AddTagValueAsync(string normalized)
    {
        var preview = SelectedPreview;
        if (preview == null ||
            !ReferenceEquals(preview, _annotationPreview) ||
            !preview.MediaFormat.SupportsAnnotations ||
            preview.AnnotationSnapshot == null)
        {
            return;
        }

        await SaveSelectedAnnotationsAsync(SelectedRating, CurrentTags.Append(normalized), CancellationToken.None);
        NewTag = string.Empty;
    }

    public string AnnotationEditorStatus
    {
        get => _annotationEditorStatus;
        private set => SetProperty(ref _annotationEditorStatus, value);
    }

    public bool IsSavingAnnotations
    {
        get => _isSavingAnnotations;
        private set
        {
            if (SetProperty(ref _isSavingAnnotations, value))
            {
                RaisePropertyChanged(nameof(CanEditAnnotations));
                RaisePropertyChanged(nameof(HasAnnotatableSelection));
            }
        }
    }

    public bool CanEditAnnotations => HasAnnotatableSelection &&
                                      !IsSavingAnnotations &&
                                      !_isLoadingSelectedAnnotations &&
                                      SelectedPreview?.AnnotationSnapshot != null;

    public bool HasAnnotatableSelection => SelectedPreview?.MediaFormat.SupportsAnnotations == true;

    public IReadOnlyList<string> CurrentTags => SelectedPreview?.Tags ?? Array.Empty<string>();

    public System.Collections.ObjectModel.ObservableCollection<string> AvailableAnnotationTags { get; } = new();

    public bool HasAvailableAnnotationTags => AvailableAnnotationTags.Count > 0;

    public ICommand SaveRatingCommand { get; }

    public ICommand AddTagCommand { get; }

    public ICommand RemoveTagCommand { get; }

    public bool IsSortAscending => _sortDirection == MediaSortDirection.OldestFirst;

    public bool CanChangeSort => !FilesLoading &&
                                 ImagePreviews.Count > 0 &&
                                 !string.IsNullOrWhiteSpace(SelectedFolderPath);

    public int IndexInView
    {
        get => _indexInView;
        set
        {
            _indexInView = value;
            RaisePropertyChanged(nameof(IndexInView));
        }
    }

    /// <inheritdoc />
    public override void OnNavigatedTo(NavigationContext navigationContext)
    {
        FileManagerPanel = navigationContext.Parameters["panel"] is FileManagerPanel panel
            ? panel
            : FileManagerPanel.Undefined;
        ApplySortDirection(GetSavedSortDirection(), save: false);

        _disposable?.Dispose();
        _folderSelectedCancellationTokenSource.Dispose();
        _folderSelectedCancellationTokenSource = new CancellationTokenSource();

        _disposable = new CompositeDisposable
        {
            _fileSystemWatcherService.FileCreated
                                     .Buffer(TimeSpan.FromMilliseconds(150))
                                     .Where(files => files.Count > 0)
                                     .ObserveOn(_synchronizationContext)
                                     .Subscribe(OnFilesCreated),
            _fileSystemWatcherService.FileChanged
                                     .Buffer(TimeSpan.FromMilliseconds(150))
                                     .Where(files => files.Count > 0)
                                     .ObserveOn(_synchronizationContext)
                                     .Subscribe(OnFilesChanged),
            _fileSystemWatcherService.FileDeleted
                                     .Buffer(TimeSpan.FromMilliseconds(150))
                                     .Where(files => files.Count > 0)
                                     .ObserveOn(_synchronizationContext)
                                     .Subscribe(OnFilesDeleted),
            _fileSystemWatcherService.FileRenamed
                                     .ObserveOn(_synchronizationContext)
                                     .Subscribe(OnFileRenamed),
            _fileSystemWatcherService.RescanRequested
                                     .ObserveOn(_synchronizationContext)
                                     .Subscribe(path => _ = ReconcileMediaListingAndAnnotationsAsync(
                                         path,
                                         _folderSelectedCancellationTokenSource.Token)),
            _fileOperationsService.FileOperationCompleted
                                  .ObserveOn(_synchronizationContext)
                                  .Subscribe(OnFileOperationCompleted),
            _folderService.FileSystemItemSelected.Subscribe(OnFolderSelected),
            _fileOperationsService.MediaPreviewNavigationRequested
                                  .ObserveOn(_synchronizationContext)
                                  .Subscribe(OnMediaPreviewNavigationRequested),
            _fileOperationsService.ImagePreviewSelected.Subscribe(OnImagePreviewSelected),
            _folderClusterizationService.ClusterizationCompleted
                                        .ObserveOn(_synchronizationContext)
                                        .Subscribe(OnClusterizationCompleted),
            _folderStatisticsService.ScanProgress
                                    .ObserveOn(_synchronizationContext)
                                    .Subscribe(OnScanProgressChanged),
            TimelineVm.DateSelected.Subscribe(OnTimelineDateSelected),
            TimelineVm.StatisticsClick.Subscribe(OnStatisticsClick)
        };
    }

    /// <inheritdoc />
    public override void OnNavigatedFrom(NavigationContext navigationContext)
    {
        _disposable?.Dispose();
        _scrollCancellation.Cancel();
        _selectedAnnotationCancellation?.Cancel();
        _folderSelectedCancellationTokenSource.Cancel();
        _folderSelectedCancellationTokenSource.Dispose();
    }

    internal async Task HandleScrollAsync(double horizontalOffset, double viewportWidth)
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

            var result = await _fileTransferCoordinator.CopyAsync(
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

            var result = await _fileTransferCoordinator.MoveAsync(
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
            ApplyCachedAnnotations(mediaPreviewViewModel);

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
                    if (!_fileSystemService.FileExists(mediaPreviewViewModel.Url) ||
                        ImagePreviews.Any(existing => PathsEqual(existing.Url, mediaPreviewViewModel.Url)))
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

    private MediaSortDirection GetSavedSortDirection()
    {
        var configuration = _configurationService.Configuration.Value;
        return FileManagerPanel switch
        {
            FileManagerPanel.Left => configuration.LeftPreviewSortDirection,
            FileManagerPanel.Right => configuration.RightPreviewSortDirection,
            _ => MediaSortDirection.NewestFirst
        };
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
        lock (_imagePathsLock)
        {
            _imagePaths?.SetComparer(new FileModelCreationDateTimeComparer(direction));
        }

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

    private void OnFolderSelected(SelectedDirectory selectedFileSystemItem)
    {
        if (selectedFileSystemItem.FileManagerPanel != FileManagerPanel)
        {
            return;
        }

        var navigationDirectory = string.IsNullOrWhiteSpace(_navigationTargetPath)
            ? null
            : Path.GetDirectoryName(_navigationTargetPath);
        if (!string.IsNullOrWhiteSpace(navigationDirectory) &&
            !PathsEqual(navigationDirectory, selectedFileSystemItem.Path))
        {
            _navigationTargetPath = null;
        }

        ResetAnalysisState();
        _selectedDirectory = selectedFileSystemItem;

        _folderStatisticsService.Stop();
        _fileSystemWatcherService.StopWatching();

        _folderSelectedCancellationTokenSource.Cancel();
        _folderSelectedCancellationTokenSource.Dispose();
        _folderSelectedCancellationTokenSource = new CancellationTokenSource();

        ClearPreviewPanel();

        if (selectedFileSystemItem.Path == string.Empty)
        {
            return;
        }

        _ = LoadFolderAsync(selectedFileSystemItem, _folderSelectedCancellationTokenSource.Token);

        SelectedFolderPath = selectedFileSystemItem.Path;

        if (SelectedFolderPath ==DirectoryModel.RootPath)
        {
            return;
        }

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

            _ = LoadInitialImagesAsync(token);
            _ = _folderStatisticsService.StartAsync(selectedFileSystemItem.Path, token);
            _ = RefreshCacheThenReconcileAsync(selectedFileSystemItem.Path, token);
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

    private void OnFilesCreated(IList<FileModel> files)
    {
        foreach (var file in files.Where(file => IsXmpPath(file.FullName)))
        {
            _ = RefreshChangedAnnotationAsync(file.FullName);
        }

        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var newFiles = files
            .Where(file => MediaFormat.IsSupportedExtension(System.IO.Path.GetExtension(file.FullName)))
            .Where(file =>
                PathsEqual(System.IO.Path.GetDirectoryName(file.FullName), SelectedFolderPath) &&
                _fileSystemService.FileExists(file.FullName))
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

    private void OnFilesChanged(IList<FileModel> files)
    {
        foreach (var file in files)
        {
            _ = RefreshChangedAnnotationAsync(file.FullName);
        }
    }

    private async Task ProcessCreatedFilesAsync(IReadOnlyList<FileModel> files)
    {
        await Parallel.ForEachAsync(
            files,
            new ParallelOptions { MaxDegreeOfParallelism = 4 },
            async (file, _) => await CreateImagePreviewFromPathAsync(file.FullName, true));
    }

    private void OnFilesDeleted(IList<FileModel> files)
    {
        try
        {
            var sidecarPaths = files.Select(file => file.FullName).Where(IsXmpPath).ToArray();
            foreach (var sidecarPath in sidecarPaths)
            {
                _ = RefreshChangedAnnotationAsync(sidecarPath);
            }

            if (sidecarPaths.Length > 0 && !string.IsNullOrWhiteSpace(SelectedFolderPath))
            {
                _ = ReconcileFolderAndRefreshAsync(
                    SelectedFolderPath,
                    _folderSelectedCancellationTokenSource.Token);
            }

            var pathComparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            var deletedPaths = files
                .Select(file => file.FullName)
                .Where(path => MediaFormat.IsSupportedExtension(System.IO.Path.GetExtension(path)))
                .ToHashSet(pathComparer);

            lock (_imagePathsLock)
            {
                _imagePaths.RemoveWhere(file => deletedPaths.Contains(file.FullName));
            }

            foreach (var deletedPath in deletedPaths)
            {
                RemoveImagePreviewByPath(deletedPath);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unexpected exception during batch file deletion handling");
        }
    }

    private void OnFileRenamed(FileRenamedModel model)
    {
        try
        {
            var oldPath = model.OldFileModel.FullName;
            var newPath = model.NewFileModel.FullName;
            if (IsXmpPath(oldPath) || IsXmpPath(newPath))
            {
                _ = RefreshChangedAnnotationAsync(oldPath);
                _ = RefreshChangedAnnotationAsync(newPath);
                if (!string.IsNullOrWhiteSpace(SelectedFolderPath))
                {
                    _ = ReconcileFolderAndRefreshAsync(
                        SelectedFolderPath,
                        _folderSelectedCancellationTokenSource.Token);
                }

                return;
            }

            if (MediaFormat.IsSupportedExtension(System.IO.Path.GetExtension(oldPath)))
            {
                RemoveFilePath(oldPath);
                RemoveImagePreviewByPath(oldPath);
            }

            if (MediaFormat.IsSupportedExtension(System.IO.Path.GetExtension(newPath)))
            {
                AddFilePath(model.NewFileModel);
                _ = CreateImagePreviewFromPathAsync(newPath, false);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unexpected exception during handling of file renaming: {Path}", model.OldFileModel.FullName);
        }
    }

    private void OnFileOperationCompleted(MediaFileOperationCompleted operation)
    {
        var sourceDirectory = System.IO.Path.GetDirectoryName(operation.SourcePath);
        if (operation.Kind is MediaFileOperationKind.Move or MediaFileOperationKind.Delete &&
            PathsEqual(sourceDirectory, SelectedFolderPath))
        {
            RemoveFilePath(operation.SourcePath);
            RemoveImagePreviewByPath(operation.SourcePath);
        }

        if (operation.Kind is MediaFileOperationKind.Copy or MediaFileOperationKind.Move or MediaFileOperationKind.Restore &&
            operation.DestinationPath is { } destinationPath &&
            MediaFormat.IsSupportedExtension(System.IO.Path.GetExtension(destinationPath)) &&
            PathsEqual(System.IO.Path.GetDirectoryName(destinationPath), SelectedFolderPath))
        {
            var fileModel = new FileModel(System.IO.Path.GetFileName(destinationPath), destinationPath, null);
            AddFilePath(fileModel);
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

    private async Task CreateImagePreviewFromPathAsync(string filePath, bool loadToTimeline)
    {
        try
        {
            if (!MediaFormat.IsSupportedExtension(System.IO.Path.GetExtension(filePath)) ||
                !_fileSystemService.FileExists(filePath))
            {
                return;
            }

            var imagePreview = await _imageService.GetMediaPreviewAsync(filePath);

            if (imagePreview == null || !_fileSystemService.FileExists(filePath))
            {
                return;
            }

            if (ImagePreviews.Any(p => p.Url.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            await AddImagePreviewAsync(imagePreview);
            if (imagePreview.MediaFormat.SupportsAnnotations)
            {
                _ = RefreshChangedAnnotationAsync(filePath);
            }
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
            var imagePreviewViewModel = ImagePreviews.FirstOrDefault(vm => PathsEqual(vm.Url, filePath));
            if (imagePreviewViewModel == null)
            {
                return;
            }

            var indexToRemove = ImagePreviews.IndexOf(imagePreviewViewModel);
            var wasSelected = ReferenceEquals(SelectedPreview, imagePreviewViewModel);
            imagePreviewViewModel.UnloadPreview();
            lock (_loadedPreviewsLock)
            {
                _loadedPreviews.Remove(imagePreviewViewModel);
            }

            ImagePreviews.Remove(imagePreviewViewModel);

            if (wasSelected)
            {
                SelectedPreview = ImagePreviews.Count == 0
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

    private void ClearPreviewPanel()
    {
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
        SelectedPreview = null;
        TimelineVm.Clear();
        _annotationsByPath = new Dictionary<string, IndexedMediaAnnotations>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    private void OnMediaPreviewNavigationRequested(SelectedMediaPreview requestedPreview)
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
            PathsEqual(targetDirectory, SelectedFolderPath))
        {
            TrySelectNavigationTarget(_isPreviewAnalysisComplete);
        }
    }

    private void TrySelectNavigationTarget(bool finalize)
    {
        if (string.IsNullOrWhiteSpace(_navigationTargetPath))
        {
            return;
        }

        var target = ImagePreviews.FirstOrDefault(preview => PathsEqual(preview.Url, _navigationTargetPath));
        if (target != null)
        {
            var index = ImagePreviews.IndexOf(target);
            if (!ReferenceEquals(SelectedPreview, target))
            {
                SelectedPreview = target;
                _ = LoadImageAsync(index, _folderSelectedCancellationTokenSource.Token);
            }

            IndexInView = -1;
            IndexInView = index;
        }

        if (finalize)
        {
            _navigationTargetPath = null;
        }
    }

    private void OnImagePreviewSelected(SelectedMediaPreview selectedImagePreview)
    {
        if (selectedImagePreview.FileManagerPanel != FileManagerPanel)
        {
            SelectedPreview = null;
        }
    }

    private async Task LoadInitialImagesAsync(CancellationToken token)
    {
        try
        {
            if (_imagePaths.Count == 0)
            {
                TrySelectNavigationTarget(finalize: true);
                _isPreviewAnalysisComplete = true;
                RaisePropertyChanged(nameof(CanChangeSort));
                return;
            }

            var initialCount = Math.Min(PreloadCount * 2, _imagePaths.Count);
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
                _folderClusterizationService.StartAsync(SelectedFolderPath, token)
                                       .Forget(_logger, nameof(IFolderClusterizationService.StartAsync));
            }

            _isPreviewAnalysisComplete = true;
            RaisePropertyChanged(nameof(CanChangeSort));
            _synchronizationContext.Post(_ => TrySelectNavigationTarget(finalize: true), null);
        }
        catch (OperationCanceledException)
        {
            // Expected when the user selects another folder.
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Unexpected exception during loading initial images: {ex.Message}");
        }
    }

    private async Task LoadChunkAsync(FileModel[] chunk, CancellationToken token)
    {
        var previews = new ConcurrentBag<MediaPreviewViewModel>();

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

                var mediaPreviewViewModel = _mapper.Map<MediaPreviewViewModel>(previewImage);
                ApplyCachedAnnotations(mediaPreviewViewModel);
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

    private async Task RefreshCacheThenReconcileAsync(string directoryPath, CancellationToken token)
    {
        await RefreshCachedAnnotationsAsync(directoryPath, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (PathsEqual(SelectedFolderPath, directoryPath))
        {
            await ReconcileFolderAndRefreshAsync(directoryPath, token).ConfigureAwait(false);
        }
    }

    private async Task RefreshCachedAnnotationsAsync(string directoryPath, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || directoryPath == DirectoryModel.RootPath)
        {
            return;
        }

        try
        {
            var cached = await _annotationsService.GetDirectoryAsync(directoryPath, token).ConfigureAwait(false);
            var cachedByPath = cached.ToDictionary(
                item => item.MediaPath,
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            token.ThrowIfCancellationRequested();
            var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _synchronizationContext.Post(_ =>
            {
                try
                {
                    if (!token.IsCancellationRequested && PathsEqual(SelectedFolderPath, directoryPath))
                    {
                        _annotationsByPath = cachedByPath;
                        foreach (var preview in ImagePreviews)
                        {
                            ApplyCachedAnnotations(preview);
                        }

                        UpdateAnnotationEditor();
                    }
                }
                finally
                {
                    applied.TrySetResult();
                }
            }, null);
            await applied.Task.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to read cached annotations for {Directory}", directoryPath);
        }
    }

    private async Task ReconcileMediaListingAndAnnotationsAsync(string directoryPath, CancellationToken token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directoryPath)
                || !PathsEqual(SelectedFolderPath, directoryPath)
                || !_fileSystemService.DirectoryExists(directoryPath))
            {
                return;
            }

            var files = await Task.Run(
                () => _fileSystemService.GetFiles(directoryPath)
                    .Where(path => MediaFormat.IsSupportedExtension(Path.GetExtension(path)))
                    .Select(path => new FileModel(Path.GetFileName(path), path, File.GetLastWriteTime(path)))
                    .ToArray(),
                token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!PathsEqual(SelectedFolderPath, directoryPath))
            {
                return;
            }

            var comparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            var actual = files.ToDictionary(file => file.FullName, comparer);
            string[] stale;
            FileModel[] missing;
            lock (_imagePathsLock)
            {
                stale = _imagePaths.Select(file => file.FullName)
                    .Where(path => !actual.ContainsKey(path))
                    .ToArray();
                var known = _imagePaths.Select(file => file.FullName).ToHashSet(comparer);
                missing = files.Where(file => !known.Contains(file.FullName)).ToArray();
            }

            var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _synchronizationContext.Post(_ =>
            {
                try
                {
                    if (token.IsCancellationRequested || !PathsEqual(SelectedFolderPath, directoryPath))
                    {
                        return;
                    }

                    foreach (var path in stale.Where(path => !_fileSystemService.FileExists(path)))
                    {
                        RemoveFilePath(path);
                        RemoveImagePreviewByPath(path);
                    }

                    foreach (var file in missing.Where(file => _fileSystemService.FileExists(file.FullName)))
                    {
                        AddFilePath(file);
                        _ = CreateImagePreviewFromPathAsync(file.FullName, true);
                    }
                }
                finally
                {
                    applied.TrySetResult();
                }
            }, null);
            await applied.Task.WaitAsync(token).ConfigureAwait(false);
            await ReconcileFolderAndRefreshAsync(directoryPath, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to reconcile media listing for {Directory}", directoryPath);
        }
    }

    private async Task ReconcileFolderAndRefreshAsync(string directoryPath, CancellationToken token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || directoryPath == DirectoryModel.RootPath)
            {
                return;
            }

            await _annotationsService.RefreshDirectoryAsync(directoryPath, token).ConfigureAwait(false);
            var refreshed = await _annotationsService.GetDirectoryAsync(directoryPath, token).ConfigureAwait(false);
            var refreshedByPath = refreshed.ToDictionary(
                item => item.MediaPath,
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            _synchronizationContext.Post(_ =>
            {
                if (!PathsEqual(SelectedFolderPath, directoryPath))
                {
                    return;
                }

                _annotationsByPath = refreshedByPath;
                foreach (var preview in ImagePreviews)
                {
                    ApplyCachedAnnotations(preview);
                }

                UpdateAnnotationEditor();
            }, null);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to reconcile annotations for {Directory}", directoryPath);
        }
    }

    private void ApplyCachedAnnotations(MediaPreviewViewModel preview)
    {
        if (!preview.MediaFormat.SupportsAnnotations)
        {
            return;
        }

        if (ReferenceEquals(preview, SelectedPreview) && preview.AnnotationSnapshot is { } currentSnapshot)
        {
            preview.ApplyAnnotations(
                currentSnapshot.Annotations,
                _localization["Annotations_StateCurrent"],
                currentSnapshot);
            return;
        }

        var annotations = _annotationsByPath.TryGetValue(preview.Url, out var indexed)
            ? indexed.Annotations
            : MediaAnnotations.Empty;
        preview.ApplyAnnotations(annotations, _localization["Annotations_StateCached"]);
    }

    private async Task RefreshChangedAnnotationAsync(string changedPath)
    {
        try
        {
            var ownerPath = ResolveAnnotationOwnerPath(changedPath);
            if (ownerPath == null || !File.Exists(ownerPath))
            {
                return;
            }

            var snapshot = await _annotationsService.GetSnapshotAsync(ownerPath).ConfigureAwait(false);
            _synchronizationContext.Post(_ =>
            {
                var preview = ImagePreviews.FirstOrDefault(item => PathsEqual(item.Url, ownerPath));
                if (preview == null)
                {
                    return;
                }

                preview.ApplyAnnotations(snapshot.Annotations, _localization["Annotations_StateCurrent"], snapshot);
                if (ReferenceEquals(preview, SelectedPreview))
                {
                    UpdateAnnotationEditor();
                }
            }, null);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to refresh changed annotations for {Path}", changedPath);
        }
    }

    private string? ResolveAnnotationOwnerPath(string changedPath)
    {
        if (!IsXmpPath(changedPath))
        {
            return ImagePreviews.FirstOrDefault(preview => PathsEqual(preview.Url, changedPath))?.Url;
        }

        var owners = ImagePreviews
            .Where(preview => preview.MediaFormat.SupportsAnnotations)
            .Select(preview => new
            {
                preview.Url,
                Sidecar = XmpSidecarPath.GetExternalSidecarPath(preview.Url)
            })
            .Where(candidate => PathsEqual(candidate.Sidecar, changedPath))
            .Select(candidate => candidate.Url)
            .ToArray();
        if (owners.Length > 1)
        {
            _logger.Warning(
                "Skipped ambiguous XMP watcher refresh for {Sidecar}; owners: {Owners}",
                changedPath,
                string.Join(", ", owners));
            if (!string.IsNullOrWhiteSpace(SelectedFolderPath))
            {
                _ = ReconcileFolderAndRefreshAsync(
                    SelectedFolderPath,
                    _folderSelectedCancellationTokenSource.Token);
            }

            return null;
        }

        return owners.FirstOrDefault();
    }

    private static bool IsXmpPath(string path) =>
        Path.GetExtension(path).Equals(".xmp", StringComparison.OrdinalIgnoreCase);

    private async Task LoadSelectedAnnotationSnapshotAsync(
        MediaPreviewViewModel preview,
        CancellationTokenSource cancellation)
    {
        try
        {
            var snapshot = await _annotationsService.GetSnapshotAsync(preview.Url, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (ReferenceEquals(preview, SelectedPreview) && PathsEqual(preview.Url, snapshot.MediaPath))
            {
                preview.ApplyAnnotations(snapshot.Annotations, _localization["Annotations_StateCurrent"], snapshot);
                UpdateAnnotationEditor();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load current annotations for selected image {Path}", preview.Url);
        }
        finally
        {
            if (ReferenceEquals(_selectedAnnotationCancellation, cancellation))
            {
                _selectedAnnotationCancellation = null;
                SetSelectedAnnotationLoading(false);
                UpdateAnnotationEditor();
            }

            cancellation.Dispose();
        }
    }

    private void SetSelectedAnnotationLoading(bool value)
    {
        if (_isLoadingSelectedAnnotations == value)
        {
            return;
        }

        _isLoadingSelectedAnnotations = value;
        RaisePropertyChanged(nameof(CanEditAnnotations));
    }

    private void UpdateAnnotationEditor()
    {
        _annotationPreview = SelectedPreview;
        var rating = SelectedPreview?.Rating ?? 0;
        if (_selectedRating != Math.Clamp(rating, 0, 5))
        {
            _selectedRating = Math.Clamp(rating, 0, 5);
            RaisePropertyChanged(nameof(SelectedRating));
        }
        AnnotationEditorStatus = SelectedPreview?.AnnotationState ?? string.Empty;
        RaisePropertyChanged(nameof(CurrentTags));
        RaisePropertyChanged(nameof(HasCurrentTags));
        _ = RefreshAvailableAnnotationTagsAsync();
    }

    private async Task RefreshAvailableAnnotationTagsAsync()
    {
        try
        {
            var indexedTags = await _annotationsService.GetAllTagsAsync();
            var pinnedTags = _configurationService.Configuration.Value.PinnedTags ?? [];
            var tags = pinnedTags.Concat(indexedTags)
                .Select(MediaAnnotations.NormalizeTag)
                .Where(tag => tag.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag, StringComparer.Create(_localization.Culture, ignoreCase: true))
                .ThenBy(tag => tag, StringComparer.Ordinal)
                .ToArray();
            _synchronizationContext.Post(_ =>
            {
                _allAvailableAnnotationTags = tags;
                ApplyAvailableAnnotationTagFilter();
            }, null);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load available annotation tags");
        }
    }

    private void ApplyAvailableAnnotationTagFilter()
    {
        try
        {
            var query = MediaAnnotations.NormalizeTag(NewTag ?? string.Empty);
            var source = _allAvailableAnnotationTags ?? Array.Empty<string>();
            var filteredTags = query.Length == 0
                ? source
                : source.Where(tag => MediaAnnotations.MatchesTagQuery(tag, query));

            AvailableAnnotationTags.Clear();
            foreach (var tag in filteredTags)
            {
                AvailableAnnotationTags.Add(tag);
            }

            RaisePropertyChanged(nameof(HasAvailableAnnotationTags));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to apply annotation tag filter");
            try
            {
                AvailableAnnotationTags.Clear();
            }
            catch
            {
            }
        }
    }

    private Task SaveRatingAsync(IProgress<int> progress, CancellationToken token) =>
        SaveSelectedAnnotationsAsync(SelectedRating, CurrentTags, token);

    private async Task AddTagAsync(IProgress<int> progress, CancellationToken token)
    {
        var normalized = MediaAnnotations.NormalizeTag(NewTag);
        if (normalized.Length == 0)
        {
            return;
        }

        await AddTagValueAsync(normalized);
    }

    private Task RemoveTagAsync(string tag) =>
        SaveSelectedAnnotationsAsync(
            SelectedRating,
            CurrentTags.Where(current => !current.Equals(tag, StringComparison.OrdinalIgnoreCase)),
            CancellationToken.None);

    private async Task SaveSelectedAnnotationsAsync(
        int rating,
        IEnumerable<string> tags,
        CancellationToken token)
    {
        var preview = SelectedPreview;
        if (preview == null ||
            !preview.MediaFormat.SupportsAnnotations ||
            preview.AnnotationSnapshot == null ||
            IsSavingAnnotations ||
            _isLoadingSelectedAnnotations)
        {
            return;
        }

        var previous = new MediaAnnotations(preview.Rating, preview.Tags);
        var updated = new MediaAnnotations(rating, tags);
        try
        {
            IsSavingAnnotations = true;
            preview.IsAnnotationsSaving = true;
            preview.ApplyAnnotations(updated, _localization["Annotations_StateSaving"], preview.AnnotationSnapshot);
            UpdateAnnotationEditor();
            var snapshot = await _annotationsService.SaveAsync(
                preview.Url,
                updated,
                preview.AnnotationSnapshot?.FileState,
                token);
            preview.ApplyAnnotations(snapshot.Annotations, _localization["Annotations_StateSaved"], snapshot);
            AnnotationEditorStatus = preview.AnnotationState;
            RaisePropertyChanged(nameof(CurrentTags));
            await RefreshAvailableAnnotationTagsAsync();
        }
        catch (OperationCanceledException)
        {
            preview.ApplyAnnotations(previous, _localization["Annotations_StateCached"], preview.AnnotationSnapshot);
        }
        catch (Exception exception)
        {
            preview.ApplyAnnotations(previous, _localization["Annotations_StateError"], preview.AnnotationSnapshot);
            _logger.Error(exception, "Failed to save annotations for {Path}", preview.Url);
            _notificationService.SendNotification(
                new ErrorNotification(_localization["Annotations_SaveFailed"], exception.Message));
        }
        finally
        {
            preview.IsAnnotationsSaving = false;
            IsSavingAnnotations = false;
            UpdateAnnotationEditor();
        }
    }

    private async void OnTimelineDateSelected(DateTime dateTime)
    {
        try
        {
            using var operation = _scrollCancellation.Begin();
            var token = operation.Token;
            var targetPreview = ImagePreviews.FirstOrDefault(p =>
                                                                 p.FileDate.Date == dateTime.Date);

            if (targetPreview == null)
            {
                return;
            }

            var index = ImagePreviews.IndexOf(targetPreview);

            // Load range nearby item
            var start = Math.Max(0, index - PreloadCount / 2);
            var end = Math.Min(ImagePreviews.Count - 1, index + PreloadCount / 2);

            await LoadImageAsync(index, token);
            SelectedPreview = targetPreview;

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

    private void OnStatisticsClick(Unit unit)
    {
        try
        {
            var parameters = new DialogParameters { { "selectedFolder", _selectedDirectory! } };
            _dialogService.Show("glanceViewer", parameters, null, "childWindow");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open glance viewer window");
        }
    }
}