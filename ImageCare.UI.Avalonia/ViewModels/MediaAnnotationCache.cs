using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Domain;
using ImageCare.UI.Avalonia.ViewModels.Preview;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

/// <summary>
/// Keeps the annotation index for the active media folder and applies cached or refreshed
/// annotations to preview cards. Surface-specific behavior is supplied through optional callbacks.
/// </summary>
internal sealed class MediaAnnotationCache
{
    private readonly IMediaAnnotationsService _annotationsService;
    private readonly LocalizationManager _localization;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;

    private Dictionary<string, IndexedMediaAnnotations> _annotationsByPath = NewIndex();
    private Func<IReadOnlyList<MediaPreviewViewModel>> _previews =
        Array.Empty<MediaPreviewViewModel>;
    private Func<string> _selectedFolderPath = () => string.Empty;
    private Func<MediaPreviewViewModel?> _selectedPreview = () => null;
    private Func<CancellationToken> _folderToken = () => CancellationToken.None;
    private Func<string, CancellationToken, Task>? _reconcileRequested;

    public MediaAnnotationCache(
        IMediaAnnotationsService annotationsService,
        LocalizationManager localization,
        ILogger logger,
        SynchronizationContext synchronizationContext)
    {
        _annotationsService = annotationsService;
        _localization = localization;
        _logger = logger;
        _synchronizationContext = synchronizationContext;
    }

    /// <summary>Raised after cached annotations have been applied to all current cards.</summary>
    public event EventHandler? AnnotationsApplied;

    /// <summary>Raised after a changed media file received an authoritative snapshot.</summary>
    public event Action<MediaPreviewViewModel>? ChangedAnnotationApplied;

    public void SetSources(
        Func<IReadOnlyList<MediaPreviewViewModel>> previews,
        Func<string> selectedFolderPath,
        Func<MediaPreviewViewModel?>? selectedPreview = null,
        Func<CancellationToken>? folderToken = null)
    {
        _previews = previews ?? throw new ArgumentNullException(nameof(previews));
        _selectedFolderPath = selectedFolderPath ?? throw new ArgumentNullException(nameof(selectedFolderPath));
        _selectedPreview = selectedPreview ?? (() => null);
        _folderToken = folderToken ?? (() => CancellationToken.None);
    }

    /// <summary>
    /// Sets the callback used when an XMP sidecar cannot be attributed to one media file.
    /// </summary>
    public void SetReconcileRequest(Func<string, CancellationToken, Task> reconcileRequested)
    {
        _reconcileRequested = reconcileRequested ?? throw new ArgumentNullException(nameof(reconcileRequested));
    }

    public void Reset() => _annotationsByPath = NewIndex();

    public void ApplyCached(MediaPreviewViewModel preview)
    {
        if (!preview.MediaFormat.SupportsAnnotations)
        {
            return;
        }

        if (ReferenceEquals(preview, _selectedPreview()) && preview.AnnotationSnapshot is { } snapshot)
        {
            preview.ApplyAnnotations(
                snapshot.Annotations,
                _localization["Annotations_StateCurrent"],
                snapshot);
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
        if (!CanUseDirectory(directoryPath))
        {
            return;
        }

        try
        {
            var cached = await _annotationsService.GetDirectoryAsync(directoryPath, token)
                .ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await ApplyWhenStillCurrentAsync(directoryPath, ToIndex(cached), token).ConfigureAwait(false);
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
        if (!CanUseDirectory(directoryPath))
        {
            return;
        }

        try
        {
            await _annotationsService.RefreshDirectoryAsync(directoryPath, token).ConfigureAwait(false);
            var refreshed = await _annotationsService.GetDirectoryAsync(directoryPath, token)
                .ConfigureAwait(false);
            await ApplyWhenStillCurrentAsync(directoryPath, ToIndex(refreshed), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when a folder surface closes or switches folders.
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
        catch (OperationCanceledException)
        {
            // Expected when the active folder changes.
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to refresh changed annotations for {Path}", changedPath);
        }
    }

    private async Task ApplyWhenStillCurrentAsync(
        string directoryPath,
        Dictionary<string, IndexedMediaAnnotations> annotationsByPath,
        CancellationToken token)
    {
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _synchronizationContext.Post(
            _ =>
            {
                try
                {
                    if (!token.IsCancellationRequested &&
                        PreviewPathUtility.PathsEqual(_selectedFolderPath(), directoryPath))
                    {
                        _annotationsByPath = annotationsByPath;
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

    private string? ResolveAnnotationOwnerPath(string changedPath)
    {
        if (!PreviewPathUtility.IsXmpPath(changedPath))
        {
            return _previews()
                .FirstOrDefault(preview =>
                    preview.MediaFormat.SupportsAnnotations &&
                    PreviewPathUtility.PathsEqual(preview.Url, changedPath))
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
        if (owners.Length <= 1)
        {
            return owners.FirstOrDefault();
        }

        _logger.Warning(
            "Skipped ambiguous XMP watcher refresh for {Sidecar}; owners: {Owners}",
            changedPath,
            string.Join(", ", owners));
        var folderPath = _selectedFolderPath();
        if (_reconcileRequested != null && !string.IsNullOrWhiteSpace(folderPath))
        {
            _ = _reconcileRequested(folderPath, _folderToken());
        }

        return null;
    }

    private static bool CanUseDirectory(string directoryPath) =>
        !string.IsNullOrWhiteSpace(directoryPath) && directoryPath != DirectoryModel.RootPath;

    private static Dictionary<string, IndexedMediaAnnotations> NewIndex() =>
        new(PreviewPathUtility.PathComparer);

    private static Dictionary<string, IndexedMediaAnnotations> ToIndex(
        IReadOnlyList<IndexedMediaAnnotations> annotations) =>
        annotations.ToDictionary(item => item.MediaPath, PreviewPathUtility.PathComparer);
}
