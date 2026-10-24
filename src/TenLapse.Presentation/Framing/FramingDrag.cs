using TenLapse.Core.Projects;

namespace TenLapse.Presentation;

/// <summary>A corner of the framing drawn over the preview, which a drag resizes it by (#46).</summary>
public enum FramingCorner
{
    /// <summary>The top left corner.</summary>
    TopLeft,

    /// <summary>The top right corner.</summary>
    TopRight,

    /// <summary>The bottom left corner.</summary>
    BottomLeft,

    /// <summary>The bottom right corner.</summary>
    BottomRight,
}

/// <summary>
/// What a drag on the framing over the preview makes of it (#46), in shares of the frame, so the overlay only turns
/// pointer positions into shares. Every result lies inside the frame and keeps the frame's shape: in shares of the frame
/// a framing is a square, its side <see cref="Framing.Size"/> across and down.
/// </summary>
public static class FramingDrag
{
    /// <summary>The framing <paramref name="start"/> moved by <paramref name="dx"/> across and <paramref name="dy"/> down, stopped at the frame's edges.</summary>
    public static Framing Moved(Framing start, double dx, double dy) =>
        Framing.Clamped(start.Size, start.CenterX + dx, start.CenterY + dy);

    /// <summary>
    /// The framing <paramref name="start"/> resized by dragging <paramref name="corner"/> to the point (<paramref name="x"/>,
    /// <paramref name="y"/>): the opposite corner stays where it is, and the framing grows or shrinks to the larger of the
    /// two distances to that point (its shape is the frame's), from <see cref="Framing.MinSize"/> to as large as the frame
    /// allows on that side.
    /// </summary>
    public static Framing Resized(Framing start, FramingCorner corner, double x, double y)
    {
        // The corner that stays, and the way the dragged one lies from it.
        var right = corner is FramingCorner.TopRight or FramingCorner.BottomRight;
        var down = corner is FramingCorner.BottomLeft or FramingCorner.BottomRight;
        var anchorX = right ? start.Left : start.Left + start.Size;
        var anchorY = down ? start.Top : start.Top + start.Size;
        var room = Math.Min(right ? 1 - anchorX : anchorX, down ? 1 - anchorY : anchorY);
        var wanted = Math.Max(right ? x - anchorX : anchorX - x, down ? y - anchorY : anchorY - y);
        var size = Math.Clamp(double.IsFinite(wanted) ? wanted : start.Size, Framing.MinSize, Math.Max(Framing.MinSize, room));
        var half = size / 2;
        return Framing.Clamped(size, anchorX + (right ? half : -half), anchorY + (down ? half : -half));
    }

    /// <summary>The framing <paramref name="start"/> zoomed by <paramref name="factor"/> about its centre (above 1 zooms in), kept inside the frame.</summary>
    public static Framing Zoomed(Framing start, double factor) =>
        factor > 0 && double.IsFinite(factor) ? Framing.Clamped(start.Size / factor, start.CenterX, start.CenterY) : start;
}
