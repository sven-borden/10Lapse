using TenLapse.Presentation;
using TenLapse.Presentation.FirstLaunch;

namespace TenLapse.Tests.Presentation;

public sealed class DragVerdictTests
{
    [Theory]
    [InlineData(true, false, null)]
    [InlineData(true, true, 12)]
    [InlineData(false, false, null)]
    public void WithSettingsOpenEveryDragIsRefusedWithTheSettingsCaption(bool holdsFiles, bool counted, int? photos)
    {
        var verdict = DragVerdict.Of(settingsOpen: true, holdsFiles, counted, photos);

        Assert.Equal(new DragVerdict(Accepts: false, ShowsLook: false, SettingsOpen: true), verdict);
    }

    [Fact]
    public void ADragWithoutFilesIsRefused()
    {
        Assert.Equal(new DragVerdict(false, false, false), DragVerdict.Of(settingsOpen: false, holdsFiles: false, counted: true, photos: null));
    }

    [Fact]
    public void WhileTheCountIsPendingTheCursorTakesTheDragButTheLookWaits()
    {
        // #12's cursor: "Add photos" until the count is known. The amber look waits for it.
        var verdict = DragVerdict.Of(settingsOpen: false, holdsFiles: true, counted: false, photos: null);

        Assert.True(verdict.Accepts);
        Assert.False(verdict.ShowsLook);
    }

    [Fact]
    public void ACountThatFindsNothingToImportRefusesTheDragAndNeverShowedTheLook()
    {
        // Text files, a zip or a video: refused once counted, and the look did not show while counting either.
        var pending = DragVerdict.Of(settingsOpen: false, holdsFiles: true, counted: false, photos: null);
        var counted = DragVerdict.Of(settingsOpen: false, holdsFiles: true, counted: true, photos: 0);

        Assert.False(pending.ShowsLook);
        Assert.Equal(new DragVerdict(false, false, false), counted);
    }

    [Theory]
    [InlineData(null)] // folders among the items: counted when the import walks them
    [InlineData(12)]
    public void ACountedDragWithSomethingToImportShowsTheLook(int? photos)
    {
        var verdict = DragVerdict.Of(settingsOpen: false, holdsFiles: true, counted: true, photos: photos);

        Assert.Equal(new DragVerdict(Accepts: true, ShowsLook: true, SettingsOpen: false), verdict);
    }

    [Fact]
    public void TheDropZoneFollowsTheVerdictThroughACount()
    {
        // The sequence of M2: DragOver while counting, then DragOver once the count found 0 importable files.
        var viewModel = new FirstLaunchViewModel();

        viewModel.DragOver(DragVerdict.Of(false, true, counted: false, photos: null).ShowsLook);
        Assert.False(viewModel.IsDragOver);

        viewModel.DragOver(DragVerdict.Of(false, true, counted: true, photos: 0).ShowsLook);
        Assert.False(viewModel.IsDragOver);

        viewModel.DragOver(DragVerdict.Of(false, true, counted: true, photos: 3).ShowsLook);
        Assert.True(viewModel.IsDragOver);
    }
}
