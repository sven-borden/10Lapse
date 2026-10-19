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
/// The transport's keys (DESIGN.md, Keyboard shortcuts; #19): Space plays or pauses, the arrows step one frame,
/// Shift with an arrow ten, Home and End jump to the ends. A key typed into a text input stays there, a key with
/// Ctrl, Alt or the Windows key belongs to someone else (Alt+Left is Back), and a control that uses the arrows keeps
/// them. The App asks this only for keys no control has handled.
/// </summary>
public static class TransportKeys
{
    /// <summary>The action for <paramref name="key"/>, or <see cref="TransportAction.None"/> when the key is not the transport's here.</summary>
    public static TransportAction Resolve(TransportKey key, bool shift, bool otherModifier, KeyFocus focus)
    {
        if (otherModifier || focus == KeyFocus.TextInput)
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
}
