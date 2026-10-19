using TenLapse.Presentation.Filmstrip;
using TenLapse.Presentation.Preview;

namespace TenLapse.Tests.Presentation;

/// <summary>
/// The key routing between the filmstrip (#20) and the transport (#19): the strip's own keys while a thumbnail has the
/// focus, Delete and Ctrl+Z anywhere but a text input, and plain Space always the transport's.
/// </summary>
public sealed class FilmstripKeysTests
{
    [Theory]
    [InlineData(FilmstripKey.Left, false, false, false, FilmstripAction.Previous)]
    [InlineData(FilmstripKey.Right, false, false, false, FilmstripAction.Next)]
    [InlineData(FilmstripKey.Home, false, false, false, FilmstripAction.First)]
    [InlineData(FilmstripKey.End, false, false, false, FilmstripAction.Last)]
    [InlineData(FilmstripKey.Left, true, false, false, FilmstripAction.ExtendPrevious)]
    [InlineData(FilmstripKey.Right, true, false, false, FilmstripAction.ExtendNext)]
    [InlineData(FilmstripKey.Home, true, false, false, FilmstripAction.ExtendFirst)]
    [InlineData(FilmstripKey.End, true, false, false, FilmstripAction.ExtendLast)]
    [InlineData(FilmstripKey.Left, false, true, false, FilmstripAction.FocusPrevious)]
    [InlineData(FilmstripKey.Right, false, true, false, FilmstripAction.FocusNext)]
    [InlineData(FilmstripKey.Space, false, true, false, FilmstripAction.Toggle)]
    [InlineData(FilmstripKey.Space, true, false, false, FilmstripAction.ExtendToFocused)]
    [InlineData(FilmstripKey.Enter, false, false, false, FilmstripAction.Activate)]
    [InlineData(FilmstripKey.A, false, true, false, FilmstripAction.SelectAll)]
    [InlineData(FilmstripKey.Escape, false, false, false, FilmstripAction.Collapse)]
    [InlineData(FilmstripKey.Delete, false, false, false, FilmstripAction.Remove)]
    [InlineData(FilmstripKey.Z, false, true, false, FilmstripAction.Undo)]
    [InlineData(FilmstripKey.Left, false, false, true, FilmstripAction.MoveEarlier)]
    [InlineData(FilmstripKey.Right, false, false, true, FilmstripAction.MoveLater)]
    [InlineData(FilmstripKey.Space, false, false, false, FilmstripAction.None)]
    [InlineData(FilmstripKey.Left, true, false, true, FilmstripAction.None)]
    [InlineData(FilmstripKey.Z, true, true, false, FilmstripAction.None)]
    [InlineData(FilmstripKey.Other, false, false, false, FilmstripAction.None)]
    public void OnAThumbnailTheStripTakesItsKeys(FilmstripKey key, bool shift, bool control, bool alt, FilmstripAction action) =>
        Assert.Equal(action, FilmstripKeys.Resolve(key, shift, control, alt, inStrip: true, Focus(FocusedControl.FilmstripItem)));

    [Theory]
    [InlineData(FocusedControl.Other)]
    [InlineData(FocusedControl.ComboBox)]
    [InlineData(FocusedControl.RangeBase)]
    [InlineData(FocusedControl.RadioButton)]
    public void ElsewhereOnlyDeleteAndUndoAreTheStrips(FocusedControl focused)
    {
        var focus = Focus(focused);

        Assert.Equal(FilmstripAction.Remove, FilmstripKeys.Resolve(FilmstripKey.Delete, false, false, false, inStrip: false, focus));
        Assert.Equal(FilmstripAction.Undo, FilmstripKeys.Resolve(FilmstripKey.Z, false, true, false, inStrip: false, focus));
        foreach (var key in new[] { FilmstripKey.Left, FilmstripKey.Right, FilmstripKey.Home, FilmstripKey.End, FilmstripKey.Space, FilmstripKey.Enter, FilmstripKey.Escape })
        {
            Assert.Equal(FilmstripAction.None, FilmstripKeys.Resolve(key, false, false, false, inStrip: false, focus));
        }

        // Alt+Left is Back on the Settings page and nothing in the editor outside the strip.
        Assert.Equal(FilmstripAction.None, FilmstripKeys.Resolve(FilmstripKey.Left, false, false, true, inStrip: false, focus));
        Assert.Equal(FilmstripAction.None, FilmstripKeys.Resolve(FilmstripKey.A, false, true, false, inStrip: false, focus));
    }

    [Theory]
    [InlineData(FocusedControl.TextBox)]
    [InlineData(FocusedControl.NumberBox)]
    [InlineData(FocusedControl.EditableComboBox)]
    public void ATextInputKeepsDeleteAndUndo(FocusedControl focused)
    {
        Assert.Equal(FilmstripAction.None, FilmstripKeys.Resolve(FilmstripKey.Delete, false, false, false, inStrip: false, Focus(focused)));
        Assert.Equal(FilmstripAction.None, FilmstripKeys.Resolve(FilmstripKey.Z, false, true, false, inStrip: false, Focus(focused)));
    }

    [Fact]
    public void HoldingDeleteOrCtrlZDoesNotRepeat()
    {
        var focus = Focus(FocusedControl.FilmstripItem);

        Assert.Equal(FilmstripAction.None, FilmstripKeys.Resolve(FilmstripKey.Delete, false, false, false, true, focus, repeat: true));
        Assert.Equal(FilmstripAction.None, FilmstripKeys.Resolve(FilmstripKey.Z, false, true, false, true, focus, repeat: true));
        Assert.Equal(FilmstripAction.Next, FilmstripKeys.Resolve(FilmstripKey.Right, false, false, false, true, focus, repeat: true));
        Assert.Equal(FilmstripAction.MoveLater, FilmstripKeys.Resolve(FilmstripKey.Right, false, false, true, true, focus, repeat: true));
    }

    [Fact]
    public void WithAThumbnailFocusedSpacePlaysAndTheArrowsStayWithTheStrip()
    {
        // The strip is an arrow control to the transport: the arrows never step the playhead behind the strip's back,
        // while plain Space (which the strip leaves) plays or pauses, as the canvas's keyboard map has it.
        var focus = Focus(FocusedControl.FilmstripItem);

        Assert.Equal(KeyFocus.ArrowControl, focus);
        Assert.Equal(FilmstripAction.None, FilmstripKeys.Resolve(FilmstripKey.Space, false, false, false, inStrip: true, focus));
        Assert.Equal(TransportAction.TogglePlay, TransportKeys.Resolve(TransportKey.Space, shift: false, otherModifier: false, focus));
        Assert.Equal(TransportAction.None, TransportKeys.Resolve(TransportKey.Left, shift: false, otherModifier: false, focus));
        Assert.Equal(TransportAction.None, TransportKeys.Resolve(TransportKey.Right, shift: true, otherModifier: false, focus));

        // Ctrl+Space and Shift+Space select in the strip; the transport leaves both alone.
        Assert.Equal(TransportAction.None, TransportKeys.Resolve(TransportKey.Space, shift: false, otherModifier: true, focus));
        Assert.Equal(TransportAction.None, TransportKeys.Resolve(TransportKey.Space, shift: true, otherModifier: false, focus));
    }

    private static KeyFocus Focus(FocusedControl control) => TransportKeys.Classify(control);
}
