using System.Threading;

namespace NordControl.Tests;

public static class TestPorts
{
    private static int _nextPort = 48000;

    public static (int udp, int tcp) NextPair()
    {
        var basePort = Interlocked.Add(ref _nextPort, 2);
        return (basePort, basePort + 1);
    }
}
