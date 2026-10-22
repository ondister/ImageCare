using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.MediaAnnotationsService;

using Serilog;

namespace ImageCare.Core.Services.FolderTagService;

public sealed class FolderTagApplicationService : IFolderTagApplicationService
{
    private const int MaxParallelism = 6;

    private readonly IMediaAnnotationsService _annotationsService;
    private readonly IFolderTagsStore _store;
    private readonly IFileSystemService _fileSystemService;
    private readonly IConfigurationService _configurationService;
    private readonly FolderTagApplicationLog _log;
    private readonly ILogger _logger;
    private readonly BehaviorSubject<FolderTagApplicationStatus> _status =
        new(FolderTagApplicationStatus.Idle);
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private bool _disposed;

    public FolderTagApplicationService(
        IMediaAnnotationsService annotationsService,
        IFolderTagsStore store,
        IFileSystemService fileSystemService,
        IConfigurationService configurationService,
        FolderTagApplicationLog log,
        ILogger logger)
    {
        _annotationsService = annotationsService ?? throw new ArgumentNullException(nameof(annotationsService));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IObservable<FolderTagApplicationStatus> ApplicationStatus => _status.AsObservable();

    public IReadOnlyList<string> GetEffectiveTags(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return [];
        }

        return Resolve(directoryPath);
    }

    public async Task<FolderTagApplicationResult> ApplyToFileAsync(
        string mediaFilePath,
        string action,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaFilePath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(mediaFilePath));
        if (string.IsNullOrEmpty(directory))
        {
            return new FolderTagApplicationResult(mediaFilePath, FolderTagApplicationOutcome.Failed, []);
        }

        var tags = Resolve(directory);
        return await ApplyAsync(mediaFilePath, directory, tags, action, cancellationToken).ConfigureAwait(false);
    }

    public async Task ApplyToDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        var directory = Path.GetFullPath(directoryPath);
        var tags = Resolve(directory);

        await _runGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (tags.Count == 0)
            {
                _status.OnNext(new FolderTagApplicationStatus(
                    FolderTagApplicationState.Completed,
                    null,
                    0,
                    0,
                    0,
                    0,
                    0));
                return;
            }

            var files = EnumerateMediaFiles(directory);
            if (files.Count == 0)
            {
                _status.OnNext(new FolderTagApplicationStatus(
                    FolderTagApplicationState.Completed,
                    null,
                    0,
                    0,
                    0,
                    0,
                    0));
                return;
            }

            _status.OnNext(new FolderTagApplicationStatus(
                FolderTagApplicationState.Running,
                null,
                0,
                files.Count,
                0,
                0,
                0));

            var processed = 0;
            var updated = 0;
            var skipped = 0;
            var failed = 0;

            try
            {
                await Parallel.ForEachAsync(
                    files,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = MaxParallelism,
                        CancellationToken = cancellationToken
                    },
                    async (file, token) =>
                    {
                        FolderTagApplicationOutcome outcome;
                        try
                        {
                            var result = await ApplyAsync(file, directory, tags, "manual", token)
                                .ConfigureAwait(false);
                            outcome = result.Outcome;
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            _logger.Warning(exception, "Folder tag application failed for {Path}", file);
                            outcome = FolderTagApplicationOutcome.Failed;
                        }

                        switch (outcome)
                        {
                            case FolderTagApplicationOutcome.Updated:
                                Interlocked.Increment(ref updated);
                                break;
                            case FolderTagApplicationOutcome.Skipped:
                                Interlocked.Increment(ref skipped);
                                break;
                            default:
                                Interlocked.Increment(ref failed);
                                break;
                        }

                        var current = Interlocked.Increment(ref processed);
                        _status.OnNext(new FolderTagApplicationStatus(
                            FolderTagApplicationState.Running,
                            file,
                            current,
                            files.Count,
                            Volatile.Read(ref updated),
                            Volatile.Read(ref skipped),
                            Volatile.Read(ref failed)));
                    }).ConfigureAwait(false);

                _status.OnNext(new FolderTagApplicationStatus(
                    FolderTagApplicationState.Completed,
                    null,
                    processed,
                    files.Count,
                    updated,
                    skipped,
                    failed));
            }
            catch (OperationCanceledException)
            {
                _status.OnNext(new FolderTagApplicationStatus(
                    FolderTagApplicationState.Cancelled,
                    null,
                    Volatile.Read(ref processed),
                    files.Count,
                    Volatile.Read(ref updated),
                    Volatile.Read(ref skipped),
                    Volatile.Read(ref failed)));
                throw;
            }
        }
        finally
        {
            _runGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _status.OnCompleted();
        _status.Dispose();
        _runGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private IReadOnlyList<string> Resolve(string directoryPath)
    {
        try
        {
            return FolderTagsInheritance.Resolve(
                directoryPath,
                path => _store.Read(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.Warning(exception, "Failed to resolve folder tags for {Directory}", directoryPath);
            return [];
        }
    }

    private async Task<FolderTagApplicationResult> ApplyAsync(
        string mediaFilePath,
        string sourceDirectory,
        IReadOnlyList<string> folderTags,
        string action,
        CancellationToken cancellationToken)
    {
        if (folderTags.Count == 0)
        {
            return new FolderTagApplicationResult(mediaFilePath, FolderTagApplicationOutcome.Skipped, []);
        }

        var snapshot = await _annotationsService.GetSnapshotAsync(mediaFilePath, cancellationToken)
            .ConfigureAwait(false);
        var existing = snapshot.Annotations.Tags;
        var existingKeys = new HashSet<string>(existing.Select(MediaAnnotations.GetTagKey), StringComparer.Ordinal);

        var added = folderTags
            .Where(tag => !existingKeys.Contains(MediaAnnotations.GetTagKey(tag)))
            .ToArray();
        if (added.Length == 0)
        {
            // Idempotent: nothing new to write, leave the file untouched.
            return new FolderTagApplicationResult(mediaFilePath, FolderTagApplicationOutcome.Skipped, []);
        }

        var merged = new MediaAnnotations(
            snapshot.Annotations.Rating,
            existing.Concat(added));
        await _annotationsService.SaveAsync(
                mediaFilePath,
                merged,
                snapshot.FileState,
                cancellationToken)
            .ConfigureAwait(false);

        WriteLog(action, mediaFilePath, sourceDirectory, added);
        return new FolderTagApplicationResult(mediaFilePath, FolderTagApplicationOutcome.Updated, added);
    }

    private void WriteLog(string action, string mediaPath, string sourceDirectory, IReadOnlyList<string> addedTags)
    {
        if (!_configurationService.Configuration.Value.FolderTagApplicationLogEnabled)
        {
            return;
        }

        _log.Append(action, mediaPath, sourceDirectory, addedTags);
    }

    private List<string> EnumerateMediaFiles(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory)
                .Where(path => MediaFormat.IsSupportedExtension(Path.GetExtension(path)))
                .Where(path => !Path.GetFileName(path).Equals(FolderTagsFile.FileName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Failed to enumerate media files in {Directory}", directory);
            return [];
        }
    }
}
