using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using TenLapse.Presentation.Export;
using TenLapse.Presentation.Motion;
using TenLapse.Presentation.Services;
using TenLapse.Services;
using TenLapse.Themes;

namespace TenLapse.Shell;

/// <summary>
/// Swaps what the inspector column shows (#24; DESIGN.md, Export panel; motion spec, Gate): the outgoing part leaves in
/// 167 ms to -12 px (Exit), then the incoming one enters in 400 ms from +16 px (Enter). With animations off both are
/// short fades (90 ms out, 150 ms in). Nothing blocks input meanwhile; a newer swap overtakes an older one.
/// </summary>
internal sealed class PanelSwap
{
    private readonly UIElement[] _parts;
    private readonly DispatcherQueue _dispatcher;
    private readonly IErrorSink _log;
    private UIElement _shown;
    private int _generation;

    /// <param name="parts">Every part that can show; the first is shown now, the others are collapsed.</param>
    /// <param name="dispatcher">The UI thread's queue, for the animations' completion.</param>
    /// <param name="log">Where a failure goes (log only).</param>
    public PanelSwap(UIElement[] parts, DispatcherQueue dispatcher, IErrorSink log)
    {
        _parts = parts;
        _dispatcher = dispatcher;
        _log = log;
        _shown = parts[0];
        foreach (var part in parts)
        {
            ElementCompositionPreview.SetIsTranslationEnabled(part, true);
            part.Visibility = part == _shown ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>The part shown, or arriving.</summary>
    public UIElement Shown => _shown;

    /// <summary>
    /// Swaps to <paramref name="next"/>. <paramref name="focus"/> moves the keyboard focus into it at once (on the next
    /// tick, once the bindings that enable its controls have run), not after the animation (#38: no animation delays
    /// focus); <paramref name="arrived"/> runs as it starts to enter (its own arrival animation).
    /// </summary>
    public void Show(UIElement next, Action? focus, Action? arrived = null)
    {
        if (next == _shown)
        {
            focus?.Invoke();
            arrived?.Invoke();
            return;
        }

        var leaving = _shown;
        _shown = next;
        var generation = ++_generation;

        // Anything a swap overtaken earlier left half-shown goes at once.
        foreach (var part in _parts.Where(part => part != leaving && part != next))
        {
            Settle(part, Visibility.Collapsed);
        }

        // The incoming part joins the layout now, unseen, so it can take the focus before the outgoing one collapses:
        // a focused control that collapses would drop the focus to the first control of the window.
        Settle(next, Visibility.Visible);
        ElementCompositionPreview.GetElementVisual(next).Opacity = 0f;
        _dispatcher.TryEnqueueGuarded(
            _log,
            () =>
            {
                if (generation != _generation)
                {
                    return;
                }

                // Laid out first: a part that was collapsed has no templates yet, and a control without one takes no focus.
                next.UpdateLayout();
                focus?.Invoke();
                SwapOut(leaving, next, generation, arrived);
            },
            ExportText.PanelFailed);
    }

    private void SwapOut(UIElement leaving, UIElement next, int generation, Action? arrived)
    {
        if (leaving.Visibility != Visibility.Visible)
        {
            Enter(next, arrived);
            return;
        }

        var step = MotionTokens.Plan(MotionCatalogue.PanelOut);

        // The swap runs out, then in: an InfoBar waits for both (the entrance holds its own time again as it starts).
        MotionTokens.Quiet.Hold(step.Duration + MotionCatalogue.PanelIn.Plan(step.AnimationsEnabled).Duration);
        if (!step.Animates)
        {
            Settle(leaving, Visibility.Collapsed);
            Enter(next, arrived);
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(leaving);
        var batch = visual.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        Animate(visual, 0f, MotionSpec.PanelOutTravel, step);
        batch.End();
        batch.Completed += (_, _) => _dispatcher.TryEnqueueGuarded(
            _log,
            () =>
            {
                if (generation != _generation)
                {
                    return;
                }

                Settle(leaving, Visibility.Collapsed);
                Enter(next, arrived);
            },
            ExportText.PanelFailed);
    }

    private static void Enter(UIElement next, Action? arrived)
    {
        var step = MotionTokens.Plan(MotionCatalogue.PanelIn);
        var visual = ElementCompositionPreview.GetElementVisual(next);
        visual.Opacity = step.Animates ? 0f : 1f;
        visual.Properties.InsertVector3("Translation", new Vector3(step.Moves ? MotionSpec.PanelInTravel : 0f, 0, 0));
        if (step.Animates)
        {
            Animate(visual, 1f, 0f, step);
        }

        arrived?.Invoke();
    }

    // The opacity to its end, and with a move the translation along X too, on the step's timing.
    private static void Animate(Visual visual, float opacity, float x, MotionStep step)
    {
        var compositor = visual.Compositor;
        var easing = MotionTokens.CreateEasing(compositor, step.Easing);
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, opacity, easing);
        fade.Duration = step.Duration;
        visual.StartAnimation(nameof(Visual.Opacity), fade);
        if (step.Moves)
        {
            var move = compositor.CreateVector3KeyFrameAnimation();
            move.InsertKeyFrame(1f, new Vector3(x, 0, 0), easing);
            move.Duration = step.Duration;
            visual.StartAnimation("Translation", move);
        }
    }

    // Stops the part's animations and puts it at rest, shown or not.
    private static void Settle(UIElement part, Visibility visibility)
    {
        var visual = ElementCompositionPreview.GetElementVisual(part);
        visual.StopAnimation(nameof(Visual.Opacity));
        visual.StopAnimation("Translation");
        visual.Opacity = 1f;
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        part.Visibility = visibility;
    }
}
