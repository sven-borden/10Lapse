using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TenLapse.Presentation.Services;

namespace TenLapse.Services;

/// <summary>Asks <see cref="IDialogService"/> questions with a <see cref="ContentDialog"/> on the window's XAML root.</summary>
/// <param name="xamlRoot">Gives the root of the window the dialog belongs to, once its content is loaded.</param>
public sealed class ContentDialogService(Func<XamlRoot?> xamlRoot) : IDialogService
{
    private bool _isOpen;

    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(Confirmation confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);

        // WinUI allows one open ContentDialog per thread; a second ShowAsync throws. The commands that ask are
        // AsyncRelayCommands (they cannot overlap themselves), so this only guards two different commands.
        if (_isOpen)
        {
            return false;
        }

        var root = xamlRoot() ?? throw new InvalidOperationException("The window has no XAML root yet, so the dialog cannot be shown.");
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = confirmation.Title,
            Content = confirmation.Message,
            PrimaryButtonText = confirmation.ConfirmText,
            CloseButtonText = confirmation.CancelText,
            // Going ahead loses work, so Enter must not do it.
            DefaultButton = ContentDialogButton.Close,
        };

        // A dialog built in code does not pick up the Fluent style or the window's theme by itself.
        if (Application.Current.Resources.TryGetValue("DefaultContentDialogStyle", out var style) && style is Style dialogStyle)
        {
            dialog.Style = dialogStyle;
        }

        if (root.Content is FrameworkElement content)
        {
            dialog.RequestedTheme = content.ActualTheme;
        }

        _isOpen = true;
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            _isOpen = false;
        }
    }
}
