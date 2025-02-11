using ImageCare.Core.Domain;

namespace ImageCare.Core.Services;

public interface IVisorClient
{
    public IObservable<MediaPreview> ImagePreviewReceived { get; }
}