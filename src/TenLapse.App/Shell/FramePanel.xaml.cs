using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using TenLapse.Presentation;

namespace TenLapse.Shell;

/// <summary>The Frame section of the inspector (see the comment at the top of FramePanel.xaml). The shell hosts it in its Editing state.</summary>
public sealed partial class FramePanel : UserControl
{
    public FramePanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>The panel's view model, for x:Bind. Set once, before the panel is loaded.</summary>
    public FramePanelViewModel? ViewModel { get; set; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.Announced -= OnAnnounced;
            viewModel.Announced += OnAnnounced;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.Announced -= OnAnnounced;
        }
    }

    // The buttons vanish with the suggestion, so keyboard focus moves on to the shape the user just chose.
    private void OnPortraitApply(object sender, RoutedEventArgs e)
    {
        ViewModel?.ApplyPortraitHint();
        MoveFocusAway();
    }

    private void OnPortraitDismiss(object sender, RoutedEventArgs e)
    {
        ViewModel?.DismissPortraitHint();
        MoveFocusAway();
    }

    private void MoveFocusAway() => ShapeControl.FocusSelected();

    // Narrator does not reliably hear a text that changes under it, so what a change came to is raised as a notification.
    private void OnAnnounced(object? sender, string message) =>
        Announcer.Announce(this, message, AutomationNotificationProcessing.ImportantMostRecent, "FrameChanged");
}
