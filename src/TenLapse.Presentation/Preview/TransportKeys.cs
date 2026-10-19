namespace TenLapse.Presentation.Preview;

/// <summary>The keys the transport answers to (the App maps <c>VirtualKey</c> onto these).</summary>
public enum TransportKey
{
    /// <summary>Any other key.</summary>
    Other,

    /// <summary>Space.</summary>
    Space,

    /// <summary>Left arrow.</summary>
    Left,

    /// <summary>Right arrow.</summary>
    Right,

    /// <summary>Home.</summary>
    Home,

    /// <summary>End.</summary>
    End,
}

/// <summary>What a transport key does.</summary>
public enum TransportAction
{
    /// <summary>Nothing: the key belongs to someone else.</summary>
    None,

    /// <summary>Play or pause.</summary>
    TogglePlay,

    /// <summary>One frame back.</summary>
    StepBack,

    /// <summary>One frame forward.</summary>
    StepForward,

    /// <summary>Ten frames back.</summary>
    StepBackTen,

    /// <summary>Ten frames forward.</summary>
    StepForwardTen,

    /// <summary>The first frame.</summary>
    First,

    /// <summary>The last frame.</summary>
    Last,
}

/// <summary>Where the keyboard focus is when a transport key arrives.</summary>
public enum KeyFocus
{
    /// <summary>On the stage, a button, the scrubber or nothing in particular: the transport may take the key.</summary>
    Free,

    /// <summary>In a text input (a box, an editable combo box, a number box): the key is the user's typing.</summary>
    TextInput,

    /// <summary>On a control that uses arrows itself (a slider, a list, a segmented control): only Space, Home and End are ours.</summary>
    ArrowControl,
}

/// <summary>
/// What kind of control has the keyboard focus, as the App reads it from WinUI's focused element
/// (<c>TransportKeyboard</c>); <see cref="TransportKeys.Classify"/> says whose keys are whose.
/// </summary>
public enum FocusedControl
{
    /// <summary>Nothing, or a control none of the others describe (a button, the stage, the scrubber).</summary>
    Other,

    /// <summary>A TextBox.</summary>
    TextBox,

    /// <summary>A PasswordBox.</summary>
    PasswordBox,

    /// <summary>A RichEditBox.</summary>
    RichEditBox,

    /// <summary>An AutoSuggestBox.</summary>
    AutoSuggestBox,

    /// <summary>A NumberBox.</summary>
    NumberBox,

    /// <summary>A ComboBox the user can type into.</summary>
    EditableComboBox,

    /// <summary>A ComboBox that only selects (the Output panel's).</summary>
    ComboBox,

    /// <summary>A slider or another RangeBase.</summary>
    RangeBase,

    /// <summary>A ListView or GridView.</summary>
    ListViewBase,

    /// <summary>A ListBox.</summary>
    ListBox,

    /// <summary>Another Selector (FlipView, a selector of its own).</summary>
    Selector,

    /// <summary>A RadioButton, one segment of a segmented control.</summary>
    RadioButton,

    /// <summary>A RadioButtons group.</summary>
    RadioButtons,

    /// <summary>A ToggleSwitch.</summary>
    ToggleSwitch,

    /// <summary>A ScrollViewer.</summary>
    ScrollViewer,

    /// <summary>An item of an open menu.</summary>
    MenuItem,
}

/// <summary>
/// The transport's keys (#19): Space plays or pauses, the arrows step one frame,
/// Shift with an arrow ten, Home and End jump to the ends. A key typed into a text input stays there, a key with
/// Ctrl, Alt or the Windows key belongs to someone else (Alt+Left is Back), and a control that uses the arrows keeps
/// them. The App asks this only for keys no control has handled.
/// </summary>
public static class TransportKeys
{
    /// <summary>The action for <paramref name="key"/>, or <see cref="TransportAction.None"/> when the key is not the transport's here.</summary>
    /// <param name="key">The key.</param>
    /// <param name="shift">Shift is down.</param>
    /// <param name="otherModifier">Ctrl, Alt or the Windows key is down.</param>
    /// <param name="focus">Where the focus is.</param>
    /// <param name="repeat">
    /// The key is held and Windows repeats it. Space then does nothing (holding it would flicker between play and
    /// pause); the arrows keep stepping.
    /// </param>
    public static TransportAction Resolve(TransportKey key, bool shift, bool otherModifier, KeyFocus focus, bool repeat = false)
    {
        if (otherModifier || focus == KeyFocus.TextInput || (repeat && key == TransportKey.Space))
        {
            return TransportAction.None;
        }

        var arrowsTaken = focus == KeyFocus.ArrowControl;
        return key switch
        {
            TransportKey.Space when !shift => TransportAction.TogglePlay,
            TransportKey.Left when !arrowsTaken => shift ? TransportAction.StepBackTen : TransportAction.StepBack,
            TransportKey.Right when !arrowsTaken => shift ? TransportAction.StepForwardTen : TransportAction.StepForward,
            TransportKey.Home when !shift => TransportAction.First,
            TransportKey.End when !shift => TransportAction.Last,
            _ => TransportAction.None,
        };
    }

    /// <summary>
    /// Whose keys they are with <paramref name="control"/> focused: a text input keeps every key, a control that uses
    /// the arrows keeps them (a slider, a list, a select list, a segmented control, a switch, a scroller, a menu), and
    /// anything else leaves them to the transport.
    /// </summary>
    public static KeyFocus Classify(FocusedControl control) => control switch
    {
        FocusedControl.TextBox or FocusedControl.PasswordBox or FocusedControl.RichEditBox or FocusedControl.AutoSuggestBox
            or FocusedControl.NumberBox or FocusedControl.EditableComboBox => KeyFocus.TextInput,
        FocusedControl.ComboBox or FocusedControl.RangeBase or FocusedControl.ListViewBase or FocusedControl.ListBox
            or FocusedControl.Selector or FocusedControl.RadioButton or FocusedControl.RadioButtons or FocusedControl.ToggleSwitch
            or FocusedControl.ScrollViewer or FocusedControl.MenuItem => KeyFocus.ArrowControl,
        _ => KeyFocus.Free,
    };
}
