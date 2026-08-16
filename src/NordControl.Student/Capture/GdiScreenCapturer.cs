using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NordControl.Protocol;

namespace NordControl.Student.Capture;

public class GdiScreenCapturer : IScreenCapturer
{
    private static readonly ImageCodecInfo? JpegEncoder = ImageCodecInfo
        .GetImageEncoders()
        .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);

    private bool _isDisposed;

    public Task<JpegFrame?> CaptureFrameAsync(int maxDimension = 1280, int quality = 70, CancellationToken ct = default)
    {
        if (_isDisposed || ct.IsCancellationRequested)
            return Task.FromResult<JpegFrame?>(null);

        return Task.Run(() =>
        {
            try
            {
                if (ct.IsCancellationRequested || _isDisposed)
                    return null;

                var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
                var srcWidth = Math.Max(1, bounds.Width);
                var srcHeight = Math.Max(1, bounds.Height);

                using var rawBitmap = new Bitmap(srcWidth, srcHeight, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(rawBitmap))
                {
                    g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                }

                if (ct.IsCancellationRequested)
                    return null;

                int targetWidth = srcWidth;
                int targetHeight = srcHeight;

                if (srcWidth > maxDimension || srcHeight > maxDimension)
                {
                    double scale = Math.Min((double)maxDimension / srcWidth, (double)maxDimension / srcHeight);
                    targetWidth = Math.Max(1, (int)(srcWidth * scale));
                    targetHeight = Math.Max(1, (int)(srcHeight * scale));
                }

                Bitmap targetBitmap;
                bool needsDisposeTarget = false;

                if (targetWidth != srcWidth || targetHeight != srcHeight)
                {
                    targetBitmap = new Bitmap(targetWidth, targetHeight, PixelFormat.Format24bppRgb);
                    needsDisposeTarget = true;
                    using var g = Graphics.FromImage(targetBitmap);
                    g.InterpolationMode = InterpolationMode.Bilinear;
                    g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                    g.SmoothingMode = SmoothingMode.HighSpeed;
                    g.DrawImage(rawBitmap, 0, 0, targetWidth, targetHeight);
                }
                else
                {
                    targetBitmap = rawBitmap;
                }

                try
                {
                    using var ms = new MemoryStream();
                    if (JpegEncoder != null)
                    {
                        using var encoderParams = new EncoderParameters(1);
                        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)Math.Clamp(quality, 10, 100));
                        targetBitmap.Save(ms, JpegEncoder, encoderParams);
                    }
                    else
                    {
                        targetBitmap.Save(ms, ImageFormat.Jpeg);
                    }

                    var jpegBytes = ms.ToArray();
                    var timestampMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    return (JpegFrame?)new JpegFrame((uint)targetWidth, (uint)targetHeight, timestampMs, jpegBytes);
                }
                finally
                {
                    if (needsDisposeTarget)
                    {
                        targetBitmap.Dispose();
                    }
                }
            }
            catch
            {
                return null;
            }
        }, ct);
    }

    public void Dispose()
    {
        _isDisposed = true;
    }
}
