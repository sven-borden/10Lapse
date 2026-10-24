using System.Buffers.Binary;

namespace TenLapse.Tests.Media;

/// <summary>Writes small 16-bit PCM WAV files of sine tones, for the music tests (#50): every Windows has their decoder.</summary>
internal static class WaveFiles
{
    /// <summary>One part of a song: <paramref name="Seconds"/> of a <paramref name="Frequency"/> Hz sine at <paramref name="Amplitude"/> (0 to 1; 0 is silence).</summary>
    public readonly record struct Tone(double Seconds, double Frequency, double Amplitude);

    /// <summary>Writes <paramref name="tones"/> one after the other to <paramref name="path"/>, every channel the same.</summary>
    public static string Write(string path, int rate, int channels, params Tone[] tones)
    {
        var frames = tones.Sum(tone => (int)Math.Round(tone.Seconds * rate));
        var dataBytes = frames * channels * 2;
        var bytes = new byte[44 + dataBytes];
        var span = bytes.AsSpan();
        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + dataBytes);
        "WAVEfmt "u8.CopyTo(span[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], rate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], rate * channels * 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], (short)(channels * 2));
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataBytes);
        var at = 44;
        var frame = 0;
        foreach (var tone in tones)
        {
            var count = (int)Math.Round(tone.Seconds * rate);
            for (var i = 0; i < count; i++, frame++)
            {
                var value = (short)Math.Round(tone.Amplitude * 32767 * Math.Sin(2 * Math.PI * tone.Frequency * frame / rate));
                for (var channel = 0; channel < channels; channel++, at += 2)
                {
                    BinaryPrimitives.WriteInt16LittleEndian(span[at..], value);
                }
            }
        }

        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>The root mean square of the left channel of interleaved stereo <paramref name="stereo"/> from <paramref name="start"/> s for <paramref name="length"/> s at <paramref name="rate"/>.</summary>
    public static double Rms(float[] stereo, int rate, double start, double length)
    {
        var first = (int)(start * rate);
        var count = (int)(length * rate);
        var sum = 0.0;
        for (var i = first; i < first + count; i++)
        {
            var value = stereo[i * 2];
            sum += value * value;
        }

        return Math.Sqrt(sum / count);
    }

    /// <summary>How many times the left channel crosses zero upwards between <paramref name="start"/> and <paramref name="start"/> + <paramref name="length"/> seconds: a tone's frequency × the length.</summary>
    public static int RisingCrossings(float[] stereo, int rate, double start, double length)
    {
        var first = (int)(start * rate);
        var count = (int)(length * rate);
        var crossings = 0;
        for (var i = first + 1; i < first + count; i++)
        {
            if (stereo[(i - 1) * 2] < 0 && stereo[i * 2] >= 0)
            {
                crossings++;
            }
        }

        return crossings;
    }
}
