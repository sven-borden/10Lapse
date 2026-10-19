using System.Collections.Immutable;
using System.Drawing;
using System.Numerics;

namespace TenLapse.Presentation.FirstLaunch;

/// <summary>
/// The colors of the Daybreak harbor, from the token dictionaries: the brand's day-to-night ramp for the horizon
/// (DESIGN.md, Brand ramp) and the Daybreak* tokens for the sky overhead, the land, the sun and the lights. The App
/// reads them from Themes/Colors.xaml; tests pass the same values.
/// </summary>
/// <param name="Zenith">The sky overhead from day to night, at <see cref="DaybreakScene.ZenithStops"/>.</param>
/// <param name="Horizon">The sky at the horizon from day to night (the brand ramp), at <see cref="DaybreakScene.HorizonStops"/>.</param>
/// <param name="LandDay">The skyline by day.</param>
/// <param name="LandNight">The skyline by night.</param>
/// <param name="DeepWater">What the water darkens toward at the bottom of the frame.</param>
/// <param name="SunDay">The sun's core high in the sky.</param>
/// <param name="SunDusk">The sun's core as it sets.</param>
/// <param name="GlowDay">The sun's glow (and its glint on the water) high in the sky.</param>
/// <param name="GlowDusk">The sun's glow as it sets.</param>
/// <param name="Star">The stars.</param>
/// <param name="Light">The skyline's windows at night.</param>
/// <param name="LightPale">Every other window, a paler light.</param>
public sealed record DaybreakPalette(
    ImmutableArray<Color> Zenith,
    ImmutableArray<Color> Horizon,
    Color LandDay,
    Color LandNight,
    Color DeepWater,
    Color SunDay,
    Color SunDusk,
    Color GlowDay,
    Color GlowDusk,
    Color Star,
    Color Light,
    Color LightPale)
{
    private static readonly string[] _zenithNames = ["Day", "Golden", "Amber", "Rose", "Dusk", "BlueHour", "Night"];
    private static readonly string[] _horizonNames = ["Daylight", "Golden", "Amber", "Rose", "Dusk", "BlueHour", "Night"];

    /// <summary>
    /// The palette from the color tokens at the root of Themes/Colors.xaml: <paramref name="token"/> returns the
    /// color for a key such as "DaybreakStarColor". The horizon is the brand ramp (DaylightColor to NightColor), the
    /// rest are the Daybreak* tokens. The App reads them with ThemeTokens.FixedColor; tests from the file itself.
    /// </summary>
    public static DaybreakPalette FromTokens(Func<string, Color> token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return new DaybreakPalette(
            Zenith: [.. _zenithNames.Select(name => token($"DaybreakZenith{name}Color"))],
            Horizon: [.. _horizonNames.Select(name => token($"{name}Color"))],
            LandDay: token("DaybreakLandDayColor"),
            LandNight: token("DaybreakLandNightColor"),
            DeepWater: token("DaybreakDeepWaterColor"),
            SunDay: token("DaybreakSunDayColor"),
            SunDusk: token("DaybreakSunDuskColor"),
            GlowDay: token("DaybreakGlowDayColor"),
            GlowDusk: token("DaybreakGlowDuskColor"),
            Star: token("DaybreakStarColor"),
            Light: token("DaybreakLightColor"),
            LightPale: token("DaybreakLightPaleColor"));
    }
}

/// <summary>
/// The harbor at one moment from day (0) to night (1): every color and the few numbers that move. Positions are
/// fractions of the frame (<see cref="SunY"/> of its height); opacities are 0 to 1.
/// </summary>
public readonly record struct DaybreakSky(
    Color Top,
    Color Middle,
    Color Horizon,
    Color Land,
    Color WaterTop,
    Color WaterBottom,
    Color Sun,
    Color SunGlow,
    double SunY,
    double SunOpacity,
    double GlintOpacity,
    double StarsOpacity,
    double LightsOpacity);

/// <summary>
/// The Daybreak harbor of the prototype (docs/design/canvas/Main.dc.html, <c>sky()</c> and the "Daybreak" frame),
/// as numbers a renderer draws: the colors at any phase, and the fixed layout of the sky, the skyline, its lights,
/// the stars, the water and the sun's glint. All positions are fractions of the frame (or of the band they sit in).
/// </summary>
public static class DaybreakScene
{
    /// <summary>The phases of <see cref="DaybreakPalette.Zenith"/>'s seven colors.</summary>
    public static ImmutableArray<double> ZenithStops { get; } = [0, 0.35, 0.5, 0.62, 0.75, 0.88, 1];

    /// <summary>The phases of <see cref="DaybreakPalette.Horizon"/>'s seven colors (Daylight to Night).</summary>
    public static ImmutableArray<double> HorizonStops { get; } = [0, 0.33, 0.48, 0.6, 0.73, 0.86, 1];

    /// <summary>The sky's vertical gradient: top at 0, middle at 0.46, horizon from 0.68 of the frame's height down.</summary>
    public static (double Middle, double Horizon) SkyGradient => (0.46, 0.68);

    /// <summary>The sun's center across the frame, and its diameter as a fraction of the frame's width.</summary>
    public static (double X, double Diameter) Sun => (0.63, 0.07);

    /// <summary>The sun's radial gradient: core color to 0.36, glow at 0.58, clear at 0.72 of 0.7071 diameters (CSS's farthest corner).</summary>
    public static (double Core, double Glow, double Clear, double Radius) SunGradient => (0.36, 0.58, 0.72, Math.Sqrt(0.5));

    /// <summary>The skyline band: from <c>Top</c> to <c>Top + Height</c> of the frame's height; the water fills the rest below it.</summary>
    public static (double Top, double Height) LandBand => (0.66, 0.17);

    /// <summary>The water: the bottom 17 % of the frame.</summary>
    public static double WaterTop => 0.83;

    /// <summary>The sun's glint on the water: left edge, top and size as fractions of the frame; 2 px corners.</summary>
    public static (double X, double Y, double Width, double Height) Glint => (0.624, 0.84, 0.014, 0.13);

    /// <summary>The band the stars sit in: the top 58 % of the frame.</summary>
    public static double StarBand => 0.58;

    /// <summary>The skyline's outline in the land band (x across the frame, y down the band).</summary>
    public static ImmutableArray<Vector2> Skyline { get; } =
    [
        new(0, 0.70f), new(0.04f, 0.70f), new(0.04f, 0.45f), new(0.07f, 0.45f), new(0.07f, 0.62f), new(0.11f, 0.62f),
        new(0.11f, 0.30f), new(0.13f, 0.30f), new(0.13f, 0.22f), new(0.14f, 0.22f), new(0.14f, 0.30f), new(0.16f, 0.30f),
        new(0.16f, 0.58f), new(0.22f, 0.58f), new(0.22f, 0.48f), new(0.27f, 0.48f), new(0.27f, 0.66f), new(0.33f, 0.66f),
        new(0.33f, 0.38f), new(0.36f, 0.38f), new(0.36f, 0.52f), new(0.41f, 0.52f), new(0.41f, 0.72f), new(0.47f, 0.72f),
        new(0.47f, 0.55f), new(0.52f, 0.55f), new(0.52f, 0.26f), new(0.54f, 0.26f), new(0.54f, 0.10f), new(0.55f, 0.10f),
        new(0.55f, 0.26f), new(0.57f, 0.26f), new(0.57f, 0.50f), new(0.63f, 0.50f), new(0.63f, 0.68f), new(0.70f, 0.68f),
        new(0.70f, 0.40f), new(0.74f, 0.40f), new(0.74f, 0.60f), new(0.80f, 0.60f), new(0.80f, 0.34f), new(0.83f, 0.34f),
        new(0.83f, 0.56f), new(0.89f, 0.56f), new(0.89f, 0.74f), new(0.94f, 0.74f), new(0.94f, 0.52f), new(1, 0.52f),
        new(1, 1), new(0, 1),
    ];

    /// <summary>The stars in the star band: center (x across the frame, y down the band) and radius in DIPs.</summary>
    public static ImmutableArray<(Vector2 Center, float Radius)> Stars { get; } =
    [
        (new(0.08f, 0.14f), 0.84f), (new(0.17f, 0.40f), 0.72f), (new(0.26f, 0.09f), 0.6f), (new(0.34f, 0.28f), 0.96f),
        (new(0.42f, 0.52f), 0.6f), (new(0.51f, 0.16f), 0.72f), (new(0.58f, 0.38f), 0.6f), (new(0.66f, 0.08f), 0.84f),
        (new(0.73f, 0.30f), 0.6f), (new(0.81f, 0.18f), 0.72f), (new(0.88f, 0.44f), 0.6f), (new(0.94f, 0.12f), 0.9f),
    ];

    /// <summary>
    /// The skyline's lit windows in the land band: center (x across the frame, y down the band), radii in DIPs
    /// (0.96 by 1.2), and whether the window takes the pale light.
    /// </summary>
    public static ImmutableArray<(Vector2 Center, bool Pale)> Lights { get; } =
    [
        (new(0.055f, 0.60f), false), (new(0.12f, 0.45f), false), (new(0.125f, 0.70f), true), (new(0.19f, 0.75f), false),
        (new(0.24f, 0.62f), true), (new(0.345f, 0.50f), false), (new(0.345f, 0.80f), false), (new(0.44f, 0.85f), true),
        (new(0.53f, 0.38f), false), (new(0.53f, 0.62f), true), (new(0.60f, 0.70f), false), (new(0.72f, 0.55f), true),
        (new(0.77f, 0.75f), false), (new(0.815f, 0.45f), false), (new(0.86f, 0.70f), true), (new(0.97f, 0.66f), false),
    ];

    /// <summary>A lit window's radii in DIPs.</summary>
    public static Vector2 LightRadius => new(0.96f, 1.2f);

    /// <summary>A thumbnail's gradient: the sky from top to horizon down to 0.62 of its height, the land below.</summary>
    public static double ThumbHorizon => 0.62;

    /// <summary>The harbor at <paramref name="phase"/> (clamped to 0 to 1), with the prototype's curves.</summary>
    public static DaybreakSky SkyAt(this DaybreakPalette palette, double phase)
    {
        ArgumentNullException.ThrowIfNull(palette);
        var t = Math.Clamp(phase, 0, 1);
        var top = Ramp(palette.Zenith, ZenithStops, t);
        var horizon = Ramp(palette.Horizon, HorizonStops, t);
        var land = Mix(palette.LandDay, palette.LandNight, Math.Clamp(t * 1.25, 0, 1));
        var sunOpacity = Math.Clamp(1 - ((t - 0.4) / 0.13), 0, 1);
        var setting = Math.Clamp(t * 1.9, 0, 1);
        return new DaybreakSky(
            Top: top,
            Middle: Mix(top, horizon, 0.55),
            Horizon: horizon,
            Land: land,
            WaterTop: Mix(horizon, land, 0.45),
            WaterBottom: Mix(land, palette.DeepWater, 0.5),
            Sun: Mix(palette.SunDay, palette.SunDusk, setting),
            SunGlow: Mix(palette.GlowDay, palette.GlowDusk, setting),
            SunY: 0.22 + t,
            SunOpacity: sunOpacity,
            GlintOpacity: sunOpacity * 0.7,
            StarsOpacity: Math.Clamp((t - 0.66) / 0.22, 0, 1),
            LightsOpacity: Math.Clamp((t - 0.46) / 0.2, 0, 1));
    }

    /// <summary>The thumbnail of the strip at <paramref name="phase"/>: its sky top, horizon and land.</summary>
    public static (Color Top, Color Horizon, Color Land) ThumbAt(this DaybreakPalette palette, double phase)
    {
        var sky = palette.SkyAt(phase);
        return (sky.Top, sky.Horizon, sky.Land);
    }

    /// <summary>Linear mix of two opaque colors per channel, rounded half up (as the prototype's <c>mix</c>).</summary>
    public static Color Mix(Color from, Color to, double amount)
    {
        var u = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(Channel(from.R, to.R, u), Channel(from.G, to.G, u), Channel(from.B, to.B, u));
    }

    private static Color Ramp(ImmutableArray<Color> colors, ImmutableArray<double> stops, double t)
    {
        if (colors.Length != stops.Length)
        {
            throw new ArgumentException($"The ramp needs {stops.Length} colors, not {colors.Length}.", nameof(colors));
        }

        for (var i = 0; i < stops.Length - 1; i++)
        {
            if (t <= stops[i + 1])
            {
                return Mix(colors[i], colors[i + 1], (t - stops[i]) / (stops[i + 1] - stops[i]));
            }
        }

        return colors[^1];
    }

    private static int Channel(byte from, byte to, double u) =>
        (int)Math.Round(from + ((to - from) * u), MidpointRounding.AwayFromZero);
}
