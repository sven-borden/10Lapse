using Microsoft.Graphics.Canvas;
using Windows.Foundation;
using Windows.UI;

namespace TenLapse.Media.Inspection;

/// <summary>
/// A frame number drawn as a row of black and white cells, readable back from a
/// decoded frame after scaling, cropping and lossy compression. Tests and the
/// spike harness stamp source frames with it and check, after encoding and
/// decoding, that frame n of the file shows number n: no frame dropped,
/// duplicated, reordered or overwritten while the encoder still held it.
/// </summary>
/// <remarks>
/// The cells span the whole width in a band from 35 % to 65 % of the height,
/// so any centred fill crop to an aspect between 3:2 and 21:9 keeps the band's
/// middle row, where <see cref="Read"/> samples.
/// </remarks>
public static class FrameCode
{
    /// <summary>Bits drawn: frame numbers 0 to 4,095.</summary>
    public const int Bits = 12;

    /// <summary>The largest number the code can hold.</summary>
    public const int MaxValue = (1 << Bits) - 1;

    private const float BandTop = 0.35f;
    private const float BandHeight = 0.30f;

    /// <summary>Draws <paramref name="value"/> across a frame of the given size (in DIPs), most significant bit on the left.</summary>
    public static void Draw(CanvasDrawingSession session, int value, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxValue);

        var cellWidth = width / Bits;
        var top = height * BandTop;
        var bandHeight = height * BandHeight;
        for (var bit = 0; bit < Bits; bit++)
        {
            var on = ((value >> (Bits - 1 - bit)) & 1) == 1;
            var color = on ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 0, 0, 0);
            session.FillRectangle(new Rect(bit * cellWidth, top, cellWidth, bandHeight), color);
        }
    }

    /// <summary>Reads the number back from a BGRA frame, sampling the centre of each cell on the middle row.</summary>
    public static int Read(ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, Bits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var row = height / 2;
        var value = 0;
        for (var bit = 0; bit < Bits; bit++)
        {
            var x = (int)((bit + 0.5) * width / Bits);
            var offset = (row * stride) + (x * 4);
            var luma = (bgra[offset] + (2 * bgra[offset + 1]) + bgra[offset + 2]) / 4;
            value = (value << 1) | (luma >= 128 ? 1 : 0);
        }

        return value;
    }
}
