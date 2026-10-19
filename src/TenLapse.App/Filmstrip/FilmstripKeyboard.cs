using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using TenLapse.Presentation.Filmstrip;
using TenLapse.Presentation.Preview;
using TenLapse.Preview;
using Windows.System;

namespace TenLapse.Filmstrip;

/// <summary>
/// Reads a WinUI key for the filmstrip (#20): the key, its modifiers, its repeat flag and the focused control's kind. The
/// rules are <see cref="FilmstripKeys"/>' and are tested in Presentation.
/// </summary>
internal static class FilmstripKeyboard
{
    /// <summary>The filmstrip's action for <paramref name="e"/>; <see cref="FilmstripAction.None"/> when the key is not its.</summary>
    /// <param name="e">The key.</param>
    /// <param name="inStrip">A thumbnail has the focus (the strip's own handler); false for the window's.</param>
    /// <param name="root">The window's content, for the focused element.</param>
    public static FilmstripAction Resolve(KeyRoutedEventArgs e, bool inStrip, XamlRoot? root)
    {
        var key = e.Key switch
        {
            VirtualKey.Left => FilmstripKey.Left,
            VirtualKey.Right => FilmstripKey.Right,
            VirtualKey.Home => FilmstripKey.Home,
            VirtualKey.End => FilmstripKey.End,
            VirtualKey.Space => FilmstripKey.Space,
            VirtualKey.Enter => FilmstripKey.Enter,
            VirtualKey.Escape => FilmstripKey.Escape,
            VirtualKey.Delete => FilmstripKey.Delete,
            VirtualKey.A => FilmstripKey.A,
            VirtualKey.Z => FilmstripKey.Z,
            _ => FilmstripKey.Other,
        };
        if (key == FilmstripKey.Other)
        {
            return FilmstripAction.None;
        }

        var shift = TransportKeyboard.IsDown(VirtualKey.Shift);
        var control = TransportKeyboard.IsDown(VirtualKey.Control);
        var alt = e.KeyStatus.IsMenuKeyDown || TransportKeyboard.IsDown(VirtualKey.Menu);
        if (TransportKeyboard.IsDown(VirtualKey.LeftWindows) || TransportKeyboard.IsDown(VirtualKey.RightWindows))
        {
            return FilmstripAction.None;
        }

        var focus = root is null ? KeyFocus.Free : TransportKeys.Classify(TransportKeyboard.Describe(FocusManager.GetFocusedElement(root)));
        return FilmstripKeys.Resolve(key, shift, control, alt, inStrip, focus, repeat: e.KeyStatus.WasKeyDown);
    }
}
