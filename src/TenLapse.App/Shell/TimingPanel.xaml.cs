using System.ComponentModel;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TenLapse.Presentation;
using TenLapse.Presentation.Motion;
using TenLapse.Themes;
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
        viewModel.Retimed -= OnRetimed;
        viewModel.Retimed += OnRetimed;
        UpdateBar();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel.Announced -= OnAnnounced;
            viewModel.Retimed -= OnRetimed;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimingPanelViewModel.LengthFraction))
        {
            UpdateBar();
        }
    }

    // The bar's fill is the clip's length against a minute: the track's width, scaled along X from its left edge, so a new
    // speed can grow or shrink it (Retime). Set at once here; OnRetimed animates the way there.
    private void UpdateBar()
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        LengthFill.Width = Math.Max(0, LengthTrack.ActualWidth);
        var fraction = (float)viewModel.LengthFraction;
        _barFrom = _barShown < 0 ? fraction : _barShown;
        _barShown = fraction;
        LengthFill.Scale = new Vector3(fraction, 1, 1);
    }

    // The speed changed (motion spec, Retime): the new length rises 6 px into place and fades in (250 ms Enter), and the bar
    // grows or shrinks to it (400 ms Enter); with animations off the readout fades in and the bar is at its length at once.
    // The values are already the new ones; the animations only draw the way there.
    private void OnRetimed(object? sender, EventArgs e)
    {
        var compositor = CompositionTarget.GetCompositorForCurrentThread();
        var bar = MotionTokens.Plan(MotionCatalogue.RetimeLengthBar);
        if (bar.Animates && _barFrom != _barShown && LengthFill.Width > 0)
        {
            var grow = compositor.CreateVector3KeyFrameAnimation();
            grow.Target = nameof(UIElement.Scale);
            grow.InsertKeyFrame(0f, new Vector3(_barFrom, 1, 1));
            grow.InsertKeyFrame(1f, new Vector3(_barShown, 1, 1), MotionTokens.CreateEasing(compositor, bar.Easing));
            grow.Duration = bar.Duration;
            LengthFill.StartAnimation(grow);
        }

        _barFrom = _barShown;
        var readout = MotionTokens.Plan(MotionCatalogue.RetimeReadout);
        if (!readout.Animates)
        {
            return;
        }

        var easing = MotionTokens.CreateEasing(compositor, readout.Easing);
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Target = nameof(UIElement.Opacity);
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f, easing);
        fade.Duration = readout.Duration;
        ReadoutPanel.StartAnimation(fade);
        if (readout.Moves)
        {
            var rise = compositor.CreateVector3KeyFrameAnimation();
            rise.Target = nameof(UIElement.Translation);
            rise.InsertKeyFrame(0f, new Vector3(0, ReadoutRise, 0));
            rise.InsertKeyFrame(1f, Vector3.Zero, easing);
            rise.Duration = readout.Duration;
            ReadoutPanel.StartAnimation(rise);
        }
    }

    // The new length starts this far below its place (motion spec, Retime: rises 6 px).
    private const float ReadoutRise = 6f;

    // The bar's length before the last change and now, as fractions of a minute (-1 before the first).
    private float _barFrom = -1;
    private float _barShown = -1;

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
