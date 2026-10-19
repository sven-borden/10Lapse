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
using TenLapse.Presentation.Motion;
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
    private const float DetailsTravel = -4f;

    private readonly InfoBarQueue _queue;
    private readonly FrameworkElement _layer;
    private readonly InfoBar _bar;
    private readonly FrameworkElement _details;
    private readonly ItemsControl _detailList;
    private readonly DispatcherQueue _dispatcher;
    private readonly IErrorSink _errors;
    private readonly Visual _visual;
    private readonly Compositor _compositor;

    // Syncs again once the running transition has ended, for an InfoBar that waits for it.
    private readonly DispatcherQueueTimer _held;

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
        ElementCompositionPreview.SetIsTranslationEnabled(details, true);
        _visual = ElementCompositionPreview.GetElementVisual(layer);
        _compositor = _visual.Compositor;
        _layer.Visibility = Visibility.Collapsed;
        NameCloseButton();
        _held = _dispatcher.CreateTimer();
        _held.IsRepeating = false;
        _held.Tick += OnHeldTick;

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
        _held.Stop();
        _held.Tick -= OnHeldTick;
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

    // Brings the screen in line with the queue: one step at a time, each animation finishing before the next, and no
    // entrance while a screen, arrival, panel or theme transition runs (motion spec, InfoBar: never appears during a
    // transition). A held InfoBar stays the queue's current one, so nothing is lost or reordered; the timer syncs again
    // when the transition has ended, and a newer transition holds it longer.
    private void Sync()
    {
        var current = _queue.Current;
        var untilQuiet = MotionTokens.Quiet.Remaining;
        switch (InfoBarSync.Next(current, _shown, _leaving, untilQuiet))
        {
            case InfoBarMove.Leave:
                Leave();
                break;
            case InfoBarMove.Enter:
                _held.Stop();
                Enter(current!);
                break;
            case InfoBarMove.Wait:
#if DEBUG
                Debugging.DebugMotion.Note(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"InfoBar.In held {untilQuiet.TotalMilliseconds:0} ms (a transition runs)"));
#endif
                _held.Interval = untilQuiet;
                _held.Start();
                break;
        }
    }

    private void OnHeldTick(DispatcherQueueTimer sender, object args) => _errors.RunLogged(Sync, ProjectText.InfoBarSyncFailed);

    private void Enter(InfoBarTicket ticket)
    {
        _shown = ticket;
        Fill(ticket);
        _bar.IsOpen = true;
        _layer.IsHitTestVisible = true;
        Announce(ticket.Request);

        var step = MotionTokens.Plan(MotionCatalogue.InfoBarIn);
        _visual.Opacity = step.Animates ? 0f : 1f;
        _layer.Visibility = Visibility.Visible;
        _visual.Properties.InsertVector3("Translation", new Vector3(0, step.Moves ? Travel : 0, 0));
        Animate(_visual, toOpacity: 1f, toY: 0f, step, completed: null);
    }

    private void Leave()
    {
        _leaving = true;

        // What is leaving takes no more presses (its buttons check the ticket too, for the keyboard).
        _layer.IsHitTestVisible = false;
        ReturnFocus();
        Animate(
            _visual,
            toOpacity: 0f,
            toY: Travel,
            MotionTokens.Plan(MotionCatalogue.InfoBarOut),
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

    // The detail list opens under the bar from -4 px (250 ms Enter; a fade with animations off) and closes at once, as the
    // canvas does.
    private void ShowDetails(bool open)
    {
        var visual = ElementCompositionPreview.GetElementVisual(_details);
        visual.StopAnimation(nameof(Visual.Opacity));
        visual.StopAnimation("Translation");
        visual.Opacity = 1f;
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        _details.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (!open)
        {
            return;
        }

        var step = MotionTokens.Plan(MotionCatalogue.InfoBarDetails);
        if (step.Animates)
        {
            visual.Opacity = 0f;
            visual.Properties.InsertVector3("Translation", new Vector3(0, step.Moves ? DetailsTravel : 0, 0));
            Animate(visual, toOpacity: 1f, toY: 0f, step, completed: null, own: false);
        }
    }

    // Animates the layer (own: its generation decides whether the completion still counts) or the detail list. A step
    // that animates nothing ends at once, on the same path.
    private void Animate(Visual visual, float toOpacity, float toY, MotionStep step, Action? completed, bool own = true)
    {
        var generation = own ? ++_generation : _generation;
        if (!step.Animates)
        {
            visual.StopAnimation(nameof(Visual.Opacity));
            visual.StopAnimation("Translation");
            visual.Opacity = toOpacity;
            visual.Properties.InsertVector3("Translation", new Vector3(0, step.Moves ? toY : 0, 0));
            if (completed is not null)
            {
                _dispatcher.TryEnqueueGuarded(_errors, () => Complete(generation, completed), ProjectText.InfoBarSyncFailed);
            }

            return;
        }

        var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var easing = MotionTokens.CreateEasing(_compositor, step.Easing);
        var opacity = _compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(1f, toOpacity, easing);
        opacity.Duration = step.Duration;
        visual.StartAnimation(nameof(Visual.Opacity), opacity);

        if (step.Moves)
        {
            var translation = _compositor.CreateVector3KeyFrameAnimation();
            translation.InsertKeyFrame(1f, new Vector3(0, toY, 0), easing);
            translation.Duration = step.Duration;
            visual.StartAnimation("Translation", translation);
        }

        batch.End();
        if (completed is not null)
        {
            // Completed may arrive off the UI thread; a newer animation makes this one's ending moot.
            batch.Completed += (_, _) => _dispatcher.TryEnqueueGuarded(_errors, () => Complete(generation, completed), ProjectText.InfoBarSyncFailed);
        }
    }

    private void Complete(int generation, Action completed)
    {
        if (generation == _generation)
        {
            completed();
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
            toggle.Click += (_, _) => _errors.RunLogged(
                () =>
                {
                    if (IsLive(ticket))
                    {
                        _detailsOpen = !_detailsOpen;
                        ShowDetails(_detailsOpen);
                        toggle.Content = _detailsOpen ? ProjectText.InfoBarHideDetails : ProjectText.InfoBarShowDetails;
                    }
                },
                ProjectText.InfoBarSyncFailed);
            _bar.ActionButton = toggle;
        }
        else if (request.ActionLabel is { } label)
        {
            var button = new Button { Content = label };

            // An event handler that awaits one guarded call (CLAUDE.md): the queue's RunActionAsync never throws, and
            // it runs the action only while this ticket is the current one and its action is not already running, so
            // a press during the exit (the ticket is no longer current) or a second press does nothing.
            button.Click += async (_, _) => await _queue.RunActionAsync(ticket);
            _bar.ActionButton = button;
        }
        else
        {
            _bar.ActionButton = null;
        }
    }
}
