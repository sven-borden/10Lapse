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
/// where it was. The rules are tested in Presentation; this only reads WinUI's key and focus.
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
        var focus = root is null ? KeyFocus.Free : Classify(FocusManager.GetFocusedElement(root));
        var action = TransportKeys.Resolve(key, shift, other, focus);
        if (action != TransportAction.None && player.Handle(action))
        {
            e.Handled = true;
        }
    }

    /// <summary>What kind of control has the focus.</summary>
    internal static KeyFocus Classify(object? focused) => focused switch
    {
        TextBox or PasswordBox or RichEditBox or AutoSuggestBox or NumberBox => KeyFocus.TextInput,
        ComboBox { IsEditable: true } => KeyFocus.TextInput,
        RangeBase or ListViewBase or ListBox or Selector or RadioButton or RadioButtons or ToggleSwitch or ScrollViewer or MenuFlyoutItemBase => KeyFocus.ArrowControl,
        _ => KeyFocus.Free,
    };

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
