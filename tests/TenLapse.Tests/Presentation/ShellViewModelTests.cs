using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Presentation;
using TenLapse.Tests.Projects;

namespace TenLapse.Tests.Presentation;

public sealed class ShellViewModelTests
{
    [Fact]
    public async Task NewProjectDisposesThePreviewAndCachesAndLeavesNothingBehind()
    {
        var dialogs = new FakeDialogService(answer: true);
        var shell = new ShellViewModel(dialogs);
        var old = shell.CurrentProject;
        old.Model.AddFrames(TestFrames.Folder("a", 742));
        old.Model.AddSkippedFiles([new SkippedFile(@"C:\Photos\a\broken.jpg", SkipReason.DecodeFailed, "Damaged.")]);
        var cache = new FakeProxyCache { Count = 742 };
        var preview = new FakePreviewSource();
        old.AddResource(cache);
        old.AddResource(preview);
        var replaced = new List<ProjectReplacedEventArgs>();
        shell.ProjectReplaced += (_, e) => replaced.Add(e);

        await shell.NewProjectCommand.ExecuteAsync(null);

        Assert.Single(dialogs.Asked);
        Assert.Equal(0, shell.CurrentProject.FrameCount);
        Assert.Empty(shell.CurrentProject.Model.Frames);
        Assert.Equal(0, shell.CurrentProject.SkippedFileCount);
        Assert.Empty(shell.CurrentProject.Model.SkippedFiles);
        var reset = Assert.Single(replaced);
        Assert.Same(old, reset.Previous);
        Assert.Same(shell.CurrentProject, reset.Current);
        Assert.True(cache.IsDisposed);
        Assert.Equal(0, cache.Count);
        Assert.True(preview.IsDisposed);
        Assert.Null(preview.Source);
        Assert.True(old.IsDisposed);
        Assert.False(shell.CurrentProject.IsDisposed);
    }

    [Fact]
    public async Task TheResetRunsBeforeThePreviousProjectIsDisposed()
    {
        var shell = new ShellViewModel(new FakeDialogService());
        var cache = new FakeProxyCache();
        shell.CurrentProject.AddResource(cache);
        bool? disposedDuringReset = null;
        shell.ProjectReplaced += (_, _) => disposedDuringReset = cache.IsDisposed;

        await shell.NewProjectCommand.ExecuteAsync(null);

        Assert.False(disposedDuringReset);
        Assert.True(cache.IsDisposed);
    }

    [Fact]
    public async Task NewProjectOnAnEmptyProjectDoesNotAsk()
    {
        var dialogs = new FakeDialogService(answer: false);
        var shell = new ShellViewModel(dialogs);
        var first = shell.CurrentProject;
        var replaced = 0;
        shell.ProjectReplaced += (_, _) => replaced++;

        await shell.NewProjectCommand.ExecuteAsync(null);

        Assert.Empty(dialogs.Asked);
        Assert.Equal(1, replaced);
        Assert.NotSame(first, shell.CurrentProject);
    }

    [Fact]
    public async Task NewProjectAfterAnExportDoesNotAsk()
    {
        var dialogs = new FakeDialogService(answer: false);
        var shell = new ShellViewModel(dialogs);
        shell.CurrentProject.Model.AddFrames(TestFrames.Folder("a", 10));
        shell.CurrentProject.Model.MarkExported(shell.CurrentProject.Model.Version);

        await shell.NewProjectCommand.ExecuteAsync(null);

        Assert.Empty(dialogs.Asked);
        Assert.Equal(0, shell.CurrentProject.FrameCount);
    }

    [Fact]
    public async Task CancelKeepsEverything()
    {
        var dialogs = new FakeDialogService(answer: false);
        var shell = new ShellViewModel(dialogs);
        var project = shell.CurrentProject;
        var frames = TestFrames.Folder("a", 742);
        project.Model.AddFrames(frames);
        project.Model.Timing = new TimingSettings(FrameRates.Ntsc24, SpeedMode.PerSecond);
        var cache = new FakeProxyCache { Count = 742 };
        var preview = new FakePreviewSource();
        project.AddResource(cache);
        project.AddResource(preview);
        var replaced = 0;
        shell.ProjectReplaced += (_, _) => replaced++;

        await shell.NewProjectCommand.ExecuteAsync(null);

        var asked = Assert.Single(dialogs.Asked);
        Assert.Equal("Start a new project?", asked.Title);
        Assert.Equal("This project's 742 photos and settings will be cleared. The photo files stay on your PC.", asked.Message);
        Assert.Equal("Start new project", asked.ConfirmText);
        Assert.Equal("Cancel", asked.CancelText);
        Assert.Same(project, shell.CurrentProject);
        Assert.Equal(frames, project.Model.Frames);
        Assert.Equal(FrameRates.Ntsc24, project.Model.Timing.Rate);
        Assert.True(project.IsDirty);
        Assert.Equal(0, replaced);
        Assert.False(cache.IsDisposed);
        Assert.Equal(742, cache.Count);
        Assert.NotNull(preview.Source);
        Assert.False(project.IsDisposed);
    }

    [Fact]
    public async Task TwoImportsIntoFreshProjectsNeverMix()
    {
        var shell = new ShellViewModel(new FakeDialogService(answer: true));
        var harbor = TestFrames.Folder("harbor", 5);
        var forest = TestFrames.Folder("forest", 3);
        shell.CurrentProject.Model.AddFrames(harbor);
        var first = shell.CurrentProject.Model;

        await shell.NewProjectCommand.ExecuteAsync(null);
        shell.CurrentProject.Model.AddFrames(forest);

        Assert.Equal(forest, shell.CurrentProject.Model.Frames);
        Assert.Equal(3, shell.CurrentProject.FrameCount);
        Assert.Equal(harbor, first.Frames);
    }

    [Fact]
    public async Task NewProjectBeforeAnyImportDoesNotThrow()
    {
        // Legacy Clear() called Images.Clear() on a null collection when nothing had been imported.
        var shell = new ShellViewModel(new FakeDialogService());

        var failure = await Record.ExceptionAsync(() => shell.NewProjectCommand.ExecuteAsync(null));

        Assert.Null(failure);
        Assert.Equal(0, shell.CurrentProject.FrameCount);
    }

    [Fact]
    public async Task ASecondImportAfterNewProjectDoesNotAppendToTheFirst()
    {
        // Legacy LoadClip() added every new clip to the same MediaComposition.
        var shell = new ShellViewModel(new FakeDialogService(answer: true));
        var photos = TestFrames.Folder("a", 4);
        shell.CurrentProject.Model.AddFrames(photos);

        await shell.NewProjectCommand.ExecuteAsync(null);
        var again = TestFrames.Folder("a", 4);
        var added = shell.CurrentProject.Model.AddFrames(again);

        Assert.Equal(4, added);
        Assert.Equal(again, shell.CurrentProject.Model.Frames);
        Assert.Equal("4 photos → 0.1 s at 30 fps", shell.CurrentProject.TimingReadout);
    }

    [Fact]
    public async Task NewProjectReallyResetsEveryPartOfTheProject()
    {
        // Legacy Clear() emptied the thumbnails only; the clips, file list and preview stayed.
        var shell = new ShellViewModel(new FakeDialogService(answer: true));
        var model = shell.CurrentProject.Model;
        model.AddFrames(TestFrames.Folder("a", 742));
        model.AddSkippedFiles([new SkippedFile(@"C:\Photos\a\x.heic", SkipReason.UnsupportedFormat, "No HEIF decoder.")]);
        model.Name = "Harbor sunset";
        model.Timing = new TimingSettings(new Fraction(2, 1), SpeedMode.PerPhoto);
        model.FrameSettings = new FrameSettings(FrameAspect.Ratio9x16, FitMode.Fit);
        model.Output = new OutputSettings(OutputResolution.Uhd2160, VideoFormat.Mp4Hevc, OutputQuality.Maximum);
        model.SortMode = SortMode.Manual;

        await shell.NewProjectCommand.ExecuteAsync(null);

        var fresh = shell.CurrentProject.Model;
        Assert.NotSame(model, fresh);
        Assert.Empty(fresh.Frames);
        Assert.Empty(fresh.SkippedFiles);
        Assert.Equal(Project.DefaultName, fresh.Name);
        Assert.Equal(TimingSettings.Default, fresh.Timing);
        Assert.Equal(FrameSettings.Default, fresh.FrameSettings);
        Assert.Equal(OutputSettings.Default, fresh.Output);
        Assert.Equal(SortMode.CaptureTime, fresh.SortMode);
        Assert.False(fresh.IsDirty);
        Assert.Equal("10Lapse", shell.Title);
        Assert.Equal("0 photos → 0.0 s at 30 fps", shell.CurrentProject.TimingReadout);
    }

    [Fact]
    public async Task NewProjectCannotRunAgainWhileTheQuestionIsOpen()
    {
        var dialogs = new PendingDialogService();
        var shell = new ShellViewModel(dialogs);
        shell.CurrentProject.Model.AddFrames(TestFrames.Folder("a", 2));

        var running = shell.NewProjectCommand.ExecuteAsync(null);

        Assert.False(shell.NewProjectCommand.CanExecute(null));
        dialogs.Answer(confirmed: true);
        await running;
        Assert.True(shell.NewProjectCommand.CanExecute(null));
        Assert.Equal(1, dialogs.Asked);
        Assert.Equal(0, shell.CurrentProject.FrameCount);
    }

    [Fact]
    public void TheTitleShowsTheNameAndTheUnsavedMarker()
    {
        var shell = new ShellViewModel(new FakeDialogService());
        var raised = new List<string?>();
        shell.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.Equal("10Lapse", shell.Title);
        Assert.False(shell.HasUnsavedChanges);

        shell.CurrentProject.Model.Name = Project.NameFromFolder(@"C:\Photos\Harbor sunset");
        shell.CurrentProject.Model.AddFrames(TestFrames.Folder("a", 3));

        Assert.Equal("● Harbor sunset – 10Lapse", shell.Title);
        Assert.True(shell.HasUnsavedChanges);
        Assert.Contains(nameof(ShellViewModel.Title), raised);
        Assert.Contains(nameof(ShellViewModel.HasUnsavedChanges), raised);

        shell.CurrentProject.Model.MarkExported(shell.CurrentProject.Model.Version);

        Assert.Equal("Harbor sunset – 10Lapse", shell.Title);
        Assert.False(shell.HasUnsavedChanges);
    }

    [Fact]
    public async Task TheTitleFollowsOnlyTheCurrentProject()
    {
        var shell = new ShellViewModel(new FakeDialogService());
        var old = shell.CurrentProject.Model;
        await shell.NewProjectCommand.ExecuteAsync(null);
        var raised = 0;
        shell.PropertyChanged += (_, _) => raised++;

        old.AddFrames(TestFrames.Folder("a", 3));

        Assert.Equal(0, raised);
        Assert.Equal("10Lapse", shell.Title);
    }

    [Fact]
    public async Task TheFactoryMustReturnANewEmptyProject()
    {
        var shared = new Project();
        var shell = new ShellViewModel(new FakeDialogService(), () => shared);

        await Assert.ThrowsAsync<InvalidOperationException>(() => shell.NewProjectCommand.ExecuteAsync(null));
        Assert.Same(shared, shell.CurrentProject.Model);

        var used = new Project();
        used.AddFrames(TestFrames.Folder("a", 1));
        Assert.Throws<InvalidOperationException>(() => new ShellViewModel(new FakeDialogService(), () => used));
    }

    [Fact]
    public async Task TheFactoryCanSeedTheUsersDefaults()
    {
        var timing = new TimingSettings(new Fraction(24, 1), SpeedMode.PerSecond);
        var frame = new FrameSettings(FrameAspect.Ratio9x16, FitMode.Fit);
        var output = OutputSettings.Default with { Quality = OutputQuality.Maximum };
        var dialogs = new FakeDialogService(answer: false);
        var shell = new ShellViewModel(dialogs, () => new Project(timing, frame, output, SortMode.Name));

        await shell.NewProjectCommand.ExecuteAsync(null);

        var model = shell.CurrentProject.Model;
        Assert.Equal(timing, model.Timing);
        Assert.Equal(frame, model.FrameSettings);
        Assert.Equal(output, model.Output);
        Assert.Equal(SortMode.Name, model.SortMode);
        Assert.False(model.IsDirty);
        Assert.False(shell.HasUnsavedChanges);
        Assert.Equal("10Lapse", shell.Title);
        Assert.Empty(dialogs.Asked);
    }

    [Fact]
    public void AFactoryThatSetsSettingsAfterwardsGivesADirtyProjectAndIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => new ShellViewModel(
            new FakeDialogService(),
            () => new Project { SortMode = SortMode.Name }));
    }
}
