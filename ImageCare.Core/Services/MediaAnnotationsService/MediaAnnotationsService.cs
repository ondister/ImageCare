using System.Reactive.Linq;
using System.Reactive.Subjects;
using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Services.MediaAnnotationsIndex;

namespace ImageCare.Core.Services.MediaAnnotationsService;

public sealed class MediaAnnotationsService : IMediaAnnotationsService
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly IMediaAnnotationsStorageService _storage;
    private readonly IMediaAnnotationsIndex _index;
    private readonly BehaviorSubject<AnnotationsReconciliationStatus> _status =
        new(AnnotationsReconciliationStatus.Idle);
    private readonly SemaphoreSlim _reconciliationGate = new(1, 1);
    private readonly object _rootsGate = new();
    private readonly object _backgroundGate = new();
    private readonly HashSet<string> _registeredRoots = new(PathComparer);
    private CancellationTokenSource? _backgroundCancellation;
    private Task? _backgroundTask;
    private bool _disposed;

    public MediaAnnotationsService(
        IMediaAnnotationsStorageService storage,
        IMediaAnnotationsIndex index)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _index = index ?? throw new ArgumentNullException(nameof(index));
    }

    public IObservable<AnnotationsReconciliationStatus> ReconciliationStatus =>
        _status.AsObservable();

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _index.InitializeAsync(cancellationToken);
    }

    public async Task<MediaAnnotations> GetAsync(
        string mediaPath,
        CancellationToken cancellationToken = default) =>
        (await GetSnapshotAsync(mediaPath, cancellationToken).ConfigureAwait(false)).Annotations;

    public Task<MediaAnnotationsSnapshot> GetSnapshotAsync(
        string mediaPath,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _storage.ReadAsync(mediaPath, cancellationToken);
    }

    public async Task<MediaAnnotationsSnapshot> SaveAsync(
        string mediaPath,
        MediaAnnotations annotations,
        AnnotationFileState? expectedState = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var snapshot = await _storage.WriteAsync(mediaPath, annotations, expectedState, cancellationToken)
            .ConfigureAwait(false);
        await _index.UpsertAsync(ToIndexed(snapshot), CancellationToken.None).ConfigureAwait(false);
        return snapshot;
    }

    public Task<IReadOnlyList<IndexedMediaAnnotations>> GetDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _index.GetDirectoryAsync(directoryPath, cancellationToken);
    }

    public Task<IReadOnlyList<IndexedMediaAnnotations>> SearchAsync(
        MediaAnnotationsSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _index.SearchAsync(query, cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetAllTagsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _index.GetAllTagsAsync(cancellationToken);
    }

    public async Task RegisterDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        var directory = RegisterRoot(directoryPath);
        await RunReconciliationAsync([directory], cancellationToken).ConfigureAwait(false);
    }

    public async Task RefreshDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));

        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _status.OnNext(new AnnotationsReconciliationStatus(
                AnnotationsReconciliationState.Running,
                directory,
                0));
            await _index.ReconcileDirectoryAsync(directory, cancellationToken).ConfigureAwait(false);
            _status.OnNext(new AnnotationsReconciliationStatus(
                AnnotationsReconciliationState.Completed,
                null,
                0));
        }
        catch (OperationCanceledException)
        {
            _status.OnNext(new AnnotationsReconciliationStatus(
                AnnotationsReconciliationState.Cancelled,
                directory,
                0));
            throw;
        }
        catch (Exception exception)
        {
            _status.OnNext(new AnnotationsReconciliationStatus(
                AnnotationsReconciliationState.Failed,
                directory,
                0,
                exception.Message));
            throw;
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    public async Task RegisterRootsAsync(
        IEnumerable<string> roots,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(roots);
        var registered = roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(RegisterRoot)
            .Distinct(PathComparer)
            .ToArray();
        if (registered.Length > 0)
        {
            await RunReconciliationAsync(registered, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task ReconcileRegisteredRootsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string[] roots;
        lock (_rootsGate)
        {
            roots = _registeredRoots.ToArray();
        }

        return RunReconciliationAsync(roots, cancellationToken);
    }

    public Task StartBackgroundReconciliationAsync(
        TimeSpan interval,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "The interval must be positive.");
        }

        lock (_backgroundGate)
        {
            if (_backgroundTask is { IsCompleted: false })
            {
                return Task.CompletedTask;
            }

            _backgroundCancellation?.Dispose();
            _backgroundCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _backgroundTask = RunBackgroundLoopAsync(interval, _backgroundCancellation.Token);
        }

        return Task.CompletedTask;
    }

    public async Task StopBackgroundReconciliationAsync(CancellationToken cancellationToken = default)
    {
        Task? backgroundTask;
        lock (_backgroundGate)
        {
            _backgroundCancellation?.Cancel();
            backgroundTask = _backgroundTask;
        }

        if (backgroundTask is null)
        {
            return;
        }

        try
        {
            await backgroundTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopBackgroundReconciliationAsync().ConfigureAwait(false);
        _backgroundCancellation?.Dispose();
        _reconciliationGate.Dispose();
        _status.OnCompleted();
        _status.Dispose();
    }

    private async Task RunBackgroundLoopAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await ReconcileRegisteredRootsAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _status.OnNext(new AnnotationsReconciliationStatus(
                AnnotationsReconciliationState.Cancelled,
                null,
                _status.Value.ProcessedFiles));
        }
        catch (Exception exception)
        {
            _status.OnNext(new AnnotationsReconciliationStatus(
                AnnotationsReconciliationState.Failed,
                null,
                _status.Value.ProcessedFiles,
                exception.Message));
        }
    }

    private async Task RunReconciliationAsync(
        IReadOnlyCollection<string> roots,
        CancellationToken cancellationToken)
    {
        if (roots.Count == 0)
        {
            return;
        }

        await _reconciliationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _status.OnNext(new AnnotationsReconciliationStatus(
                AnnotationsReconciliationState.Running,
                null,
                0));
            var progress = new DirectProgress<AnnotationsReconciliationStatus>(_status.OnNext);
            await _index.RebuildAsync(roots, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _status.OnNext(new AnnotationsReconciliationStatus(
                AnnotationsReconciliationState.Cancelled,
                null,
                _status.Value.ProcessedFiles));
            throw;
        }
        catch (Exception exception)
        {
            _status.OnNext(new AnnotationsReconciliationStatus(
                AnnotationsReconciliationState.Failed,
                _status.Value.CurrentPath,
                _status.Value.ProcessedFiles,
                exception.Message));
            throw;
        }
        finally
        {
            _reconciliationGate.Release();
        }
    }

    private string RegisterRoot(string root)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        lock (_rootsGate)
        {
            _registeredRoots.Add(fullPath);
        }

        return fullPath;
    }

    private static IndexedMediaAnnotations ToIndexed(MediaAnnotationsSnapshot snapshot) =>
        new(
            snapshot.MediaPath,
            snapshot.Annotations,
            snapshot.FileState.MediaLength,
            snapshot.FileState.MediaModifiedUtc,
            snapshot.FileState.StorageExists ? snapshot.FileState.StorageLength : 0,
            snapshot.FileState.StorageExists
                ? snapshot.FileState.StorageModifiedUtc
                : DateTime.MinValue,
            DateTime.UtcNow);

    private sealed class DirectProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
