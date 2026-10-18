namespace TenLapse.Media.Inspection;

/// <summary>
/// One decoded frame, valid only during the <see cref="DecodedFrameHandler"/>
/// call that receives it: the pixels live in a Media Foundation buffer that is
/// unlocked and released right after.
/// </summary>
public readonly ref struct DecodedVideoFrame
{
    internal DecodedVideoFrame(int index, long time, int width, int height, int stride, ReadOnlySpan<byte> pixels)
    {
        Index = index;
        Time = time;
        Width = width;
        Height = height;
        Stride = stride;
        Pixels = pixels;
    }

    /// <summary>The frame's position in presentation order, from 0.</summary>
    public int Index { get; }

    /// <summary>The presentation time in 100 ns ticks.</summary>
    public long Time { get; }

    /// <summary>Visible width in pixels.</summary>
    public int Width { get; }

    /// <summary>Visible height in pixels.</summary>
    public int Height { get; }

    /// <summary>Bytes from the start of one row to the next, always positive: row 0 is the top row.</summary>
    public int Stride { get; }

    /// <summary>BGRA, 4 bytes per pixel, top-down, <see cref="Height"/> rows of <see cref="Stride"/> bytes.</summary>
    public ReadOnlySpan<byte> Pixels { get; }
}

/// <summary>Receives one decoded frame from <see cref="VideoFileReader.ReadFrames"/>.</summary>
public delegate void DecodedFrameHandler(DecodedVideoFrame frame);
