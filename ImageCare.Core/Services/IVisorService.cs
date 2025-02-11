using ImageCare.Core.Domain;

namespace ImageCare.Core.Services;

public interface IVisorService
{
    Task SendMediaPreviewAsync(MediaPreview mediaPreview);

    bool Start();

    void Stop();
}