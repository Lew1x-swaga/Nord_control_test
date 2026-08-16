using System;

namespace NordControl.Protocol;

public static class UdpPackets
{
    public static string Probe() => $"{ProtocolConstants.UdpMagic}|probe|v={ProtocolConstants.Version}";

    public static string Announce(string name, string ip, int tcpPort) =>
        $"{ProtocolConstants.UdpMagic}|announce|v={ProtocolConstants.Version}|name={name}|ip={ip}|tcp={tcpPort}";

    public static bool TryParseAnnounce(string packet, out string name, out string ip, out int tcpPort)
    {
        name = string.Empty;
        ip = string.Empty;
        tcpPort = 0;

        if (string.IsNullOrWhiteSpace(packet))
            return false;

        var parts = packet.Split('|');
        if (parts.Length < 3)
            return false;

        if (parts[0] != ProtocolConstants.UdpMagic)
            return false;

        if (parts[1] != "announce")
            return false;

        if (parts[2] != $"v={ProtocolConstants.Version}")
            return false;

        string? parsedName = null;
        string? parsedIp = null;
        int? parsedPort = null;

        for (int i = 3; i < parts.Length; i++)
        {
            var part = parts[i];
            var eqIdx = part.IndexOf('=');
            if (eqIdx <= 0)
                continue;

            var key = part.Substring(0, eqIdx);
            var value = part.Substring(eqIdx + 1);

            switch (key)
            {
                case "name":
                    parsedName = value;
                    break;
                case "ip":
                    parsedIp = value;
                    break;
                case "tcp":
                    if (int.TryParse(value, out var port) && port > 0 && port <= 65535)
                    {
                        parsedPort = port;
                    }
                    break;
            }
        }

        if (parsedName == null || parsedIp == null || parsedPort == null)
            return false;

        name = parsedName;
        ip = parsedIp;
        tcpPort = parsedPort.Value;
        return true;
    }
}
