using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VisorServer;

internal sealed class DiscoveryService:IDisposable
{
    private readonly string _serverAddressToBroadcast;
    private readonly UdpClient _udpClient;
    private readonly IPEndPoint _endPoint;
    private CancellationTokenSource? _cancellationTokenSource;

    public DiscoveryService(int broadcastPort, string serverAddressToBroadcast)
    {
        _serverAddressToBroadcast = serverAddressToBroadcast;
        _udpClient = new UdpClient
        {
            EnableBroadcast = true
        };

        _endPoint = new IPEndPoint(IPAddress.Broadcast, broadcastPort);
    }

    public void Start()
    {
        Stop();

        _cancellationTokenSource = new CancellationTokenSource();

        _ = BroadcastServerAddressAsync(_cancellationTokenSource.Token);
    }

    public void Stop()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    private async Task BroadcastServerAddressAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var messageBytes = Encoding.UTF8.GetBytes(_serverAddressToBroadcast);
            await _udpClient.SendAsync(messageBytes, messageBytes.Length, _endPoint);
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _udpClient.Dispose();
        _cancellationTokenSource?.Dispose();
    }
}