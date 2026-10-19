using TenLapse.Core.Import;
using TenLapse.Core.Projects;
using TenLapse.Presentation;
using TenLapse.Presentation.Services;
using TenLapse.Tests.Fixtures;

namespace TenLapse.Tests.Import;

/// <summary>The import end to end, from the view model to the project, on a stand-in UI thread.</summary>
public sealed class ImportViewModelTests
{
    [Fact]
    public async Task ImportingTheFixtureFolderAddsTheSixteenGoodPhotosAndRaisesOneInfoBarForTheFourSkipped()
    {
        using var harness = ImportHarness.WithPhotoImporter();

        await harness.ImportAsync(PhotoFixtureCatalogue.Directory);

        var project = harness.Project;
        Assert.Equal(16, project.Frames.Length);
        Assert.All(project.Frames, frame => Assert.NotNull(frame.PixelSize));
        Assert.Equal(
            ["empty.jpg", "no-eoi.jpg", "text.heic", "truncated.jpg"],
            project.SkippedFiles.Select(file => Path.GetFileName(file.Path)).Order(StringComparer.Ordinal));
        var reasons = PhotoFixtureCatalogue.All.Where(fixture => fixture.Reason is not null).ToDictionary(fixture => fixture.Name, fixture => fixture.Reason);
        Assert.All(project.SkippedFiles, file => Assert.Equal(reasons[Path.GetFileName(file.Path)], file.Reason));

        var bar = Assert.Single(harness.InfoBars.Shown);
        Assert.Equal(InfoBarSeverity.Caution, bar.Severity);
        Assert.Equal("4 photos couldn't be read and were skipped.", bar.Message);
        Assert.Equal(4, bar.Details.Count);

        // Named after the folder; not every photo has a capture time, so the default sort is by name.
        Assert.Equal("Photos", project.Name);
        Assert.Equal(SortMode.Name, project.SortMode);
        Assert.False(harness.Import.IsImporting);
        Assert.Empty(harness.RealFailures());
        Assert.Empty(harness.Ui.Failures);
    }

    [Fact]
    public async Task ImportingTheSameFolderTwiceAddsNothingTheSecondTimeAndSaysSo()
    {
        using var harness = ImportHarness.With(new ScriptedImportSource([ScriptedImportSource.Photo("a.jpg")]));
        var frame = ScriptedImportSource.Photo("a.jpg");
        harness.Ui.Run(() => harness.Project.AddFrames([frame]));

        await harness.ImportAsync(@"C:\Scripted");

        Assert.Equal([frame.Id], harness.Project.Frames.Select(f => f.Id));
        Assert.Equal(ProjectText.ImportAlreadyAdded, Assert.Single(harness.InfoBars.Shown).Message);
    }

    [Fact]
    public async Task AFolderThatWillNotOpenGetsItsOwnInfoBarAndIsNotASkippedPhoto()
    {
        using var harness = ImportHarness.WithPhotoImporter();
        var gone = Path.Combine(Path.GetTempPath(), $"10lapse-gone-{Guid.NewGuid():N}", "Card");

        await harness.ImportAsync(gone);

        Assert.Empty(harness.Project.Frames);
        Assert.Empty(harness.Project.SkippedFiles);
        var bar = Assert.Single(harness.InfoBars.Shown);
        Assert.Equal(InfoBarSeverity.Caution, bar.Severity);
        Assert.Equal(ProjectText.ImportFolderFailed(gone), bar.Message);
        Assert.StartsWith("Couldn't open the folder Card. ", bar.Message, StringComparison.Ordinal);
        Assert.Empty(harness.RealFailures());
    }

    [Fact]
    public async Task SeveralFoldersThatWillNotOpenAreListedInOneInfoBar()
    {
        using var harness = ImportHarness.WithPhotoImporter();
        var parent = Path.Combine(Path.GetTempPath(), $"10lapse-gone-{Guid.NewGuid():N}");

        await harness.ImportAsync(Path.Combine(parent, "A"), Path.Combine(parent, "B"));

        var bar = Assert.Single(harness.InfoBars.Shown);
        Assert.Equal(ProjectText.ImportFoldersFailed(2), bar.Message);
        Assert.Equal(2, bar.Details.Count);
        Assert.Empty(harness.Project.SkippedFiles);
    }

    [Fact]
    public async Task ALateBatchAfterNewProjectNeverLandsInTheNewProject()
    {
        var source = new ScriptedImportSource(
            [ScriptedImportSource.Photo("IMG_1.jpg"), ScriptedImportSource.Photo("IMG_2.jpg")],
            [ScriptedImportSource.Photo("IMG_3.jpg")]);
        var gate = source.HoldListBatch(1);
        using var harness = ImportHarness.With(source);
        var old = harness.Shell.CurrentProject;

        var import = harness.ImportAsync(@"C:\Scripted");
        await source.ListBatchReached.Task.WaitAsync(TestContext.Current.CancellationToken);
        await harness.Ui.InvokeAsync(() => old.Model.Frames.Length).WaitAsync(TestContext.Current.CancellationToken);
        await harness.Ui.RunAsync(() => harness.Shell.NewProjectCommand.ExecuteAsync(null));
        gate.SetResult();
        await import;

        Assert.NotSame(old, harness.Shell.CurrentProject);
        Assert.True(old.Model.IsClosed);
        Assert.Equal(2, old.Model.Frames.Length);
        Assert.Empty(harness.Project.Frames);
        Assert.Empty(harness.Project.SkippedFiles);
        // A cancellation by the user's own New project: nothing to show.
        Assert.Empty(harness.RealFailures());
        Assert.Empty(harness.InfoBars.Shown);
        Assert.False(harness.Import.IsImporting);
    }

    [Fact]
    public async Task AReadingBatchAfterNewProjectNeverLandsEither()
    {
        var source = new ScriptedImportSource([ScriptedImportSource.Photo("IMG_1.jpg"), ScriptedImportSource.Photo("IMG_2.jpg"), ScriptedImportSource.Photo("IMG_3.jpg")])
        {
            Read = frame => frame.FileName == "IMG_3.jpg"
                ? FrameReading.Failed(frame, new SkippedFile(frame.SourcePath, SkipReason.Truncated, "cut"))
                : FrameReading.Read(frame with { PixelSize = new PixelSize(4, 3) }),
        };
        var gate = source.HoldReadBatch(1);
        using var harness = ImportHarness.With(source);
        var old = harness.Shell.CurrentProject;

        var import = harness.ImportAsync(@"C:\Scripted");
        await source.ReadBatchReached.Task.WaitAsync(TestContext.Current.CancellationToken);
        await harness.Ui.RunAsync(() => harness.Shell.NewProjectCommand.ExecuteAsync(null));
        gate.SetResult();
        await import;

        Assert.Empty(harness.Project.Frames);
        Assert.Empty(harness.Project.SkippedFiles);
        Assert.Empty(old.Model.SkippedFiles);
        Assert.Empty(harness.RealFailures());
        Assert.Empty(harness.InfoBars.Shown);
    }

    [Fact]
    public async Task DroppingOnAProjectWithPhotosAsksAndStartNewProjectReplacesIt()
    {
        using var harness = ImportHarness.WithPhotoImporter();
        var kept = ScriptedImportSource.Photo("old.jpg");
        harness.Ui.Run(() => harness.Project.AddFrames([kept]));
        var old = harness.Shell.CurrentProject;
        harness.Dialogs.Choice = ChoiceResult.Secondary;

        await harness.DropAsync(PhotoFixtureCatalogue.Directory);

        var asked = Assert.Single(harness.Dialogs.Chosen);
        Assert.Equal(ProjectText.DropChoice(1), asked);
        Assert.Equal(ProjectText.NewProjectConfirm, asked.SecondaryText);
        Assert.True(old.IsDisposed);
        Assert.Equal([kept.Id], old.Model.Frames.Select(frame => frame.Id));
        Assert.Equal(16, harness.Project.Frames.Length);
        Assert.Empty(harness.Dialogs.Asked);
        Assert.Empty(harness.RealFailures());
    }

    [Fact]
    public async Task DroppingAndChoosingAddToProjectAddsToTheProjectThatIsOpen()
    {
        using var harness = ImportHarness.WithPhotoImporter();
        var kept = ScriptedImportSource.Photo("old.jpg");
        harness.Ui.Run(() => harness.Project.AddFrames([kept]));
        var project = harness.Shell.CurrentProject;
        harness.Dialogs.Choice = ChoiceResult.Primary;

        await harness.DropAsync(PhotoFixtureCatalogue.Directory);

        Assert.Same(project, harness.Shell.CurrentProject);
        Assert.Equal(17, project.Model.Frames.Length);
        Assert.Contains(project.Model.Frames, frame => frame.Id == kept.Id);
    }

    [Fact]
    public async Task CancellingTheDropQuestionImportsNothing()
    {
        using var harness = ImportHarness.WithPhotoImporter();
        harness.Ui.Run(() => harness.Project.AddFrames([ScriptedImportSource.Photo("old.jpg")]));
        harness.Dialogs.Choice = ChoiceResult.Cancel;

        await harness.DropAsync(PhotoFixtureCatalogue.Directory);

        Assert.Single(harness.Project.Frames);
        Assert.Empty(harness.InfoBars.Shown);
    }

    [Fact]
    public async Task DroppingOnAnEmptyProjectImportsWithoutAsking()
    {
        using var harness = ImportHarness.WithPhotoImporter();

        await harness.DropAsync(PhotoFixtureCatalogue.Directory);

        Assert.Empty(harness.Dialogs.Chosen);
        Assert.Equal(16, harness.Project.Frames.Length);
    }

    [Fact]
    public async Task ADropWithNothingToImportSaysSoWithoutAsking()
    {
        using var harness = ImportHarness.WithPhotoImporter();
        harness.Ui.Run(() => harness.Project.AddFrames([ScriptedImportSource.Photo("old.jpg")]));
        var text = Path.Combine(Path.GetTempPath(), $"10lapse-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(text, "not a photo", TestContext.Current.CancellationToken);
        try
        {
            await harness.DropAsync(text, @"C:\nowhere\IMG_1.jpg");
        }
        finally
        {
            File.Delete(text);
        }

        Assert.Empty(harness.Dialogs.Chosen);
        Assert.Equal(ProjectText.ImportNothingFound, Assert.Single(harness.InfoBars.Shown).Message);
        Assert.Single(harness.Project.Frames);
    }

    [Fact]
    public async Task AnEmptyProjectTakesTheDefaultSortCaptureTimeWhenEveryPhotoHasOne()
    {
        // Listed in name order; the capture times run the other way.
        var source = new ScriptedImportSource([ScriptedImportSource.Photo("IMG_1.jpg"), ScriptedImportSource.Photo("IMG_2.jpg"), ScriptedImportSource.Photo("IMG_3.jpg")])
        {
            Read = frame => FrameReading.Read(frame with
            {
                PixelSize = new PixelSize(4, 3),
                CaptureTime = new DateTime(2026, 5, 1, 18, 0, 0).AddMinutes(-int.Parse(frame.FileName[4..5], System.Globalization.CultureInfo.InvariantCulture)),
            }),
        };
        using var harness = ImportHarness.With(source);
        harness.Ui.Run(() => harness.Project.SortMode = SortMode.Manual);

        await harness.ImportAsync(@"C:\Scripted");

        Assert.Equal(SortMode.CaptureTime, harness.Project.SortMode);
        Assert.Equal(["IMG_3.jpg", "IMG_2.jpg", "IMG_1.jpg"], harness.Project.Frames.Select(frame => frame.FileName));
    }

    [Fact]
    public async Task AProjectWithPhotosKeepsItsOwnSortAndAManualOrderKeepsTheNewPhotosAtTheEnd()
    {
        var source = new ScriptedImportSource([ScriptedImportSource.Photo("IMG_1.jpg"), ScriptedImportSource.Photo("IMG_4.jpg")]);
        using var named = ImportHarness.With(source);
        named.Ui.Run(() =>
        {
            named.Project.AddFrames([ScriptedImportSource.Photo("IMG_3.jpg"), ScriptedImportSource.Photo("IMG_2.jpg")]);
            named.Project.SortMode = SortMode.Name;
            return true;
        });
        using var manual = ImportHarness.With(source);
        manual.Ui.Run(() =>
        {
            manual.Project.AddFrames([ScriptedImportSource.Photo("IMG_3.jpg"), ScriptedImportSource.Photo("IMG_2.jpg")]);
            manual.Project.SortMode = SortMode.Manual;
            return true;
        });

        await named.ImportAsync(@"C:\Scripted");
        await manual.ImportAsync(@"C:\Scripted");

        Assert.Equal(SortMode.Name, named.Project.SortMode);
        Assert.Equal(["IMG_1.jpg", "IMG_2.jpg", "IMG_3.jpg", "IMG_4.jpg"], named.Project.Frames.Select(frame => frame.FileName));
        Assert.Equal(SortMode.Manual, manual.Project.SortMode);
        Assert.Equal(["IMG_3.jpg", "IMG_2.jpg", "IMG_1.jpg", "IMG_4.jpg"], manual.Project.Frames.Select(frame => frame.FileName));
    }

    [Fact]
    public async Task CancelKeepsThePhotosAlreadyReadAndTakesOutTheUnreadOnes()
    {
        var source = new ScriptedImportSource([.. Enumerable.Range(1, 6).Select(i => ScriptedImportSource.Photo($"IMG_{i}.jpg"))]) { ReadBatch = 2 };
        var gate = source.HoldReadBatch(1);
        using var harness = ImportHarness.With(source);

        var import = harness.ImportAsync(@"C:\Scripted");
        await source.ReadBatchReached.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(6, await harness.Ui.InvokeAsync(() => harness.Project.Frames.Length));
        await harness.Ui.InvokeAsync(() =>
        {
            harness.Import.CancelCommand.Execute(null);
            return true;
        });
        await import;

        Assert.Equal(["IMG_1.jpg", "IMG_2.jpg"], harness.Project.Frames.Select(frame => frame.FileName));
        Assert.All(harness.Project.Frames, frame => Assert.NotNull(frame.PixelSize));
        Assert.Equal(ProjectText.ImportStopped(2), Assert.Single(harness.InfoBars.Shown).Message);
        Assert.Empty(harness.RealFailures());
        Assert.False(harness.Import.IsImporting);
        GC.KeepAlive(gate);
    }

    [Fact]
    public async Task CancelWhileListingTakesOutTheListedPhotosNoneOfWhichWereRead()
    {
        var source = new ScriptedImportSource([ScriptedImportSource.Photo("IMG_1.jpg"), ScriptedImportSource.Photo("IMG_2.jpg")], [ScriptedImportSource.Photo("IMG_3.jpg")]);
        source.HoldListBatch(1);
        using var harness = ImportHarness.With(source);
        harness.Ui.Run(() => harness.Project.AddFrames([ScriptedImportSource.Photo("IMG_0.jpg")]));

        var import = harness.ImportAsync(@"C:\Scripted");
        await source.ListBatchReached.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, await harness.Ui.InvokeAsync(() => harness.Project.Frames.Length));
        await harness.Ui.InvokeAsync(() =>
        {
            harness.Import.CancelCommand.Execute(null);
            return true;
        });
        await import;

        Assert.Equal(["IMG_0.jpg"], harness.Project.Frames.Select(frame => frame.FileName));
        Assert.Equal(ProjectText.ImportStoppedNone, Assert.Single(harness.InfoBars.Shown).Message);
        Assert.Empty(harness.RealFailures());
    }

    [Fact]
    public async Task ThePickersStartInPicturesThenBesideTheLastFolder()
    {
        using var harness = ImportHarness.With(new ScriptedImportSource());
        harness.Picker.Folder = @"C:\Shoots\Day 1";

        await harness.Ui.RunAsync(() => harness.Import.ChooseFolderCommand.ExecuteAsync(null));
        await harness.Import.WhenIdleAsync();
        harness.Picker.Photos = [@"D:\Card\DCIM\IMG_1.jpg"];
        await harness.Ui.RunAsync(() => harness.Import.ChoosePhotosCommand.ExecuteAsync(null));
        await harness.Import.WhenIdleAsync();
        await harness.Ui.RunAsync(() => harness.Import.ChooseFolderCommand.ExecuteAsync(null));
        await harness.Import.WhenIdleAsync();

        Assert.Equal([null, @"C:\Shoots", @"D:\Card\DCIM"], harness.Picker.StartFolders);
        Assert.Equal(@"C:\Shoots", harness.Folders.LastFolder);
    }

    [Fact]
    public async Task IncludeSubfoldersReachesTheRequest()
    {
        var source = new RecordingSource();
        using var harness = ImportHarness.With(source);
        harness.Ui.Run(() => harness.Import.IncludeSubfolders = true);

        await harness.ImportAsync(@"C:\Scripted");

        Assert.True(Assert.Single(source.Requests).IncludeSubfolders);
    }

    [Fact]
    public void TheDragCaptionCountsPhotoFilesWhenItCan()
    {
        Assert.Equal("Add photos", ProjectText.DragCaption(null));
        Assert.Equal("Add 1 photo", ProjectText.DragCaption(1));
        Assert.Equal("Add 1,200 photos", ProjectText.DragCaption(1200));
        Assert.Equal("Adding 1,240 of 5,000 photos…", ProjectText.ImportReadingProgress(1240, 5000));
        Assert.Equal("Found 5,000 photos…", ProjectText.ImportListingProgress(5000));
    }

    private sealed class RecordingSource : IPhotoImportSource
    {
        public List<ImportRequest> Requests { get; } = [];

        public bool IsPhotoFile(string path) => true;

        public async IAsyncEnumerable<ImportListBatch> ListAsync(ImportRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Requests.Add(request);
            await Task.Yield();
            yield break;
        }

        public async IAsyncEnumerable<IReadOnlyList<FrameReading>> ReadAsync(IReadOnlyList<Frame> frames, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield break;
        }
    }
}
