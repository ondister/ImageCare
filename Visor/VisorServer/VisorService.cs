using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services;

namespace VisorServer;

public sealed class VisorService : IVisorService,IDisposable
{
    private const int broadcastPort = 55125;

    private readonly DiscoveryService _discoveryService;


    public VisorService()
    {
        _discoveryService = new DiscoveryService(broadcastPort, "test");
    }

    /// <inheritdoc />
    public async Task SendMediaPreviewAsync(MediaPreview mediaPreview) { }

    /// <inheritdoc />
    public bool Start()
    {
        _discoveryService.Start();
        return true;
    }

    /// <inheritdoc />
    public void Stop()
    {
        _discoveryService.Stop();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _discoveryService.Dispose();
    }
}