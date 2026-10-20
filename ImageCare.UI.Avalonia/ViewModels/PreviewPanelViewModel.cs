using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.Notification;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.FileSystemWatcherService;
using ImageCare.Core.Services.FolderClusterizationService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.FolderStatisticsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.UI.Avalonia.Behaviors;
using ImageCare.UI.Avalonia.Collections;
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
    private readonly IDialogService _dialogService;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;

    private readonly object _imagePathsLock = new();
    private MediaPreviewViewModel? _selectedPreview;
    private CompositeDisposable? _disposable;

    private CancellationTokenSource _folderSelectedCancellationTokenSource;
    private SortedObservableCollection<FileModel> _imagePaths;
    private CancellationTokenSource _currentScrollCancellation = new();

    private bool _isScrollResetRequested;
    private bool _filesLoading;
    private bool _isPreviewAnalysisComplete;
    private bool _isStatisticsAnalysisComplete;
    private MediaSortDirection _sortDirection = MediaSortDirection.NewestFirst;
    private SelectedDirectory? _selectedDirectory;
    private int _indexInView;

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
        _dialogService = dialogService;
        _mapper = mapper;
        _logger = logger;
        _synchronizationContext = synchronizationContext;

        ImagePreviewDropHandler = imagePreviewDropHandler;
        CopyImagePreviewCommand = CreateAsyncCommand(CopyImagePreviewAsync);
        MoveImagePreviewCommand = CreateAsyncCommand(MoveImagePreviewAsync);
        DeleteImagePreviewCommand = CreateAsyncCommand(DeleteImagePreview);
        ToggleSortDirectionCommand = CreateCommand(ToggleSortDirection);

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

    public ICommand ToggleSortDirectionCommand { get; }

    public bool IsSortAscending => _sortDirection == MediaSortDirection.OldestFirst;

    public bool CanChangeSort => !FilesLoading &&
                                 _isPreviewAnalysisComplete &&
                                 _isStatisticsAnalysisComplete &&
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
        FileManagerPanel = (FileManagerPanel)navigationContext.Parameters["panel"];
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
            _fileSystemWatcherService.FileDeleted
                                     .Buffer(TimeSpan.FromMilliseconds(150))
                                     .Where(files => files.Count > 0)
                                     .ObserveOn(_synchronizationContext)
                                     .Subscribe(OnFilesDeleted),
            _fileSystemWatcherService.FileRenamed
                                     .ObserveOn(_synchronizationContext)
                                     .Subscribe(OnFileRenamed),
            _fileOperationsService.FileOperationCompleted
                                  .ObserveOn(_synchronizationContext)
                                  .Subscribe(OnFileOperationCompleted),
            _folderService.FileSystemItemSelected.Subscribe(OnFolderSelected),
            _fileOperationsService.ImagePreviewSelected.Subscribe(OnImagePreviewSelected),
            _folderClusterizationService.ClusterizationCompleted.Subscribe(OnClusterizationCompleted),
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
        _folderSelectedCancellationTokenSource.Cancel();
        _folderSelectedCancellationTokenSource.Dispose();
    }

    internal async Task HandleScrollAsync(double horizontalOffset, double viewportWidth)
    {
        var nextCancellation = new CancellationTokenSource();
        var previousCancellation = Interlocked.Exchange(ref _currentScrollCancellation, nextCancellation);

        try
        {
            await previousCancellation.CancelAsync();
        }
        finally
        {
            previousCancellation.Dispose();
        }

        var token = nextCancellation.Token;

        try
        {
            var totalContentCount = ImagePreviews.Count * MaxItemWidth;

            var firstVisibleIndex = (int)(horizontalOffset / MaxItemWidth);
            var lastVisibleIndex = (int)((horizontalOffset + viewportWidth) / MaxItemWidth);

            var epsilon = 2;

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
                .GroupBy(file => file.FullName, pathComparer)
                .Select(group => group.First());
            lock (_imagePathsLock)
            {
                _imagePaths.AddRange(distinctFiles);
            }

            _ = LoadInitialImagesAsync(token);
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

    private void OnFilesCreated(IList<FileModel> files)
    {
        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var newFiles = files
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
            var pathComparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            var deletedPaths = files.Select(file => file.FullName).ToHashSet(pathComparer);

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
            RemoveFilePath(model.OldFileModel.FullName);
            RemoveImagePreviewByPath(model.OldFileModel.FullName);
            AddFilePath(model.NewFileModel);
            _ = CreateImagePreviewFromPathAsync(model.NewFileModel.FullName, false);
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
            if (!_fileSystemService.FileExists(filePath))
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
                _ = LoadImageAsync(indexToLoad, _currentScrollCancellation.Token);
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

            var initialCount = Math.Min(PreloadCount * 2, _imagePaths.Count);

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
            _isPreviewAnalysisComplete = true;
            RaisePropertyChanged(nameof(CanChangeSort));
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
                var previewImage = await _imageService.GetMediaPreviewAsync(fileModel.FullName).ConfigureAwait(false);
                if (previewImage == null)
                {
                    return;
                }

                var metadata = await _imageService.GetMediaMetadataAsync(previewImage).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                var mediaPreviewViewModel = _mapper.Map<MediaPreviewViewModel>(previewImage);
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