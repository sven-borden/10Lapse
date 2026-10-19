using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TenLapse.Presentation;
using Windows.System;

namespace TenLapse.Shell;

/// <summary>The Timing section of the inspector (see the comment at the top of TimingPanel.xaml). The shell hosts it in its Editing state.</summary>
public sealed partial class TimingPanel : UserControl
{
    public TimingPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        LengthTrack.SizeChanged += (_, _) => UpdateBar();
    }

    /// <summary>The panel's view model, for x:Bind. Set once, before the panel is loaded.</summary>
    public TimingPanelViewModel? ViewModel { get; set; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.Announced -= OnAnnounced;
        viewModel.Announced += OnAnnounced;
        UpdateBar();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel.Announced -= OnAnnounced;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimingPanelViewModel.LengthFraction))
        {
            UpdateBar();
        }
    }

    // The bar's fill is the clip's length against a minute. A width, not a scale: nothing animates here yet (#35).
    private void UpdateBar()
    {
        if (ViewModel is { } viewModel)
        {
            LengthFill.Width = Math.Max(0, LengthTrack.ActualWidth * viewModel.LengthFraction);
        }
    }

    // The box takes its entry when Enter is pressed or the box loses focus; Escape drops the entry and shows the stored value.
    private void OnValueKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            viewModel.CommitValue(ValueBox.Text);
            ValueBox.SelectAll();
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            ValueBox.Text = viewModel.ValueText;
            ValueBox.SelectAll();
        }
    }

    private void OnValueLostFocus(object sender, RoutedEventArgs e) => ViewModel?.CommitValue(ValueBox.Text);

    private void OnValueGotFocus(object sender, RoutedEventArgs e) => ValueBox.SelectAll();

    // Narrator does not reliably hear a text that changes under it, so what a change came to is raised as a notification:
    // the sentence about a refused or corrected entry, then the readout.
    private void OnAnnounced(object? sender, string message)
    {
        var peer = FrameworkElementAutomationPeer.FromElement(this) ?? FrameworkElementAutomationPeer.CreatePeerForElement(this);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, message, "TimingChanged");
    }
}
