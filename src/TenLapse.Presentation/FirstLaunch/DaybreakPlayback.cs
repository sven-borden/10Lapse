namespace TenLapse.Presentation.FirstLaunch;

/// <summary>What Daybreak does now (motion spec, Daybreak).</summary>
public enum DaybreakMode
{
    /// <summary>Not on screen: the loop is stopped and the clock paused where it was.</summary>
    Hidden,

    /// <summary>
    /// On screen, standing still: paused where it was while the window is in the background, or at the golden-hour
    /// still frame (<see cref="Daybreak.StillFrame"/>) with animations off.
    /// </summary>
    Still,

    /// <summary>On screen and playing: the 30 fps loop runs.</summary>
    Run,
}

/// <summary>What Daybreak's mode depends on; the view gathers them and hands them to <see cref="DaybreakPlayback.Update"/>.</summary>
/// <param name="Loaded">The view is loaded and has its colors.</param>
/// <param name="RegionVisible">The shell shows its Empty region: no project, Settings closed.</param>
/// <param name="FitsLayout">The drop zone is wide and tall enough for it (<see cref="FirstLaunchLayout.ShowsIllustration"/>).</param>
/// <param name="AnimationsEnabled">Windows allows animations (and, in Debug, no <c>--reduce-motion</c>).</param>
/// <param name="WindowActive">The window is the active one.</param>
/// <param name="HighContrast">
/// Windows runs a high contrast theme. Daybreak's harbor is drawn in fixed colors that no system color can stand in
/// for, so it is not shown then (DESIGN.md: high contrast maps every token to a system color).
/// </param>
public readonly record struct DaybreakConditions(
    bool Loaded,
    bool RegionVisible,
    bool FitsLayout,
    bool AnimationsEnabled,
    bool WindowActive,
    bool HighContrast);

/// <summary>
/// When the first-launch timelapse runs, stands still or hides, and where it is in its loop. The view feeds it
/// <see cref="DaybreakConditions"/> whenever one changes (and polls animations at every tick), then starts or stops
/// its timer by <see cref="Mode"/> and shows <see cref="Frame"/>.
/// </summary>
/// <remarks>
/// It runs only on screen, in the active window, with animations on. Hidden or in the background, its clock pauses,
/// so it picks up where it was. With animations off it goes back to the still frame, and plays on from there once
/// they return. A drawing failure (<see cref="Break"/>) hides it for good; the drop zone works on without it.
/// </remarks>
public sealed class DaybreakPlayback
{
    private readonly Func<TimeSpan>? _now;
    private DaybreakClock _clock;

    /// <summary>A hidden timelapse at its still frame, on the system's monotonic clock unless <paramref name="now"/> is given (tests).</summary>
    public DaybreakPlayback(Func<TimeSpan>? now = null)
    {
        _now = now;
        _clock = StillClock();
    }

    /// <summary>Run, Still or Hidden, as of the last <see cref="Update"/> or <see cref="Break"/>.</summary>
    public DaybreakMode Mode { get; private set; } = DaybreakMode.Hidden;

    /// <summary>
    /// Whether Daybreak keeps its column in the drop zone: it fits, it is not broken and high contrast is off. When
    /// false the texts stand alone, centered.
    /// </summary>
    public bool TakesPlace { get; private set; }

    /// <summary>True once drawing it failed; it stays hidden from then on.</summary>
    public bool IsBroken { get; private set; }

    /// <summary>The frame to show now, from the playback time (never counted per tick).</summary>
    public int Frame => Daybreak.FrameAt(_clock.Played);

    /// <summary>Takes the conditions as they are now and returns the mode they give.</summary>
    public DaybreakMode Update(DaybreakConditions conditions)
    {
        TakesPlace = conditions.FitsLayout && !conditions.HighContrast && !IsBroken;
        var onScreen = TakesPlace && conditions.Loaded && conditions.RegionVisible;
        Mode = !onScreen
            ? DaybreakMode.Hidden
            : conditions.AnimationsEnabled && conditions.WindowActive ? DaybreakMode.Run : DaybreakMode.Still;

        if (Mode == DaybreakMode.Run)
        {
            _clock.Start();
        }
        else
        {
            _clock.Stop();
            if (!conditions.AnimationsEnabled && !IsBroken)
            {
                // Reduced motion: the golden-hour still, and the loop starts from there when animations return.
                _clock = StillClock();
            }
        }

        return Mode;
    }

    /// <summary>Drawing failed: stops and hides Daybreak for good.</summary>
    public void Break()
    {
        IsBroken = true;
        TakesPlace = false;
        Mode = DaybreakMode.Hidden;
        _clock.Stop();
    }

    private DaybreakClock StillClock() => new(Daybreak.StartOf(Daybreak.StillFrame), _now);
}
