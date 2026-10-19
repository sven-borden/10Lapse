using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using TenLapse.Presentation;

namespace TenLapse.Shell;

/// <summary>The Output section of the inspector (see the comment at the top of OutputPanel.xaml). The shell hosts it in its Editing state.</summary>
public sealed partial class OutputPanel : UserControl
{
    public OutputPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>The panel's view model, for x:Bind. Set once, before the panel is loaded.</summary>
    public OutputPanelViewModel? ViewModel { get; set; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        if (viewModel.IsPending)
        {
            Announce(viewModel.Status, "OutputPending");
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    // Narrator does not reliably hear a text that appears by a Collapsed to Visible flip, so the three changes a
    // person has to know about are raised as notifications: the lists appearing, and the panel replacing a setting.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        if (e.PropertyName == nameof(OutputPanelViewModel.IsSettled) && viewModel.IsSettled)
        {
            Announce(OutputText.Ready, "OutputReady");
        }
        else if (e.PropertyName == nameof(OutputPanelViewModel.Notice) && viewModel.HasNotice)
        {
            Announce(viewModel.Notice, "OutputNotice");
        }
    }

    private void Announce(string message, string activityId)
    {
        var peer = FrameworkElementAutomationPeer.FromElement(this) ?? FrameworkElementAutomationPeer.CreatePeerForElement(this);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantAll, message, activityId);
    }
}
