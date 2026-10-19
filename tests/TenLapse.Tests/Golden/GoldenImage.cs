using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;

namespace TenLapse.Tests.Golden;

/// <summary>
/// A BGRA frame, top-down and tightly packed: a frame decoded from an export, or the reference the preview's call
/// draws. Holds the measurements the golden tests compare: luma, black bars, edges along a line, mean error.
/// </summary>
internal sealed class GoldenImage
{
    public GoldenImage(byte[] pixels, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(pixels.Length, width * height * 4);
        (Pixels, Width, Height) = (pixels, width, height);
    }

    public byte[] Pixels { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Copies a frame with any stride (a decoded frame's buffer lives only during the read callback).</summary>
    public static GoldenImage Copy(ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            bgra.Slice(y * stride, width * 4).CopyTo(pixels.AsSpan(y * width * 4));
        }

        return new GoldenImage(pixels, width, height);
    }

    /// <summary>A luma with <see cref="Media.Inspection.FrameCode"/>'s weights: (B + 2G + R) / 4.</summary>
    public int Luma(int x, int y)
    {
        var i = ((y * Width) + x) * 4;
        return (Pixels[i] + (2 * Pixels[i + 1]) + Pixels[i + 2]) / 4;
    }

    /// <summary>The mean absolute difference per channel (B, G, R), in levels.</summary>
    public static (double B, double G, double R) MeanAbsoluteError(GoldenImage a, GoldenImage b)
    {
        if (a.Width != b.Width || a.Height != b.Height)
        {
            throw new ArgumentException($"Sizes differ: {a.Width}x{a.Height} and {b.Width}x{b.Height}.");
        }

        long sb = 0, sg = 0, sr = 0;
        for (var i = 0; i < a.Pixels.Length; i += 4)
        {
            sb += Math.Abs(a.Pixels[i] - b.Pixels[i]);
            sg += Math.Abs(a.Pixels[i + 1] - b.Pixels[i + 1]);
            sr += Math.Abs(a.Pixels[i + 2] - b.Pixels[i + 2]);
        }

        double count = a.Width * a.Height;
        return (sb / count, sg / count, sr / count);
    }

    /// <summary>
    /// The black bars on a row (left, right) or a column (top, bottom): how many pixels from each end are darker
    /// than <paramref name="threshold"/>. The test photos never go below a luma of about 100, so only bars count.
    /// </summary>
    public (int Start, int End) BarsAlongRow(int y, int threshold = 40) =>
        Bars(Width, x => Luma(x, y), threshold);

    /// <inheritdoc cref="BarsAlongRow"/>
    public (int Start, int End) BarsAlongColumn(int x, int threshold = 40) =>
        Bars(Height, y => Luma(x, y), threshold);

    /// <summary>The edges crossed walking along row <paramref name="y"/>, left to right.</summary>
    public IReadOnlyList<Edge> EdgesAlongRow(int y) => Edges(Width, x => Luma(x, y));

    /// <summary>The edges crossed walking down column <paramref name="x"/>.</summary>
    public IReadOnlyList<Edge> EdgesAlongColumn(int x) => Edges(Height, y => Luma(x, y));

    /// <summary>
    /// Rows far (at least <paramref name="margin"/> pixels) from any horizontal edge, nearest to each target
    /// fraction of the height: a scan along them meets the vertical edges only, the same in both images however
    /// the codec softens the horizontal ones.
    /// </summary>
    public IReadOnlyList<int> QuietRows(IEnumerable<double> fractions, int margin = 4) =>
        Quiet(Height, Width, (along, across) => Luma(along, across), fractions, margin);

    /// <inheritdoc cref="QuietRows"/>
    public IReadOnlyList<int> QuietColumns(IEnumerable<double> fractions, int margin = 4) =>
        Quiet(Width, Height, (along, across) => Luma(across, along), fractions, margin);

    /// <summary>Writes the frame as a PNG (opaque) through WIC.</summary>
    public async Task SavePngAsync(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var stream = file.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)Width, (uint)Height, 96, 96, Pixels);
        await encoder.FlushAsync();
    }

    private static (int Start, int End) Bars(int length, Func<int, int> luma, int threshold)
    {
        var start = 0;
        while (start < length && luma(start) < threshold)
        {
            start++;
        }

        var end = 0;
        while (end < length - start && luma(length - 1 - end) < threshold)
        {
            end++;
        }

        return (start, end);
    }

    // An edge is a run of same-sign luma steps (each at least NoiseStep) that adds up to at least EdgeContrast; it
    // sits where the run has made half its change. Summing the run keeps an edge the codec or a scale has spread
    // over two or three pixels whole, and the half-way point does not move with the spread.
    private const int NoiseStep = 6;
    private const int EdgeContrast = 60;

    private static List<Edge> Edges(int length, Func<int, int> luma)
    {
        var edges = new List<Edge>();
        var i = 0;
        while (i < length - 1)
        {
            var step = luma(i + 1) - luma(i);
            if (Math.Abs(step) < NoiseStep)
            {
                i++;
                continue;
            }

            var sign = Math.Sign(step);
            var start = i;
            var end = i;
            while (end + 1 < length - 1)
            {
                var next = luma(end + 2) - luma(end + 1);
                if (Math.Sign(next) != sign || Math.Abs(next) < NoiseStep)
                {
                    break;
                }

                end++;
            }

            var total = luma(end + 1) - luma(start);
            if (Math.Abs(total) >= EdgeContrast)
            {
                var half = luma(start) + (total / 2.0);
                var at = start;
                while (at <= end && (sign > 0 ? luma(at + 1) < half : luma(at + 1) > half))
                {
                    at++;
                }

                // Between pixel `at` and `at + 1`, where the luma crosses the half-way level.
                var before = luma(at);
                var after = luma(at + 1);
                var fraction = after == before ? 0.5 : (half - before) / (after - before);
                edges.Add(new Edge(at + Math.Clamp(fraction, 0, 1), sign));
            }

            i = end + 1;
        }

        return edges;
    }

    private static List<int> Quiet(int count, int length, Func<int, int, int> luma, IEnumerable<double> fractions, int margin)
    {
        // busy[k]: line k differs from line k - 1 somewhere by half an edge's contrast.
        var busy = new bool[count];
        for (var k = 1; k < count; k++)
        {
            for (var along = 0; along < length; along++)
            {
                if (Math.Abs(luma(along, k) - luma(along, k - 1)) >= EdgeContrast / 2)
                {
                    busy[k] = true;
                    break;
                }
            }
        }

        bool IsQuiet(int line)
        {
            for (var k = Math.Max(1, line - margin); k <= Math.Min(count - 1, line + margin + 1); k++)
            {
                if (busy[k])
                {
                    return false;
                }
            }

            return line >= margin && line < count - margin;
        }

        var lines = new List<int>();
        foreach (var fraction in fractions)
        {
            var target = (int)(fraction * count);
            for (var distance = 0; distance < count; distance++)
            {
                if (IsQuiet(target - distance))
                {
                    lines.Add(target - distance);
                    break;
                }

                if (IsQuiet(target + distance))
                {
                    lines.Add(target + distance);
                    break;
                }
            }
        }

        return [.. lines.Distinct()];
    }
}

/// <summary>A step in luma along a scan line: its position in pixels (sub-pixel) and direction (+1 brighter, -1 darker).</summary>
internal readonly record struct Edge(double Position, int Sign)
{
    public override string ToString() => $"{(Sign > 0 ? "+" : "-")}{Position:0.0}";
}
