using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TenLapse.Presentation.Services;

namespace TenLapse.Services;

/// <summary>Asks <see cref="IDialogService"/> questions with a <see cref="ContentDialog"/> on the window's XAML root.</summary>
/// <param name="xamlRoot">Gives the root of the window the dialog belongs to, once its content is loaded.</param>
public sealed class ContentDialogService(Func<XamlRoot?> xamlRoot) : IDialogService
{
    // WinUI allows one open ContentDialog per window and throws on a second ShowAsync. Questions therefore wait
    // their turn here, and each waits for any ContentDialog it did not open, so none is lost or read as "no".
    private readonly DialogQueue _queue = new();

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(Confirmation confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);

        var root = xamlRoot() ?? throw new InvalidOperationException("The window has no XAML root yet, so the dialog cannot be shown.");
        return _queue.RunAsync(() => WaitForOpenDialogsAsync(root), () => ShowAsync(confirmation, root));
    }

    // While the queue holds the turn none of its own dialogs is open, so any open ContentDialog belongs to
    // someone else: wait until its popup closes, then look again.
    private static async Task WaitForOpenDialogsAsync(XamlRoot root)
    {
        while (VisualTreeHelper.GetOpenPopupsForXamlRoot(root).FirstOrDefault(popup => popup.Child is ContentDialog) is { } popup)
        {
            var closed = new TaskCompletionSource();
            void OnClosed(object? sender, object e) => closed.TrySetResult();
            popup.Closed += OnClosed;
            try
            {
                if (popup.IsOpen)
                {
                    await closed.Task;
                }
            }
            finally
            {
                popup.Closed -= OnClosed;
            }
        }
    }

    private static async Task<bool> ShowAsync(Confirmation confirmation, XamlRoot root)
    {
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

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
