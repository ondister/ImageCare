using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Domain;
using ImageCare.UI.Avalonia.ViewModels.Preview;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Glance;

/// <summary>
/// Keeps the per-directory annotation index for the folder overview and applies cached
/// rating/tag state to preview cards.
/// </summary>
internal sealed class GlanceAnnotationCache
{
    private readonly IMediaAnnotationsService _annotationsService;
    private readonly LocalizationManager _localization;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;

    private Dictionary<string, IndexedMediaAnnotations> _annotationsByPath = NewIndex();
    private Func<IReadOnlyList<GlanceMediaPreviewViewModel>> _previews = Array.Empty<GlanceMediaPreviewViewModel>;
    private Func<string> _selectedFolderPath = () => string.Empty;

    public GlanceAnnotationCache(
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

    public void SetSources(
        Func<IReadOnlyList<GlanceMediaPreviewViewModel>> previews,
        Func<string> selectedFolderPath)
    {
        _previews = previews ?? throw new ArgumentNullException(nameof(previews));
        _selectedFolderPath = selectedFolderPath ?? throw new ArgumentNullException(nameof(selectedFolderPath));
    }

    public void Reset() => _annotationsByPath = NewIndex();

    public void ApplyCached(MediaPreviewViewModel preview)
    {
        if (!preview.MediaFormat.SupportsAnnotations)
        {
            return;
        }

        var annotations = _annotationsByPath.TryGetValue(preview.Url, out var indexed)
            ? indexed.Annotations
            : MediaAnnotations.Empty;
        preview.ApplyAnnotations(annotations, _localization["Annotations_StateCached"]);
    }

    public async Task LoadCachedDirectoryAsync(string directoryPath, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        try
        {
            var cached = await _annotationsService.GetDirectoryAsync(directoryPath, token).ConfigureAwait(false);
            var cachedByPath = ToIndex(cached);
            token.ThrowIfCancellationRequested();
            ApplyWhenStillCurrent(directoryPath, cachedByPath);
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
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        try
        {
            await _annotationsService.RefreshDirectoryAsync(directoryPath, token).ConfigureAwait(false);
            var refreshed = await _annotationsService.GetDirectoryAsync(directoryPath, token).ConfigureAwait(false);
            ApplyWhenStillCurrent(directoryPath, ToIndex(refreshed));
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
                },
                null);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to refresh changed annotations for {Path}", changedPath);
        }
    }

    private void ApplyWhenStillCurrent(
        string directoryPath,
        Dictionary<string, IndexedMediaAnnotations> annotationsByPath)
    {
        _synchronizationContext.Post(
            _ =>
            {
                if (!PreviewPathUtility.PathsEqual(_selectedFolderPath(), directoryPath))
                {
                    return;
                }

                _annotationsByPath = annotationsByPath;
                foreach (var preview in _previews())
                {
                    ApplyCached(preview);
                }
            },
            null);
    }

    private string? ResolveAnnotationOwnerPath(string changedPath)
    {
        if (!PreviewPathUtility.IsXmpPath(changedPath))
        {
            return _previews()
                .FirstOrDefault(preview => preview.MediaFormat.SupportsAnnotations &&
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
        if (owners.Length > 1)
        {
            _logger.Warning(
                "Skipped ambiguous XMP watcher refresh for {Sidecar}; owners: {Owners}",
                changedPath,
                string.Join(", ", owners));
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
