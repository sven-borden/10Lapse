namespace TenLapse.Core.Projects;

/// <summary>
/// The part of the frame the video shows (#46, crop and pan): a rectangle of the frame's own shape, given by its
/// <see cref="Size"/> (its share of the frame's width, and so of its height) and its centre, both as shares of the frame.
/// Because it has the frame's shape and is measured in shares of it, a framing means the same with every aspect preset
/// and resolution: changing 16:9 to 1:1 keeps the same part of the picture in view. The <c>default</c> is
/// <see cref="Whole"/>: the whole frame, as before #46.
/// </summary>
/// <remarks>
/// The rectangle always lies inside the frame: <see cref="Size"/> is <see cref="MinSize"/> to 1 and the centre at least
/// half a size from every edge (the constructor refuses anything else; <see cref="Clamped"/> moves a value inside, for an
/// editor). A zero field stands for the whole frame's value (no real size or centre is zero), so the struct's zero is
/// the whole frame; the values are kept as given, so a framing written and read back as decimal text is the same framing.
/// </remarks>
public readonly record struct Framing
{
    /// <summary>The smallest view, a quarter of the frame's width: 4× zoom.</summary>
    public const double MinSize = 0.25;

    // How far an edge may stray outside the frame and still be read as on it: a framing written and read back as
    // decimal text, or one made from a size and its edges, may come back a few ulps off.
    private const double Tolerance = 1e-9;

    // Size, CenterX and CenterY, each 0 where it is the whole frame's (1, 0.5, 0.5): all zero is the struct's default.
    private readonly double _size;
    private readonly double _x;
    private readonly double _y;

    /// <summary>Creates a framing.</summary>
    /// <param name="size">The view's share of the frame's width and height, <see cref="MinSize"/> to 1.</param>
    /// <param name="centerX">The view's centre across, as a share of the frame's width (0 the left edge, 1 the right).</param>
    /// <param name="centerY">The view's centre down, as a share of the frame's height (0 the top, 1 the bottom).</param>
    /// <exception cref="ArgumentOutOfRangeException">The size is outside its range, or the view would leave the frame.</exception>
    public Framing(double size, double centerX, double centerY)
    {
        if (!double.IsFinite(size) || size < MinSize - Tolerance || size > 1 + Tolerance)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, $"A framing's size must be from {MinSize} to 1.");
        }

        size = Math.Clamp(size, MinSize, 1);
        centerX = Centre(centerX, size, nameof(centerX));
        centerY = Centre(centerY, size, nameof(centerY));
        _size = size == 1 ? 0 : size;
        _x = centerX == 0.5 ? 0 : centerX;
        _y = centerY == 0.5 ? 0 : centerY;
    }

    /// <summary>The whole frame: what the video shows without crop and pan.</summary>
    public static Framing Whole => default;

    /// <summary>The view's share of the frame's width and of its height, <see cref="MinSize"/> to 1.</summary>
    public double Size => _size == 0 ? 1 : _size;

    /// <summary>The view's centre across, as a share of the frame's width.</summary>
    public double CenterX => _x == 0 ? 0.5 : _x;

    /// <summary>The view's centre down, as a share of the frame's height.</summary>
    public double CenterY => _y == 0 ? 0.5 : _y;

    /// <summary>The view's left edge, as a share of the frame's width.</summary>
    public double Left => CenterX - (Size / 2);

    /// <summary>The view's top edge, as a share of the frame's height.</summary>
    public double Top => CenterY - (Size / 2);

    /// <summary>How much the view enlarges the frame: 1 for the whole frame, 4 at <see cref="MinSize"/>.</summary>
    public double Zoom => 1 / Size;

    /// <summary>True when this is the whole frame.</summary>
    public bool IsWhole => _size == 0;

    /// <summary>
    /// The framing nearest to the one asked for that lies inside the frame: the size brought into its range, then the
    /// centre moved in until the view fits. What an editor makes of a drag or a typed value.
    /// </summary>
    public static Framing Clamped(double size, double centerX, double centerY)
    {
        size = double.IsFinite(size) ? Math.Clamp(size, MinSize, 1) : 1;
        var half = size / 2;
        return new Framing(
            size,
            double.IsFinite(centerX) ? Math.Clamp(centerX, half, 1 - half) : 0.5,
            double.IsFinite(centerY) ? Math.Clamp(centerY, half, 1 - half) : 0.5);
    }

    /// <summary>
    /// The framing a share <paramref name="eased"/> of the way from <paramref name="from"/> to <paramref name="to"/> (0 is
    /// <paramref name="from"/>, 1 is <paramref name="to"/>). The size changes by the same factor in equal steps (a steady
    /// zoom, not one that speeds up as the view shrinks), and the centre follows the size so that the one point both
    /// framings show in the same place stays put: a zoom into a corner closes in on that corner. With equal sizes it is a
    /// straight, even pan. Every framing on the way lies inside the frame.
    /// </summary>
    public static Framing Between(Framing from, Framing to, double eased)
    {
        if (eased <= 0)
        {
            return from;
        }

        if (eased >= 1)
        {
            return to;
        }

        var size = from.Size * Math.Pow(to.Size / from.Size, eased);

        // The centre is a straight line in the size (through both framings), so the view grows or shrinks about their
        // common point; a pan without a zoom has no such point and moves evenly instead. Both keep the view inside: the
        // framings that fit the frame are a convex set in (size, centre), and the path is the segment between two of them.
        var along = Math.Abs(to.Size - from.Size) > 1e-12 ? (size - from.Size) / (to.Size - from.Size) : eased;
        return Clamped(size, from.CenterX + ((to.CenterX - from.CenterX) * along), from.CenterY + ((to.CenterY - from.CenterY) * along));
    }

    private static double Centre(double centre, double size, string name)
    {
        var half = size / 2;
        if (!double.IsFinite(centre) || centre < half - Tolerance || centre > 1 - half + Tolerance)
        {
            throw new ArgumentOutOfRangeException(name, centre, $"A framing of size {size} must have its centre from {half} to {1 - half}.");
        }

        return Math.Clamp(centre, half, 1 - half);
    }
}
