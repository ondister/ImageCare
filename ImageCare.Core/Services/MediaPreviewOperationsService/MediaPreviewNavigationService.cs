using System.Reactive.Linq;
using System.Reactive.Subjects;

using ImageCare.Core.Domain.Preview;

namespace ImageCare.Core.Services.MediaPreviewOperationsService;

internal sealed class MediaPreviewNavigationService : IDisposable
{
    private readonly Subject<SelectedMediaPreview> _imagePreviewSelectedSubject = new();
    private readonly Subject<SelectedMediaPreview> _mediaPreviewNavigationRequestedSubject = new();
    private SelectedMediaPreview? _lastSelectedMediaPreview;

    public IObservable<SelectedMediaPreview> ImagePreviewSelected =>
        _imagePreviewSelectedSubject.AsObservable();

    public IObservable<SelectedMediaPreview> MediaPreviewNavigationRequested =>
        _mediaPreviewNavigationRequestedSubject.AsObservable();

    public void SetSelected(SelectedMediaPreview selectedMediaPreview)
    {
        ArgumentNullException.ThrowIfNull(selectedMediaPreview);

        _lastSelectedMediaPreview = selectedMediaPreview;
        _imagePreviewSelectedSubject.OnNext(selectedMediaPreview);
    }

    public void RequestNavigation(SelectedMediaPreview selectedMediaPreview)
    {
        _mediaPreviewNavigationRequestedSubject.OnNext(selectedMediaPreview);
    }

    public MediaPreview? GetLastSelected() => _lastSelectedMediaPreview;

    public void Dispose()
    {
        _imagePreviewSelectedSubject.Dispose();
        _mediaPreviewNavigationRequestedSubject.Dispose();
    }
}
