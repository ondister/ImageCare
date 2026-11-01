using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading.Tasks;

using ImageCare.UI.Avalonia.ViewModels.Domain;
using ImageCare.UI.Avalonia.ViewModels.Preview;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Glance;

/// <summary>
/// Turns a timeline date selection into a selection and scroll request on the media grid.
/// </summary>
internal sealed class GlanceTimelineCoordinator : IDisposable
{
    private readonly TimelineViewModel _timeline;
    private readonly GlancePreviewGridViewModel _previewGrid;
    private readonly GlanceSelectionViewModel _selection;
    private readonly ILogger _logger;
    private CompositeDisposable? _subscriptions;

    public GlanceTimelineCoordinator(
        TimelineViewModel timeline,
        GlancePreviewGridViewModel previewGrid,
        GlanceSelectionViewModel selection,
        ILogger logger)
    {
        _timeline = timeline;
        _previewGrid = previewGrid;
        _selection = selection;
        _logger = logger;
    }

    public void Start()
    {
        _subscriptions?.Dispose();
        _subscriptions = new CompositeDisposable
        {
            _timeline.DateSelected.Subscribe(date => _ = OnTimelineDateSelectedAsync(date))
        };
    }

    public void Stop()
    {
        _subscriptions?.Dispose();
        _subscriptions = null;
    }

    public void Dispose() => Stop();

    private async Task OnTimelineDateSelectedAsync(DateTime dateTime)
    {
        try
        {
            var representativeFilePath = _timeline.GetRepresentativeFilePath(dateTime);
            GlanceMediaPreviewViewModel? targetPreview = null;
            if (!string.IsNullOrWhiteSpace(representativeFilePath))
            {
                targetPreview = _previewGrid.ImagePreviews.FirstOrDefault(
                    preview => PreviewPathUtility.PathsEqual(preview.Url, representativeFilePath));
                if (targetPreview == null)
                {
                    await _previewGrid.CreateImagePreviewFromPathAsync(representativeFilePath);
                    targetPreview = _previewGrid.ImagePreviews.FirstOrDefault(
                        preview => PreviewPathUtility.PathsEqual(preview.Url, representativeFilePath));
                }
            }

            targetPreview ??= _previewGrid.ImagePreviews.FirstOrDefault(
                preview => preview.FileDate.Date == dateTime.Date);
            if (targetPreview == null)
            {
                _logger.Warning("No overview image found for timeline date {Date}", dateTime.Date);
                return;
            }

            var index = _previewGrid.ImagePreviews.IndexOf(targetPreview);
            _selection.SelectedPreview = targetPreview;
            _previewGrid.ResetScrollToRow(index / Math.Max(1, _previewGrid.ImagesPerLine));

            await _previewGrid.LoadImageAsync(index, _previewGrid.ImageLoadToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when the overview closes or switches folders.
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Date selection failed: {Date}", dateTime);
        }
    }
}
