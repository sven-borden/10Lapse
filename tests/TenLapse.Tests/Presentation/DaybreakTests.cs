using System.Drawing;
using System.Globalization;
using TenLapse.Core.Timing;
using TenLapse.Presentation.FirstLaunch;
using TenLapse.Tests.Design;

namespace TenLapse.Tests.Presentation;

public sealed class DaybreakTests
{
    [Fact]
    public void TheLoopIsTwentySecondsAtThirtyFramesPerSecond()
    {
        Assert.Equal(20 * FrameTiming.TicksPerSecond, Daybreak.LengthTicks);
        Assert.Equal(new Fraction(30, 1), Daybreak.Rate);
    }

    [Fact]
    public void EveryFrameIsShownFromItsOwnStartToTheNextOne()
    {
        for (var frame = 0; frame < Daybreak.FrameCount; frame++)
        {
            var start = Daybreak.StartOf(frame);
            Assert.Equal(frame, Daybreak.FrameAt(start));
            Assert.Equal(TimeSpan.FromTicks(FrameTiming.FrameStart(frame, Daybreak.Rate)), start);
            if (frame > 0)
            {
                Assert.Equal(frame - 1, Daybreak.FrameAt(start - TimeSpan.FromTicks(1)));
            }
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]                  // a clock before its start shows the first frame
    [InlineData(200_000_000L, 0)]        // 20 s: the loop starts again
    [InlineData(199_999_999L, 599)]
    [InlineData(260_000_000L, 180)]      // 26 s is 6 s into the second loop
    public void TheFrameLoops(long ticks, int expected)
    {
        Assert.Equal(expected, Daybreak.FrameAt(TimeSpan.FromTicks(ticks)));
    }

    [Fact]
    public void TheStillFrameIsAtGoldenHour()
    {
        var phase = Daybreak.Phase(Daybreak.StillFrame);
        var sky = Palette().SkyAt(phase);

        Assert.InRange(phase, 0.25, 0.35);
        Assert.Equal(1, sky.SunOpacity);     // the sun is out, low
        Assert.InRange(sky.SunY, 0.45, 0.6);
        Assert.Equal(0, sky.LightsOpacity);  // the city is not lit yet
        Assert.Equal("Frame 0181 of 0600", Daybreak.FrameCaption(Daybreak.StillFrame));
        Assert.Equal("00:06.00 / 00:20.00", Daybreak.TimeCaption(Daybreak.StillFrame));
    }

    [Theory]
    [InlineData(0, "Frame 0001 of 0600", "00:00.00 / 00:20.00")]
    [InlineData(1, "Frame 0002 of 0600", "00:00.03 / 00:20.00")]
    [InlineData(599, "Frame 0600 of 0600", "00:19.96 / 00:20.00")]
    public void TheCaptionNamesTheFrameAndItsTime(int frame, string frameCaption, string timeCaption)
    {
        Assert.Equal(frameCaption, Daybreak.FrameCaption(frame));
        Assert.Equal(timeCaption, Daybreak.TimeCaption(frame));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(27, 0)]
    [InlineData(28, 1)]    // phase 0.0467, nearest of twelve thumbnails is the second
    [InlineData(299, 5)]
    [InlineData(300, 6)]
    [InlineData(599, 11)]
    public void TheRingSitsOnTheNearestThumbnail(int frame, int thumb)
    {
        Assert.Equal(thumb, Daybreak.SelectedThumb(frame));
    }

    [Fact]
    public void ThumbnailsRunFromDayToNight()
    {
        Assert.Equal(0, Daybreak.ThumbPhase(0));
        Assert.Equal(1, Daybreak.ThumbPhase(Daybreak.ThumbCount - 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Daybreak.ThumbPhase(Daybreak.ThumbCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => Daybreak.Phase(Daybreak.FrameCount));
    }

    [Fact]
    public void TheClockAdvancesOnlyWhileRunning()
    {
        var now = TimeSpan.FromSeconds(100);
        var clock = new DaybreakClock(Daybreak.StartOf(Daybreak.StillFrame), () => now);

        now += TimeSpan.FromSeconds(5);
        Assert.Equal(Daybreak.StillFrame, Daybreak.FrameAt(clock.Played));

        clock.Start();
        now += TimeSpan.FromSeconds(1);
        Assert.Equal(Daybreak.StillFrame + 30, Daybreak.FrameAt(clock.Played));

        clock.Stop();
        now += TimeSpan.FromMinutes(3); // the window was in the background
        Assert.Equal(Daybreak.StillFrame + 30, Daybreak.FrameAt(clock.Played));

        clock.Start();
        clock.Start(); // a second start changes nothing
        now += TimeSpan.FromSeconds(0.5);
        Assert.True(clock.IsRunning);
        Assert.Equal(Daybreak.StillFrame + 45, Daybreak.FrameAt(clock.Played));
    }

    [Fact]
    public void DayAndNightAreTheEndsOfTheRamps()
    {
        var palette = Palette();
        var day = palette.SkyAt(0);
        var night = palette.SkyAt(1);

        Assert.Equal(palette.Zenith[0], day.Top);
        Assert.Equal(palette.Horizon[0], day.Horizon);
        Assert.Equal(palette.LandDay, day.Land);
        Assert.Equal(palette.Zenith[^1], night.Top);
        Assert.Equal(palette.Horizon[^1], night.Horizon);
        Assert.Equal(palette.LandNight, night.Land);
        Assert.Equal(day, palette.SkyAt(-1));
        Assert.Equal(night, palette.SkyAt(2));
    }

    [Fact]
    public void TheSkyMatchesThePrototypeAtItsOpeningPhase()
    {
        // Main.dc.html opens at phase 0.18: sky(0.18) there gives these colors.
        var sky = Palette().SkyAt(0.18);

        Assert.Equal("#73A1D3", Hex(sky.Top));
        Assert.Equal("#DFD7B8", Hex(sky.Horizon));
        Assert.Equal("#2A354B", Hex(sky.Land));
        Assert.Equal(0.40, sky.SunY, 6);
    }

    [Fact]
    public void TheSunSetsThenTheCityLightsUpThenTheStarsComeOut()
    {
        var palette = Palette();

        Assert.Equal(1, palette.SkyAt(0.4).SunOpacity);
        Assert.Equal(0, palette.SkyAt(0.6).SunOpacity);
        Assert.Equal(0, palette.SkyAt(0.46).LightsOpacity);
        Assert.Equal(1, palette.SkyAt(0.7).LightsOpacity);
        Assert.Equal(0, palette.SkyAt(0.66).StarsOpacity);
        Assert.Equal(1, palette.SkyAt(0.9).StarsOpacity);
        Assert.Equal(palette.SkyAt(0.45).SunOpacity * 0.7, palette.SkyAt(0.45).GlintOpacity, 9);
    }

    [Fact]
    public void MixRoundsHalfUpPerChannel()
    {
        var mixed = DaybreakScene.Mix(Color.FromArgb(0, 0, 0), Color.FromArgb(1, 3, 255), 0.5);

        Assert.Equal(Color.FromArgb(1, 2, 128), mixed);
    }

    [Fact]
    public void ARampWithTheWrongNumberOfColorsIsRefused()
    {
        var palette = Palette() with { Zenith = [Color.Black, Color.White] };

        Assert.Throws<ArgumentException>(() => palette.SkyAt(0.5));
    }

    [Fact]
    public void TheSceneLayoutStaysInsideTheFrame()
    {
        Assert.All(DaybreakScene.Skyline, point => Assert.InRange(point.X, 0, 1));
        Assert.All(DaybreakScene.Skyline, point => Assert.InRange(point.Y, 0, 1));
        Assert.All(DaybreakScene.Stars, star => Assert.InRange(star.Center.Y, 0, 1));
        Assert.All(DaybreakScene.Lights, light => Assert.InRange(light.Center.X, 0, 1));
        Assert.Equal(1, DaybreakScene.LandBand.Top + DaybreakScene.LandBand.Height + (1 - DaybreakScene.WaterTop), 9);
        Assert.Equal(DaybreakScene.ZenithStops.Length, DaybreakScene.HorizonStops.Length);
    }

    [Fact]
    public void ThePaletteComesFromTheColorTokens()
    {
        // The App builds the palette from these keys (FirstLaunchView.LoadPalette); they must exist with these values.
        var palette = Palette();

        Assert.Equal(7, palette.Zenith.Length);
        Assert.Equal("#C4E0F3", Hex(palette.Horizon[0]));  // DESIGN.md daylight
        Assert.Equal("#10162E", Hex(palette.Horizon[^1])); // DESIGN.md night
        Assert.Equal("#5E9BD6", Hex(palette.Zenith[0]));
    }

    /// <summary>The palette as the App reads it, from the root of Themes/Colors.xaml.</summary>
    internal static DaybreakPalette Palette()
    {
        var root = DesignFiles.RootEntries(DesignFiles.LoadTheme("Colors.xaml"));
        Color Token(string key) => Color.FromArgb(int.Parse(root[key].Value.Trim().TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture) | unchecked((int)0xFF000000));

        string[] zenith = ["Day", "Golden", "Amber", "Rose", "Dusk", "BlueHour", "Night"];
        string[] horizon = ["Daylight", "Golden", "Amber", "Rose", "Dusk", "BlueHour", "Night"];
        return new DaybreakPalette(
            Zenith: [.. zenith.Select(name => Token($"DaybreakZenith{name}Color"))],
            Horizon: [.. horizon.Select(name => Token($"{name}Color"))],
            LandDay: Token("DaybreakLandDayColor"),
            LandNight: Token("DaybreakLandNightColor"),
            DeepWater: Token("DaybreakDeepWaterColor"),
            SunDay: Token("DaybreakSunDayColor"),
            SunDusk: Token("DaybreakSunDuskColor"),
            GlowDay: Token("DaybreakGlowDayColor"),
            GlowDusk: Token("DaybreakGlowDuskColor"),
            Star: Token("DaybreakStarColor"),
            Light: Token("DaybreakLightColor"),
            LightPale: Token("DaybreakLightPaleColor"));
    }

    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
