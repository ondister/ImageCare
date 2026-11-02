using System.Threading;
using System.Threading.Tasks;

namespace ImageCare.UI.Avalonia.Services;

public interface IImageCareMcpServerController
{
    bool IsRunning { get; }

    string? EndpointUrl { get; }

    string? LastError { get; }

    Task StartAsync(
        int? port = null,
        string? accessToken = null,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
