using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.FileSystemWatcherService;
using ImageCare.Core.Services.FolderClusterizationService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.FolderStatisticsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.UI.Avalonia.Collections;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Preview;

/// <summary>
/// Tracks the selected folder and the media files inside it, keeps the preview strip and
/// the annotation cache in sync with file-system watcher events, and owns the folder-scoped
/// cancellation lifetime.
/// </summary>
internal sealed class PreviewFolderContentCoordinator : IDisposable
{
    private readonly IFolderService _folderService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IFileSystemWatcherService _fileSystemWatcherService;
    private readonly IFolderStatisticsService _folderStatisticsService;
    private readonly IFolderClusterizationService _folderClusterizationService;
    private readonly IMediaPreviewOperationsService _fileOperationsService;
    private readonly PreviewImageStripViewModel _mediaStrip;
    private readonly PreviewAnnotationCache _annotationCache;
    private readonly TimelineViewModel _timeline;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;

    private readonly object _imagePathsLock = new();
    private CompositeDisposable? _subscriptions;
    private CancellationTokenSource _folderSelectedCancellationTokenSource = new();
    private SortedObservableCollection<FileModel> _imagePaths = new();

    public PreviewFolderContentCoordinator(
        IFolderService folderService,
        IFileSystemService fileSystemService,
        IFileSystemWatcherService fileSystemWatcherService,
        IFolderStatisticsService folderStatisticsService,
        IFolderClusterizationService folderClusterizationService,
        IMediaPreviewOperationsService fileOperationsService,
        PreviewImageStripViewModel mediaStrip,
        PreviewAnnotationCache annotationCache,
        TimelineViewModel timeline,
        ILogger logger,
        SynchronizationContext synchronizationContext)
    {
        _folderService = folderService;
        _fileSystemService = fileSystemService;
        _fileSystemWatcherService = fileSystemWatcherService;
        _folderStatisticsService = folderStatisticsService;
        _folderClusterizationService = folderClusterizationService;
        _fileOperationsService = fileOperationsService;
        _mediaStrip = mediaStrip;
        _annotationCache = annotationCache;
        _timeline = timeline;
        _logger = logger;
        _synchronizationContext = synchronizationContext;

        _mediaStrip.SortDirectionChanged += OnSortDirectionChanged;
    }

    public SelectedDirectory? SelectedDirectory { get; private set; }

    public CancellationToken FolderToken => _folderSelectedCancellationTokenSource.Token;

    public void Start()
    {
        _subscriptions?.Dispose();
        _folderSelectedCancellationTokenSource.Cancel();
        _folderSelectedCancellationTokenSource.Dispose();
        _folderSelectedCancellationTokenSource = new CancellationTokenSource();

        _subscriptions = new CompositeDisposable
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
                                         FolderToken)),
            _fileOperationsService.FileOperationCompleted
                                  .ObserveOn(_synchronizationContext)
                                  .Subscribe(OnFileOperationCompleted),
            _fileOperationsService.MediaPreviewNavigationRequested
                                  .ObserveOn(_synchronizationContext)
                                  .Subscribe(_mediaStrip.NavigateToPreview),
            _fileOperationsService.ImagePreviewSelected
                                  .Subscribe(_mediaStrip.HandleExternalPreviewSelected),
            _folderService.FileSystemItemSelected.Subscribe(OnFolderSelected),
            _folderClusterizationService.ClusterizationCompleted
                                        .ObserveOn(_synchronizationContext)
                                        .Subscribe(_mediaStrip.ApplyClusterColors),
            _timeline.DateSelected.ObserveOn(_synchronizationContext)
                    .Subscribe(date => _ = _mediaStrip.HandleTimelineDateSelectedAsync(date)),
            _folderStatisticsService.ScanProgress
                                    .ObserveOn(_synchronizationContext)
                                    .Subscribe(_ => _mediaStrip.RefreshSortState())
        };
    }

    public void Stop()
    {
        _subscriptions?.Dispose();
        _subscriptions = null;
        _folderSelectedCancellationTokenSource.Cancel();
        _folderSelectedCancellationTokenSource.Dispose();
        _folderSelectedCancellationTokenSource = new CancellationTokenSource();
    }

    public void Dispose()
    {
        _mediaStrip.SortDirectionChanged -= OnSortDirectionChanged;
        _subscriptions?.Dispose();
        _folderSelectedCancellationTokenSource.Cancel();
        _folderSelectedCancellationTokenSource.Dispose();
    }

    public async Task ReconcileFolderAndRefreshAsync(string directoryPath, CancellationToken token)
    {
        await _annotationCache.RefreshDirectoryAsync(directoryPath, token).ConfigureAwait(false);
    }

    private void OnSortDirectionChanged(object? sender, MediaSortDirection direction)
    {
        lock (_imagePathsLock)
        {
            _imagePaths.SetComparer(new FileModelCreationDateTimeComparer(direction));
        }
    }

    private void OnFolderSelected(SelectedDirectory selectedFileSystemItem)
    {
        if (selectedFileSystemItem.FileManagerPanel != _mediaStrip.FileManagerPanel)
        {
            return;
        }

        _mediaStrip.ClearNavigationTargetOutside(selectedFileSystemItem.Path);
        SelectedDirectory = selectedFileSystemItem;

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

        _mediaStrip.SelectedFolderPath = selectedFileSystemItem.Path;

        if (_mediaStrip.SelectedFolderPath == DirectoryModel.RootPath)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_mediaStrip.SelectedFolderPath))
        {
            try
            {
                _fileSystemWatcherService.StartWatchingDirectory(_mediaStrip.SelectedFolderPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Unexpected exception during set watching directory {_mediaStrip.SelectedFolderPath}");
            }
        }
    }

    private async Task LoadFolderAsync(SelectedDirectory selectedFileSystemItem, CancellationToken token)
    {
        try
        {
            lock (_imagePathsLock)
            {
                _mediaStrip.FilesLoading = true;
                _imagePaths = new SortedObservableCollection<FileModel>(
                    new FileModelCreationDateTimeComparer(_mediaStrip.SortDirection));
            }

            var files = await _folderService.GetFileModelAsync(selectedFileSystemItem, "*");

            token.ThrowIfCancellationRequested();
            var distinctFiles = files
                .Where(file => MediaFormat.IsSupportedExtension(Path.GetExtension(file.FullName)))
                .GroupBy(file => file.FullName, PreviewPathUtility.PathComparer)
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
            _mediaStrip.FilesLoading = false;
        }
    }

    private async Task LoadInitialImagesAsync(CancellationToken token)
    {
        try
        {
            if (_imagePaths.Count == 0)
            {
                _mediaStrip.CompleteInitialLoad();
                return;
            }

            var initialCount = Math.Min(PreviewImageStripViewModel.PreloadCount * 2, _imagePaths.Count);
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
                await _mediaStrip.AppendPreviewsAsync(chunk, token);

                if (offset == 0)
                {
                    for (var i = 0; i < count; i++)
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        _ = _mediaStrip.LoadImageAsync(i, token);
                    }
                }

                offset += count;
                batchSize = Math.Min(batchSize * 2, 4_096);
            }

            if (_imagePaths.Count <= 5_000)
            {
                _folderClusterizationService.StartAsync(_mediaStrip.SelectedFolderPath, token)
                                           .Forget(_logger, nameof(IFolderClusterizationService.StartAsync));
            }

            _mediaStrip.CompleteInitialLoad();
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

    private void OnFilesCreated(IList<FileModel> files)
    {
        foreach (var file in files.Where(file => PreviewPathUtility.IsXmpPath(file.FullName)))
        {
            _ = _annotationCache.RefreshChangedAsync(file.FullName);
        }

        var newFiles = files
            .Where(file => MediaFormat.IsSupportedExtension(Path.GetExtension(file.FullName)))
            .Where(file =>
                PreviewPathUtility.PathsEqual(
                    Path.GetDirectoryName(file.FullName),
                    _mediaStrip.SelectedFolderPath) &&
                _fileSystemService.FileExists(file.FullName))
            .GroupBy(file => file.FullName, PreviewPathUtility.PathComparer)
            .Select(group => group.Last())
            .ToArray();

        lock (_imagePathsLock)
        {
            var existingPaths = _imagePaths.Select(file => file.FullName)
                                           .ToHashSet(PreviewPathUtility.PathComparer);
            _imagePaths.AddRange(newFiles.Where(file => existingPaths.Add(file.FullName)));
        }

        _ = ProcessCreatedFilesAsync(newFiles);
    }

    private void OnFilesChanged(IList<FileModel> files)
    {
        foreach (var file in files)
        {
            _ = _annotationCache.RefreshChangedAsync(file.FullName);
        }
    }

    private async Task ProcessCreatedFilesAsync(IReadOnlyList<FileModel> files)
    {
        await Parallel.ForEachAsync(
            files,
            new ParallelOptions { MaxDegreeOfParallelism = 4 },
            async (file, _) => await _mediaStrip.CreateImagePreviewFromPathAsync(file.FullName));
    }

    private void OnFilesDeleted(IList<FileModel> files)
    {
        try
        {
            var sidecarPaths = files.Select(file => file.FullName)
                                    .Where(PreviewPathUtility.IsXmpPath)
                                    .ToArray();
            foreach (var sidecarPath in sidecarPaths)
            {
                _ = _annotationCache.RefreshChangedAsync(sidecarPath);
            }

            if (sidecarPaths.Length > 0 && !string.IsNullOrWhiteSpace(_mediaStrip.SelectedFolderPath))
            {
                _ = ReconcileFolderAndRefreshAsync(
                    _mediaStrip.SelectedFolderPath,
                    FolderToken);
            }

            var deletedPaths = files
                .Select(file => file.FullName)
                .Where(path => MediaFormat.IsSupportedExtension(Path.GetExtension(path)))
                // A file-system watcher can report a transient Deleted event when a file is
                // atomically replaced (for example when XMP is embedded into a JPEG by writing
                // a temporary file and moving it over the original). Only treat the path as
                // deleted when it is genuinely gone from disk, otherwise the preview would
                // vanish even though the file still exists.
                .Where(path => !_fileSystemService.FileExists(path))
                .ToHashSet(PreviewPathUtility.PathComparer);

            lock (_imagePathsLock)
            {
                _imagePaths.RemoveWhere(file => deletedPaths.Contains(file.FullName));
            }

            foreach (var deletedPath in deletedPaths)
            {
                _mediaStrip.RemoveImagePreviewByPath(deletedPath);
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
            if (PreviewPathUtility.IsXmpPath(oldPath) || PreviewPathUtility.IsXmpPath(newPath))
            {
                _ = _annotationCache.RefreshChangedAsync(oldPath);
                _ = _annotationCache.RefreshChangedAsync(newPath);
                if (!string.IsNullOrWhiteSpace(_mediaStrip.SelectedFolderPath))
                {
                    _ = ReconcileFolderAndRefreshAsync(
                        _mediaStrip.SelectedFolderPath,
                        FolderToken);
                }

                return;
            }

            if (MediaFormat.IsSupportedExtension(Path.GetExtension(oldPath)) &&
                !_fileSystemService.FileExists(oldPath))
            {
                RemoveFilePath(oldPath);
                _mediaStrip.RemoveImagePreviewByPath(oldPath);
            }

            if (MediaFormat.IsSupportedExtension(Path.GetExtension(newPath)))
            {
                AddFilePath(model.NewFileModel);
                _ = _mediaStrip.CreateImagePreviewFromPathAsync(newPath);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(
                ex,
                "Unexpected exception during handling of file renaming: {Path}",
                model.OldFileModel.FullName);
        }
    }

    private void OnFileOperationCompleted(MediaFileOperationCompleted operation)
    {
        var sourceDirectory = Path.GetDirectoryName(operation.SourcePath);
        if (operation.Kind is MediaFileOperationKind.Move or MediaFileOperationKind.Delete &&
            PreviewPathUtility.PathsEqual(sourceDirectory, _mediaStrip.SelectedFolderPath))
        {
            RemoveFilePath(operation.SourcePath);
            _mediaStrip.RemoveImagePreviewByPath(operation.SourcePath);
        }

        if (operation.Kind is MediaFileOperationKind.Copy or MediaFileOperationKind.Move or
                MediaFileOperationKind.Restore &&
            operation.DestinationPath is { } destinationPath &&
            MediaFormat.IsSupportedExtension(Path.GetExtension(destinationPath)) &&
            PreviewPathUtility.PathsEqual(
                Path.GetDirectoryName(destinationPath),
                _mediaStrip.SelectedFolderPath))
        {
            var fileModel = new FileModel(Path.GetFileName(destinationPath), destinationPath, null);
            AddFilePath(fileModel);
            _ = _mediaStrip.CreateImagePreviewFromPathAsync(destinationPath);
        }
    }

    private void AddFilePath(FileModel fileModel)
    {
        lock (_imagePathsLock)
        {
            if (_imagePaths.All(existing =>
                    !PreviewPathUtility.PathsEqual(existing.FullName, fileModel.FullName)))
            {
                _imagePaths.Add(fileModel);
            }
        }
    }

    private void RemoveFilePath(string filePath)
    {
        lock (_imagePathsLock)
        {
            var existing = _imagePaths.FirstOrDefault(file =>
                PreviewPathUtility.PathsEqual(file.FullName, filePath));
            if (existing != null)
            {
                _imagePaths.Remove(existing);
            }
        }
    }

    private async Task RefreshCacheThenReconcileAsync(string directoryPath, CancellationToken token)
    {
        await _annotationCache.LoadCachedDirectoryAsync(directoryPath, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (PreviewPathUtility.PathsEqual(_mediaStrip.SelectedFolderPath, directoryPath))
        {
            await ReconcileFolderAndRefreshAsync(directoryPath, token).ConfigureAwait(false);
        }
    }

    private async Task ReconcileMediaListingAndAnnotationsAsync(string directoryPath, CancellationToken token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directoryPath)
                || !PreviewPathUtility.PathsEqual(_mediaStrip.SelectedFolderPath, directoryPath)
                || !_fileSystemService.DirectoryExists(directoryPath))
            {
                return;
            }

            var files = await Task.Run(
                () => _fileSystemService.GetFiles(directoryPath)
                    .Where(path => MediaFormat.IsSupportedExtension(Path.GetExtension(path)))
                    .Select(path => new FileModel(
                        Path.GetFileName(path),
                        path,
                        File.GetLastWriteTime(path)))
                    .ToArray(),
                token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!PreviewPathUtility.PathsEqual(_mediaStrip.SelectedFolderPath, directoryPath))
            {
                return;
            }

            var actual = files.ToDictionary(file => file.FullName, PreviewPathUtility.PathComparer);
            string[] stale;
            FileModel[] missing;
            lock (_imagePathsLock)
            {
                stale = _imagePaths.Select(file => file.FullName)
                    .Where(path => !actual.ContainsKey(path))
                    .ToArray();
                var known = _imagePaths.Select(file => file.FullName)
                                       .ToHashSet(PreviewPathUtility.PathComparer);
                missing = files.Where(file => !known.Contains(file.FullName)).ToArray();
            }

            var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _synchronizationContext.Post(
                _ =>
                {
                    try
                    {
                        if (token.IsCancellationRequested ||
                            !PreviewPathUtility.PathsEqual(_mediaStrip.SelectedFolderPath, directoryPath))
                        {
                            return;
                        }

                        foreach (var path in stale.Where(path => !_fileSystemService.FileExists(path)))
                        {
                            RemoveFilePath(path);
                            _mediaStrip.RemoveImagePreviewByPath(path);
                        }

                        foreach (var file in missing.Where(file =>
                                     _fileSystemService.FileExists(file.FullName)))
                        {
                            AddFilePath(file);
                            _ = _mediaStrip.CreateImagePreviewFromPathAsync(file.FullName);
                        }
                    }
                    finally
                    {
                        applied.TrySetResult();
                    }
                },
                null);
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

    private void ClearPreviewPanel()
    {
        lock (_imagePathsLock)
        {
            _imagePaths.Clear();
        }

        _mediaStrip.ClearPreviews();
        _timeline.Clear();
        _annotationCache.Reset();
    }
}
