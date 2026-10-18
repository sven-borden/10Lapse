using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Presentation;

namespace TenLapse.Tests.Presentation;

public sealed class ProjectTextTests
{
    // The input is frames per second (Per second), seconds (Length) or seconds per photo (Per photo).
    [Theory]
    [InlineData(742, 30, 1, SpeedMode.PerSecond, "742 photos → 24.7 s at 30 fps")]
    [InlineData(720, 24, 1, SpeedMode.PerSecond, "720 photos → 30.0 s at 24 fps")]
    [InlineData(5000, 30000, 1001, SpeedMode.PerSecond, "5,000 photos → 2 min 47 s at 29.97 fps")]
    [InlineData(0, 30, 1, SpeedMode.PerSecond, "0 photos → 0.0 s at 30 fps")]
    [InlineData(1, 30, 1, SpeedMode.PerSecond, "1 photo → 0.0 s at 30 fps")]
    [InlineData(3, 30, 1, SpeedMode.PerSecond, "3 photos → 0.1 s at 30 fps")]
    [InlineData(5000, 30, 1, SpeedMode.PerSecond, "5,000 photos → 2 min 47 s at 30 fps")]
    [InlineData(742, 30, 1, SpeedMode.Length, "742 photos → 30.0 s at 24.73 fps")]
    [InlineData(0, 30, 1, SpeedMode.Length, "0 photos → 0.0 s at 10 s per photo")]
    [InlineData(1, 30, 1, SpeedMode.Length, "1 photo → 10.0 s at 10 s per photo")]
    [InlineData(3, 30, 1, SpeedMode.Length, "3 photos → 30.0 s at 10 s per photo")]
    [InlineData(5000, 30, 1, SpeedMode.Length, "5,000 photos → 41.7 s at 120 fps")]
    [InlineData(40, 1, 2, SpeedMode.PerPhoto, "40 photos → 20.0 s at 0.5 s per photo")]
    [InlineData(0, 1, 2, SpeedMode.PerPhoto, "0 photos → 0.0 s at 0.5 s per photo")]
    [InlineData(1, 1, 2, SpeedMode.PerPhoto, "1 photo → 0.5 s at 0.5 s per photo")]
    [InlineData(3, 1, 2, SpeedMode.PerPhoto, "3 photos → 1.5 s at 0.5 s per photo")]
    [InlineData(5000, 1, 2, SpeedMode.PerPhoto, "5,000 photos → 41 min 40 s at 0.5 s per photo")]
    [InlineData(10, 2, 1, SpeedMode.PerPhoto, "10 photos → 20.0 s at 2 s per photo")]
    public void TheTimingReadoutReadsAsTheProductSaysIt(int count, int numerator, int denominator, SpeedMode mode, string expected)
    {
        var input = new Fraction(numerator, denominator);
        var timing = mode switch
        {
            SpeedMode.PerSecond => TimingSettings.PerSecond(input),
            SpeedMode.Length => TimingSettings.Length(input),
            _ => TimingSettings.PerPhoto(input),
        };

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
