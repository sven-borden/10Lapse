namespace TenLapse.Core.Projects;

/// <summary>
/// The Look section's settings (#45): deflicker on or off, and how strongly it corrects. Immutable; a <c>with</c>
/// expression validates too. The correction itself is computed from the photos' measurements
/// (<see cref="Look.ExposureCurve"/>), never stored here.
/// </summary>
public sealed record LookSettings
{
    /// <summary>The weakest strength, 0 %: on, but nothing changes.</summary>
    public const int MinStrength = 0;

    /// <summary>The strongest, 100 %: each photo is brought fully onto the smoothed curve.</summary>
    public const int MaxStrength = 100;

    /// <summary>Off, at full strength once turned on: a project from before #45 has no look group and reads as this.</summary>
    public static LookSettings Default { get; } = new(false, MaxStrength);

    /// <summary>Creates look settings.</summary>
    /// <param name="deflicker">Whether the exposure pass corrects the photos.</param>
    /// <param name="strength">How much of the correction is applied, in percent, <see cref="MinStrength"/> to <see cref="MaxStrength"/>.</param>
    public LookSettings(bool deflicker, int strength)
    {
        Deflicker = deflicker;
        Strength = strength;
    }

    /// <summary>Whether the exposure pass (flicker and the day-to-night ramp) corrects the photos.</summary>
    public bool Deflicker { get; init; }

    /// <summary>How much of the correction is applied, in percent.</summary>
    public int Strength
    {
        get;
        init => field = value is >= MinStrength and <= MaxStrength
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "The strength must be from 0 to 100 %.");
    }
}
