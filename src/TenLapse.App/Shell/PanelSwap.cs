using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using TenLapse.Presentation.Export;
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
    private const float OutTravel = -12f;
    private const float InTravel = 16f;

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

    /// <summary>Swaps to <paramref name="next"/>; <paramref name="arrived"/> runs once it is on screen (to move focus).</summary>
    public void Show(UIElement next, Action? arrived)
    {
        if (next == _shown)
        {
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

        Settle(next, Visibility.Collapsed);
        var animate = MotionTokens.AnimationsEnabled && !ExportPanel.ReduceMotionSwitch;
        if (leaving.Visibility != Visibility.Visible)
        {
            Enter(next, animate, generation, arrived);
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(leaving);
        var compositor = visual.Compositor;
        var easing = animate ? (CompositionEasingFunction)MotionTokens.CreateExit(compositor) : MotionTokens.CreateLinear(compositor);
        var duration = animate ? MotionTokens.Control : MotionTokens.ReducedFadeOut;
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        Animate(visual, 0f, animate ? OutTravel : 0f, duration, easing, animate);
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
                Enter(next, animate, generation, arrived);
            },
            ExportText.PanelFailed);
    }

    private void Enter(UIElement next, bool animate, int generation, Action? arrived)
    {
        var visual = ElementCompositionPreview.GetElementVisual(next);
        var compositor = visual.Compositor;
        visual.Opacity = 0f;
        visual.Properties.InsertVector3("Translation", new Vector3(animate ? InTravel : 0f, 0, 0));
        next.Visibility = Visibility.Visible;
        var easing = animate ? (CompositionEasingFunction)MotionTokens.CreateEnter(compositor) : MotionTokens.CreateLinear(compositor);
        Animate(visual, 1f, 0f, animate ? MotionTokens.Scene : MotionTokens.ReducedFadeIn, easing, animate);

        // Focus once the part has a layout (it was collapsed until now).
        if (arrived is not null)
        {
            _dispatcher.TryEnqueueGuarded(
                _log,
                () =>
                {
                    if (generation == _generation)
                    {
                        arrived();
                    }
                },
                ExportText.PanelFailed);
        }
    }

    private static void Animate(Visual visual, float opacity, float x, TimeSpan duration, CompositionEasingFunction easing, bool moves)
    {
        var compositor = visual.Compositor;
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, opacity, easing);
        fade.Duration = duration;
        visual.StartAnimation(nameof(Visual.Opacity), fade);
        if (moves)
        {
            var move = compositor.CreateVector3KeyFrameAnimation();
            move.InsertKeyFrame(1f, new Vector3(x, 0, 0), easing);
            move.Duration = duration;
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
