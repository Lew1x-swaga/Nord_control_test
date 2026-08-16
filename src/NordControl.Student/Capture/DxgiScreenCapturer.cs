using System;
using System.Threading;
using System.Threading.Tasks;
using NordControl.Protocol;

namespace NordControl.Student.Capture;

public class DxgiScreenCapturer : IScreenCapturer
{
    private readonly GdiScreenCapturer _gdiFallback = new();
    private bool _isDisposed;

    public async Task<JpegFrame?> CaptureFrameAsync(int maxDimension = 1280, int quality = 70, CancellationToken ct = default)
    {
        if (_isDisposed || ct.IsCancellationRequested)
            return null;

        try
        {
            // Primary attempt: Fallback to GDI capturer for desktop capture across all desktop/session states
            return await _gdiFallback.CaptureFrameAsync(maxDimension, quality, ct);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _gdiFallback.Dispose();
    }
}
