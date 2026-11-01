using System;
using System.Collections.Generic;
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
using ImageCare.Core.Services.FileSystemWatcherService;
using ImageCare.Core.Services.FolderClusterizationService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.FolderStatisticsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.UI.Avalonia.Collections;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Domain;
using ImageCare.UI.Avalonia.ViewModels.Preview;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Glance;

/// <summary>
/// Tracks the folder shown in the overview, loads its media files, and keeps the preview
/// grid and annotation cache in sync with file-system watcher and file-operation events.
/// </summary>
internal sealed class GlanceFolderContentCoordinator : IDisposable
{
    private readonly IFolderService _folderService;
    private readonly IFileSystemWatcherService _fileSystemWatcherService;
    private readonly IFolderStatisticsService _folderStatisticsService;
    private readonly IFolderClusterizationService _folderClusterizationService;
    private readonly IMediaPreviewOperationsService _fileOperationsService;
    private readonly GlancePreviewGridViewModel _previewGrid;
    private readonly GlanceAnnotationCache _annotationCache;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly object _imagePathsLock = new();

    private CompositeDisposable? _subscriptions;
    private CancellationTokenSource _folderTokenSource = new();
    private SortedObservableCollection<FileModel> _imagePaths = new();

    public GlanceFolderContentCoordinator(
        IFolderService folderService,
        IFileSystemWatcherService fileSystemWatcherService,
        IFolderStatisticsService folderStatisticsService,
        IFolderClusterizationService folderClusterizationService,
        IMediaPreviewOperationsService fileOperationsService,
        GlancePreviewGridViewModel previewGrid,
        GlanceAnnotationCache annotationCache,
        ILogger logger,
        SynchronizationContext synchronizationContext)
    {
        _folderService = folderService;
        _fileSystemWatcherService = fileSystemWatcherService;
        _folderStatisticsService = folderStatisticsService;
        _folderClusterizationService = folderClusterizationService;
        _fileOperationsService = fileOperationsService;
        _previewGrid = previewGrid;
        _annotationCache = annotationCache;
        _logger = logger;
        _synchronizationContext = synchronizationContext;

        _previewGrid.SortDirectionChanged += OnSortDirectionChanged;
    }

    public CancellationToken FolderToken => _folderTokenSource.Token;

    public void Start(SelectedDirectory selectedDirectory)
    {
        _subscriptions?.Dispose();
        RenewFolderToken();

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
            _fileOperationsService.FileOperationCompleted
                                  .ObserveOn(_synchronizationContext)
                                  .Subscribe(OnFileOperationCompleted),
            _folderClusterizationService.ClusterizationCompleted
                                        .ObserveOn(_synchronizationContext)
                                        .Subscribe(_previewGrid.ApplyClusterColors),
            _folderStatisticsService.ScanProgress
                                    .ObserveOn(_synchronizationContext)
                                    .Subscribe(_ => _previewGrid.RefreshSortState())
        };

        _folderStatisticsService.Stop();
        _fileSystemWatcherService.StopWatching();
        _previewGrid.CancelImageLoading();
        _previewGrid.Clear();
        _annotationCache.Reset();
        _previewGrid.SelectedFolderPath = selectedDirectory.Path;

        _ = LoadFolderAsync(selectedDirectory, FolderToken);

        if (!string.IsNullOrWhiteSpace(selectedDirectory.Path))
        {
            try
            {
                _fileSystemWatcherService.StartWatchingDirectory(selectedDirectory.Path);
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    $"Unexpected exception during set watching directory {selectedDirectory.Path}");
            }
        }
    }

    public void Stop()
    {
        _subscriptions?.Dispose();
        _subscriptions = null;
        _folderStatisticsService.Stop();
        _fileSystemWatcherService.StopWatching();
        _folderTokenSource.Cancel();
        _folderTokenSource.Dispose();
        _folderTokenSource = new CancellationTokenSource();
    }

    public void Dispose()
    {
        _previewGrid.SortDirectionChanged -= OnSortDirectionChanged;
        _subscriptions?.Dispose();
        _folderTokenSource.Cancel();
        _folderTokenSource.Dispose();
    }

    private void RenewFolderToken()
    {
        _folderTokenSource.Cancel();
        _folderTokenSource.Dispose();
        _folderTokenSource = new CancellationTokenSource();
    }

    private void OnSortDirectionChanged(object? sender, MediaSortDirection direction)
    {
        lock (_imagePathsLock)
        {
            _imagePaths.SetComparer(new FileModelCreationDateTimeComparer(direction));
        }
    }

    private async Task LoadFolderAsync(SelectedDirectory selectedDirectory, CancellationToken token)
    {
        try
        {
            lock (_imagePathsLock)
            {
                _previewGrid.FilesLoading = true;
                _imagePaths = new SortedObservableCollection<FileModel>(
                    new FileModelCreationDateTimeComparer(_previewGrid.SortDirection));
            }

            var files = await _folderService.GetFileModelAsync(selectedDirectory, "*");

            token.ThrowIfCancellationRequested();
            var distinctFiles = files
                .Where(file => MediaFormat.IsSupportedExtension(Path.GetExtension(file.FullName)))
                .GroupBy(file => file.FullName, PreviewPathUtility.PathComparer)
                .Select(group => group.First());
            lock (_imagePathsLock)
            {
                _imagePaths.AddRange(distinctFiles);
            }

            _previewGrid.FilesLoading = false;
            _ = LoadInitialImagesAsync(token);
            _ = _folderStatisticsService.StartAsync(selectedDirectory.Path, token);
            _ = RefreshCacheThenReconcileAsync(selectedDirectory.Path, token);
        }
        catch (OperationCanceledException)
        {
            // Ignored
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Failed on initial folder loading: {selectedDirectory.Path}");
        }
        finally
        {
            _previewGrid.FilesLoading = false;
        }
    }

    private async Task RefreshCacheThenReconcileAsync(string directoryPath, CancellationToken token)
    {
        try
        {
            await _annotationCache.LoadCachedDirectoryAsync(directoryPath, token).ConfigureAwait(false);
            await _annotationCache.RefreshDirectoryAsync(directoryPath, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when the overview closes or switches folders.
        }
    }

    private async Task LoadInitialImagesAsync(CancellationToken token)
    {
        try
        {
            if (_imagePaths.Count == 0)
            {
                _previewGrid.RefreshSortState();
                return;
            }

            var initialCount = Math.Min(_previewGrid.ImagesPerLine * 10, _imagePaths.Count);
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
                await _previewGrid.AppendPreviewsAsync(chunk, token);

                if (offset == 0)
                {
                    for (var i = 0; i < count; i++)
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        _ = _previewGrid.LoadImageAsync(i, token);
                    }
                }

                offset += count;
                batchSize = Math.Min(batchSize * 2, 4_096);
            }

            if (_imagePaths.Count <= 5_000)
            {
                _folderClusterizationService.StartAsync(_previewGrid.SelectedFolderPath, token)
                                           .Forget(_logger, nameof(IFolderClusterizationService.StartAsync));
            }

            _previewGrid.RefreshSortState();
        }
        catch (OperationCanceledException)
        {
            // Expected when the user closes the overview.
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
            .Where(file => PreviewPathUtility.PathsEqual(
                Path.GetDirectoryName(file.FullName),
                _previewGrid.SelectedFolderPath))
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
            async (file, _) => await _previewGrid.CreateImagePreviewFromPathAsync(file.FullName));
    }

    private void OnFilesDeleted(IList<FileModel> files)
    {
        try
        {
            foreach (var file in files.Where(file => PreviewPathUtility.IsXmpPath(file.FullName)))
            {
                _ = _annotationCache.RefreshChangedAsync(file.FullName);
            }

            var deletedPaths = files
                .Select(file => file.FullName)
                .ToHashSet(PreviewPathUtility.PathComparer);

            lock (_imagePathsLock)
            {
                _imagePaths.RemoveWhere(file => deletedPaths.Contains(file.FullName));
            }

            foreach (var deletedPath in deletedPaths)
            {
                _previewGrid.RemoveImagePreviewByPath(deletedPath);
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
            if (MediaFormat.IsSupportedExtension(Path.GetExtension(oldPath)))
            {
                RemoveFilePath(oldPath);
                _previewGrid.RemoveImagePreviewByPath(oldPath);
            }

            if (MediaFormat.IsSupportedExtension(Path.GetExtension(newPath)))
            {
                AddFilePath(model.NewFileModel);
                _ = _previewGrid.CreateImagePreviewFromPathAsync(newPath);
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
        if (operation.Kind is MediaFileOperationKind.Move or MediaFileOperationKind.Delete &&
            PreviewPathUtility.PathsEqual(
                Path.GetDirectoryName(operation.SourcePath),
                _previewGrid.SelectedFolderPath))
        {
            RemoveFilePath(operation.SourcePath);
            _previewGrid.RemoveImagePreviewByPath(operation.SourcePath);
        }

        if (operation.Kind is MediaFileOperationKind.Copy or MediaFileOperationKind.Move or
                MediaFileOperationKind.Restore &&
            operation.DestinationPath is { } destinationPath &&
            MediaFormat.IsSupportedExtension(Path.GetExtension(destinationPath)) &&
            PreviewPathUtility.PathsEqual(
                Path.GetDirectoryName(destinationPath),
                _previewGrid.SelectedFolderPath))
        {
            AddFilePath(new FileModel(Path.GetFileName(destinationPath), destinationPath, null));
            _ = _previewGrid.CreateImagePreviewFromPathAsync(destinationPath);
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
}
