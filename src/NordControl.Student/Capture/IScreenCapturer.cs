using System;
using System.Threading;
using System.Threading.Tasks;
using NordControl.Protocol;

namespace NordControl.Student.Capture;

public interface IScreenCapturer : IDisposable
{
    Task<JpegFrame?> CaptureFrameAsync(int maxDimension = 1280, int quality = 70, CancellationToken ct = default);
}
