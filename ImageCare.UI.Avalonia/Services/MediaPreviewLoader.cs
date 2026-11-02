using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.MediaPreviewService;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using MapsterMapper;

namespace ImageCare.UI.Avalonia.Services;

/// <summary>
/// Creates preview cards and owns their shared metadata, bitmap loading, and unloading mechanics.
/// Viewport and quality policies remain with the owning view model.
/// </summary>
internal sealed class MediaPreviewLoader(
    IMediaPreviewService mediaPreviewService,
    IMapper mapper)
{
    private readonly object _loadedPreviewsLock = new();
    private readonly HashSet<MediaPreviewViewModel> _loadedPreviews = [];

    public async Task<TPreview?> CreateAsync<TPreview>(
        FileModel fileModel,
        CancellationToken cancellationToken)
        where TPreview : MediaPreviewViewModel
    {
        var preview = await mediaPreviewService.GetMediaPreviewAsync(
            fileModel,
            cancellationToken).ConfigureAwait(false);
        if (preview == null)
        {
            return null;
        }

        var previewViewModel = Create<TPreview>(preview);
        previewViewModel.FileDate = fileModel.CreatedDateTime ?? DateTime.MinValue;
        previewViewModel.DateTimeString = previewViewModel.FileDate.ToString("dd.MM.yyyy HH:mm");
        return previewViewModel;
    }

    public TPreview Create<TPreview>(MediaPreview preview)
        where TPreview : MediaPreviewViewModel => mapper.Map<TPreview>(preview);

    public Task<MediaPreview?> GetMediaPreviewAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        mediaPreviewService.GetMediaPreviewAsync(path, cancellationToken);

    public async Task LoadAsync(
        MediaPreviewViewModel preview,
        int decodeWidth,
        MediaPreviewSize previewSize,
        bool refreshMetadata,
        CancellationToken cancellationToken)
    {
        if (refreshMetadata || preview.Metadata == null)
        {
            var source = await mediaPreviewService.GetMediaPreviewAsync(
                preview.Url,
                cancellationToken);
            if (source == null)
            {
                return;
            }

            await LoadMetadataAsync(preview, source, cancellationToken);
        }

        await LoadBitmapAsync(preview, decodeWidth, previewSize, cancellationToken);
    }

    public async Task LoadMetadataAsync(
        MediaPreviewViewModel preview,
        MediaPreview source,
        CancellationToken cancellationToken = default)
    {
        var metadata = await mediaPreviewService.GetMediaMetadataAsync(
            source,
            cancellationToken);
        preview.FileDate = metadata.CreationDateTime;
        preview.MetadataString = metadata.GetString();
        preview.DateTimeString = metadata.CreationDateTime.ToString("dd.MM.yyyy HH:mm");
        preview.Metadata = metadata;
        preview.RotateAngle = metadata.Orientation.ToRotationAngle();
    }

    public async Task LoadBitmapAsync(
        MediaPreviewViewModel preview,
        int decodeWidth,
        MediaPreviewSize previewSize,
        CancellationToken cancellationToken)
    {
        await preview.LoadPreviewAsync(decodeWidth, previewSize, cancellationToken);
        if (preview.PreviewBitmap == null)
        {
            return;
        }

        lock (_loadedPreviewsLock)
        {
            _loadedPreviews.Add(preview);
        }
    }

    public void Unload(MediaPreviewViewModel preview)
    {
        lock (_loadedPreviewsLock)
        {
            _loadedPreviews.Remove(preview);
        }

        preview.UnloadPreview();
    }

    public void UnloadOutside(IReadOnlySet<MediaPreviewViewModel> retainedPreviews)
    {
        MediaPreviewViewModel[] previewsToUnload;
        lock (_loadedPreviewsLock)
        {
            previewsToUnload = _loadedPreviews
                .Where(preview => !retainedPreviews.Contains(preview))
                .ToArray();
            foreach (var preview in previewsToUnload)
            {
                _loadedPreviews.Remove(preview);
            }
        }

        foreach (var preview in previewsToUnload)
        {
            preview.UnloadPreview();
        }
    }

    public void UnloadAll(IEnumerable<MediaPreviewViewModel> previews)
    {
        foreach (var preview in previews)
        {
            preview.UnloadPreview();
        }

        lock (_loadedPreviewsLock)
        {
            _loadedPreviews.Clear();
        }
    }
}
