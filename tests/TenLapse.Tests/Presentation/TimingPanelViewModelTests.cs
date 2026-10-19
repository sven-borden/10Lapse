using System.Globalization;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Presentation;
using TenLapse.Tests.Import;
using TenLapse.Tests.Projects;

namespace TenLapse.Tests.Presentation;

/// <summary>The Timing panel (#17): modes, presets, custom values, bounds, the readout, and that every change goes through the project.</summary>
public sealed class TimingPanelViewModelTests
{
    private static readonly CultureInfo _en = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo _de = CultureInfo.GetCultureInfo("de-DE");
    private static readonly CultureInfo _fr = CultureInfo.GetCultureInfo("fr-FR");

    private static ShellViewModel Shell(FakeErrorSink? errors = null) =>
        new(new FakeDialogService(), errors ?? new FakeErrorSink());

    private static TimingPanelViewModel Panel(ShellViewModel shell, int photos = 742, CultureInfo? culture = null, FakeErrorSink? errors = null)
    {
        if (photos > 0)
        {
            shell.CurrentProject.Model.AddFrames(TestFrames.Folder("a", photos));
        }

        return new TimingPanelViewModel(shell, errors ?? new FakeErrorSink(), culture ?? _en);
    }

    [Fact]
    public void ADefaultProjectShowsPerSecondAtThirtyWithTheReadout()
    {
        var panel = Panel(Shell());

        Assert.Equal(["Per second", "Length", "Per photo"], panel.ModeLabels);
        Assert.Equal(0, panel.SelectedModeIndex);
        Assert.Equal(["12", "24", "25", "30", "50", "60"], panel.PresetLabels);
        Assert.Equal(3, panel.SelectedPresetIndex);
        Assert.Equal("30", panel.ValueText);
        Assert.Equal("742 photos → 24.7 s", panel.Headline);
        Assert.Equal("at 30 fps", panel.Detail);
        Assert.Equal("742 photos → 24.7 s at 30 fps", panel.Readout);
        Assert.False(panel.HasMessage);
        Assert.False(panel.HasNotice);
    }

    [Fact]
    public void ThePresetsAreTheSixFramesPerSecondOfTheIssueAndPickingOneWritesTheProject()
    {
        var shell = Shell();
        var panel = Panel(shell);
        var project = shell.CurrentProject.Model;

        var expected = new[] { 12, 24, 25, 30, 50, 60 };
        for (var i = 0; i < expected.Length; i++)
        {
            panel.SelectedPresetIndex = i;

            Assert.Equal(TimingSettings.Default with { FramesPerSecond = new Fraction(expected[i], 1) }, project.Timing);
            Assert.Equal(SpeedMode.PerSecond, project.Timing.Mode);
            Assert.Equal(i, panel.SelectedPresetIndex);
            Assert.Equal(expected[i].ToString(CultureInfo.InvariantCulture), panel.ValueText);
            Assert.Equal(new Fraction(expected[i], 1), project.EffectiveRate.Rate);
        }
    }

    [Fact]
    public void ACustomValueMatchesNoPresetAndTheBoxShowsIt()
    {
        var shell = Shell();
        var panel = Panel(shell);

        panel.CommitValue("37");

        Assert.Equal(new Fraction(37, 1), shell.CurrentProject.Model.Timing.FramesPerSecond);
        Assert.Equal(-1, panel.SelectedPresetIndex);
        Assert.Equal("37", panel.ValueText);
        Assert.Equal("742 photos → 20.1 s", panel.Headline);
        Assert.False(panel.HasMessage);
    }

    [Fact]
    public void TypingAPresetsValueSelectsThePreset()
    {
        var panel = Panel(Shell());
        panel.CommitValue("37");

        panel.CommitValue("50");

        Assert.Equal(4, panel.SelectedPresetIndex);
    }

    [Fact]
    public void NtscRatesAreStoredAsExactRationalsAndReadOutAs2997()
    {
        var shell = Shell();
        var panel = Panel(shell);

        panel.CommitValue("29.97");

        Assert.Equal(new Fraction(30000, 1001), shell.CurrentProject.Model.Timing.FramesPerSecond);
        Assert.Equal(new Fraction(30000, 1001), shell.CurrentProject.Model.EffectiveRate.Rate);
        Assert.Equal("29.97", panel.ValueText);
        Assert.Equal("at 29.97 fps", panel.Detail);
        Assert.Equal(-1, panel.SelectedPresetIndex);

        panel.CommitValue("59,94");

        Assert.Equal(new Fraction(60000, 1001), shell.CurrentProject.Model.Timing.FramesPerSecond);
        Assert.Equal("59.94", panel.ValueText);
        Assert.Equal("at 59.94 fps", panel.Detail);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void TheBoxTakesAndShowsADecimalCommaInGermanAndFrench(string name)
    {
        var culture = CultureInfo.GetCultureInfo(name);
        var shell = Shell();
        var panel = Panel(shell, culture: culture);

        panel.CommitValue("29,97");

        Assert.Equal(new Fraction(30000, 1001), shell.CurrentProject.Model.Timing.FramesPerSecond);
        Assert.Equal("29,97", panel.ValueText);

        panel.CommitValue("37,4829");

        Assert.Equal(new Fraction(3748, 100), shell.CurrentProject.Model.Timing.FramesPerSecond);
        Assert.Equal("37,48", panel.ValueText);
        Assert.Equal("Rounded to 37,48 fps.", panel.Message);
    }

    [Fact]
    public void ACommaAndADotAreBothDecimalPointsInEveryCulture()
    {
        foreach (var culture in new[] { _en, _de, _fr })
        {
            var shell = Shell();
            var panel = Panel(shell, culture: culture);

            panel.CommitValue("12.5");
            Assert.Equal(new Fraction(25, 2), shell.CurrentProject.Model.Timing.FramesPerSecond);

            panel.CommitValue("24,5");
            Assert.Equal(new Fraction(49, 2), shell.CurrentProject.Model.Timing.FramesPerSecond);
        }
    }

    [Fact]
    public void AValueWithTooManyDecimalsIsRoundedAndTheBoxNeverShowsFractionalGarbage()
    {
        var shell = Shell();
        var panel = Panel(shell);

        panel.CommitValue("37.4829");

        Assert.Equal(new Fraction(3748, 100), shell.CurrentProject.Model.Timing.FramesPerSecond);
        Assert.Equal("37.48", panel.ValueText);
        Assert.Equal("Rounded to 37.48 fps.", panel.Message);
        Assert.True(panel.HasMessage);
    }

    [Theory]
    [InlineData("0", 1)]
    [InlineData("0.5", 1)]
    [InlineData("121", 120)]
    [InlineData("9999", 120)]
    public void AFrameRateOutsideTheRangeIsHeldToTheBoundWithAMessage(string text, int stored)
    {
        var shell = Shell();
        var panel = Panel(shell);

        panel.CommitValue(text);

        Assert.Equal(new Fraction(stored, 1), shell.CurrentProject.Model.Timing.FramesPerSecond);
        Assert.Equal(stored.ToString(CultureInfo.InvariantCulture), panel.ValueText);
        Assert.Equal($"Frame rates run from 1 to 120 fps, so {text} is now {stored} fps.", panel.Message);
    }

    [Fact]
    public void TextThatIsNotANumberChangesNothingAndTheBoxGoesBackToTheStoredValue()
    {
        var shell = Shell();
        var panel = Panel(shell);
        panel.SelectedPresetIndex = 2;
        var version = shell.CurrentProject.Model.Version;
        var raised = new List<string?>();
        panel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        panel.CommitValue("fast");

        Assert.Equal(version, shell.CurrentProject.Model.Version);
        Assert.Equal(new Fraction(25, 1), shell.CurrentProject.Model.Timing.FramesPerSecond);
        Assert.Equal("Type a frame rate from 1 to 120 fps, like 24 or 29.97.", panel.Message);

        // The binding is told the stored value again, so "fast" does not stay in the box.
        Assert.Contains(nameof(TimingPanelViewModel.ValueText), raised);
        Assert.Equal("25", panel.ValueText);
    }

    [Fact]
    public void AMessageGoesWhenTheSpeedIsChangedAnotherWay()
    {
        var panel = Panel(Shell());
        panel.CommitValue("9999");
        Assert.True(panel.HasMessage);

        panel.SelectedPresetIndex = 1;

        Assert.False(panel.HasMessage);
    }

    [Fact]
    public void TextEqualToWhatTheBoxShowsChangesNothingAndKeepsTheMessage()
    {
        var shell = Shell();
        var panel = Panel(shell);
        panel.CommitValue("9999");
        var version = shell.CurrentProject.Model.Version;

        panel.CommitValue("120");

        Assert.Equal(version, shell.CurrentProject.Model.Version);
        Assert.True(panel.HasMessage);
    }

    [Fact]
    public void LengthModeStoresTheTargetAndComputesTheRateFromThePhotos()
    {
        var shell = Shell();
        var panel = Panel(shell);

        panel.SelectedModeIndex = 1;

        var project = shell.CurrentProject.Model;
        Assert.Equal(SpeedMode.Length, project.Timing.Mode);
        Assert.Equal(["10 s", "15 s", "30 s", "60 s"], panel.PresetLabels);
        Assert.Equal(2, panel.SelectedPresetIndex);
        Assert.Equal("Custom length in seconds", panel.ValueLabel);
        Assert.Equal("30", panel.ValueText);
        Assert.Equal("742 photos → 30.0 s", panel.Headline);
        Assert.Equal("at 24.73 fps", panel.Detail);
        Assert.Equal(new Fraction(742, 30), project.EffectiveRate.Rate);

        panel.CommitValue("24.7");

        Assert.Equal(new Fraction(247, 10), project.Timing.LengthSeconds);
        Assert.Equal("24.7", panel.ValueText);
        Assert.Equal(-1, panel.SelectedPresetIndex);
        Assert.Equal("742 photos → 24.7 s", panel.Headline);
        Assert.Equal("at 30.04 fps", panel.Detail);
        Assert.Equal(new Fraction(742 * 10, 247), project.EffectiveRate.Rate);
        Assert.False(panel.HasNotice);
    }

    [Fact]
    public void LengthTakesMinutesAndSecondsAndHoldsToTheRange()
    {
        var shell = Shell();
        var panel = Panel(shell, photos: 100);
        panel.SelectedModeIndex = 1;

        panel.CommitValue("1:30");

        Assert.Equal(new Fraction(90, 1), shell.CurrentProject.Model.Timing.LengthSeconds);
        Assert.Equal("90", panel.ValueText);

        panel.CommitValue("0");

        Assert.Equal(new Fraction(1, 10), shell.CurrentProject.Model.Timing.LengthSeconds);
        Assert.Equal("A video can be from 0.1 s to 10 h 00 min long, so 0 is now 0.1 s.", panel.Message);
    }

    [Fact]
    public void ALengthTheFastestSpeedCannotMeetIsPlayedAtTheLimitAndSaid()
    {
        var shell = Shell();
        var panel = Panel(shell, photos: 742);
        panel.SelectedModeIndex = 1;

        panel.CommitValue("3");

        var project = shell.CurrentProject.Model;
        Assert.Equal(new Fraction(120, 1), project.EffectiveRate.Rate);
        Assert.Equal(RateClamp.Max, project.EffectiveRate.Clamp);
        Assert.Equal("742 photos → 6.2 s", panel.Headline);
        Assert.Equal("at 120 fps", panel.Detail);
        Assert.Equal("Your photos can't play in 3 s: the fastest speed is 120 fps, so the video is 6.2 s long.", panel.Notice);
        Assert.True(panel.HasNotice);

        // It follows the photos: with fewer, the target is reachable and the notice goes.
        project.RemoveFrames(project.Frames.Take(700).Select(frame => frame.Id));

        Assert.False(panel.HasNotice);
    }

    [Fact]
    public void ALengthThePhotosCannotFillAtTheSlowestSpeedIsPlayedAtTheLimitAndSaid()
    {
        var shell = Shell();
        var panel = Panel(shell, photos: 3);
        panel.SelectedModeIndex = 1;

        panel.CommitValue("60");

        Assert.Equal("Your photos can't fill 60 s: the slowest speed is 10 s per photo, so the video is 30.0 s long.", panel.Notice);
        Assert.Equal("3 photos → 30.0 s", panel.Headline);
    }

    [Fact]
    public void PerPhotoStoresSecondsAndTheRateIsTheirReciprocal()
    {
        var shell = Shell();
        var panel = Panel(shell, photos: 40);

        panel.SelectedModeIndex = 2;

        var project = shell.CurrentProject.Model;
        Assert.Equal(SpeedMode.PerPhoto, project.Timing.Mode);
        Assert.Equal(["0.25 s", "0.5 s", "1 s", "2 s", "5 s"], panel.PresetLabels);
        Assert.Equal(1, panel.SelectedPresetIndex);
        Assert.Equal("Custom seconds per photo", panel.ValueLabel);
        Assert.Equal("s per photo", panel.ValueUnit);
        Assert.Equal("0.5", panel.ValueText);
        Assert.Equal("40 photos → 20.0 s", panel.Headline);
        Assert.Equal("at 0.5 s per photo", panel.Detail);
        Assert.Equal(new Fraction(2, 1), project.EffectiveRate.Rate);

        panel.CommitValue("0,25");

        Assert.Equal(new Fraction(1, 4), project.Timing.SecondsPerPhoto);
        Assert.Equal(new Fraction(4, 1), project.EffectiveRate.Rate);
        Assert.Equal(0, panel.SelectedPresetIndex);
        Assert.Equal("40 photos → 10.0 s", panel.Headline);

        panel.CommitValue("0.05");

        Assert.Equal(new Fraction(1, 10), project.Timing.SecondsPerPhoto);
        Assert.Equal(new Fraction(10, 1), project.EffectiveRate.Rate);
        Assert.Equal("A photo can stay from 0.1 s to 10 s, so 0.05 is now 0.1 s.", panel.Message);
    }

    [Fact]
    public void EachModeKeepsItsOwnValueAcrossModeChanges()
    {
        var shell = Shell();
        var panel = Panel(shell);
        panel.CommitValue("29.97");
        panel.SelectedModeIndex = 1;
        panel.CommitValue("24.7");
        panel.SelectedModeIndex = 2;
        panel.CommitValue("1.5");

        panel.SelectedModeIndex = 0;
        Assert.Equal("29.97", panel.ValueText);
        Assert.Equal("at 29.97 fps", panel.Detail);

        panel.SelectedModeIndex = 1;
        Assert.Equal("24.7", panel.ValueText);

        panel.SelectedModeIndex = 2;
        Assert.Equal("1.5", panel.ValueText);

        var timing = shell.CurrentProject.Model.Timing;
        Assert.Equal(new TimingSettings(SpeedMode.PerPhoto, new Fraction(30000, 1001), new Fraction(247, 10), new Fraction(3, 2)), timing);
    }

    [Fact]
    public void AllThreeModesRoundTripThroughTheCoreTypes()
    {
        // The panel stores a rational rate or the mode's own value; the rate it plays is what #10's conversions say.
        var shell = Shell();
        var panel = Panel(shell, photos: 742);
        var project = shell.CurrentProject.Model;

        panel.CommitValue("29.97");
        Assert.Equal(FrameRates.Ntsc30, project.EffectiveRate.Rate);
        Assert.Equal(FrameRates.Ntsc30.Reciprocal(), FrameRates.ToSecondsPerPhoto(project.EffectiveRate.Rate));

        panel.SelectedModeIndex = 1;
        panel.CommitValue("30");
        Assert.Equal(FrameRates.FromTotalLength(742, new Fraction(30, 1)), project.EffectiveRate.Rate);
        Assert.Equal(new Fraction(30, 1), FrameRates.TotalLengthSeconds(742, project.EffectiveRate.Rate));

        panel.SelectedModeIndex = 2;
        panel.CommitValue("2");
        Assert.Equal(FrameRates.FromSecondsPerPhoto(new Fraction(2, 1)), project.EffectiveRate.Rate);
        Assert.Equal(new Fraction(2, 1), FrameRates.ToSecondsPerPhoto(project.EffectiveRate.Rate));
        Assert.Equal("742 photos → 24 min 44 s", panel.Headline);
    }

    [Fact]
    public void TheReadoutJoinsToTheProjectTextReadout()
    {
        var shell = Shell();
        var panel = Panel(shell, photos: 1);

        Assert.Equal("1 photo → 0.0 s", panel.Headline);
        Assert.Equal(ProjectText.TimingReadout(1, shell.CurrentProject.Model.Timing), panel.Readout);
        Assert.Equal(shell.CurrentProject.TimingReadout, panel.Readout);

        shell.CurrentProject.Model.AddFrames(TestFrames.Folder("b", 4999));

        Assert.Equal("5,000 photos → 2 min 47 s", panel.Headline);
        Assert.Equal(shell.CurrentProject.TimingReadout, panel.Readout);
    }

    [Fact]
    public void ChangesGoThroughTheProjectAndMarkItChanged()
    {
        var shell = Shell();
        var panel = Panel(shell);
        var project = shell.CurrentProject.Model;
        var changes = new List<ProjectChanges>();
        project.Changed += (_, e) => changes.Add(e.Changes);
        var version = project.Version;

        panel.SelectedModeIndex = 2;
        panel.SelectedPresetIndex = 3;
        panel.CommitValue("1.5");

        Assert.Equal(3, changes.Count);
        Assert.All(changes, change => Assert.Equal(ProjectChanges.Timing, change & ProjectChanges.Timing));
        Assert.Equal(version + 3, project.Version);
        Assert.True(project.IsDirty);
        Assert.Equal(new TimingSettings(SpeedMode.PerPhoto, new Fraction(30, 1), new Fraction(30, 1), new Fraction(3, 2)), project.Timing);
    }

    [Fact]
    public void ChangingTheSpeedTouchesNothingButTheTiming()
    {
        var shell = Shell();
        var panel = Panel(shell);
        var project = shell.CurrentProject.Model;
        var frames = project.Frames;
        var output = project.Output;
        var frameSettings = project.FrameSettings;
        var all = ProjectChanges.None;
        project.Changed += (_, e) => all |= e.Changes;

        panel.SelectedPresetIndex = 5;
        panel.SelectedModeIndex = 1;
        panel.CommitValue("12");

        Assert.Equal(ProjectChanges.Timing, all & ~ProjectChanges.Dirty);
        Assert.Equal(frames, project.Frames);
        Assert.Equal(output, project.Output);
        Assert.Equal(frameSettings, project.FrameSettings);
    }

    [Fact]
    public void AnUnchangedChoiceWritesNothing()
    {
        var shell = Shell();
        var panel = Panel(shell);
        var version = shell.CurrentProject.Model.Version;

        panel.SelectedModeIndex = 0;
        panel.SelectedPresetIndex = 3;
        panel.CommitValue("30");
        panel.CommitValue("30.0");

        Assert.Equal(version, shell.CurrentProject.Model.Version);
    }

    [Fact]
    public void OutOfRangeIndexesAreIgnored()
    {
        var shell = Shell();
        var panel = Panel(shell);
        var version = shell.CurrentProject.Model.Version;

        panel.SelectedModeIndex = -1;
        panel.SelectedModeIndex = 3;
        panel.SelectedPresetIndex = -1;
        panel.SelectedPresetIndex = 6;

        Assert.Equal(version, shell.CurrentProject.Model.Version);
        Assert.Equal(0, panel.SelectedModeIndex);
        Assert.Equal(3, panel.SelectedPresetIndex);
    }

    [Fact]
    public void APhotoCountChangeUpdatesTheReadoutAndNothingElse()
    {
        var shell = Shell();
        var panel = Panel(shell, photos: 300);
        Assert.Equal("300 photos → 10.0 s", panel.Headline);
        var raised = new List<string?>();
        panel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        shell.CurrentProject.Model.AddFrames(TestFrames.Folder("b", 300));

        Assert.Equal("600 photos → 20.0 s", panel.Headline);
        Assert.Contains(nameof(TimingPanelViewModel.Headline), raised);
        Assert.DoesNotContain(nameof(TimingPanelViewModel.SelectedPresetIndex), raised);
        Assert.DoesNotContain(nameof(TimingPanelViewModel.PresetLabels), raised);
    }

    [Fact]
    public void TheLengthBarIsTheClipAgainstAMinuteAndStopsAtFull()
    {
        var shell = Shell();
        var panel = Panel(shell, photos: 300);
        Assert.Equal(10.0 / 60, panel.LengthFraction, 6);

        shell.CurrentProject.Model.AddFrames(TestFrames.Folder("b", 3000));

        Assert.Equal(1.0, panel.LengthFraction);
    }

    [Fact]
    public async Task NewProjectMakesThePanelFollowTheNewProject()
    {
        var shell = Shell();
        var panel = Panel(shell);
        panel.CommitValue("9999");
        shell.CurrentProject.Model.Timing = TimingSettings.PerPhoto(new Fraction(2, 1));
        Assert.Equal("2", panel.ValueText);

        await shell.NewProjectCommand.ExecuteAsync(null);

        Assert.Equal(0, panel.SelectedModeIndex);
        Assert.Equal("30", panel.ValueText);
        Assert.False(panel.HasMessage);

        panel.SelectedPresetIndex = 0;

        Assert.Equal(new Fraction(12, 1), shell.CurrentProject.Model.Timing.FramesPerSecond);
    }

    [Fact]
    public void ADisposedPanelStopsFollowingTheProject()
    {
        var shell = Shell();
        var panel = Panel(shell);
        panel.Dispose();

        shell.CurrentProject.Model.Timing = TimingSettings.PerSecond(new Fraction(60, 1));

        Assert.Equal("30", panel.ValueText);
    }

    [Fact]
    public void AClosedProjectTakesNoEditsAndTheFailureIsNotRaised()
    {
        var errors = new FakeErrorSink();
        var shell = Shell(errors);
        var panel = Panel(shell, errors: errors);
        shell.CurrentProject.Model.Close();

        panel.SelectedPresetIndex = 0;

        Assert.Equal(new Fraction(30, 1), shell.CurrentProject.Model.Timing.FramesPerSecond);
        Assert.Equal(3, panel.SelectedPresetIndex);
    }

    [Fact]
    public void AFailedWriteGoesToTheErrorPathAndThePanelShowsTheStoredValueAgain()
    {
        var errors = new FakeErrorSink();
        var shell = Shell(errors);
        var panel = Panel(shell, errors: errors);
        var project = shell.CurrentProject.Model;
        project.Changed += (_, e) =>
        {
            if ((e.Changes & ProjectChanges.Timing) != 0)
            {
                throw new InvalidOperationException("A listener failed.");
            }
        };
        var raised = new List<string?>();
        panel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        panel.SelectedPresetIndex = 0;

        Assert.NotEmpty(errors.Shown.Concat(errors.Reported));
        Assert.Contains(nameof(TimingPanelViewModel.SelectedPresetIndex), raised);
    }

    [Fact]
    public void EveryUserChangeIsAnnouncedWithTheReadout()
    {
        var panel = Panel(Shell());
        var said = new List<string>();
        panel.Announced += (_, text) => said.Add(text);

        panel.SelectedPresetIndex = 1;
        panel.CommitValue("37.4829");
        panel.CommitValue("fast");

        Assert.Equal(
            [
                "742 photos → 30.9 s at 24 fps",
                "Rounded to 37.48 fps. 742 photos → 19.8 s at 37.48 fps",
                "Type a frame rate from 1 to 120 fps, like 24 or 29.97. 742 photos → 19.8 s at 37.48 fps",
            ],
            said);
    }
}
