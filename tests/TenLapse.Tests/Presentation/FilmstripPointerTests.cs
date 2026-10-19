using TenLapse.Presentation;
using TenLapse.Presentation.Filmstrip;
using TenLapse.Presentation.Preview;
using TenLapse.Tests.Projects;

namespace TenLapse.Tests.Presentation;

/// <summary>The filmstrip's press, drag and cancel (#20; #82 review, M2), without WinUI.</summary>
public sealed class FilmstripPointerTests
{
    private const uint Mouse = 1;

    [Fact]
    public void AReleaseWithoutMovingIsAClickWithThePressModifiers()
    {
        var (_, player, strip, pointer) = Strip(10);
        strip.Click(2, false, false);

        pointer.Press(6, 50, 10, Mouse, shift: false, control: true, touch: false);
        Assert.Equal(FilmstripPointerResult.Pending, pointer.Move(Mouse, 53, 11));
        Assert.Equal(FilmstripPointerResult.Clicked, pointer.Release(Mouse));

        Assert.Equal([2, 6], Selected(strip));
        Assert.Equal(6, player.CurrentFrame);
        Assert.False(pointer.IsPressed);
    }

    [Fact]
    public void MovingSixDipsStartsADragThatTheReleaseDrops()
    {
        var (shell, _, strip, pointer) = Strip(10);
        var ids = shell.CurrentProject.Model.Frames.Select(frame => frame.Id).ToList();

        pointer.Press(1, 50, 10, Mouse, false, false, false);
        Assert.Equal(FilmstripPointerResult.DragStarted, pointer.Move(Mouse, 56, 10));
        Assert.True(strip.IsDragging);
        Assert.Equal(FilmstripPointerResult.Dragging, pointer.Move(Mouse, 300, 10));
        pointer.Over(5);
        Assert.Equal(FilmstripPointerResult.Dropped, pointer.Release(Mouse));

        Assert.Equal(ids[1], shell.CurrentProject.Model.Frames[4].Id);
        Assert.False(strip.IsDragging);
    }

    [Fact]
    public void EscapeThenMoveThenReleaseCommitsNothing()
    {
        var (shell, _, strip, pointer) = Strip(10);
        var project = shell.CurrentProject.Model;
        strip.Click(1, false, false);
        var selected = Selected(strip);
        var version = project.Version;

        pointer.Press(1, 50, 10, Mouse, false, false, false);
        pointer.Move(Mouse, 80, 10);
        pointer.Over(6);
        Assert.True(pointer.Cancel());

        // The pointer is still down and still moving: no new drag, and the release neither drops nor clicks.
        Assert.Equal(FilmstripPointerResult.Ignored, pointer.Move(Mouse, 200, 10));
        Assert.Equal(FilmstripPointerResult.Ignored, pointer.Release(Mouse));

        Assert.Equal(version, project.Version);
        Assert.False(strip.CanUndo);
        Assert.False(strip.IsDragging);
        Assert.Equal(selected, Selected(strip));
    }

    [Fact]
    public void EscapeBeforeTheDragCancelsTheClick()
    {
        var (_, player, strip, pointer) = Strip(10);
        strip.Click(2, false, false);

        pointer.Press(7, 50, 10, Mouse, false, false, false);
        pointer.Cancel();

        Assert.Equal(FilmstripPointerResult.Ignored, pointer.Release(Mouse));
        Assert.Equal([2], Selected(strip));
        Assert.Equal(2, player.CurrentFrame);
    }

    [Fact]
    public void ATouchNeverDragsAndATapIsAClick()
    {
        var (shell, _, strip, pointer) = Strip(10);
        var order = shell.CurrentProject.Model.Frames;

        pointer.Press(4, 50, 10, 7, false, false, touch: true);
        Assert.Equal(FilmstripPointerResult.Pending, pointer.Move(7, 250, 10));
        Assert.False(strip.IsDragging);
        Assert.Equal(FilmstripPointerResult.Clicked, pointer.Release(7));

        Assert.Equal([4], Selected(strip));
        Assert.Equal(order, shell.CurrentProject.Model.Frames);
    }

    [Fact]
    public void AnotherPointerIsIgnoredAndADropOutsideTheSeamsMovesNothing()
    {
        var (shell, _, strip, pointer) = Strip(10);
        var version = shell.CurrentProject.Model.Version;

        pointer.Press(3, 50, 10, Mouse, false, false, false);
        Assert.Equal(FilmstripPointerResult.Ignored, pointer.Move(2, 300, 10));
        Assert.Equal(FilmstripPointerResult.Ignored, pointer.Release(2));
        Assert.Equal(FilmstripPointerResult.DragStarted, pointer.Move(Mouse, 300, 10));

        // No seam was reported (the pointer never crossed one): the drop changes nothing.
        Assert.Equal(FilmstripPointerResult.Dropped, pointer.Release(Mouse));
        Assert.Equal(version, shell.CurrentProject.Model.Version);
        Assert.False(strip.CanUndo);
    }

    private static List<int> Selected(FilmstripViewModel strip) =>
        [.. strip.Items.Where(item => item.IsSelected).Select(item => item.Number - 1)];

    private (ShellViewModel Shell, PlayerViewModel Player, FilmstripViewModel Strip, FilmstripPointer Pointer) Strip(int frames)
    {
        var shell = new ShellViewModel(new FakeDialogService(), new FakeErrorSink());
        shell.CurrentProject.Model.AddFrames(TestFrames.Folder("a", frames));
        var player = new PlayerViewModel(shell, new FakeErrorSink(), () => 0, 1000);
        var strip = new FilmstripViewModel(shell, player, new FakeErrorSink());
        return (shell, player, strip, new FilmstripPointer(strip));
    }
}
