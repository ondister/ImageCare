using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.MediaAnnotationsIndex;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;

using Serilog;

namespace ImageCare.UI.Avalonia.Services;

internal sealed class MediaAnnotationsCoordinator : IDisposable
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly IMediaAnnotationsService _annotationsService;
    private readonly IMediaAnnotationsIndex _index;
    private readonly IConfigurationService _configurationService;
    private readonly IMediaPreviewOperationsService _previewOperations;
    private readonly IFolderTagApplicationService _folderTagApplicationService;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _schedulerCancellation = new();
    private readonly CancellationTokenSource _workerCancellation = new();
    private readonly CompositeDisposable _subscriptions = new();
    private readonly Channel<MediaFileOperationCompleted> _operationChannel =
        Channel.CreateUnbounded<MediaFileOperationCompleted>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    private Task? _initializationTask;
    private Task? _workerTask;
    private int _cleanupStarted;
    private bool _started;
    private bool _disposed;

    public MediaAnnotationsCoordinator(
        IMediaAnnotationsService annotationsService,
        IMediaAnnotationsIndex index,
        IConfigurationService configurationService,
        IMediaPreviewOperationsService previewOperations,
        IFolderTagApplicationService folderTagApplicationService,
        ILogger logger)
    {
        _annotationsService = annotationsService;
        _index = index;
        _configurationService = configurationService;
        _previewOperations = previewOperations;
        _folderTagApplicationService = folderTagApplicationService;
        _logger = logger;
    }

    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        _subscriptions.Add(_previewOperations.FileOperationCompleted.Subscribe(operation =>
        {
            if (!_operationChannel.Writer.TryWrite(operation))
            {
                _logger.Warning("Annotation index operation queue is closed for {Operation} {Path}",
                    operation.Kind, operation.SourcePath);
            }
        }));
        _workerTask = RunOperationWorkerAsync(_workerCancellation.Token);
        _initializationTask = RunInitializationAndReconciliationAsync(_schedulerCancellation.Token);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _subscriptions.Dispose();
        _operationChannel.Writer.TryComplete();
        _schedulerCancellation.Cancel();

        var startupFinished = WaitBounded(
            _initializationTask,
            ShutdownTimeout,
            "annotation startup reconciliation");
        var workerFinished = WaitBounded(
            _workerTask,
            ShutdownTimeout,
            "annotation operation worker");
        if (!workerFinished)
        {
            _workerCancellation.Cancel();
            workerFinished = WaitBounded(
                _workerTask,
                TimeSpan.FromSeconds(1),
                "cancelled annotation operation worker");
        }

        if (startupFinished && workerFinished)
        {
            CleanupResourcesAsync().GetAwaiter().GetResult();
            return;
        }

        _workerCancellation.Cancel();
        ScheduleDeferredCleanup();
    }

    private async Task RunInitializationAndReconciliationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _annotationsService.InitializeAsync(cancellationToken).ConfigureAwait(false);
            var roots = NormalizeRoots(_configurationService.Configuration.Value.MediaSearchRootPaths);
            if (roots.Count > 0)
            {
                // One startup pass warms and incrementally updates the persisted index. Current
                // folders are maintained by watcher/shallow reconciliation; Settings owns the
                // explicit full Rescan. Do not add a hidden recurring full-root traversal here.
                await _annotationsService.RegisterRootsAsync(roots, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to perform startup media annotations reconciliation");
        }
    }

    private async Task RunOperationWorkerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var operation in _operationChannel.Reader.ReadAllAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                await SynchronizeOperationAsync(operation, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task SynchronizeOperationAsync(
        MediaFileOperationCompleted operation,
        CancellationToken cancellationToken)
    {
        try
        {
            if (operation.Kind == MediaFileOperationKind.Delete && IsImagePath(operation.SourcePath))
            {
                await _index.RemoveAsync(operation.SourcePath, cancellationToken).ConfigureAwait(false);
            }

            if (operation.Kind == MediaFileOperationKind.Move
                && operation.DestinationPath is { } movedPath
                && IsImagePath(operation.SourcePath)
                && IsImagePath(movedPath))
            {
                await _index.MoveAsync(operation.SourcePath, movedPath, cancellationToken).ConfigureAwait(false);
            }

            if (operation.Kind is MediaFileOperationKind.Copy or MediaFileOperationKind.Move or MediaFileOperationKind.Restore
                && operation.DestinationPath is { } destinationPath
                && IsImagePath(destinationPath)
                && File.Exists(destinationPath))
            {
                if (operation.Kind is MediaFileOperationKind.Copy or MediaFileOperationKind.Move)
                {
                    await ApplyFolderTagsAsync(operation.Kind, destinationPath, cancellationToken)
                        .ConfigureAwait(false);
                }

                var snapshot = await _annotationsService.GetSnapshotAsync(destinationPath, cancellationToken)
                    .ConfigureAwait(false);
                await _index.UpsertAsync(ToIndexed(snapshot), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to synchronize annotation index after {Operation} for {Path}",
                operation.Kind, operation.SourcePath);
        }
    }

    private async Task ApplyFolderTagsAsync(
        MediaFileOperationKind kind,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (!_configurationService.Configuration.Value.FolderTagsEnabled)
        {
            return;
        }

        try
        {
            var action = kind == MediaFileOperationKind.Move ? "move" : "copy";
            await _folderTagApplicationService.ApplyToFileAsync(destinationPath, action, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Folder tags are best-effort: a failure must not break the index synchronisation
            // nor the completed file operation.
            _logger.Warning(exception, "Failed to apply folder tags after {Operation} for {Path}",
                kind, destinationPath);
        }
    }

    private void ScheduleDeferredCleanup()
    {
        var startup = _initializationTask ?? Task.CompletedTask;
        var worker = _workerTask ?? Task.CompletedTask;
        _ = Task.WhenAll(startup, worker).ContinueWith(
                static async (_, state) =>
                    await ((MediaAnnotationsCoordinator)state!).CleanupResourcesAsync().ConfigureAwait(false),
                this,
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default)
            .Unwrap();
    }

    private async Task CleanupResourcesAsync()
    {
        if (Interlocked.Exchange(ref _cleanupStarted, 1) != 0)
        {
            return;
        }

        try
        {
            await _annotationsService.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to dispose media annotations service cleanly");
        }
        finally
        {
            _workerCancellation.Cancel();
            _schedulerCancellation.Dispose();
            _workerCancellation.Dispose();
        }
    }

    private bool WaitBounded(Task? task, TimeSpan timeout, string operation)
    {
        if (task is null)
        {
            return true;
        }

        try
        {
            task.WaitAsync(timeout).GetAwaiter().GetResult();
            return true;
        }
        catch (TimeoutException)
        {
            _logger.Warning("Timed out waiting for {Operation} during shutdown", operation);
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed while waiting for {Operation} during shutdown", operation);
            return true;
        }
    }

    private static IReadOnlyList<string> NormalizeRoots(IEnumerable<string>? roots) =>
        (roots ?? [])
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToArray();

    private static bool IsImagePath(string path)
    {
        return MediaFormat.GetMediaFormatByExtension(Path.GetExtension(path))?.SupportsAnnotations == true;
    }

    private static IndexedMediaAnnotations ToIndexed(MediaAnnotationsSnapshot snapshot) =>
        new(
            snapshot.MediaPath,
            snapshot.Annotations,
            snapshot.FileState.MediaLength,
            snapshot.FileState.MediaModifiedUtc,
            snapshot.FileState.StorageExists ? snapshot.FileState.StorageLength : 0,
            snapshot.FileState.StorageExists ? snapshot.FileState.StorageModifiedUtc : DateTime.MinValue,
            DateTime.UtcNow);
}
