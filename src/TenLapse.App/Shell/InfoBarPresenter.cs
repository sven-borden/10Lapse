using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using TenLapse.Presentation;
using TenLapse.Presentation.Services;
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
    private readonly IErrorSink _errors;
    private readonly DispatcherQueue _dispatcher;
    private readonly Visual _visual;
    private readonly Compositor _compositor;

    // The ticket on screen (also while it animates out), and whether it is leaving.
    private InfoBarTicket? _shown;
    private bool _leaving;
    private int _generation;

    /// <param name="queue">The InfoBars to show.</param>
    /// <param name="layer">The floating layer that holds the InfoBar and its shadow; it is animated and collapsed.</param>
    /// <param name="bar">The InfoBar inside the layer.</param>
    /// <param name="errors">Takes the failures of InfoBar actions.</param>
    public InfoBarPresenter(InfoBarQueue queue, FrameworkElement layer, InfoBar bar, IErrorSink errors)
    {
        _queue = queue;
        _layer = layer;
        _bar = bar;
        _errors = errors;
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("The InfoBar presenter must be created on the UI thread.");

        ElementCompositionPreview.SetIsTranslationEnabled(layer, true);
        _visual = ElementCompositionPreview.GetElementVisual(layer);
        _compositor = _visual.Compositor;
        _layer.Visibility = Visibility.Collapsed;

        // The InfoBar's own close would collapse it at once; the queue decides instead, and the exit animates.
        _bar.Closing += OnClosing;
        _queue.CurrentChanged += OnCurrentChanged;
        Sync();
    }

    /// <summary>Stops following the queue (the window closed).</summary>
    public void Detach()
    {
        _queue.CurrentChanged -= OnCurrentChanged;
        _bar.Closing -= OnClosing;
    }

    // The queue may change on any thread; the layer is touched only on the UI thread.
    private void OnCurrentChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(Sync);

    private void OnClosing(InfoBar sender, InfoBarClosingEventArgs args)
    {
        args.Cancel = true;
        if (_shown is { } ticket && !_leaving)
        {
            _queue.Dismiss(ticket);
        }
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
        Fill(ticket.Request);
        _bar.IsOpen = true;

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
        var animate = MotionTokens.AnimationsEnabled;
        Animate(
            toOpacity: 0f,
            toY: Travel,
            duration: animate ? MotionTokens.Control : MotionTokens.ReducedFadeOut,
            easing: animate ? MotionTokens.CreateExit(_compositor) : MotionTokens.CreateLinear(_compositor),
            moves: animate,
            completed: () =>
            {
                _layer.Visibility = Visibility.Collapsed;
                _bar.ActionButton = null;
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
            batch.Completed += (_, _) => _dispatcher.TryEnqueue(() =>
            {
                if (generation == _generation)
                {
                    completed();
                }
            });
        }
    }

    private void Fill(InfoBarRequest request)
    {
        _bar.Severity = request.Severity switch
        {
            Severity.Success => XamlSeverity.Success,
            Severity.Caution => XamlSeverity.Warning,
            Severity.Critical => XamlSeverity.Error,
            _ => XamlSeverity.Informational,
        };
        _bar.Message = request.Message;

        if (request.ActionLabel is { } label && request.Action is { } action)
        {
            var button = new Button { Content = label };
            // An event handler that awaits one guarded call (CLAUDE.md).
            button.Click += async (_, _) => await RunActionAsync(action);
            _bar.ActionButton = button;
        }
        else
        {
            _bar.ActionButton = null;
        }
    }

    // The action ends the InfoBar's job: dismiss it first, then run the action. Never throws: a failure goes
    // to the error sink.
    private async Task RunActionAsync(Func<Task> action)
    {
        if (_shown is { } ticket && !_leaving)
        {
            _queue.Dismiss(ticket);
        }

        try
        {
            await action();
        }
        catch (Exception exception)
        {
            _errors.Report(exception, ProjectText.InfoBarActionFailed);
        }
    }
}
