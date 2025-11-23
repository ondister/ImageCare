using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;
using ImageCare.Core.Services.MediaPreviewService;
using System.Collections.Concurrent;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace ImageCare.Core.Services.FolderStatisticsService;

public sealed class FileWatcherFolderStatisticsService : IFolderStatisticsService
{
    private readonly IMediaPreviewService _previewService;
    private readonly ConcurrentDictionary<DateTime, FilesBucket> _buckets;
    private readonly Subject<FilesBucket> _bucketChangedSubject;
    private readonly BehaviorSubject<ScanProgress> _progressSubject;
    private readonly BehaviorSubject<int> _totalFilesSubject;

    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers;
    private readonly object _syncLock = new();
    private int _currentTotalFiles;
    private CompositeDisposable? _subscriptions;

    private CancellationTokenSource? _scanCancellation;
    private volatile bool _isDisposed;
    private volatile bool _isScanning;

    public FileWatcherFolderStatisticsService(IMediaPreviewService previewService)
    {
        _previewService = previewService;
        _buckets = new ConcurrentDictionary<DateTime, FilesBucket>();
        _bucketChangedSubject = new Subject<FilesBucket>();
        _progressSubject = new BehaviorSubject<ScanProgress>(new ScanProgress(ScanStatus.Idle));
        _totalFilesSubject = new BehaviorSubject<int>(0);
        _currentTotalFiles = 0;
        _watchers = new ConcurrentDictionary<string, FileSystemWatcher>();
    }

    public IObservable<FilesBucket> BucketChanged => _bucketChangedSubject.AsObservable();

    public IObservable<ScanProgress> ScanProgress => _progressSubject.AsObservable();

    public IObservable<int> TotalFilesCount => _totalFilesSubject.AsObservable();

    public int CurrentTotalFiles => _currentTotalFiles;

    public bool IsScanning => _isScanning;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Stop();
        _bucketChangedSubject.Dispose();
        _progressSubject.Dispose();
        _scanCancellation?.Dispose();
        _subscriptions?.Dispose();

        foreach (var watcher in _watchers.Values)
        {
            watcher.Dispose();
        }
    }

    public async Task StartAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }

        Stop();

        lock (_syncLock)
        {
            _scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _isScanning = true;
        }

        _progressSubject.OnNext(new ScanProgress(ScanStatus.InitialScanStarted));

        try
        {
            ClearBuckets();
            SetupFileSystemWatcher(directoryPath);
            await PerformInitialScanAsync(directoryPath, _scanCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected during cancellation
            _progressSubject.OnNext(new ScanProgress(ScanStatus.Idle));
        }
        catch (Exception ex)
        {
            _progressSubject.OnNext(new ScanProgress(ScanStatus.ErrorOccurred, Error: ex));
        }
    }

    public void Stop()
    {
        lock (_syncLock)
        {
            if (!_isScanning)
            {
                return;
            }

            _scanCancellation?.Cancel();
            _isScanning = false;
        }

        ClearBuckets();
        _subscriptions?.Dispose();
        _subscriptions = null;

        foreach (var watcher in _watchers.Values)
        {
            watcher.Dispose();
        }

        _watchers.Clear();

        _progressSubject.OnNext(new ScanProgress(ScanStatus.Idle));
    }

    private void ClearBuckets()
    {
        _buckets.Clear();
        _currentTotalFiles = 0;
        _totalFilesSubject.OnNext(0);
    }

    private async Task PerformInitialScanAsync(string directoryPath, CancellationToken cancellationToken)
    {
        // Get all files and filter by supported formats
        var allFiles = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.TopDirectoryOnly);
        var supportedFiles = allFiles.Where(IsSupportedMediaFile);

        var processedFiles = 0;

        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken
        };

        _currentTotalFiles = 0;
        _totalFilesSubject.OnNext(0);

        try
        {
            await Parallel.ForEachAsync(
                              supportedFiles,
                              parallelOptions,
                              async (filePath, ct) =>
                              {
                                  var fileModel = await ProcessFileAsync(filePath, ct).ConfigureAwait(false);
                                  if (fileModel != null && fileModel.CreatedDateTime.HasValue)
                                  {
                                      AddOrUpdateFileInBucket(fileModel);
                                      var newCount = Interlocked.Increment(ref processedFiles);

                                      // Update progress periodically
                                      if (newCount % 100 == 0)
                                      {
                                          _progressSubject.OnNext(
                                              new ScanProgress(
                                                  ScanStatus.InitialScanStarted,
                                                  processedFiles,
                                                  _buckets.Count,
                                                  _currentTotalFiles)); // Include total files in progress
                                      }
                                  }
                              })
                          .ConfigureAwait(false);

            _progressSubject.OnNext(
                new ScanProgress(
                    ScanStatus.InitialScanCompleted,
                    processedFiles,
                    _buckets.Count,
                    _currentTotalFiles));
        }
        catch (OperationCanceledException)
        {
            // Ignored
        }
    }

    private void AddOrUpdateFileInBucket(FileModel fileModel)
    {
        var date = fileModel.CreatedDateTime!.Value.Date;
        var isNewFile = false;

        var updatedBucket = _buckets.AddOrUpdate(
            date,

            // Add new bucket
            _ =>
            {
                isNewFile = true;
                return new FilesBucket(
                    date,
                    1,
                    new ConcurrentDictionary<string, FileModel>
                    {
                        [fileModel.FullName] = fileModel
                    });
            },

            // Update existing bucket
            (_, existingBucket) =>
            {
                if (!existingBucket.Files.ContainsKey(fileModel.FullName))
                {
                    isNewFile = true;
                }

                existingBucket.Files[fileModel.FullName] = fileModel;
                return existingBucket with { FilesCount = existingBucket.Files.Count };
            });

        // Update total files count if new file was added
        if (isNewFile)
        {
            UpdateTotalFilesCount(1);
        }

        _bucketChangedSubject.OnNext(updatedBucket);
    }

    private void RemoveFileFromBucket(string filePath, DateTime date)
    {
        if (_buckets.TryGetValue(date, out var existingBucket))
        {
            if (existingBucket.Files.TryRemove(filePath, out _))
            {
                UpdateTotalFilesCount(-1);

                if (existingBucket.Files.IsEmpty)
                {
                    // Remove empty bucket
                    if (_buckets.TryRemove(date, out var removedBucket))
                    {
                        var updatedBucket = removedBucket with { FilesCount = existingBucket.Files.Count };
                        _bucketChangedSubject.OnNext(updatedBucket);
                    }
                }
                else
                {
                    // Update bucket with new count
                    var updatedBucket = existingBucket with { FilesCount = existingBucket.Files.Count };
                    _buckets[date] = updatedBucket;
                    _bucketChangedSubject.OnNext(updatedBucket);
                }
            }
        }
    }

    private void UpdateTotalFilesCount(int delta)
    {
        var newTotal = Interlocked.Add(ref _currentTotalFiles, delta);
        _totalFilesSubject.OnNext(newTotal);
    }

    private async ValueTask<FileModel?> ProcessFileAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                return null;
            }

            var preview = await _previewService.GetMediaPreviewAsync(fileInfo.FullName);
            if (preview == null || preview == MediaPreview.Empty)
            {
                return null;
            }

            var creationDate = await _previewService.GetCreationDateTime(preview);
            return new FileModel(fileInfo.Name, fileInfo.FullName, creationDate);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ServiceException($"Failed to process file: {filePath}", ex);
        }
    }

    private void SetupFileSystemWatcher(string directoryPath)
    {
        if (!_watchers.TryGetValue(directoryPath, out var watcher))
        {
            watcher = new FileSystemWatcher(directoryPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
                IncludeSubdirectories = false
            };

            var createdObservable = Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
                                                  h => watcher.Created += h,
                                                  h => watcher.Created -= h)
                                              .Throttle(TimeSpan.FromMilliseconds(100))
                                              .Where(args => IsSupportedMediaFile(args.EventArgs.FullPath)); // Filter supported formats

            var deletedObservable = Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
                                                  h => watcher.Deleted += h,
                                                  h => watcher.Deleted -= h)
                                              .Throttle(TimeSpan.FromMilliseconds(100))
                                              .Where(args => IsSupportedMediaFile(args.EventArgs.FullPath)); // Filter supported formats

            _subscriptions = new CompositeDisposable
            {
                createdObservable.Subscribe(args => _ = HandleFileAddedAsync(args.EventArgs.FullPath)),
                deletedObservable.Subscribe(args => HandleFileDeleted(args.EventArgs.FullPath))
            };

            _watchers[directoryPath] = watcher;
        }
    }

    private async Task HandleFileAddedAsync(string filePath)
    {
        if (_isDisposed || !_isScanning)
        {
            return;
        }

        try
        {
            var fileModel = await ProcessFileAsync(filePath, CancellationToken.None);
            if (fileModel != null && fileModel.CreatedDateTime.HasValue)
            {
                AddOrUpdateFileInBucket(fileModel);
                _progressSubject.OnNext(new ScanProgress(ScanStatus.FileChanged));
            }
        }
        catch (Exception ex)
        {
            throw new ServiceException($"Failed to handle file added: {filePath}", ex);
        }
    }

    private void HandleFileDeleted(string filePath)
    {
        if (_isDisposed || !_isScanning)
        {
            return;
        }

        try
        {
            // Find file date in existing buckets
            foreach (var (date, bucket) in _buckets)
            {
                if (bucket.Files.ContainsKey(filePath))
                {
                    RemoveFileFromBucket(filePath, date);
                    _progressSubject.OnNext(new ScanProgress(ScanStatus.FileChanged));
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            throw new ServiceException($"Failed to handle file deleted: {filePath}", ex);
        }
    }

    private bool IsSupportedMediaFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return MediaFormat.IsSupportedExtension(extension);
    }
}