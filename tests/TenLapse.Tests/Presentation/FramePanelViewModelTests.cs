using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Presentation;
using TenLapse.Tests.Import;
using TenLapse.Tests.Projects;

namespace TenLapse.Tests.Presentation;

/// <summary>The Frame panel (#18): the shape and Fit or Fill write the project, Match photos, the portrait suggestion, and the Output panel following.</summary>
public sealed class FramePanelViewModelTests
{
    private static readonly FrameAspect[] _aspects =
        [FrameAspect.MatchPhotos, FrameAspect.Ratio16x9, FrameAspect.Ratio9x16, FrameAspect.Ratio1x1, FrameAspect.Ratio4x5, FrameAspect.Ratio4x3];

    private static ShellViewModel Shell(FakeErrorSink? errors = null) =>
        new(new FakeDialogService(), errors ?? new FakeErrorSink());

    private static FramePanelViewModel Panel(ShellViewModel shell, FakeErrorSink? errors = null) =>
        new(shell, errors ?? new FakeErrorSink());

    private static Frame[] Photos(int count, int width, int height, string folder = "a") =>
        [.. TestFrames.Folder(folder, count).Select(frame => frame with { PixelSize = new PixelSize(width, height) })];

    private static Frame[] Portrait(int count, string folder = "p") => Photos(count, 3024, 4032, folder);

    private static Frame[] Landscape(int count, string folder = "l") => Photos(count, 4032, 3024, folder);

    [Fact]
    public void ADefaultProjectShowsSixShapesWithSixteenByNineAndFillSelected()
    {
        var panel = Panel(Shell());

        Assert.Equal(["Match", "16:9", "9:16", "1:1", "4:5", "4:3"], panel.ShapeLabels);
        Assert.Equal(["Match photos", "16:9", "9:16", "1:1", "4:5", "4:3"], panel.ShapeNames);
        Assert.Equal(["Fit", "Fill"], panel.FitLabels);
        Assert.Equal(1, panel.SelectedShapeIndex);
        Assert.Equal(1, panel.SelectedFitIndex);
        Assert.Equal(FrameText.FillHint, panel.Description);
        Assert.False(panel.HasMatchLine);
        Assert.False(panel.HasPortraitHint);
    }

    [Fact]
    public void EveryShapeWithFitAndFillWritesTheRightFrameSettings()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        var panel = Panel(shell);

        foreach (var (fitIndex, fit) in new[] { (0, FitMode.Fit), (1, FitMode.Fill) })
        {
            panel.SelectedFitIndex = fitIndex;
            Assert.Equal(fit, project.FrameSettings.Fit);

            for (var shape = 0; shape < _aspects.Length; shape++)
            {
                panel.SelectedShapeIndex = shape;

                Assert.Equal(new FrameSettings(_aspects[shape], fit), project.FrameSettings);
                Assert.Equal(shape, panel.SelectedShapeIndex);
                Assert.Equal(fitIndex, panel.SelectedFitIndex);
                Assert.Equal(new FrameSettings(_aspects[shape], fit), project.Snapshot.FrameSettings);
            }
        }
    }

    [Fact]
    public void TheLineUnderTheControlsSaysWhatFitAndFillDo()
    {
        var panel = Panel(Shell());

        panel.SelectedFitIndex = 0;
        Assert.Equal("Fit shows the whole photo with bars where shapes differ.", panel.Description);

        panel.SelectedFitIndex = 1;
        Assert.Equal("Fill crops the edges so the frame has no bars.", panel.Description);
    }

    [Fact]
    public void AChangeOfShapeIsOneEventForFrameSettingsAndMarksTheProjectChanged()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        var panel = Panel(shell);
        var events = new List<ProjectChanges>();
        project.Changed += (_, e) => events.Add(e.Changes);

        panel.SelectedShapeIndex = 2;

        var change = Assert.Single(events);
        Assert.True(change.HasFlag(ProjectChanges.FrameSettings));
        Assert.True(project.IsDirty);
    }

    [Fact]
    public void ThePanelFollowsAChangeMadeToTheProjectDirectly()
    {
        var shell = Shell();
        var panel = Panel(shell);

        shell.CurrentProject.Model.FrameSettings = new FrameSettings(FrameAspect.Ratio4x5, FitMode.Fit);

        Assert.Equal(4, panel.SelectedShapeIndex);
        Assert.Equal(0, panel.SelectedFitIndex);
        Assert.Equal(FrameText.FitHint, panel.Description);
    }

    [Theory]
    [InlineData(FrameAspect.Ratio16x9, 1920, 1080)]
    [InlineData(FrameAspect.Ratio9x16, 1080, 1920)]
    [InlineData(FrameAspect.Ratio1x1, 1080, 1080)]
    [InlineData(FrameAspect.Ratio4x5, 1080, 1350)]
    [InlineData(FrameAspect.Ratio4x3, 1440, 1080)]
    public void TheVideoSizeFollowsTheShapeTheUserPicks(FrameAspect aspect, int width, int height)
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        project.AddFrames(Landscape(3));
        var panel = Panel(shell);

        panel.SelectedShapeIndex = Array.IndexOf(_aspects, aspect);

        Assert.Equal(new PixelSize(width, height), project.Snapshot.VideoSize);
    }

    [Fact]
    public void MatchPhotosTakesTheMostCommonPhotoSizeAndSaysWhich()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        project.AddFrames([.. Portrait(6), .. Landscape(4)]);
        var panel = Panel(shell);

        panel.SelectedShapeIndex = 0;

        Assert.Equal(FrameAspect.MatchPhotos, project.FrameSettings.Aspect);
        Assert.Equal(OutputSize.MostCommonPhotoSize(project.Snapshot.Frames), new PixelSize(3024, 4032));
        Assert.Equal(new PixelSize(1080, 1440), project.Snapshot.VideoSize);
        Assert.True(panel.HasMatchLine);
        Assert.Equal("Matching your photos: 3024 × 4032.", panel.MatchLine);
    }

    [Fact]
    public void MatchPhotosFollowsThePhotosAsTheyChange()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        project.AddFrames(Landscape(2));
        var panel = Panel(shell);
        panel.SelectedShapeIndex = 0;
        Assert.Equal(new PixelSize(1440, 1080), project.Snapshot.VideoSize);

        project.AddFrames(Photos(5, 1080, 1920, "v"));

        Assert.Equal("Matching your photos: 1080 × 1920.", panel.MatchLine);
        Assert.Equal(new PixelSize(1080, 1920), project.Snapshot.VideoSize);
    }

    [Fact]
    public void MatchPhotosWithNoSizeKnownSaysItIsSixteenByNineForNow()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        var panel = Panel(shell);

        panel.SelectedShapeIndex = 0;

        Assert.Equal(FrameText.MatchingNoPhotos, panel.MatchLine);
        Assert.Equal(new PixelSize(1920, 1080), project.Snapshot.VideoSize);
    }

    [Fact]
    public void TheMatchLineGoesWhenAnotherShapeIsPicked()
    {
        var shell = Shell();
        var panel = Panel(shell);
        panel.SelectedShapeIndex = 0;
        Assert.True(panel.HasMatchLine);

        panel.SelectedShapeIndex = 3;

        Assert.False(panel.HasMatchLine);
        Assert.Equal(string.Empty, panel.MatchLine);
    }

    [Fact]
    public void AMostlyPortraitSetInALandscapeShapeSuggestsNineBySixteen()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        var panel = Panel(shell);
        Assert.False(panel.HasPortraitHint);

        project.AddFrames([.. Portrait(7), .. Landscape(3)]);

        Assert.True(panel.HasPortraitHint);
        Assert.Equal(FrameAspect.Ratio16x9, project.FrameSettings.Aspect);
    }

    [Fact]
    public void TheSuggestionAlsoShowsFromFourByThree()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        project.FrameSettings = new FrameSettings(FrameAspect.Ratio4x3, FitMode.Fit);
        project.AddFrames(Portrait(5));

        var panel = Panel(shell);

        Assert.True(panel.HasPortraitHint);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(3, 7)]
    [InlineData(0, 10)]
    public void ASetThatIsNotMostlyPortraitSuggestsNothing(int portrait, int landscape)
    {
        var shell = Shell();
        shell.CurrentProject.Model.AddFrames([.. Portrait(portrait), .. Landscape(landscape)]);

        var panel = Panel(shell);

        Assert.False(panel.HasPortraitHint);
    }

    [Fact]
    public void PhotosWhoseSizeIsNotKnownYetDoNotCount()
    {
        var shell = Shell();
        shell.CurrentProject.Model.AddFrames(TestFrames.Folder("unread", 20));
        var panel = Panel(shell);
        Assert.False(panel.HasPortraitHint);

        shell.CurrentProject.Model.AddFrames(Portrait(1));

        Assert.True(panel.HasPortraitHint);
    }

    [Theory]
    [InlineData(FrameAspect.Ratio9x16)]
    [InlineData(FrameAspect.Ratio1x1)]
    [InlineData(FrameAspect.Ratio4x5)]
    [InlineData(FrameAspect.MatchPhotos)]
    public void NoSuggestionForAShapeThatIsNotLandscape(FrameAspect aspect)
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        project.FrameSettings = new FrameSettings(aspect, FitMode.Fill);
        project.AddFrames(Portrait(10));

        var panel = Panel(shell);

        Assert.False(panel.HasPortraitHint);
    }

    [Fact]
    public void ApplyingTheSuggestionSetsNineBySixteenKeepsFitAndHidesIt()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        project.FrameSettings = new FrameSettings(FrameAspect.Ratio16x9, FitMode.Fit);
        project.AddFrames(Portrait(10));
        var panel = Panel(shell);
        var announced = new List<string>();
        panel.Announced += (_, text) => announced.Add(text);

        panel.ApplyPortraitHint();

        Assert.Equal(new FrameSettings(FrameAspect.Ratio9x16, FitMode.Fit), project.FrameSettings);
        Assert.Equal(2, panel.SelectedShapeIndex);
        Assert.False(panel.HasPortraitHint);
        Assert.Equal("Frame: 9:16, Fit.", Assert.Single(announced));
    }

    [Fact]
    public void AnAppliedSuggestionStaysGoneWhenTheUserGoesBackToALandscapeShape()
    {
        var shell = Shell();
        shell.CurrentProject.Model.AddFrames(Portrait(10));
        var panel = Panel(shell);
        panel.ApplyPortraitHint();

        panel.SelectedShapeIndex = 1;

        Assert.Equal(FrameAspect.Ratio16x9, shell.CurrentProject.Model.FrameSettings.Aspect);
        Assert.False(panel.HasPortraitHint);
    }

    [Fact]
    public void DismissingTheSuggestionHidesItAndChangesNothing()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        project.AddFrames(Portrait(10));
        var panel = Panel(shell);
        var settings = project.FrameSettings;
        var version = project.Snapshot.Version;
        var dirty = project.IsDirty;

        panel.DismissPortraitHint();

        Assert.False(panel.HasPortraitHint);
        Assert.Equal(settings, project.FrameSettings);
        Assert.Equal(version, project.Snapshot.Version);
        Assert.Equal(dirty, project.IsDirty);

        // More portrait photos, or a switch to 4:3, do not bring it back.
        project.AddFrames(Portrait(5, "more"));
        panel.SelectedShapeIndex = 5;
        Assert.False(panel.HasPortraitHint);
    }

    [Fact]
    public void ThePanelNeverChangesTheShapeByItself()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        var panel = Panel(shell);
        var writes = 0;
        project.Changed += (_, e) => writes += (e.Changes & ProjectChanges.FrameSettings) != 0 ? 1 : 0;

        project.AddFrames(Portrait(50));
        project.AddFrames(Portrait(50, "more"));

        Assert.True(panel.HasPortraitHint);
        Assert.Equal(0, writes);
        Assert.Equal(FrameSettings.Default, project.FrameSettings);
    }

    [Fact]
    public void PickingAPortraitShapeYourselfClosesTheSuggestionForGood()
    {
        var shell = Shell();
        shell.CurrentProject.Model.AddFrames(Portrait(10));
        var panel = Panel(shell);
        Assert.True(panel.HasPortraitHint);

        panel.SelectedShapeIndex = 4;
        panel.SelectedShapeIndex = 1;

        Assert.False(panel.HasPortraitHint);
    }

    [Fact]
    public void PickingAnotherLandscapeShapeKeepsTheSuggestion()
    {
        var shell = Shell();
        shell.CurrentProject.Model.AddFrames(Portrait(10));
        var panel = Panel(shell);

        panel.SelectedShapeIndex = 5;
        panel.SelectedFitIndex = 0;

        Assert.True(panel.HasPortraitHint);
    }

    [Fact]
    public void TheSuggestionGoesWhenThePhotosAreRemovedAndReturnsWithThem()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        var portrait = Portrait(5);
        project.AddFrames(portrait);
        var panel = Panel(shell);
        Assert.True(panel.HasPortraitHint);

        project.RemoveFramesAndSkip(portrait.Select(frame => frame.Id).ToHashSet(), []);
        Assert.False(panel.HasPortraitHint);

        project.AddFrames(Portrait(2, "again"));
        Assert.True(panel.HasPortraitHint);
    }

    [Fact]
    public void TheSuggestionAppearingWithTheImportIsAnnouncedButNotOneThatAppearsWithAnEdit()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        var panel = Panel(shell);
        var announced = new List<string>();
        panel.Announced += (_, text) => announced.Add(text);

        project.AddFrames(Portrait(10));
        Assert.Equal(FrameText.PortraitHint, Assert.Single(announced));

        // Going back to a landscape shape the project already holds changes the shape, so it is announced as that.
        announced.Clear();
        project.FrameSettings = new FrameSettings(FrameAspect.Ratio1x1, FitMode.Fill);
        panel.SelectedShapeIndex = 1;
        Assert.Equal("Frame: 16:9, Fill.", Assert.Single(announced));
    }

    [Fact]
    public void ASamePickSaysNothing()
    {
        var panel = Panel(Shell());
        var announced = new List<string>();
        panel.Announced += (_, text) => announced.Add(text);

        panel.SelectedShapeIndex = 1;
        panel.SelectedFitIndex = 1;

        Assert.Empty(announced);
    }

    [Fact]
    public async Task NewProjectMakesThePanelFollowTheNewProjectAndShowsTheSuggestionAfresh()
    {
        var shell = Shell();
        var old = shell.CurrentProject.Model;
        old.AddFrames(Portrait(10));
        var panel = Panel(shell);
        panel.DismissPortraitHint();
        panel.SelectedFitIndex = 0;

        await shell.NewProjectCommand.ExecuteAsync(null);

        var fresh = shell.CurrentProject.Model;
        Assert.NotSame(old, fresh);
        Assert.Equal(1, panel.SelectedFitIndex);
        Assert.False(panel.HasPortraitHint);
        fresh.AddFrames(Portrait(10));
        Assert.True(panel.HasPortraitHint);

        panel.SelectedShapeIndex = 3;
        Assert.Equal(FrameAspect.Ratio1x1, fresh.FrameSettings.Aspect);
        Assert.Equal(FrameAspect.Ratio16x9, old.FrameSettings.Aspect);
    }

    [Fact]
    public async Task AnEditThatCannotBeWrittenGoesToTheErrorPathAndTheControlsGoBack()
    {
        var errors = new FakeErrorSink();
        var shell = Shell(errors);
        var project = shell.CurrentProject.Model;
        project.AddFrames(Portrait(10));
        var panel = Panel(shell, errors);
        var raised = new List<string?>();
        panel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // A write from another thread throws (the project belongs to the thread that made it); the panel must not.
        using var other = new UiThread();
        await other.InvokeAsync(() => panel.SelectedShapeIndex = 3);
        await other.InvokeAsync(() => { panel.ApplyPortraitHint(); return 0; });

        Assert.Equal(FrameSettings.Default, project.FrameSettings);
        Assert.Equal(2, errors.Shown.Count);
        Assert.All(errors.Shown, shown => Assert.Equal(FrameText.ChangeFailed, shown.Context));
        Assert.Contains(nameof(FramePanelViewModel.SelectedShapeIndex), raised);
        Assert.Equal(1, panel.SelectedShapeIndex);

        // The suggestion that could not be applied is still there to try again.
        Assert.True(panel.HasPortraitHint);
    }

    [Fact]
    public void AClosedProjectTakesNoEdit()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        var panel = Panel(shell);
        project.Close();

        panel.SelectedShapeIndex = 3;

        Assert.Equal(FrameAspect.Ratio16x9, project.FrameSettings.Aspect);
    }

    [Fact]
    public void ADisposedPanelStopsFollowingTheProject()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        var panel = Panel(shell);
        panel.Dispose();

        project.FrameSettings = new FrameSettings(FrameAspect.Ratio4x3, FitMode.Fit);

        Assert.Equal(1, panel.SelectedShapeIndex);
    }

    [Fact]
    public void TheOutputPanelLabelsFollowTheShape()
    {
        var shell = Shell();
        shell.CurrentProject.Model.AddFrames(Landscape(10));
        var frame = Panel(shell);
        var output = new OutputPanelViewModel(shell, new FakeErrorSink());
        output.SetCapabilities(new FakeOutputCapabilities());
        string Label(OutputResolution resolution) => output.Resolutions.First(option => option.Value == resolution).Label;
        Assert.Equal("1080p · 1920 × 1080", Label(OutputResolution.Hd1080));

        frame.SelectedShapeIndex = 2;
        Assert.Equal("1080p · 1080 × 1920", Label(OutputResolution.Hd1080));
        Assert.Equal("720p · 720 × 1280", Label(OutputResolution.Hd720));

        frame.SelectedShapeIndex = 3;
        Assert.Equal("1080p · 1080 × 1080", Label(OutputResolution.Hd1080));

        frame.SelectedShapeIndex = 4;
        Assert.Equal("1080p · 1080 × 1350", Label(OutputResolution.Hd1080));

        frame.SelectedShapeIndex = 5;
        Assert.Equal("1080p · 1440 × 1080", Label(OutputResolution.Hd1080));

        frame.SelectedShapeIndex = 0;
        Assert.Equal("1080p · 1440 × 1080", Label(OutputResolution.Hd1080));
    }

    [Fact]
    public void TheOutputPanelRunsItsCapabilityFilterAgainWhenTheShapeChanges()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        project.Output = new OutputSettings(OutputResolution.Uhd2160, VideoFormat.Mp4H264, OutputQuality.High);
        var frame = Panel(shell);
        var output = new OutputPanelViewModel(shell, new FakeErrorSink());
        output.SetCapabilities(new PortraitLimitedCapabilities());
        Assert.Equal(OutputResolution.Uhd2160, output.SelectedResolution?.Value);
        Assert.Equal(4, output.Resolutions.Count);

        frame.SelectedShapeIndex = 2;

        // 9:16 cannot do 4K here: the list loses it, the project gets the largest size still offered, and the panel says so.
        Assert.Equal([OutputResolution.Hd720, OutputResolution.Hd1080, OutputResolution.Qhd1440], output.Resolutions.Select(option => option.Value));
        Assert.Equal(OutputResolution.Qhd1440, project.Output.Resolution);
        Assert.True(output.HasNotice);

        frame.SelectedShapeIndex = 1;
        Assert.Equal(4, output.Resolutions.Count);
    }

    [Fact]
    public void TheOutputPanelEstimateFollowsTheShape()
    {
        var shell = Shell();
        var project = shell.CurrentProject.Model;
        project.AddFrames(Landscape(3000));
        var frame = Panel(shell);
        var output = new OutputPanelViewModel(shell, new FakeErrorSink());
        output.SetCapabilities(new FakeOutputCapabilities());
        var wide = output.EstimatedSize;

        frame.SelectedShapeIndex = 3;

        var expected = ExportBitrate.EstimateBytes(3000, new Fraction(30, 1), new PixelSize(1080, 1080), VideoFormat.Mp4H264, OutputQuality.High);
        Assert.Equal(OutputText.Estimate(expected), output.EstimatedSize);
        Assert.NotEqual(wide, output.EstimatedSize);
    }

    // 4K is possible at every shape except 9:16.
    private sealed class PortraitLimitedCapabilities : IOutputCapabilities
    {
        public bool IsAvailable(VideoFormat format) => format == VideoFormat.Mp4H264;

        public bool Supports(VideoFormat format, OutputResolution resolution, FrameAspect aspect, Fraction frameRate, PixelSize? photoSize) =>
            IsAvailable(format) && (aspect != FrameAspect.Ratio9x16 || resolution <= OutputResolution.Qhd1440);

        public bool UsesHardware(VideoFormat format, OutputResolution resolution, FrameAspect aspect, Fraction frameRate, PixelSize? photoSize) =>
            Supports(format, resolution, aspect, frameRate, photoSize);

        public string Summary(VideoFormat format, FrameAspect aspect, Fraction frameRate, PixelSize? photoSize) => "Not every size.";
    }
}
