using System;
using System.Collections.Concurrent;
using System.IO;
using System.Media;

namespace NordControl.Student.Services;

public static class SoundNotification
{
    private static readonly byte[] SoftDingWav = GenerateSoftDingWav();
    private static readonly ConcurrentDictionary<string, DateTime> LastPlayBySubject = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _lastGlobalPlay = DateTime.MinValue;
    private static readonly object Lock = new();

    public static void PlayDing(string? subject = null, int antiSpamMs = 1500)
    {
        try
        {
            var now = DateTime.UtcNow;

            lock (Lock)
            {
                if ((now - _lastGlobalPlay).TotalMilliseconds < 250)
                {
                    return;
                }

                if (!string.IsNullOrEmpty(subject))
                {
                    if (LastPlayBySubject.TryGetValue(subject, out var lastSubjectPlay))
                    {
                        if ((now - lastSubjectPlay).TotalMilliseconds < antiSpamMs)
                        {
                            return;
                        }
                    }
                    LastPlayBySubject[subject] = now;
                }

                _lastGlobalPlay = now;
            }

            using var ms = new MemoryStream(SoftDingWav);
            using var player = new SoundPlayer(ms);
            player.Play();
        }
        catch
        {
            // Suppress audio playback failures gracefully (e.g. headless machines or no audio device)
        }
    }

    private static byte[] GenerateSoftDingWav()
    {
        const int sampleRate = 44100;
        const double durationSeconds = 0.22;
        var totalSamples = (int)(sampleRate * durationSeconds);
        var pcmData = new byte[totalSamples * 2]; // 16-bit mono

        const double fundamentalFreq = 659.25; // E5
        const double overtoneFreq = 1318.5;    // E6
        const double decayConstant = 18.0;

        for (int i = 0; i < totalSamples; i++)
        {
            double t = (double)i / sampleRate;
            double envelope = Math.Exp(-decayConstant * t);

            // Fundamental + overtone harmonic
            double sampleValue = (0.75 * Math.Sin(2 * Math.PI * fundamentalFreq * t)
                               + 0.25 * Math.Sin(2 * Math.PI * overtoneFreq * t)) * envelope * 0.40;

            short pcmSample = (short)Math.Clamp(sampleValue * short.MaxValue, short.MinValue, short.MaxValue);
            pcmData[i * 2] = (byte)(pcmSample & 0xFF);
            pcmData[i * 2 + 1] = (byte)((pcmSample >> 8) & 0xFF);
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        // RIFF header
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + pcmData.Length);
        writer.Write("WAVE"u8.ToArray());

        // fmt subchunk
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);             // Subchunk1Size for PCM
        writer.Write((short)1);        // AudioFormat: 1 = PCM
        writer.Write((short)1);        // NumChannels: 1 = Mono
        writer.Write(sampleRate);     // SampleRate
        writer.Write(sampleRate * 2); // ByteRate
        writer.Write((short)2);        // BlockAlign
        writer.Write((short)16);       // BitsPerSample

        // data subchunk
        writer.Write("data"u8.ToArray());
        writer.Write(pcmData.Length);
        writer.Write(pcmData);

        return stream.ToArray();
    }
}
