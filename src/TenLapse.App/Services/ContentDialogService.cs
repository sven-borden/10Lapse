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

    /// <summary>True while a <see cref="ContentDialog"/> is open on the window (the rating prompt never lands on one).</summary>
    public bool HasOpenDialog => xamlRoot() is { } root && VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Any(popup => popup.Child is ContentDialog);

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(Confirmation confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);

        var root = xamlRoot() ?? throw new InvalidOperationException("The window has no XAML root yet, so the dialog cannot be shown.");
        return _queue.RunAsync(() => WaitForOpenDialogsAsync(root), () => ShowAsync(confirmation, root));
    }

    /// <inheritdoc />
    public Task<ChoiceResult> ChooseAsync(Choice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);

        var root = xamlRoot() ?? throw new InvalidOperationException("The window has no XAML root yet, so the dialog cannot be shown.");
        return _queue.RunAsync(() => WaitForOpenDialogsAsync(root), () => ShowAsync(choice, root));
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
        var dialog = Dialog(root, confirmation.Title, confirmation.Message);
        dialog.PrimaryButtonText = confirmation.ConfirmText;
        dialog.CloseButtonText = confirmation.CancelText;
        // Going ahead loses work, so Enter must not do it.
        dialog.DefaultButton = ContentDialogButton.Close;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static async Task<ChoiceResult> ShowAsync(Choice choice, XamlRoot root)
    {
        var dialog = Dialog(root, choice.Title, choice.Message);
        dialog.PrimaryButtonText = choice.PrimaryText;
        dialog.SecondaryButtonText = choice.SecondaryText;
        dialog.CloseButtonText = choice.CancelText;
        // The primary choice loses nothing (Choice's contract), so Enter may take it.
        dialog.DefaultButton = ContentDialogButton.Primary;
        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => ChoiceResult.Primary,
            ContentDialogResult.Secondary => ChoiceResult.Secondary,
            _ => ChoiceResult.Cancel,
        };
    }

    private static ContentDialog Dialog(XamlRoot root, string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = message,
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

        return dialog;
    }
}
