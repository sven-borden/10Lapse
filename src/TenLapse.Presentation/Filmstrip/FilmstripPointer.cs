namespace TenLapse.Presentation.Filmstrip;

/// <summary>What a pointer event did in the filmstrip, so the view knows what to draw.</summary>
public enum FilmstripPointerResult
{
    /// <summary>Not the strip's: no press, or another pointer.</summary>
    Ignored,

    /// <summary>The button is down and has not moved far enough to drag (or it is a touch, which scrolls).</summary>
    Pending,

    /// <summary>The move started a drag: show the insertion bar.</summary>
    DragStarted,

    /// <summary>The drag goes on: move the insertion bar.</summary>
    Dragging,

    /// <summary>A release without a drag: a click.</summary>
    Clicked,

    /// <summary>A release that ended a drag: the frames moved to the seam (or stayed, when it was where they were).</summary>
    Dropped,
}

/// <summary>
/// The filmstrip's press, drag and cancel (#20), apart from WinUI so the tests can drive it. A press on a thumbnail may
/// become a click (released without moving) or a drag (moved <see cref="DragThreshold"/> DIPs with the button down). A
/// drag pauses the video and takes the frames that move when it starts (<see cref="FilmstripViewModel.BeginDrag"/>); the
/// release drops them at the last seam the view reported (<see cref="Over"/>). <see cref="Cancel"/> (Escape, a lost
/// capture, a cancelled pointer) forgets the press: nothing moves, and neither a later move nor the release of that
/// press does anything. A touch never drags: it scrolls the strip, and a tap is a click. On the UI thread.
/// </summary>
public sealed class FilmstripPointer
{
    /// <summary>How far (DIPs, across plus down) the pointer moves with the button down before a press is a drag.</summary>
    public const double DragThreshold = 6;

    private readonly FilmstripViewModel _strip;
    private Held? _press;
    private int _seam = -1;

    /// <summary>Clicks and drags in <paramref name="strip"/>.</summary>
    public FilmstripPointer(FilmstripViewModel strip)
    {
        _strip = strip ?? throw new ArgumentNullException(nameof(strip));
    }

    /// <summary>True while a press is held (a click or a drag to come).</summary>
    public bool IsPressed => _press is not null;

    /// <summary>True while a drag is under way.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>The seam the dragged frames land in on release, or -1.</summary>
    public int DropSeam => _seam;

    /// <summary>
    /// The left button went down on thumbnail <paramref name="index"/> at (<paramref name="x"/>, <paramref name="y"/>) in
    /// the strip's coordinates, with these modifiers. A press still held is cancelled first.
    /// </summary>
    public void Press(int index, double x, double y, uint pointerId, bool shift, bool control, bool touch)
    {
        Cancel();
        _press = new Held(index, x, y, pointerId, shift, control, touch);
    }

    /// <summary>The pointer moved to (<paramref name="x"/>, <paramref name="y"/>); far enough, a press becomes a drag.</summary>
    public FilmstripPointerResult Move(uint pointerId, double x, double y)
    {
        if (_press is not { } press || press.PointerId != pointerId)
        {
            return FilmstripPointerResult.Ignored;
        }

        if (IsDragging)
        {
            return FilmstripPointerResult.Dragging;
        }

        if (press.Touch || Math.Abs(x - press.X) + Math.Abs(y - press.Y) < DragThreshold)
        {
            return FilmstripPointerResult.Pending;
        }

        IsDragging = true;
        _seam = -1;
        _strip.BeginDrag(press.Index);
        return FilmstripPointerResult.DragStarted;
    }

    /// <summary>While dragging: the seam under the pointer now (0 before the first frame, the count after the last).</summary>
    public void Over(int seam)
    {
        if (IsDragging)
        {
            _seam = seam;
        }
    }

    /// <summary>The button went up: a click, or the drop of a drag. <see cref="FilmstripPointerResult.Ignored"/> when the press was not this pointer's, or was cancelled.</summary>
    public FilmstripPointerResult Release(uint pointerId)
    {
        if (_press is not { } press || press.PointerId != pointerId)
        {
            return FilmstripPointerResult.Ignored;
        }

        _press = null;
        if (IsDragging)
        {
            var seam = _seam;
            IsDragging = false;
            _seam = -1;
            _strip.EndDrag(seam >= 0 ? seam : null);
            return FilmstripPointerResult.Dropped;
        }

        _strip.Click(press.Index, press.Shift, press.Control);
        return FilmstripPointerResult.Clicked;
    }

    /// <summary>Escape, a lost capture or a cancelled pointer: forgets the press, and a drag ends without moving anything.</summary>
    /// <returns>True when there was a press to forget.</returns>
    public bool Cancel()
    {
        var had = _press is not null;
        _press = null;
        if (IsDragging)
        {
            IsDragging = false;
            _seam = -1;
            _strip.EndDrag(null);
        }

        return had;
    }

    private sealed record Held(int Index, double X, double Y, uint PointerId, bool Shift, bool Control, bool Touch);
}
