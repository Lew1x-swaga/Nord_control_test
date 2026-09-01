using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NordControl.Protocol;

namespace NordControl.Student.Capture;

public class GdiScreenCapturer : IScreenCapturer
{
    private const int SrcCopy = 0x00CC0020;
    private const int ColorOnColor = 3;

    private static readonly ImageCodecInfo? JpegEncoder = ImageCodecInfo
        .GetImageEncoders()
        .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);

    private readonly MemoryStream _compressionStream = new(256 * 1024);
    private readonly object _captureLock = new();
    private Bitmap? _target;
    private Graphics? _targetGraphics;
    private int _targetWidth;
    private int _targetHeight;
    private Bitmap? _scratch;
    private Graphics? _scratchGraphics;
    private int _scratchWidth;
    private int _scratchHeight;
    private EncoderParameters? _encoderParams;
    private int _cachedQuality = int.MinValue;
    private bool _isDisposed;

    public Task<JpegFrame?> CaptureFrameAsync(int maxDimension = 1280, int quality = 70, CancellationToken ct = default)
    {
        if (_isDisposed || ct.IsCancellationRequested)
            return Task.FromResult<JpegFrame?>(null);

        try
        {
            return Task.FromResult(CaptureFrame(maxDimension, quality, ct));
        }
        catch
        {
            return Task.FromResult<JpegFrame?>(null);
        }
    }

    private JpegFrame? CaptureFrame(int maxDimension, int quality, CancellationToken ct)
    {
        lock (_captureLock)
        {
            if (_isDisposed || ct.IsCancellationRequested)
                return null;

            var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            var srcWidth = Math.Max(1, bounds.Width);
            var srcHeight = Math.Max(1, bounds.Height);

            int targetWidth = srcWidth;
            int targetHeight = srcHeight;
            if (srcWidth > maxDimension || srcHeight > maxDimension)
            {
                var scale = Math.Min((double)maxDimension / srcWidth, (double)maxDimension / srcHeight);
                targetWidth = Math.Max(1, (int)(srcWidth * scale));
                targetHeight = Math.Max(1, (int)(srcHeight * scale));
            }

            EnsureTarget(targetWidth, targetHeight);
            if (!TryStretchBltFromScreen(bounds, srcWidth, srcHeight, targetWidth, targetHeight)
                && !TryCopyFromScreenFallback(bounds, srcWidth, srcHeight, targetWidth, targetHeight))
            {
                return null;
            }

            if (ct.IsCancellationRequested || _isDisposed)
                return null;

            EnsureEncoder(quality);
            _compressionStream.Position = 0;
            _compressionStream.SetLength(0);

            if (JpegEncoder != null && _encoderParams != null)
            {
                _target!.Save(_compressionStream, JpegEncoder, _encoderParams);
            }
            else
            {
                _target!.Save(_compressionStream, ImageFormat.Jpeg);
            }

            var streamLength = (int)_compressionStream.Length;
            var jpegBytes = new byte[streamLength];
            Buffer.BlockCopy(_compressionStream.GetBuffer(), 0, jpegBytes, 0, streamLength);

            var timestampMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return new JpegFrame((uint)targetWidth, (uint)targetHeight, timestampMs, jpegBytes);
        }
    }

    private void EnsureTarget(int width, int height)
    {
        if (_target != null && _targetWidth == width && _targetHeight == height)
            return;

        _targetGraphics?.Dispose();
        _target?.Dispose();
        _target = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        _targetGraphics = Graphics.FromImage(_target);
        _targetGraphics.CompositingMode = CompositingMode.SourceCopy;
        _targetWidth = width;
        _targetHeight = height;
    }

    private void EnsureScratch(int width, int height)
    {
        if (_scratch != null && _scratchWidth == width && _scratchHeight == height)
            return;

        _scratchGraphics?.Dispose();
        _scratch?.Dispose();
        _scratch = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        _scratchGraphics = Graphics.FromImage(_scratch);
        _scratchWidth = width;
        _scratchHeight = height;
    }

    private void EnsureEncoder(int quality)
    {
        quality = Math.Clamp(quality, 10, 100);
        if (_cachedQuality == quality && _encoderParams != null)
            return;

        _encoderParams?.Dispose();
        _encoderParams = new EncoderParameters(1);
        _encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);
        _cachedQuality = quality;
    }

    private bool TryStretchBltFromScreen(Rectangle bounds, int srcWidth, int srcHeight, int targetWidth, int targetHeight)
    {
        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero || _targetGraphics == null)
            return false;

        var destDc = IntPtr.Zero;
        try
        {
            destDc = _targetGraphics.GetHdc();
            SetStretchBltMode(destDc, ColorOnColor);
            return StretchBlt(
                destDc, 0, 0, targetWidth, targetHeight,
                screenDc, bounds.Left, bounds.Top, srcWidth, srcHeight,
                SrcCopy);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (destDc != IntPtr.Zero)
            {
                _targetGraphics.ReleaseHdc(destDc);
            }

            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private bool TryCopyFromScreenFallback(Rectangle bounds, int srcWidth, int srcHeight, int targetWidth, int targetHeight)
    {
        try
        {
            if (_targetGraphics == null)
                return false;

            if (targetWidth == srcWidth && targetHeight == srcHeight)
            {
                _targetGraphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                return true;
            }

            EnsureScratch(srcWidth, srcHeight);
            _scratchGraphics!.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            _targetGraphics.InterpolationMode = InterpolationMode.Bilinear;
            _targetGraphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
            _targetGraphics.SmoothingMode = SmoothingMode.HighSpeed;
            _targetGraphics.DrawImage(_scratch!, 0, 0, targetWidth, targetHeight);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        lock (_captureLock)
        {
            _encoderParams?.Dispose();
            _encoderParams = null;
            _targetGraphics?.Dispose();
            _targetGraphics = null;
            _target?.Dispose();
            _target = null;
            _scratchGraphics?.Dispose();
            _scratchGraphics = null;
            _scratch?.Dispose();
            _scratch = null;
            _compressionStream.Dispose();
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(
        IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc,
        int rop);

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(IntPtr hdc, int mode);
}
