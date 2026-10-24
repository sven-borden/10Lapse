using TenLapse.Core.Projects;
using TenLapse.Presentation;
using TenLapse.Presentation.Preview;
using TenLapse.Tests.Import;
using TenLapse.Tests.Projects;

namespace TenLapse.Tests.Presentation;

/// <summary>
/// Crop and pan in the Frame section (#46): the switch and the easing write the project, Start and End pause the player on
/// the first and last frame and edit their own framing, playing goes back to Video, and the drags over the preview stay
/// inside the frame.
/// </summary>
public sealed class CropViewModelTests
{
    private static (ShellViewModel Shell, Project Project, PlayerViewModel Player, CropViewModel Crop) Harness(int photos = 10)
    {
        var shell = new ShellViewModel(new FakeDialogService(), new FakeErrorSink());
        var project = shell.CurrentProject.Model;
        project.AddFrames(TestFrames.Folder("a", photos));
        var player = new PlayerViewModel(shell, new FakeErrorSink(), () => 0, 1000);
        return (shell, project, player, new CropViewModel(shell, player, new FakeErrorSink()));
    }

    [Fact]
    public void ANewProjectIsOffShowsTheVideoAndEditsNothing()
    {
        var (_, project, _, crop) = Harness();

        Assert.False(crop.IsOn);
        Assert.Equal(["Video", "Start", "End"], crop.ShowLabels);
        Assert.Equal(["Smooth", "Linear"], crop.EasingLabels);
        Assert.Equal(0, crop.ShowIndex);
        Assert.Equal(0, crop.EasingIndex);
        Assert.False(crop.IsEditing);
        Assert.Equal(Framing.Whole, crop.EditedFraming);
        Assert.Equal(CropSettings.Default, project.Crop);
        Assert.Equal((1.0, 4.0), (crop.MinZoom, crop.MaxZoom));
    }

    [Fact]
    public void TheSwitchAndTheEasingWriteTheProject()
    {
        var (_, project, _, crop) = Harness();

        crop.IsOn = true;
        crop.EasingIndex = 1;

        Assert.Equal(CropSettings.Default with { Enabled = true, Easing = CropEasing.Linear }, project.Crop);
        Assert.True(crop.IsOn);
        Assert.Equal(1, crop.EasingIndex);
    }

    [Fact]
    public void StartAndEndPauseOnTheFirstAndLastFrameAndEditTheirOwnFraming()
    {
        var (_, project, player, crop) = Harness(photos: 10);
        var changes = 0;
        crop.EditingChanged += (_, _) => changes++;
        crop.IsOn = true;
        player.SeekPosition(4);

        crop.ShowIndex = 1;
        Assert.True(crop.IsEditing);
        Assert.Equal(0, player.Position);
        Assert.False(player.IsPlaying);
        Assert.Equal(CropText.FramingStartName, crop.FramingName);
        crop.Zoom = 2;
        Assert.Equal(new Framing(0.5, 0.5, 0.5), project.Crop.Start);
        Assert.Equal(Framing.Whole, project.Crop.End);
        Assert.Equal(CropText.Zoom(2), crop.ZoomText);

        crop.ShowIndex = 2;
        Assert.Equal(9, player.Position);
        Assert.Equal(CropText.FramingEndName, crop.FramingName);
        crop.Nudge(0.3, 0);
        Assert.Equal(Framing.Whole, project.Crop.End);
        crop.ZoomBy(4);
        Assert.Equal(new Framing(0.25, 0.5, 0.5), project.Crop.End);
        crop.Nudge(-1, 1);
        Assert.Equal(new Framing(0.25, 0.125, 0.875), project.Crop.End);

        crop.ResetCommand.Execute(null);
        Assert.Equal(Framing.Whole, project.Crop.End);
        Assert.Equal(new Framing(0.5, 0.5, 0.5), project.Crop.Start);
        Assert.True(changes > 0);
    }

    [Fact]
    public void PlayingOrTurningItOffGoesBackToTheVideo()
    {
        var (_, project, player, crop) = Harness();
        crop.IsOn = true;
        crop.ShowIndex = 2;

        player.Play();
        Assert.Equal(0, crop.ShowIndex);
        Assert.False(crop.IsEditing);

        player.Pause();
        crop.ShowIndex = 1;
        crop.Zoom = 2;
        crop.IsOn = false;
        Assert.Equal(0, crop.ShowIndex);
        Assert.False(crop.IsEditing);

        // The framings stay in the project while it is off.
        Assert.Equal(new Framing(0.5, 0.5, 0.5), project.Crop.Start);
        crop.ShowIndex = 1;
        Assert.False(crop.IsEditing);
    }

    [Fact]
    public void NothingIsEditedWhileTheVideoShows()
    {
        var (_, project, _, crop) = Harness();
        crop.IsOn = true;

        crop.SetEditedFraming(new Framing(0.5, 0.5, 0.5));
        crop.Zoom = 3;
        crop.Nudge(0.1, 0.1);

        Assert.Equal(CropSettings.Default with { Enabled = true }, project.Crop);
    }

    [Fact]
    public void ANewProjectStartsOnTheVideo()
    {
        var (shell, _, _, crop) = Harness();
        crop.IsOn = true;
        crop.ShowIndex = 1;

        shell.ReplaceWithoutAsking(() => new Project());

        Assert.Equal(0, crop.ShowIndex);
        Assert.False(crop.IsOn);
        Assert.False(crop.IsEditing);
    }

    [Fact]
    public void ADragMovesTheFramingAndStopsAtTheEdges()
    {
        var start = new Framing(0.5, 0.5, 0.5);

        Assert.Equal(new Framing(0.5, 0.6, 0.4), FramingDrag.Moved(start, 0.1, -0.1));
        Assert.Equal(new Framing(0.5, 0.75, 0.25), FramingDrag.Moved(start, 2, -2));
    }

    [Fact]
    public void ACornerDragResizesAboutTheOppositeCornerKeepingTheShape()
    {
        // The framing from 0.25 to 0.75 each way; the bottom right corner dragged in to (0.6, 0.5): the larger distance wins.
        var start = new Framing(0.5, 0.5, 0.5);
        var resized = FramingDrag.Resized(start, FramingCorner.BottomRight, 0.6, 0.5);
        Assert.Equal((0.25, 0.25, 0.35), (resized.Left, resized.Top, Math.Round(resized.Size, 12)));

        // The top left corner dragged out past the frame: as large as the frame allows from the bottom right corner (0.75).
        var grown = FramingDrag.Resized(start, FramingCorner.TopLeft, -1, -1);
        Assert.Equal(new Framing(0.75, 0.375, 0.375), grown);

        // Dragged across the anchor: never below the smallest framing; to no point at all: as it was.
        Assert.Equal(Framing.MinSize, FramingDrag.Resized(start, FramingCorner.TopRight, 0.1, 0.9).Size);
        Assert.Equal(start, FramingDrag.Resized(start, FramingCorner.BottomLeft, double.NaN, double.NaN));
    }

    [Fact]
    public void ZoomingKeepsTheCentreWhereTheFrameAllows()
    {
        var corner = new Framing(0.25, 0.125, 0.125);

        Assert.Equal(new Framing(0.5, 0.25, 0.25), FramingDrag.Zoomed(corner, 0.5));
        Assert.Equal(Framing.Whole, FramingDrag.Zoomed(corner, 0.1));
        Assert.Equal(corner, FramingDrag.Zoomed(corner, 0));
    }
}
