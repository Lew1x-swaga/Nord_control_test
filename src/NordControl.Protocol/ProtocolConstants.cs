namespace NordControl.Protocol;

public static class ProtocolConstants
{
    public const int Version = 1;
    public const string AgentVersion = "0.1.0";
    public const int UdpPort = 47820;
    public const int TcpPort = 47821;
    public const int HeartbeatIntervalMs = 3000;
    public const int StaleStreamMs = 10_000;
    public const int ReconnectWindowMs = 120_000;
    public const int MaxFramePayload = 4 * 1024 * 1024;
    public const byte JsonMessageType = 1;
    public const byte JpegMessageType = 2;
    public const string UdpMagic = "NORD1";
    public const int PinLength = 6;
    public const int PinDigitCount = 3;
    public const int PinLetterCount = 3;
}
