using System.Net.Sockets;
using System.Text;

namespace VisorClient;

internal sealed class DiscoveryClient : IDisposable
{
    private readonly UdpClient _udpClient;

    public DiscoveryClient(int broadcastPort)
    {
        _udpClient = new UdpClient(broadcastPort);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _udpClient.Dispose();
    }

    public async Task<string> DiscoverServerAsync(TimeSpan timeout = default)
    {
        var receivedString = string.Empty;
        var cancellationTokenSource = new CancellationTokenSource(timeout);

        while (string.IsNullOrWhiteSpace(receivedString) || !cancellationTokenSource.IsCancellationRequested)
        {
            var receivedBytes = await _udpClient.ReceiveAsync();
            receivedString = Encoding.UTF8.GetString(receivedBytes.Buffer);

            return receivedString;
        }

        cancellationTokenSource.Dispose();

        return receivedString;
    }
}