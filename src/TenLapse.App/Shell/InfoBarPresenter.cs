using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TenLapse.Presentation;
using TenLapse.Presentation.Services;
using TenLapse.Services;
using TenLapse.Themes;
using Severity = TenLapse.Presentation.Services.InfoBarSeverity;
using XamlSeverity = Microsoft.UI.Xaml.Controls.InfoBarSeverity;

namespace TenLapse.Shell;

/// <summary>
/// Shows the <see cref="InfoBarQueue"/>'s current InfoBar in the shell's floating InfoBar layer, with the
/// spec's motion: in from -8 px over Reveal (250 ms, Enter), out to -8 px over Control (167 ms, Exit); with
/// animations off, a short fade. The layer floats over the stage, so showing one never moves the layout.
/// </summary>
internal sealed class InfoBarPresenter
{
    private const float Travel = -8f;

    private readonly InfoBarQueue _queue;
    private readonly FrameworkElement _layer;
    private readonly InfoBar _bar;
    private readonly FrameworkElement _details;
    private readonly ItemsControl _detailList;
    private readonly DispatcherQueue _dispatcher;
    private readonly IErrorSink _errors;
    private readonly Visual _visual;
    private readonly Compositor _compositor;

    // The ticket on screen (also while it animates out), and whether it is leaving.
    private InfoBarTicket? _shown;
    private bool _leaving;
    private int _generation;
    private bool _detailsOpen;
    private DependencyObject? _focusBefore;

    /// <param name="queue">The InfoBars to show; their actions run through it.</param>
    /// <param name="layer">The floating layer that holds the InfoBar and its shadow; it is animated and collapsed.</param>
    /// <param name="bar">The InfoBar inside the layer.</param>
    /// <param name="details">The InfoBar's content: the detail list's panel, collapsed until its toggle opens it.</param>
    /// <param name="detailList">The list inside <paramref name="details"/> that takes the request's detail lines.</param>
    /// <param name="errors">
    /// Logs a failure of the presenter's own callbacks. Log only: an InfoBar about the InfoBar presenter failing would
    /// run the presenter again.
    /// </param>
    public InfoBarPresenter(InfoBarQueue queue, FrameworkElement layer, InfoBar bar, FrameworkElement details, ItemsControl detailList, IErrorSink errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        _errors = errors;
        _queue = queue;
        _layer = layer;
        _bar = bar;
        _details = details;
        _detailList = detailList;
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("The InfoBar presenter must be created on the UI thread.");

        ElementCompositionPreview.SetIsTranslationEnabled(layer, true);
        _visual = ElementCompositionPreview.GetElementVisual(layer);
        _compositor = _visual.Compositor;
        _layer.Visibility = Visibility.Collapsed;
        NameCloseButton();

        // The close button would collapse the InfoBar at once; the queue decides instead, and the exit animates.
        _bar.Closing += OnClosing;
        _layer.GettingFocus += OnLayerGettingFocus;
        _queue.CurrentChanged += OnCurrentChanged;
        Sync();
    }

    /// <summary>Stops following the queue (the window closed).</summary>
    public void Detach()
    {
        _queue.CurrentChanged -= OnCurrentChanged;
        _bar.Closing -= OnClosing;
        _layer.GettingFocus -= OnLayerGettingFocus;
    }

    // The queue may change on any thread; the layer is touched only on the UI thread.
    private void OnCurrentChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueueGuarded(_errors, Sync, ProjectText.InfoBarSyncFailed);

    // Only the close button is cancelled (and becomes a dismissal through the queue). The presenter's own
    // IsOpen = false at the end of the exit goes through, so the next IsOpen = true is a real change, which is
    // what makes the InfoBar announce itself to screen readers.
    private void OnClosing(InfoBar sender, InfoBarClosingEventArgs args)
    {
        if (args.Reason != InfoBarCloseReason.CloseButton)
        {
            return;
        }

        args.Cancel = true;
        if (_shown is { } ticket && !_leaving)
        {
            _queue.Dismiss(ticket);
        }
    }

    // The stock name is "Close", which reads like the window's own button; the prototype says Dismiss.
    private void NameCloseButton()
    {
        var style = new Style(typeof(Button)) { BasedOn = _bar.CloseButtonStyle };
        style.Setters.Add(new Setter(AutomationProperties.NameProperty, ProjectText.InfoBarDismiss));
        style.Setters.Add(new Setter(ToolTipService.ToolTipProperty, ProjectText.InfoBarDismiss));
        _bar.CloseButtonStyle = style;
    }

    // Remembers where focus came from when it moves into the InfoBar, to give it back when the InfoBar leaves.
    private void OnLayerGettingFocus(UIElement sender, GettingFocusEventArgs args)
    {
        if (args.OldFocusedElement is { } old && !IsInLayer(old))
        {
            _focusBefore = old;
        }
    }

    private bool IsInLayer(DependencyObject element)
    {
        for (var node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, _layer))
            {
                return true;
            }
        }

        return false;
    }

    // A focused InfoBar that leaves would drop keyboard focus to the window: give it back where it came from.
    private void ReturnFocus()
    {
        var before = _focusBefore;
        _focusBefore = null;
        if (_layer.XamlRoot is not { } root
            || FocusManager.GetFocusedElement(root) is not DependencyObject focused
            || !IsInLayer(focused))
        {
            return;
        }

        if (before is UIElement { XamlRoot: not null } element && element.Focus(FocusState.Programmatic))
        {
            return;
        }

        FocusManager.TryMoveFocus(FocusNavigationDirection.Next, new FindNextElementOptions { SearchRoot = root.Content });
    }

    // Says the new InfoBar to screen readers. IsOpen going true raises the InfoBar's own notification; this one
    // carries the message with the processing its severity asks for, under the same activity, so it replaces it.
    private void Announce(InfoBarRequest request)
    {
        var peer = FrameworkElementAutomationPeer.FromElement(_bar) ?? FrameworkElementAutomationPeer.CreatePeerForElement(_bar);
        peer?.RaiseNotificationEvent(
            AutomationNotificationKind.Other,
            request.Severity is Severity.Caution or Severity.Critical
                ? AutomationNotificationProcessing.ImportantMostRecent
                : AutomationNotificationProcessing.MostRecent,
            request.Message,
            "InfoBarOpenedActivityId");
    }

    // Brings the screen in line with the queue: one step at a time, each animation finishing before the next.
    private void Sync()
    {
        var current = _queue.Current;
        if (_leaving || ReferenceEquals(current, _shown))
        {
            return;
        }

        if (_shown is not null)
        {
            Leave();
        }
        else if (current is not null)
        {
            Enter(current);
        }
    }

    private void Enter(InfoBarTicket ticket)
    {
        _shown = ticket;
        Fill(ticket);
        _bar.IsOpen = true;
        _layer.IsHitTestVisible = true;
        Announce(ticket.Request);

        _visual.Opacity = 0f;
        _layer.Visibility = Visibility.Visible;
        var animate = MotionTokens.AnimationsEnabled;
        _visual.Properties.InsertVector3("Translation", new Vector3(0, animate ? Travel : 0, 0));
        Animate(
            toOpacity: 1f,
            toY: 0f,
            duration: animate ? MotionTokens.Reveal : MotionTokens.ReducedFadeIn,
            easing: animate ? MotionTokens.CreateEnter(_compositor) : MotionTokens.CreateLinear(_compositor),
            moves: animate,
            completed: null);
    }

    private void Leave()
    {
        _leaving = true;

        // What is leaving takes no more presses (its buttons check the ticket too, for the keyboard).
        _layer.IsHitTestVisible = false;
        ReturnFocus();
        var animate = MotionTokens.AnimationsEnabled;
        Animate(
            toOpacity: 0f,
            toY: Travel,
            duration: animate ? MotionTokens.Control : MotionTokens.ReducedFadeOut,
            easing: animate ? MotionTokens.CreateExit(_compositor) : MotionTokens.CreateLinear(_compositor),
            moves: animate,
            completed: () =>
            {
                _bar.IsOpen = false;
                _layer.Visibility = Visibility.Collapsed;
                _bar.ActionButton = null;
                _detailList.ItemsSource = null;
                _shown = null;
                _leaving = false;
                Sync();
            });
    }

    private void Animate(float toOpacity, float toY, TimeSpan duration, CompositionEasingFunction easing, bool moves, Action? completed)
    {
        var generation = ++_generation;
        var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        var opacity = _compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(1f, toOpacity, easing);
        opacity.Duration = duration;
        _visual.StartAnimation(nameof(Visual.Opacity), opacity);

        if (moves)
        {
            var translation = _compositor.CreateVector3KeyFrameAnimation();
            translation.InsertKeyFrame(1f, new Vector3(0, toY, 0), easing);
            translation.Duration = duration;
            _visual.StartAnimation("Translation", translation);
        }

        batch.End();
        if (completed is not null)
        {
            // Completed may arrive off the UI thread; a newer animation makes this one's ending moot.
            batch.Completed += (_, _) => _dispatcher.TryEnqueueGuarded(
                _errors,
                () =>
                {
                    if (generation == _generation)
                    {
                        completed();
                    }
                },
                ProjectText.InfoBarSyncFailed);
        }
    }

    // True while the InfoBar of this ticket is the one on screen and not leaving: only then do its buttons act.
    private bool IsLive(InfoBarTicket ticket) => !_leaving && ReferenceEquals(_shown, ticket);

    private void Fill(InfoBarTicket ticket)
    {
        var request = ticket.Request;
        _bar.Severity = request.Severity switch
        {
            Severity.Success => XamlSeverity.Success,
            Severity.Caution => XamlSeverity.Warning,
            Severity.Critical => XamlSeverity.Error,
            _ => XamlSeverity.Informational,
        };
        _bar.Message = request.Message;

        // The detail list starts collapsed every time.
        _detailsOpen = false;
        _details.Visibility = Visibility.Collapsed;
        _detailList.ItemsSource = request.HasDetails ? request.Details : null;

        if (request.HasDetails)
        {
            var toggle = new Button { Content = ProjectText.InfoBarShowDetails };
            toggle.Click += (_, _) =>
            {
                if (IsLive(ticket))
                {
                    _detailsOpen = !_detailsOpen;
                    _details.Visibility = _detailsOpen ? Visibility.Visible : Visibility.Collapsed;
                    toggle.Content = _detailsOpen ? ProjectText.InfoBarHideDetails : ProjectText.InfoBarShowDetails;
                }
            };
            _bar.ActionButton = toggle;
        }
        else if (request.ActionLabel is { } label)
        {
            var button = new Button { Content = label };

            // An event handler that awaits one guarded call (CLAUDE.md): RunActionAsync never throws, and the
            // ticket captured here keeps a press during the exit (or a second press) from running it again.
            button.Click += async (_, _) => await RunActionAsync(ticket);
            _bar.ActionButton = button;
        }
        else
        {
            _bar.ActionButton = null;
        }
    }

    private Task RunActionAsync(InfoBarTicket ticket) => IsLive(ticket) ? _queue.RunActionAsync(ticket) : Task.CompletedTask;
}
