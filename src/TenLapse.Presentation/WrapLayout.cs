namespace TenLapse.Presentation;

/// <summary>
/// Where the children of a wrapping row go (#114; the App's <c>WrapRow</c> panel applies it): side by side, left to right,
/// a spacing apart, and a child that would pass the row's width starts a new line under them. A translation that makes two
/// buttons too wide for a 280 px panel puts them one under the other instead of cutting them off.
/// <para>
/// Measure and arrange can see different widths: a row measured at an unbounded width (an Auto grid column) is often
/// arranged a fraction of a pixel narrower than it asked for. Breaking the lines again at that width would move a child to
/// a line measure never reserved, over whatever sits below. So the panel keeps the measured layout whenever it still fits
/// the arranged width (<see cref="Fits"/>), and breaks the lines again only when the row was truly squeezed.
/// </para>
/// </summary>
public static class WrapLayout
{
    /// <summary>How far, in pixels, an arranged width may fall short of a measured layout that still counts as fitting it.</summary>
    public const double Tolerance = 1;

    /// <summary>Places children of <paramref name="sizes"/> (the visible ones, in order) within <paramref name="available"/> pixels.</summary>
    /// <param name="sizes">Each child's desired width and height.</param>
    /// <param name="spacing">The gap between two children, across and down.</param>
    /// <param name="available">The row's width; <see cref="double.PositiveInfinity"/> keeps every child on one line.</param>
    public static WrapResult Lay(IReadOnlyList<(double Width, double Height)> sizes, double spacing, double available)
    {
        ArgumentNullException.ThrowIfNull(sizes);

        var positions = new (double Left, double Top)[sizes.Count];
        double x = 0, y = 0, line = 0, widest = 0;
        for (var i = 0; i < sizes.Count; i++)
        {
            var (width, height) = sizes[i];
            if (x > 0 && x + spacing + width > available)
            {
                widest = Math.Max(widest, x);
                y += line + spacing;
                x = 0;
                line = 0;
            }

            var left = x > 0 ? x + spacing : 0;
            positions[i] = (left, y);
            x = left + width;
            line = Math.Max(line, height);
        }

        return new WrapResult([.. sizes], positions, Math.Max(widest, x), y + line);
    }

    /// <summary>
    /// The layout to arrange at <paramref name="width"/>: <paramref name="measured"/> when it was made from the same child
    /// sizes and still fits within <see cref="Tolerance"/>, else the lines broken again at <paramref name="width"/>.
    /// </summary>
    public static WrapResult ForArrange(WrapResult? measured, IReadOnlyList<(double Width, double Height)> sizes, double spacing, double width)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        return measured is not null && measured.Sizes.SequenceEqual(sizes) && Fits(measured, width)
            ? measured
            : Lay(sizes, spacing, width);
    }

    /// <summary>True when <paramref name="layout"/> fits <paramref name="width"/>, allowing <see cref="Tolerance"/>.</summary>
    public static bool Fits(WrapResult layout, double width)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return layout.Width <= width + Tolerance;
    }
}

/// <summary>A wrapping row's layout: the child sizes it was made from, each child's top-left corner, and the size it takes.</summary>
public sealed record WrapResult(IReadOnlyList<(double Width, double Height)> Sizes, IReadOnlyList<(double Left, double Top)> Positions, double Width, double Height);
