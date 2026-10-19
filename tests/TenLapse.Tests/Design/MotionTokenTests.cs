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

        Assert.Contains($"out: '{Bezier(MotionEasing.Enter)}', in: '{Bezier(MotionEasing.Exit)}', move: '{Bezier(MotionEasing.Move)}'", page, StringComparison.Ordinal);

        // The reduced fades, the check, the staggers, the crossfade and the premiere, each from MotionSpec.
        Assert.Contains($"'opacity {N(MotionSpec.ReducedFadeIn)}ms linear'", page, StringComparison.Ordinal);
        Assert.Contains($"'opacity {N(MotionSpec.ReducedFadeOut)}ms linear'", page, StringComparison.Ordinal);
        Assert.Contains($"stroke-dashoffset {N(MotionSpec.CheckDraw)}ms ' + EASE.out + ' {N(MotionSpec.CheckDelay)}ms", page, StringComparison.Ordinal);
        Assert.Contains($"<b>Check</b> stroke draws, {N(MotionSpec.CheckDraw)} ms enter after {N(MotionSpec.CheckDelay)} ms", page, StringComparison.Ordinal);
        Assert.Contains($"delay: {N(MotionSpec.ThumbnailCascadeStart)} + i * {N(MotionSpec.ThumbnailStagger)}", page, StringComparison.Ordinal);
        Assert.Contains($"delay: {N(MotionSpec.SectionCascadeStart)} + i * {N(MotionSpec.SectionStagger)}", page, StringComparison.Ordinal);
        Assert.Contains($"<b>Colors</b> crossfade, {N(MotionSpec.ThemeCrossfade)} ms linear", page, StringComparison.Ordinal);
        Assert.Contains($"playback starts after {F((float)MotionSpec.Premiere.TotalSeconds)} s", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDistancesAreTheSpecsAndThePrototypes()
    {
        // How far things move, as the spec page says it in its text and its demos, and the prototype in its own.
        var page = File.ReadAllText(Path.Combine(DesignFiles.Root, "docs", "design", "canvas", "motion", "Motion.dc.html"));
        var prototype = File.ReadAllText(Path.Combine(DesignFiles.Root, "docs", "design", "canvas", "Main.dc.html"));

        // Arrival.
        Assert.Contains($"zoneTf: a === 'zoneOut' && !r ? 'scale({F(MotionSpec.SceneShrink)})'", page, StringComparison.Ordinal);
        Assert.Contains($"<b>Preview</b> blur {F(MotionSpec.DevelopBlur)} → 0, scale {F(MotionSpec.DevelopScale)} → 1, {N(MotionSpec.Cinema)} ms enter", page, StringComparison.Ordinal);
        Assert.Contains($"'blur({F(MotionSpec.DevelopBlur)}px)'", page, StringComparison.Ordinal);
        Assert.Contains($"'scale({F(MotionSpec.DevelopScale)})'", page, StringComparison.Ordinal);
        Assert.Contains($"<b>Filmstrip</b> rises {F(MotionSpec.ThumbnailRise)} px", page, StringComparison.Ordinal);
        Assert.Contains($"{{ dy: {F(MotionSpec.ThumbnailRise)}, delay: {N(MotionSpec.ThumbnailCascadeStart)} + i * {N(MotionSpec.ThumbnailStagger)} }}", page, StringComparison.Ordinal);
        Assert.Contains($"<b>Inspector</b> slides {F(MotionSpec.SectionSlide)} px", page, StringComparison.Ordinal);
        Assert.Contains($"{{ dx: {F(MotionSpec.SectionSlide)}, delay: {N(MotionSpec.SectionCascadeStart)} + i * {N(MotionSpec.SectionStagger)} }}", page, StringComparison.Ordinal);

        // Screens (the prototype's go).
        Assert.Contains($"ent(s.scene, {{ dy: {F(MotionSpec.SceneRise)}, outScale: {F(MotionSpec.SceneShrink)} }})", prototype, StringComparison.Ordinal);

        // Retime, Gate, Wrap.
        Assert.Contains($"<b>Readout</b> rises {F(MotionSpec.ReadoutRise)} px", page, StringComparison.Ordinal);
        Assert.Contains($"ent(s.rollPh, {{ dy: {F(MotionSpec.ReadoutRise)}, dur: {N(MotionSpec.Reveal)} }})", page, StringComparison.Ordinal);
        Assert.Contains($"<b>Panel swap</b> out {N(MotionSpec.Control)} ms {Signed(MotionSpec.PanelOutTravel)} px, in {N(MotionSpec.Scene)} ms from {Signed(MotionSpec.PanelInTravel)} px", page, StringComparison.Ordinal);
        Assert.Contains($"ent(s.panelPh, {{ dx: {F(MotionSpec.PanelInTravel)}, ox: {F(MotionSpec.PanelOutTravel)} }})", prototype, StringComparison.Ordinal);
        Assert.Contains($"<b>Title</b> rises {F(MotionSpec.TitleRise)} px", page, StringComparison.Ordinal);
        Assert.Contains($"ent(s.wrap, {{ dy: {F(MotionSpec.TitleRise)} }})", page, StringComparison.Ordinal);

        // InfoBar.
        Assert.Contains($"<b>In</b> from {Signed(MotionSpec.InfoBarTravel)} px", page, StringComparison.Ordinal);
        Assert.Contains($"<b>Out</b> to {Signed(MotionSpec.InfoBarTravel)} px", page, StringComparison.Ordinal);
        Assert.Contains($"ent(s.info, {{ dy: {F(MotionSpec.InfoBarTravel)}, oy: {F(MotionSpec.InfoBarTravel)}, dur: {N(MotionSpec.Reveal)} }})", page, StringComparison.Ordinal);
        Assert.Contains($"list: ent(s.listPh, {{ dy: {F(MotionSpec.InfoBarDetailsTravel)}, dur: {N(MotionSpec.Reveal)} }})", prototype, StringComparison.Ordinal);

        // First launch.
        Assert.Contains($"dropLift: s.drag && !r ? 'translateY({F(MotionSpec.DragLift)}px)'", prototype, StringComparison.Ordinal);
    }

    private static string Ms(TimeSpan value) => string.Create(CultureInfo.InvariantCulture, $"{value.TotalMilliseconds:0}ms");

    // Milliseconds as the pages write them ("150").
    private static string N(TimeSpan value) => value.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture);

    // A number as the pages write it in code ("-12", "0.985").
    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    // A distance as the spec page writes it in its text: a true minus sign, and a plus sign for "from +16 px".
    private static string Signed(float value) => value < 0 ? "−" + F(-value) : "+" + F(value);

    private static string Point(float x, float y) => string.Create(CultureInfo.InvariantCulture, $"{x},{y}");

    private static string Bezier(MotionEasing easing)
    {
        var (x1, y1, x2, y2) = MotionSpec.Curve(easing);
        return string.Create(CultureInfo.InvariantCulture, $"cubic-bezier({x1}, {y1}, {x2}, {y2})");
    }
}
