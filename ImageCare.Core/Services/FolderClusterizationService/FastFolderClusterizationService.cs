using System.Reactive.Linq;
using System.Reactive.Subjects;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Exceptions;
using ImageCare.Core.Services.FileSystemService;

namespace ImageCare.Core.Services.FolderClusterizationService;

public class FastFolderClusterizationService : IFolderClusterizationService
{
    private readonly IFileSystemService _fileSystemService;
    private readonly Subject<FileClustersStatistics> _clusterizationCompletedSubject;
    private readonly object _syncLock = new();
    private CancellationTokenSource? _scanCancellation;
    private bool _isDisposed;

    public FastFolderClusterizationService(IFileSystemService fileSystemService)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _clusterizationCompletedSubject = new Subject<FileClustersStatistics>();
    }

    /// <inheritdoc />
    public IObservable<FileClustersStatistics> ClusterizationCompleted => _clusterizationCompletedSubject.AsObservable();

    /// <inheritdoc />
    public bool IsScanning { get; private set; }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        lock (_syncLock)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            StopInternal();

            _clusterizationCompletedSubject.Dispose();
            _scanCancellation?.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task StartAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));
        }

        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }

        lock (_syncLock)
        {
            if (IsScanning)
            {
                Stop();
            }

            IsScanning = true;
            _scanCancellation?.Dispose();
            _scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        try
        {
            await Task.Run(() => ScanDirectory(directoryPath, _scanCancellation.Token), _scanCancellation.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ServiceException("Failed to clusterize directory", ex);
        }
        finally
        {
            lock (_syncLock)
            {
                IsScanning = false;
                if (!cancellationToken.IsCancellationRequested)
                {
                    _scanCancellation?.Dispose();
                    _scanCancellation = null;
                }
            }
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        ThrowIfDisposed();
        StopInternal();
    }

    private void ScanDirectory(string directoryPath, CancellationToken cancellationToken)
    {
        try
        {
            var files = _fileSystemService.EnumerateFiles(directoryPath, "*.*", SearchOption.TopDirectoryOnly)
                                          .Where(f => f.CreatedDateTime.HasValue && IsSupportedMediaFile(f.FullName))
                                          .TakeWhile(_ => !cancellationToken.IsCancellationRequested)
                                          .Select(f => new ClusterFileModel(f))
                                          .ToList();

            cancellationToken.ThrowIfCancellationRequested();

            if (files.Count == 0)
            {
                _clusterizationCompletedSubject.OnNext(new FileClustersStatistics());
                return;
            }

            var clusteringService = new DbScanClusteringService();
            var clusters = clusteringService.ClusterAutomatically(files);

            cancellationToken.ThrowIfCancellationRequested();

            _clusterizationCompletedSubject.OnNext(clusters);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ServiceException("Error during directory scanning", ex);
        }
    }

    private void StopInternal()
    {
        lock (_syncLock)
        {
            if (!IsScanning)
            {
                return;
            }

            _scanCancellation?.Cancel();
            IsScanning = false;
        }
    }

    private bool IsSupportedMediaFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);

        return MediaFormat.IsSupportedExtension(extension);
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(FastFolderClusterizationService));
        }
    }
}