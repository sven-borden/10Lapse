using TenLapse.Presentation.Preview;

namespace TenLapse.Presentation.Filmstrip;

/// <summary>The keys the filmstrip answers to (the App maps <c>VirtualKey</c> onto these).</summary>
public enum FilmstripKey
{
    /// <summary>Any other key.</summary>
    Other,

    /// <summary>Left arrow.</summary>
    Left,

    /// <summary>Right arrow.</summary>
    Right,

    /// <summary>Home.</summary>
    Home,

    /// <summary>End.</summary>
    End,

    /// <summary>Space.</summary>
    Space,

    /// <summary>Enter.</summary>
    Enter,

    /// <summary>Escape.</summary>
    Escape,

    /// <summary>Delete.</summary>
    Delete,

    /// <summary>The A key (Ctrl+A).</summary>
    A,

    /// <summary>The Z key (Ctrl+Z).</summary>
    Z,
}

/// <summary>What a filmstrip key does.</summary>
public enum FilmstripAction
{
    /// <summary>Nothing: the key belongs to someone else (Space to the transport, for one).</summary>
    None,

    /// <summary>Select the frame before the focused one, alone, and show it.</summary>
    Previous,

    /// <summary>Select the frame after the focused one, alone, and show it.</summary>
    Next,

    /// <summary>Select the first frame, alone, and show it.</summary>
    First,

    /// <summary>Select the last frame, alone, and show it.</summary>
    Last,

    /// <summary>Extend the selection from the anchor to the frame before the focused one.</summary>
    ExtendPrevious,

    /// <summary>Extend the selection from the anchor to the frame after the focused one.</summary>
    ExtendNext,

    /// <summary>Extend the selection from the anchor to the first frame.</summary>
    ExtendFirst,

    /// <summary>Extend the selection from the anchor to the last frame.</summary>
    ExtendLast,

    /// <summary>Move the focus to the frame before, leaving the selection.</summary>
    FocusPrevious,

    /// <summary>Move the focus to the frame after, leaving the selection.</summary>
    FocusNext,

    /// <summary>Add the focused frame to the selection or take it out (Ctrl+Space).</summary>
    Toggle,

    /// <summary>Select from the anchor to the focused frame (Shift+Space).</summary>
    ExtendToFocused,

    /// <summary>Select the focused frame alone and show it (Enter).</summary>
    Activate,

    /// <summary>Select every frame (Ctrl+A).</summary>
    SelectAll,

    /// <summary>Go back from several selected frames to the one shown (Escape).</summary>
    Collapse,

    /// <summary>Remove the selected frames from the project (Delete).</summary>
    Remove,

    /// <summary>Undo the last removal or move (Ctrl+Z).</summary>
    Undo,

    /// <summary>Move the selected frames one place earlier (Alt+Left).</summary>
    MoveEarlier,

    /// <summary>Move the selected frames one place later (Alt+Right).</summary>
    MoveLater,
}

/// <summary>
/// The filmstrip's keys (#20), beside the transport's (#19, <see cref="TransportKeys"/>). Two places ask:
/// <list type="bullet">
/// <item>The strip itself, for keys pressed while a thumbnail has the focus. The strip is a list, so
/// the arrows, Home and End are its own: they select a frame and show it (Shift extends the selection, Ctrl moves only
/// the focus), and the transport never sees them. Ctrl+Space toggles the focused frame, Shift+Space selects up to it,
/// Enter selects it alone, Ctrl+A selects all, Escape goes back to one frame, and Alt+Left and Alt+Right move the
/// selected frames one place (the keyboard's reorder).</item>
/// <item>The window, for keys no control handled: Delete removes and Ctrl+Z undoes when nothing has the focus (the user
/// clicked the preview's stage, which takes none). With any other control focused (a text input, a button, a slider, a
/// ComboBox open or closed, a segmented control, a switch) they are not the strip's: removing frames out of sight of the
/// strip, behind an inspector control, would surprise (#82 review, minor 2).</item>
/// </list>
/// Plain Space is never the strip's: it plays or pauses everywhere outside a text input, as the canvas's keyboard map
/// says ("Play or pause: Space"), so it reaches the transport even with a thumbnail focused. Delete and Ctrl+Z do not
/// repeat while held, so holding Delete cannot empty the project.
/// </summary>
public static class FilmstripKeys
{
    /// <summary>The action for <paramref name="key"/>, or <see cref="FilmstripAction.None"/> when the key is not the strip's here.</summary>
    /// <param name="key">The key.</param>
    /// <param name="shift">Shift is down.</param>
    /// <param name="control">Ctrl is down.</param>
    /// <param name="alt">Alt is down.</param>
    /// <param name="focused">What has the focus: <see cref="FocusedControl.FilmstripItem"/> for a thumbnail.</param>
    /// <param name="repeat">The key is held and Windows repeats it.</param>
    public static FilmstripAction Resolve(FilmstripKey key, bool shift, bool control, bool alt, FocusedControl focused, bool repeat = false)
    {
        var inStrip = focused == FocusedControl.FilmstripItem;
        if (!inStrip && focused != FocusedControl.None)
        {
            return FilmstripAction.None;
        }

        // In the strip, or with nothing focused: remove and undo, once per press.
        if (key == FilmstripKey.Delete && !shift && !control && !alt)
        {
            return repeat ? FilmstripAction.None : FilmstripAction.Remove;
        }

        if (key == FilmstripKey.Z && control && !shift && !alt)
        {
            return repeat ? FilmstripAction.None : FilmstripAction.Undo;
        }

        if (!inStrip)
        {
            return FilmstripAction.None;
        }

        if (alt)
        {
            return (key, shift, control) switch
            {
                (FilmstripKey.Left, false, false) => FilmstripAction.MoveEarlier,
                (FilmstripKey.Right, false, false) => FilmstripAction.MoveLater,
                _ => FilmstripAction.None,
            };
        }

        return (key, shift, control) switch
        {
            (FilmstripKey.Left, false, false) => FilmstripAction.Previous,
            (FilmstripKey.Right, false, false) => FilmstripAction.Next,
            (FilmstripKey.Home, false, false) => FilmstripAction.First,
            (FilmstripKey.End, false, false) => FilmstripAction.Last,
            (FilmstripKey.Left, true, _) => FilmstripAction.ExtendPrevious,
            (FilmstripKey.Right, true, _) => FilmstripAction.ExtendNext,
            (FilmstripKey.Home, true, _) => FilmstripAction.ExtendFirst,
            (FilmstripKey.End, true, _) => FilmstripAction.ExtendLast,
            (FilmstripKey.Left, false, true) => FilmstripAction.FocusPrevious,
            (FilmstripKey.Right, false, true) => FilmstripAction.FocusNext,
            (FilmstripKey.Space, false, true) => FilmstripAction.Toggle,
            (FilmstripKey.Space, true, false) => FilmstripAction.ExtendToFocused,
            (FilmstripKey.Enter, false, false) => FilmstripAction.Activate,
            (FilmstripKey.A, false, true) => FilmstripAction.SelectAll,
            (FilmstripKey.Escape, false, false) => FilmstripAction.Collapse,
            _ => FilmstripAction.None,
        };
    }
}
