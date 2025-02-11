using System.Net;
using System.Net.Sockets;

namespace ImageCare.Visor.Server;

public static class IpHelper
{
    public static bool TryGetLocalIp(out IPAddress address)
    {
        address = IPAddress.None;

        var hostName = Dns.GetHostName();

        var ipAddresses = Dns.GetHostEntry(hostName).AddressList;

        foreach (var ip in ipAddresses)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                address = ip;

                return true;
            }
        }

        return false;
    }
}