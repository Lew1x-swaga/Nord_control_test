using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NordControl.Core.Helpers;

public static class LanEndpoints
{
    public static bool IsClassroomIpv4(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!IPAddress.TryParse(value.Trim(), out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = ip.GetAddressBytes();
        if (bytes[0] == 127)
        {
            return true;
        }

        if (bytes[0] == 10)
        {
            return true;
        }

        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
        {
            return true;
        }

        if (bytes[0] == 192 && bytes[1] == 168)
        {
            return true;
        }

        return false;
    }

    public static bool IsPreferredLanNic(NetworkInterfaceType type)
    {
        return type is NetworkInterfaceType.Ethernet
            or NetworkInterfaceType.Wireless80211
            or NetworkInterfaceType.GigabitEthernet
            or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.FastEthernetFx;
    }

    public static bool IsExcludedNic(NetworkInterfaceType type)
    {
        return type is NetworkInterfaceType.Tunnel
            or NetworkInterfaceType.Ppp
            or NetworkInterfaceType.Loopback;
    }

    public static string GetAnnounceIpv4(IPAddress remoteAddress)
    {
        if (IPAddress.IsLoopback(remoteAddress))
        {
            return "127.0.0.1";
        }

        var classroom = GetLocalUnicastIpv4();
        if (classroom.Count == 0)
        {
            return "127.0.0.1";
        }

        if (remoteAddress.AddressFamily == AddressFamily.InterNetwork)
        {
            var match = classroom.FirstOrDefault(ip => SameClassroomSubnet(ip, remoteAddress));
            if (match != null)
            {
                return match.ToString();
            }
        }

        return classroom[0].ToString();
    }

    public static IPAddress GetBroadcast(IPAddress address, IPAddress mask)
    {
        var ipBytes = address.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        if (ipBytes.Length != 4 || maskBytes.Length != 4)
        {
            return IPAddress.Broadcast;
        }

        var broadcast = new byte[4];
        for (var i = 0; i < 4; i++)
        {
            broadcast[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
        }

        return new IPAddress(broadcast);
    }

    public static IReadOnlyList<IPAddress> GetLocalUnicastIpv4()
    {
        var preferred = new List<IPAddress>();
        var fallback = new List<IPAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || IsExcludedNic(nic.NetworkInterfaceType))
                {
                    continue;
                }

                var target = IsPreferredLanNic(nic.NetworkInterfaceType) ? preferred : fallback;
                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (!IsAdvertisableUnicast(addr.Address))
                    {
                        continue;
                    }

                    AddUnique(target, addr.Address);
                }
            }
        }
        catch
        {
        }

        return preferred.Count > 0 ? preferred : fallback;
    }

    public static IReadOnlyList<IPEndPoint> GetUdpProbeDestinations(int udpPort)
    {
        var result = new List<IPEndPoint>
        {
            new(IPAddress.Broadcast, udpPort),
            new(IPAddress.Loopback, udpPort)
        };

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || IsExcludedNic(nic.NetworkInterfaceType))
                {
                    continue;
                }

                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork || addr.IPv4Mask == null)
                    {
                        continue;
                    }

                    if (!IsAdvertisableUnicast(addr.Address))
                    {
                        continue;
                    }

                    var broadcast = GetBroadcast(addr.Address, addr.IPv4Mask);
                    AddUnique(result, new IPEndPoint(broadcast, udpPort));
                    AddUnique(result, new IPEndPoint(addr.Address, udpPort));
                }
            }
        }
        catch
        {
        }

        return result;
    }

    private static bool IsAdvertisableUnicast(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static bool SameClassroomSubnet(IPAddress local, IPAddress remote)
    {
        var a = local.GetAddressBytes();
        var b = remote.GetAddressBytes();
        if (a.Length != 4 || b.Length != 4)
        {
            return false;
        }

        if (a[0] == 192 && a[1] == 168)
        {
            return b[0] == 192 && b[1] == 168 && a[2] == b[2];
        }

        if (a[0] == 10)
        {
            return b[0] == 10;
        }

        if (a[0] == 172 && a[1] >= 16 && a[1] <= 31)
        {
            return b[0] == 172 && b[1] >= 16 && b[1] <= 31;
        }

        return false;
    }

    private static void AddUnique(List<IPAddress> list, IPAddress address)
    {
        if (list.Any(existing => existing.Equals(address)))
        {
            return;
        }

        list.Add(address);
    }

    private static void AddUnique(List<IPEndPoint> list, IPEndPoint endpoint)
    {
        if (list.Any(e => e.Address.Equals(endpoint.Address) && e.Port == endpoint.Port))
        {
            return;
        }

        list.Add(endpoint);
    }
}
