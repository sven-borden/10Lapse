using System.Globalization;
using System.Text.Json;
using TenLapse.Presentation.Motion;

namespace TenLapse.Tests.Design;

/// <summary>
/// The motion tokens have one source for code, <see cref="MotionSpec"/>; the XAML resources (Themes/Motion.xaml), the design
/// sidecar (.impeccable/design.json) and DESIGN.md's Motion tokens line must say the same (#35), as the color and layout
/// tokens do.
/// </summary>
public sealed class MotionTokenTests
{
    private static readonly Dictionary<string, TimeSpan> _durations = new(StringComparer.Ordinal)
    {
        ["Tap"] = MotionSpec.Tap,
        ["Control"] = MotionSpec.Control,
        ["Reveal"] = MotionSpec.Reveal,
        ["Scene"] = MotionSpec.Scene,
        ["Cinema"] = MotionSpec.Cinema,
        ["ThemeCrossfade"] = MotionSpec.ThemeCrossfade,
        ["ReducedFadeIn"] = MotionSpec.ReducedFadeIn,
        ["ReducedFadeOut"] = MotionSpec.ReducedFadeOut,
    };

    [Fact]
    public void TheXamlDurationsAndCurvesAreTheSpecs()
    {
        var motion = DesignFiles.RootEntries(DesignFiles.LoadTheme("Motion.xaml"));
        foreach (var (name, value) in _durations)
        {
            Assert.Equal(value, TimeSpan.Parse(motion[$"Motion{name}Duration"].Value.Trim(), CultureInfo.InvariantCulture));
        }

        foreach (var easing in new[] { MotionEasing.Enter, MotionEasing.Exit, MotionEasing.Move })
        {
            var (x1, y1, x2, y2) = MotionSpec.Curve(easing);
            var spline = motion[$"Motion{easing}KeySpline"];
            Assert.Equal(Point(x1, y1), (string)spline.Attribute("ControlPoint1")!);
            Assert.Equal(Point(x2, y2), (string)spline.Attribute("ControlPoint2")!);
        }

        Assert.Equal(MotionSpec.Tap, TimeSpan.Parse((string)motion["MotionTapBrushTransition"].Attribute("Duration")!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void TheDesignSidecarMatchesTheSpec()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(DesignFiles.Root, ".impeccable", "design.json")));
        var entries = document.RootElement.EnumerateObject()
            .SelectMany(property => property.Value.ValueKind == JsonValueKind.Object && property.Value.TryGetProperty("motion", out var motion) ? motion.EnumerateArray() : Enumerable.Empty<JsonElement>())
            .Concat(document.RootElement.TryGetProperty("motion", out var root) ? root.EnumerateArray() : Enumerable.Empty<JsonElement>())
            .ToDictionary(entry => entry.GetProperty("name").GetString()!, entry => entry.GetProperty("value").GetString()!, StringComparer.Ordinal);

        Assert.Equal(Ms(MotionSpec.Tap), entries["duration-tap"]);
        Assert.Equal(Ms(MotionSpec.Control), entries["duration-control"]);
        Assert.Equal(Ms(MotionSpec.Reveal), entries["duration-reveal"]);
        Assert.Equal(Ms(MotionSpec.Scene), entries["duration-scene"]);
        Assert.Equal(Ms(MotionSpec.Cinema), entries["duration-cinema"]);
        Assert.Equal(Ms(MotionSpec.ThemeCrossfade), entries["duration-theme-crossfade"]);
        Assert.Equal(Ms(MotionSpec.ReducedFadeIn), entries["duration-reduced-fade-in"]);
        Assert.Equal(Ms(MotionSpec.ReducedFadeOut), entries["duration-reduced-fade-out"]);
        Assert.Equal(Ms(MotionSpec.ThumbnailStagger), entries["stagger-thumb"]);
        Assert.Equal(Ms(MotionSpec.SectionStagger), entries["stagger-section"]);
        Assert.Equal(Ms(MotionSpec.CheckDraw), entries["duration-check-draw"]);
        Assert.Equal(Ms(MotionSpec.CheckDelay), entries["delay-check"]);
        Assert.Equal(Ms(MotionSpec.Premiere), entries["delay-premiere"]);
        Assert.Equal(Bezier(MotionEasing.Enter), entries["ease-enter"]);
        Assert.Equal(Bezier(MotionEasing.Exit), entries["ease-exit"]);
        Assert.Equal(Bezier(MotionEasing.Move), entries["ease-move"]);
        Assert.Equal("linear", entries["ease-linear"]);
    }

    [Fact]
    public void DesignMdSaysTheSameTokens()
    {
        // "Motion tokens: Tap 83 ms, Control 167 ms, Reveal 250 ms, Scene 400 ms, Cinema 700 ms; Enter `cubic-bezier(...)`, ..."
        var line = File.ReadAllLines(Path.Combine(DesignFiles.Root, "DESIGN.md")).Single(text => text.StartsWith("- Motion tokens:", StringComparison.Ordinal));
        foreach (var name in new[] { "Tap", "Control", "Reveal", "Scene", "Cinema" })
        {
            Assert.Contains($"{name} {_durations[name].TotalMilliseconds:0} ms", line, StringComparison.Ordinal);
        }

        foreach (var easing in new[] { MotionEasing.Enter, MotionEasing.Exit, MotionEasing.Move })
        {
            Assert.Contains($"{easing} `{Bezier(easing)}`", line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheMotionSpecPageStillSaysTheseNumbers()
    {
        // The approved spec page itself: its duration table and curves (a change there must reach MotionSpec).
        var page = File.ReadAllText(Path.Combine(DesignFiles.Root, "docs", "design", "canvas", "motion", "Motion.dc.html"));
        foreach (var name in new[] { "Tap", "Control", "Reveal", "Scene", "Cinema" })
        {
            Assert.Matches($@"\['{name}', {_durations[name].TotalMilliseconds:0},", page);
        }

        Assert.Contains("out: 'cubic-bezier(0.16, 1, 0.3, 1)', in: 'cubic-bezier(0.7, 0, 0.84, 0)', move: 'cubic-bezier(0.65, 0, 0.35, 1)'", page, StringComparison.Ordinal);
        Assert.Contains("'opacity 150ms linear'", page, StringComparison.Ordinal);
        Assert.Contains("'opacity 90ms linear'", page, StringComparison.Ordinal);
        Assert.Contains("stroke-dashoffset 500ms ' + EASE.out + ' 200ms", page, StringComparison.Ordinal);
        Assert.Contains("delay: 220 + i * 18", page, StringComparison.Ordinal);
        Assert.Contains("delay: 160 + i * 60", page, StringComparison.Ordinal);
        Assert.Contains("playback starts after 1.1 s", page, StringComparison.Ordinal);
    }

    private static string Ms(TimeSpan value) => string.Create(CultureInfo.InvariantCulture, $"{value.TotalMilliseconds:0}ms");

    private static string Point(float x, float y) => string.Create(CultureInfo.InvariantCulture, $"{x},{y}");

    private static string Bezier(MotionEasing easing)
    {
        var (x1, y1, x2, y2) = MotionSpec.Curve(easing);
        return string.Create(CultureInfo.InvariantCulture, $"cubic-bezier({x1}, {y1}, {x2}, {y2})");
    }
}
