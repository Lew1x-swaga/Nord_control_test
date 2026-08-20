using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NordControl.Core.Helpers;

public readonly record struct LanUnicast(IPAddress Address, IPAddress Mask, bool IsPreferredLan);

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

    public static bool IsVpnAdapterName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var n = name.ToUpperInvariant();
        return n.Contains("VPN", StringComparison.Ordinal)
            || n.Contains("WIREGUARD", StringComparison.Ordinal)
            || n.Contains("WINTUN", StringComparison.Ordinal)
            || n.Contains("NORDLYNX", StringComparison.Ordinal)
            || n.Contains("OPENVPN", StringComparison.Ordinal)
            || n.Contains("TAP-WINDOWS", StringComparison.Ordinal)
            || n.Contains("TAP-WIN", StringComparison.Ordinal)
            || n.Contains("TUNNEL", StringComparison.Ordinal)
            || n.Contains("ZEROTIER", StringComparison.Ordinal)
            || n.Contains("ZERO TIER", StringComparison.Ordinal)
            || n.Contains("HAMACHI", StringComparison.Ordinal)
            || n.Contains("TAILSCALE", StringComparison.Ordinal)
            || n.Contains("RADMIN", StringComparison.Ordinal)
            || n.Contains("SOFTETHER", StringComparison.Ordinal)
            || n.Contains("PROTON", StringComparison.Ordinal)
            || n.Contains("MULLVAD", StringComparison.Ordinal)
            || n.Contains("CLOUDFLARE", StringComparison.Ordinal)
            || n.Contains("WARP", StringComparison.Ordinal);
    }

    public static bool SameIpv4Subnet(IPAddress address, IPAddress mask, IPAddress other)
    {
        var a = address.GetAddressBytes();
        var m = mask.GetAddressBytes();
        var b = other.GetAddressBytes();
        if (a.Length != 4 || m.Length != 4 || b.Length != 4)
        {
            return false;
        }

        if (m.All(x => x == 0))
        {
            return false;
        }

        for (var i = 0; i < 4; i++)
        {
            if ((a[i] & m[i]) != (b[i] & m[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static string GetAnnounceIpv4(IPAddress remoteAddress)
    {
        return SelectAnnounceIpv4(remoteAddress, GetLanUnicasts());
    }

    public static string SelectAnnounceIpv4(IPAddress remoteAddress, IReadOnlyList<LanUnicast> locals)
    {
        if (IPAddress.IsLoopback(remoteAddress))
        {
            return "127.0.0.1";
        }

        if (locals.Count == 0)
        {
            return "127.0.0.1";
        }

        LanUnicast? preferredMatch = null;
        LanUnicast? anyMatch = null;
        foreach (var local in locals)
        {
            if (!SameIpv4Subnet(local.Address, local.Mask, remoteAddress))
            {
                continue;
            }

            if (local.IsPreferredLan)
            {
                preferredMatch = local;
                break;
            }

            anyMatch ??= local;
        }

        if (preferredMatch.HasValue)
        {
            return preferredMatch.Value.Address.ToString();
        }

        if (anyMatch.HasValue)
        {
            return anyMatch.Value.Address.ToString();
        }

        var preferred = locals.FirstOrDefault(l => l.IsPreferredLan);
        if (preferred.Address != null)
        {
            return preferred.Address.ToString();
        }

        return locals[0].Address.ToString();
    }

    public static string? PickReachableTeacherIpv4(
        string? announcedIp,
        IPAddress? remoteAddress,
        IReadOnlyList<LanUnicast> localUnicasts)
    {
        var candidates = new List<string>();
        AddClassroomCandidate(candidates, announcedIp);
        if (remoteAddress != null)
        {
            AddClassroomCandidate(candidates, remoteAddress.ToString());
        }

        var preferred = MatchCandidate(candidates, localUnicasts, nic => nic.IsPreferredLan);
        if (preferred != null)
        {
            return preferred;
        }

        var anySubnet = MatchCandidate(candidates, localUnicasts, _ => true);
        if (anySubnet != null)
        {
            return anySubnet;
        }

        return candidates.Count > 0 ? candidates[0] : null;
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

    public static IReadOnlyList<LanUnicast> GetLanUnicasts()
    {
        var preferred = new List<LanUnicast>();
        var fallback = new List<LanUnicast>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || IsExcludedNic(nic.NetworkInterfaceType))
                {
                    continue;
                }

                var vpnLike = IsVpnAdapterName(nic.Name) || IsVpnAdapterName(nic.Description);
                var isPreferred = IsPreferredLanNic(nic.NetworkInterfaceType) && !vpnLike;

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

                    var item = new LanUnicast(addr.Address, addr.IPv4Mask, isPreferred);
                    if (isPreferred)
                    {
                        AddUnique(preferred, item);
                    }
                    else
                    {
                        AddUnique(fallback, item);
                    }
                }
            }
        }
        catch
        {
        }

        preferred.AddRange(fallback);
        return preferred;
    }

    public static IReadOnlyList<IPAddress> GetLocalUnicastIpv4()
    {
        var all = GetLanUnicasts();
        var preferred = all.Where(u => u.IsPreferredLan).Select(u => u.Address).ToList();
        return preferred.Count > 0 ? preferred : all.Select(u => u.Address).ToList();
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
            foreach (var nic in GetLanUnicasts())
            {
                var broadcast = GetBroadcast(nic.Address, nic.Mask);
                AddUnique(result, new IPEndPoint(broadcast, udpPort));
                AddUnique(result, new IPEndPoint(nic.Address, udpPort));
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

    private static void AddClassroomCandidate(List<string> candidates, string? value)
    {
        if (!IsClassroomIpv4(value))
        {
            return;
        }

        var normalized = value!.Trim();
        if (candidates.Any(existing => existing == normalized))
        {
            return;
        }

        candidates.Add(normalized);
    }

    private static string? MatchCandidate(
        List<string> candidates,
        IReadOnlyList<LanUnicast> localUnicasts,
        Func<LanUnicast, bool> nicFilter)
    {
        foreach (var candidate in candidates)
        {
            if (!IPAddress.TryParse(candidate, out var ip))
            {
                continue;
            }

            foreach (var local in localUnicasts)
            {
                if (!nicFilter(local))
                {
                    continue;
                }

                if (SameIpv4Subnet(local.Address, local.Mask, ip))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static void AddUnique(List<LanUnicast> list, LanUnicast item)
    {
        if (list.Any(existing => existing.Address.Equals(item.Address)))
        {
            return;
        }

        list.Add(item);
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
