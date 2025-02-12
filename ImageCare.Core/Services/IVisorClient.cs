using ImageCare.Core.Domain.Preview;

namespace ImageCare.Core.Services;

public interface IVisorClient
{
    public IObservable<MediaPreview> ImagePreviewReceived { get; }
}