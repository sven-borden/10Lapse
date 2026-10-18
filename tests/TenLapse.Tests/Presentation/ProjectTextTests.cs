using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Presentation;

namespace TenLapse.Tests.Presentation;

public sealed class ProjectTextTests
{
    [Theory]
    [InlineData(742, 30, 1, SpeedMode.PerSecond, "742 photos → 24.7 s at 30 fps")]
    [InlineData(720, 24, 1, SpeedMode.PerSecond, "720 photos → 30.0 s at 24 fps")]
    [InlineData(5000, 30000, 1001, SpeedMode.PerSecond, "5,000 photos → 2 min 47 s at 29.97 fps")]
    [InlineData(742, 742, 30, SpeedMode.Length, "742 photos → 30.0 s at 24.73 fps")]
    [InlineData(40, 2, 1, SpeedMode.PerPhoto, "40 photos → 20.0 s at 0.5 s per photo")]
    [InlineData(10, 1, 2, SpeedMode.PerSecond, "10 photos → 20.0 s at 2 s per photo")]
    [InlineData(1, 30, 1, SpeedMode.PerSecond, "1 photo → 0.0 s at 30 fps")]
    [InlineData(0, 30, 1, SpeedMode.PerSecond, "0 photos → 0.0 s at 30 fps")]
    public void TheTimingReadoutReadsAsTheProductSaysIt(int count, int numerator, int denominator, SpeedMode mode, string expected)
    {
        var timing = new TimingSettings(new Fraction(numerator, denominator), mode);

        Assert.Equal(expected, ProjectText.TimingReadout(count, timing));
    }

    [Theory]
    [InlineData(0, "This project's settings will be cleared.")]
    [InlineData(1, "This project's photo and settings will be cleared. The photo file stays on your PC.")]
    [InlineData(5000, "This project's 5,000 photos and settings will be cleared. The photo files stay on your PC.")]
    public void TheNewProjectQuestionSaysWhatWillBeCleared(int count, string expected)
    {
        var confirmation = ProjectText.NewProjectConfirmation(count);

        Assert.Equal("Start a new project?", confirmation.Title);
        Assert.Equal(expected, confirmation.Message);
    }

    [Theory]
    [InlineData("Timelapse", false, false, "10Lapse")]
    [InlineData("Harbor sunset", true, false, "Harbor sunset – 10Lapse")]
    [InlineData("Harbor sunset", true, true, "● Harbor sunset – 10Lapse")]
    [InlineData("Timelapse", false, true, "● Timelapse – 10Lapse")]
    public void TheWindowTitleNamesTheProjectOnceThereIsOne(string name, bool hasFrames, bool unsaved, string expected)
    {
        Assert.Equal(expected, ProjectText.WindowTitle(name, hasFrames, unsaved));
    }
}
