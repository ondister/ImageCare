using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Preview;

/// <summary>
/// Keeps the per-directory annotation index in sync with the media strip and applies
/// cached rating/tag state to preview cards. Editor behaviour lives in
/// <see cref="PreviewAnnotationEditorViewModel"/>.
/// </summary>
internal sealed class PreviewAnnotationCache
{
    private readonly IMediaAnnotationsService _annotationsService;
    private readonly IFileSystemService _fileSystemService;
    private readonly LocalizationManager _localization;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;

    private Dictionary<string, IndexedMediaAnnotations> _annotationsByPath = NewIndex();
    private Func<IReadOnlyList<MediaPreviewViewModel>> _previews = Array.Empty<MediaPreviewViewModel>;
    private Func<MediaPreviewViewModel?> _selectedPreview = () => null;
    private Func<string> _selectedFolderPath = () => string.Empty;
    private Func<CancellationToken> _folderToken = () => CancellationToken.None;
    private Func<string, CancellationToken, Task>? _reconcileRequested;

    public PreviewAnnotationCache(
        IMediaAnnotationsService annotationsService,
        IFileSystemService fileSystemService,
        LocalizationManager localization,
        ILogger logger,
        SynchronizationContext synchronizationContext)
    {
        _annotationsService = annotationsService;
        _fileSystemService = fileSystemService;
        _localization = localization;
        _logger = logger;
        _synchronizationContext = synchronizationContext;
    }

    /// <summary>Raised after cached annotations have been applied to the preview cards.</summary>
    public event EventHandler? AnnotationsApplied;

    /// <summary>Raised after a single media file received a fresh annotation snapshot.</summary>
    public event Action<MediaPreviewViewModel>? ChangedAnnotationApplied;

    public void SetSources(
        Func<IReadOnlyList<MediaPreviewViewModel>> previews,
        Func<MediaPreviewViewModel?> selectedPreview,
        Func<string> selectedFolderPath,
        Func<CancellationToken> folderToken)
    {
        _previews = previews ?? throw new ArgumentNullException(nameof(previews));
        _selectedPreview = selectedPreview ?? throw new ArgumentNullException(nameof(selectedPreview));
        _selectedFolderPath = selectedFolderPath ?? throw new ArgumentNullException(nameof(selectedFolderPath));
        _folderToken = folderToken ?? throw new ArgumentNullException(nameof(folderToken));
    }

    /// <summary>
    /// Sets the callback used when an XMP sidecar cannot be attributed to a single media file.
    /// </summary>
    public void SetReconcileRequest(Func<string, CancellationToken, Task> reconcileRequested)
    {
        _reconcileRequested = reconcileRequested;
    }

    public void Reset()
    {
        _annotationsByPath = NewIndex();
    }

    public void ApplyCached(MediaPreviewViewModel preview)
    {
        if (!preview.MediaFormat.SupportsAnnotations)
        {
            return;
        }

        if (ReferenceEquals(preview, _selectedPreview()) && preview.AnnotationSnapshot is { } currentSnapshot)
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

    public void ApplyCachedToAll()
    {
        foreach (var preview in _previews())
        {
            ApplyCached(preview);
        }

        AnnotationsApplied?.Invoke(this, EventArgs.Empty);
    }

    public async Task LoadCachedDirectoryAsync(string directoryPath, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || directoryPath == DirectoryModel.RootPath)
        {
            return;
        }

        try
        {
            var cached = await _annotationsService.GetDirectoryAsync(directoryPath, token).ConfigureAwait(false);
            var cachedByPath = ToIndex(cached);
            token.ThrowIfCancellationRequested();

            var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _synchronizationContext.Post(
                _ =>
                {
                    try
                    {
                        if (!token.IsCancellationRequested &&
                            PreviewPathUtility.PathsEqual(_selectedFolderPath(), directoryPath))
                        {
                            _annotationsByPath = cachedByPath;
                            ApplyCachedToAll();
                        }
                    }
                    finally
                    {
                        applied.TrySetResult();
                    }
                },
                null);
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

    public async Task RefreshDirectoryAsync(string directoryPath, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || directoryPath == DirectoryModel.RootPath)
        {
            return;
        }

        try
        {
            await _annotationsService.RefreshDirectoryAsync(directoryPath, token).ConfigureAwait(false);
            var refreshed = await _annotationsService.GetDirectoryAsync(directoryPath, token).ConfigureAwait(false);
            var refreshedByPath = ToIndex(refreshed);

            _synchronizationContext.Post(
                _ =>
                {
                    if (!PreviewPathUtility.PathsEqual(_selectedFolderPath(), directoryPath))
                    {
                        return;
                    }

                    _annotationsByPath = refreshedByPath;
                    ApplyCachedToAll();
                },
                null);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to reconcile annotations for {Directory}", directoryPath);
        }
    }

    public async Task RefreshChangedAsync(string changedPath)
    {
        try
        {
            var ownerPath = ResolveAnnotationOwnerPath(changedPath);
            if (ownerPath == null || !File.Exists(ownerPath))
            {
                return;
            }

            var snapshot = await _annotationsService.GetSnapshotAsync(ownerPath).ConfigureAwait(false);
            _synchronizationContext.Post(
                _ =>
                {
                    var preview = _previews()
                        .FirstOrDefault(item => PreviewPathUtility.PathsEqual(item.Url, ownerPath));
                    if (preview == null)
                    {
                        return;
                    }

                    preview.ApplyAnnotations(
                        snapshot.Annotations,
                        _localization["Annotations_StateCurrent"],
                        snapshot);
                    ChangedAnnotationApplied?.Invoke(preview);
                },
                null);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to refresh changed annotations for {Path}", changedPath);
        }
    }

    private string? ResolveAnnotationOwnerPath(string changedPath)
    {
        if (!PreviewPathUtility.IsXmpPath(changedPath))
        {
            return _previews()
                .FirstOrDefault(preview => PreviewPathUtility.PathsEqual(preview.Url, changedPath))
                ?.Url;
        }

        var owners = _previews()
            .Where(preview => preview.MediaFormat.SupportsAnnotations)
            .Select(preview => new
            {
                preview.Url,
                Sidecar = XmpSidecarPath.GetExternalSidecarPath(preview.Url)
            })
            .Where(candidate => PreviewPathUtility.PathsEqual(candidate.Sidecar, changedPath))
            .Select(candidate => candidate.Url)
            .ToArray();
        if (owners.Length > 1)
        {
            _logger.Warning(
                "Skipped ambiguous XMP watcher refresh for {Sidecar}; owners: {Owners}",
                changedPath,
                string.Join(", ", owners));
            var reconcile = _reconcileRequested;
            var folderPath = _selectedFolderPath();
            if (reconcile != null && !string.IsNullOrWhiteSpace(folderPath))
            {
                _ = reconcile(folderPath, _folderToken());
            }

            return null;
        }

        return owners.FirstOrDefault();
    }

    private static Dictionary<string, IndexedMediaAnnotations> NewIndex() =>
        new(PreviewPathUtility.PathComparer);

    private static Dictionary<string, IndexedMediaAnnotations> ToIndex(
        IReadOnlyList<IndexedMediaAnnotations> annotations) =>
        annotations.ToDictionary(item => item.MediaPath, PreviewPathUtility.PathComparer);
}
