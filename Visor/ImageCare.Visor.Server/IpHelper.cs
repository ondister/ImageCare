using System.Net;
using System.Net.Sockets;

namespace ImageCare.Visor.Server;

public static class IpHelper
{
    public static bool TryGetLocalIp(out IEnumerable<IPAddress> addresses)
    {
        addresses = new List<IPAddress>();

        var hostName = Dns.GetHostName();

        var ipAddresses = Dns.GetHostEntry(hostName).AddressList;

        addresses = ipAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork);

        return addresses.Any();
    }
}