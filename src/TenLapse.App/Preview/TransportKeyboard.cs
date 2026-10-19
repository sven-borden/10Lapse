using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using TenLapse.Presentation.Preview;
using Windows.System;
using Windows.UI.Core;

namespace TenLapse.Preview;

/// <summary>
/// The transport's keys on the window (#19): the shell hands it every key no control handled, and it asks
/// <see cref="TransportKeys"/> what to do, so a key typed into a text box, or an arrow a list or a slider uses, stays
/// where it was. The rules (which control keeps which keys, Space not repeating) are tested in Presentation; this only
/// reads WinUI's key, its repeat flag and the focused control's type.
/// </summary>
internal static class TransportKeyboard
{
    /// <summary>Plays, steps or jumps when <paramref name="e"/> is a transport key here; marks it handled then.</summary>
    public static void Handle(KeyRoutedEventArgs e, PlayerViewModel player, XamlRoot? root)
    {
        if (e.Handled)
        {
            return;
        }

        var key = e.Key switch
        {
            VirtualKey.Space => TransportKey.Space,
            VirtualKey.Left => TransportKey.Left,
            VirtualKey.Right => TransportKey.Right,
            VirtualKey.Home => TransportKey.Home,
            VirtualKey.End => TransportKey.End,
            _ => TransportKey.Other,
        };
        if (key == TransportKey.Other || e.KeyStatus.IsMenuKeyDown)
        {
            return;
        }

        var shift = IsDown(VirtualKey.Shift);
        var other = IsDown(VirtualKey.Control) || IsDown(VirtualKey.Menu) || IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows);
        var focus = root is null ? KeyFocus.Free : TransportKeys.Classify(Describe(FocusManager.GetFocusedElement(root)));
        var action = TransportKeys.Resolve(key, shift, other, focus, repeat: e.KeyStatus.WasKeyDown);
        if (action != TransportAction.None && player.Handle(action))
        {
            e.Handled = true;
        }
    }

    // What kind of control has the focus; the order matters (an editable ComboBox before ComboBox, ComboBox, ListBox
    // and the lists before Selector, their base).
    private static FocusedControl Describe(object? focused) => focused switch
    {
        TextBox => FocusedControl.TextBox,
        PasswordBox => FocusedControl.PasswordBox,
        RichEditBox => FocusedControl.RichEditBox,
        AutoSuggestBox => FocusedControl.AutoSuggestBox,
        NumberBox => FocusedControl.NumberBox,
        ComboBox { IsEditable: true } => FocusedControl.EditableComboBox,
        ComboBox => FocusedControl.ComboBox,
        RangeBase => FocusedControl.RangeBase,
        ListViewBase => FocusedControl.ListViewBase,
        ListBox => FocusedControl.ListBox,
        Selector => FocusedControl.Selector,
        RadioButton => FocusedControl.RadioButton,
        RadioButtons => FocusedControl.RadioButtons,
        ToggleSwitch => FocusedControl.ToggleSwitch,
        ScrollViewer => FocusedControl.ScrollViewer,
        MenuFlyoutItemBase => FocusedControl.MenuItem,
        _ => FocusedControl.Other,
    };

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
