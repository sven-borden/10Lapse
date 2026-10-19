namespace TenLapse.Presentation;

/// <summary>
/// Balanced line breaks for a short heading, as CSS <c>text-wrap: balance</c> does in the prototype: the narrowest
/// width that keeps the heading on as few lines as the available width gives it, so its lines come out even
/// ("Drop a folder of photos / to make a timelapse") instead of greedy with one word left over.
/// </summary>
public static class TextBalance
{
    /// <summary>
    /// The width to cap the heading at. <paramref name="heightAt"/> measures the heading wrapped at a width (DIPs);
    /// a heading that fits on one line keeps <paramref name="available"/>.
    /// </summary>
    public static double Width(double available, Func<double, double> heightAt, double precision = 1)
    {
        ArgumentNullException.ThrowIfNull(heightAt);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(precision);
        if (!(available > 0) || double.IsInfinity(available))
        {
            return available;
        }

        var height = heightAt(available);
        if (heightAt(double.PositiveInfinity) >= height)
        {
            return available; // one line
        }

        // The narrowest width that still gives the same height: lines as even as the words allow.
        var low = 0.0;
        var high = available;
        while (high - low > precision)
        {
            var middle = (low + high) / 2;
            if (heightAt(middle) <= height)
            {
                high = middle;
            }
            else
            {
                low = middle;
            }
        }

        // A little room over the measured width, so layout rounding cannot push a word to a new line.
        return Math.Min(available, Math.Ceiling(high) + precision);
    }
}
