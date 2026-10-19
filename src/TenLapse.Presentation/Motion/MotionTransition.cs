using System.Globalization;

namespace TenLapse.Presentation.Motion;

/// <summary>What a transition becomes with animations off (Windows Settings, Accessibility, Animation effects).</summary>
public enum ReducedMotion
{
    /// <summary>A short linear fade instead: <see cref="MotionSpec.ReducedFadeIn"/> arriving, <see cref="MotionSpec.ReducedFadeOut"/> leaving.</summary>
    Fade,

    /// <summary>Nothing: the change is at its end state at once (or, for a loop, stands still).</summary>
    Cut,

    /// <summary>Unchanged, because it does not move anything: a fade of strength, or a wait before playback.</summary>
    Same,
}

/// <summary>Whether a transition brings something in, takes it out, or changes it in place.</summary>
public enum MotionDirection
{
    /// <summary>Something arrives.</summary>
    In,

    /// <summary>Something leaves: always <see cref="MotionSpec.Control"/> with the Exit curve.</summary>
    Out,

    /// <summary>Something changes in place (a pill slides, a gate reshapes, a bar grows).</summary>
    Change,
}

/// <summary>What a started transition does on screen, as the motion log records it.</summary>
public enum MotionKind
{
    /// <summary>It moves: a transform, a blur, a clip or a stroke animates (and the opacity with it, often).</summary>
    Moved,

    /// <summary>Only the opacity animates.</summary>
    Faded,

    /// <summary>Nothing animates; the app waits before it acts (the premiere).</summary>
    Waited,

    /// <summary>Nothing animates: the end state is set at once.</summary>
    None,
}

/// <summary>
/// One transition of the motion spec: its name, timing, easing and what it becomes with animations off. The app asks
/// <see cref="Plan"/> each time one starts (through <c>TenLapse.Themes.MotionTokens.Plan</c>, which reads the Windows
/// setting then) and animates what the <see cref="MotionStep"/> says.
/// </summary>
/// <param name="Name">The catalogue's name for it, as the motion log writes it ("Arrival.Develop").</param>
/// <param name="Duration">How long it runs with animations on.</param>
/// <param name="Easing">Its curve with animations on.</param>
/// <param name="Direction">In, out, or a change in place.</param>
/// <param name="Moves">True when it moves something (transform, blur, clip, stroke); false when it only fades or waits.</param>
/// <param name="Reduced">What it becomes with animations off.</param>
/// <param name="Movement">What moves, in the spec's words ("rises 8 px"), for the catalogue and the log.</param>
public sealed record MotionTransition(
    string Name,
    TimeSpan Duration,
    MotionEasing Easing,
    MotionDirection Direction,
    bool Moves,
    ReducedMotion Reduced,
    string Movement)
{
    /// <summary>How long after its cue the first one starts, with animations on.</summary>
    public TimeSpan Delay { get; init; }

    /// <summary>How much later each next one of a cascade starts (thumbnails, sections), with animations on.</summary>
    public TimeSpan Stagger { get; init; }

    /// <summary>The delay of the reduced fade (a screen fades in after the old one has faded out).</summary>
    public TimeSpan ReducedDelay { get; init; }

    /// <summary>True when the app waits rather than animates (the premiere's 1.1 s).</summary>
    public bool IsWait { get; init; }

    /// <summary>
    /// True for the transitions an InfoBar waits for (screens, the arrival, the panel swap, the theme change): the motion
    /// spec's InfoBar "never appears during a transition" (<see cref="MotionQuiet"/>).
    /// </summary>
    public bool HoldsInfoBars { get; init; }

    /// <summary>
    /// What to do now: with <paramref name="animationsEnabled"/> the spec's timing (the <paramref name="index"/>-th of a
    /// cascade starts <see cref="Stagger"/> times later); without, the reduced fade or nothing.
    /// </summary>
    public MotionStep Plan(bool animationsEnabled, int index = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (animationsEnabled || Reduced == ReducedMotion.Same)
        {
            var kind = IsWait ? MotionKind.Waited : Moves && animationsEnabled ? MotionKind.Moved : MotionKind.Faded;
            return new MotionStep(this, kind, Duration, Delay + (Stagger * index), Easing, index, animationsEnabled);
        }

        return Reduced == ReducedMotion.Fade
            ? new MotionStep(this, MotionKind.Faded, Direction == MotionDirection.Out ? MotionSpec.ReducedFadeOut : MotionSpec.ReducedFadeIn, ReducedDelay, MotionEasing.Linear, index, animationsEnabled)
            : new MotionStep(this, MotionKind.None, TimeSpan.Zero, TimeSpan.Zero, MotionEasing.Linear, index, animationsEnabled);
    }
}

/// <summary>A transition as it starts now: what it does, for how long, after what delay, on which curve.</summary>
/// <param name="Transition">The catalogue entry.</param>
/// <param name="Kind">Moves, fades, waits, or nothing.</param>
/// <param name="Duration">How long it runs (zero for <see cref="MotionKind.None"/>).</param>
/// <param name="Delay">How long after its cue it starts.</param>
/// <param name="Easing">Its curve.</param>
/// <param name="Index">Its place in a cascade (0 otherwise).</param>
/// <param name="AnimationsEnabled">Whether Windows (and Debug <c>--reduce-motion</c>) allowed animations when it started.</param>
public sealed record MotionStep(MotionTransition Transition, MotionKind Kind, TimeSpan Duration, TimeSpan Delay, MotionEasing Easing, int Index, bool AnimationsEnabled)
{
    /// <summary>The transition's name.</summary>
    public string Name => Transition.Name;

    /// <summary>True when anything animates (a move or a fade), so the app builds an animation.</summary>
    public bool Animates => Kind is MotionKind.Moved or MotionKind.Faded;

    /// <summary>True when the spec's movement plays (translation, scale, blur, clip, stroke).</summary>
    public bool Moves => Kind == MotionKind.Moved;

    /// <summary>
    /// The motion log's line: "Arrival.Filmstrip #3 moved 400 ms Enter after 274 ms (rises 8 px; animations on)".
    /// </summary>
    public string ToLogLine()
    {
        var culture = CultureInfo.InvariantCulture;
        var name = Index > 0 || Transition.Stagger > TimeSpan.Zero ? string.Create(culture, $"{Name} #{Index}") : Name;
        var what = Kind switch
        {
            MotionKind.Moved => string.Create(culture, $"moved {Duration.TotalMilliseconds:0} ms {Easing}"),
            MotionKind.Faded => string.Create(culture, $"faded {Duration.TotalMilliseconds:0} ms {Easing}"),
            MotionKind.Waited => string.Create(culture, $"waited {Duration.TotalMilliseconds:0} ms"),
            _ => "none",
        };
        var delay = Delay > TimeSpan.Zero ? string.Create(culture, $" after {Delay.TotalMilliseconds:0} ms") : string.Empty;
        var movement = Kind == MotionKind.Moved ? Transition.Movement + "; " : string.Empty;
        return $"{name} {what}{delay} ({movement}animations {(AnimationsEnabled ? "on" : "off")})";
    }
}
