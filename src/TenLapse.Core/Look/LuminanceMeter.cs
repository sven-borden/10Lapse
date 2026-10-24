using TenLapse.Core.Projects;

namespace TenLapse.Core.Look;

/// <summary>
/// How bright a picture is, in stops (#45): a histogram of each pixel's log2 linear luminance (Rec. 709 weights on
/// sRGB-decoded values), and the mean of its middle 80 %. Pure arithmetic on BGRA pixels, so the exposure pass and the
/// tests measure the same way.
/// </summary>
/// <remarks>
/// <para>
/// Why the trimmed mean of the log: a gain of n stops applied in linear light moves every unclipped pixel's log by
/// exactly n, so it moves the percentiles, and this mean, by exactly n while the middle 80 % is not clipped. The curve
/// can therefore aim at a brightness and hit it. The trim keeps the clipped sun and the noise floor (the brightest and
/// darkest 10 %) out of it; a plain mean of linear values follows the few brightest pixels instead.
/// </para>
/// <para>
/// The histogram has <see cref="BinsPerStop"/> bins a stop over <see cref="PhotoExposure.MinLuminance"/> to 0, so the
/// result is exact to about 1/128 of a stop, far below what an eye sees between two frames (about 1/30).
/// </para>
/// </remarks>
public static class LuminanceMeter
{
    /// <summary>The histogram's resolution.</summary>
    public const int BinsPerStop = 64;

    /// <summary>The share of the pixels left out at each end before averaging.</summary>
    public const double TrimShare = 0.1;

    private const int Bins = -(int)PhotoExposure.MinLuminance * BinsPerStop;

    // sRGB (IEC 61966-2-1) decoded to linear light, per 8-bit code.
    private static readonly double[] _linear = [.. Enumerable.Range(0, 256).Select(code => SrgbToLinear(code / 255.0))];

    /// <summary>The linear value of an sRGB-encoded one in 0 to 1.</summary>
    public static double SrgbToLinear(double encoded) =>
        encoded <= 0.04045 ? encoded / 12.92 : Math.Pow((encoded + 0.055) / 1.055, 2.4);

    /// <summary>The sRGB encoding of a linear value in 0 to 1.</summary>
    public static double LinearToSrgb(double linear) =>
        linear <= 0.0031308 ? linear * 12.92 : (1.055 * Math.Pow(linear, 1 / 2.4)) - 0.055;

    /// <summary>
    /// The brightness of a BGRA8 picture (premultiplied or straight; fully transparent pixels are left out), in stops from
    /// <see cref="PhotoExposure.MinLuminance"/> to 0, rounded to 1/10,000.
    /// </summary>
    /// <param name="pixels">The rows, top down.</param>
    /// <param name="width">Pixels a row.</param>
    /// <param name="height">Rows.</param>
    /// <param name="stride">Bytes a row, at least 4 × <paramref name="width"/>.</param>
    public static double Measure(ReadOnlySpan<byte> pixels, int width, int height, int stride)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, width * 4);
        if (pixels.Length < ((long)stride * (height - 1)) + (width * 4))
        {
            throw new ArgumentException("The pixels are fewer than the size says.", nameof(pixels));
        }

        var histogram = new int[Bins];
        var counted = 0;
        for (var y = 0; y < height; y++)
        {
            var row = pixels.Slice(y * stride, width * 4);
            for (var x = 0; x < row.Length; x += 4)
            {
                var alpha = row[x + 3];
                if (alpha == 0)
                {
                    continue;
                }

                var luminance = (0.0722 * Linear(row[x], alpha)) + (0.7152 * Linear(row[x + 1], alpha)) + (0.2126 * Linear(row[x + 2], alpha));
                var bin = luminance > 0 ? (int)((Math.Log2(luminance) - PhotoExposure.MinLuminance) * BinsPerStop) : 0;
                histogram[Math.Clamp(bin, 0, Bins - 1)]++;
                counted++;
            }
        }

        return counted == 0 ? PhotoExposure.MinLuminance : TrimmedMean(histogram, counted);
    }

    // The mean of the bins' centres over the middle of the pixels, a bin cut at either end counting with its share.
    private static double TrimmedMean(int[] histogram, int counted)
    {
        var low = counted * TrimShare;
        var high = counted * (1 - TrimShare);
        double below = 0;
        double sum = 0;
        double weight = 0;
        for (var bin = 0; bin < histogram.Length && below < high; bin++)
        {
            var count = histogram[bin];
            var from = Math.Max(below, low);
            var to = Math.Min(below + count, high);
            if (to > from)
            {
                var centre = PhotoExposure.MinLuminance + ((bin + 0.5) / BinsPerStop);
                sum += centre * (to - from);
                weight += to - from;
            }

            below += count;
        }

        var mean = weight > 0 ? sum / weight : PhotoExposure.MinLuminance;
        return Math.Round(Math.Clamp(mean, PhotoExposure.MinLuminance, 0), 4);
    }

    // A channel as linear light, straight (a premultiplied value is divided by its alpha first).
    private static double Linear(byte value, byte alpha) =>
        alpha == 255 ? _linear[value] : _linear[Math.Min(255, ((value * 255) + (alpha / 2)) / alpha)];
}
