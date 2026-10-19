using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using TenLapse.Presentation;
using TenLapse.Presentation.Motion;
using TenLapse.Presentation.Services;
using TenLapse.Services;
using TenLapse.Themes;

namespace TenLapse.Shell;

/// <summary>
/// The screen changes between Empty, Editing and Settings (the prototype's <c>go</c>; motion spec, Arrival): the screen that
/// leaves fades out and shrinks to 98.5 % over 167 ms (Exit), and the one that arrives rises 8 px and fades in over 400 ms
/// (Enter) once the old one is gone. When the photos land on the Empty screen, the editor's surface fades in instead and
/// the arrival choreography starts (the preview develops, the filmstrip files in, the inspector's sections slide in).
/// With animations off both become short fades.
/// </summary>
/// <remarks>
/// The shell's visual states still switch the screens, at once, so the layout, focus and input are never held back. This
/// class only gives the elements whose visibility the states set Composition implicit show and hide animations, chosen
/// (and logged) the moment the view model announces the change, before x:Bind applies it: a hidden element is already
/// out of the layout and takes no input while its hide animation draws it leaving. A change that comes before the
/// previous one has finished starts from wherever the elements are and ends at the new screen's rest values.
/// </remarks>
internal sealed class ScreenMotion
{
    private const float SceneRise = 8f;
    private const float SceneShrink = 0.985f;

    private readonly ShellViewModel _viewModel;
    private readonly UIElement _empty;
    private readonly UIElement[] _editing;
    private readonly UIElement _settings;
    private readonly DispatcherQueue _dispatcher;
    private readonly IErrorSink _log;
    private readonly Action _arrived;
    private Screen _current;
    private bool _live;

    /// <param name="viewModel">The shell, whose ShowsEmpty, ShowsEditing and IsSettingsOpen pick the screen.</param>
    /// <param name="empty">The Empty screen's region.</param>
    /// <param name="editing">The Editing screen's elements whose visibility its state sets.</param>
    /// <param name="settings">The Settings page.</param>
    /// <param name="dispatcher">The UI thread's queue.</param>
    /// <param name="log">Where a failure goes (log only).</param>
    /// <param name="arrived">Starts the arrival choreography, once the photos have landed on the Empty screen.</param>
    public ScreenMotion(ShellViewModel viewModel, UIElement empty, UIElement[] editing, UIElement settings, DispatcherQueue dispatcher, IErrorSink log, Action arrived)
    {
        _viewModel = viewModel;
        _empty = empty;
        _editing = editing;
        _settings = settings;
        _dispatcher = dispatcher;
        _log = log;
        _arrived = arrived;
        _current = ScreenOf(viewModel);
        foreach (var element in Elements())
        {
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        }
    }

    private enum Screen
    {
        Empty,
        Editing,
        Settings,
    }

    /// <summary>
    /// From now on screen changes animate. Nothing animates on launch: the screen the window opens with, and any change
    /// before its first layout, appear at once.
    /// </summary>
    public void GoLive()
    {
        _current = ScreenOf(_viewModel);
        _live = true;
    }

    /// <summary>
    /// The view model says the screen may change (one of its three properties changed). Called before x:Bind sees the
    /// change, so the implicit animations are in place when the states switch the visibilities.
    /// </summary>
    public void Prepare()
    {
        var next = ScreenOf(_viewModel);
        if (next == _current)
        {
            return;
        }

        var previous = _current;
        _current = next;
        foreach (var element in Elements())
        {
            ElementCompositionPreview.SetImplicitShowAnimation(element, null);
            ElementCompositionPreview.SetImplicitHideAnimation(element, null);
        }

        if (!_live)
        {
            return;
        }

        var arrival = previous == Screen.Empty && next == Screen.Editing;
        var leaving = MotionTokens.Plan(MotionCatalogue.SceneOut);
        var arriving = MotionTokens.Plan(arrival ? MotionCatalogue.ArrivalSurface : MotionCatalogue.SceneIn);
        foreach (var element in ElementsOf(previous))
        {
            if (Hide(element, leaving) is { } hide)
            {
                ElementCompositionPreview.SetImplicitHideAnimation(element, hide);
            }
        }

        foreach (var element in ElementsOf(next))
        {
            ElementCompositionPreview.SetImplicitShowAnimation(element, Show(element, arriving));
        }

        if (arrival)
        {
            // After the states have shown the editor, so its parts are in the tree with their sizes.
            _dispatcher.TryEnqueueGuarded(_log, _arrived, ProjectText.ArrivalFailed);
        }
    }

    private static Screen ScreenOf(ShellViewModel viewModel) =>
        viewModel.IsSettingsOpen ? Screen.Settings : viewModel.ShowsEditing ? Screen.Editing : Screen.Empty;

    private IEnumerable<UIElement> ElementsOf(Screen screen) => screen switch
    {
        Screen.Empty => [_empty],
        Screen.Editing => _editing,
        _ => [_settings],
    };

    private IEnumerable<UIElement> Elements() => [_empty, .. _editing, _settings];

    // Fades out and, moving, shrinks a little around the middle; nothing when no animation plays.
    private static CompositionAnimationGroup? Hide(UIElement element, MotionStep step)
    {
        if (!step.Animates)
        {
            return null;
        }

        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        var group = compositor.CreateAnimationGroup();
        var easing = MotionTokens.CreateEasing(compositor, step.Easing);
        group.Add(Scalar(compositor, nameof(Visual.Opacity), null, 0f, step, easing));
        if (step.Moves && element is FrameworkElement { ActualWidth: > 0 } framework)
        {
            visual.CenterPoint = new Vector3((float)framework.ActualWidth / 2, (float)framework.ActualHeight / 2, 0);
            group.Add(Vector(compositor, nameof(Visual.Scale), null, new Vector3(SceneShrink, SceneShrink, 1), step, easing));
        }

        return group;
    }

    // Fades in after the old screen has gone and, moving, rises into place; always ends at rest (a hide may have shrunk it).
    private static CompositionAnimationGroup Show(UIElement element, MotionStep step)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        var group = compositor.CreateAnimationGroup();
        var easing = MotionTokens.CreateEasing(compositor, step.Easing);
        var rest = step with { Duration = step.Animates ? step.Duration : MotionSpec.Tap, Delay = step.Animates ? step.Delay : TimeSpan.Zero };
        group.Add(Scalar(compositor, nameof(Visual.Opacity), step.Animates ? 0f : 1f, 1f, rest, easing));
        group.Add(Vector(compositor, "Translation", new Vector3(0, step.Moves ? SceneRise : 0, 0), Vector3.Zero, rest, easing));
        group.Add(Vector(compositor, nameof(Visual.Scale), Vector3.One, Vector3.One, rest, easing));
        return group;
    }

    private static ScalarKeyFrameAnimation Scalar(Compositor compositor, string target, float? from, float to, MotionStep step, CompositionEasingFunction easing)
    {
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.Target = target;
        if (from is { } start)
        {
            animation.InsertKeyFrame(0f, start);
        }

        animation.InsertKeyFrame(1f, to, easing);
        Time(animation, step);
        return animation;
    }

    private static Vector3KeyFrameAnimation Vector(Compositor compositor, string target, Vector3? from, Vector3 to, MotionStep step, CompositionEasingFunction easing)
    {
        var animation = compositor.CreateVector3KeyFrameAnimation();
        animation.Target = target;
        if (from is { } start)
        {
            animation.InsertKeyFrame(0f, start);
        }

        animation.InsertKeyFrame(1f, to, easing);
        Time(animation, step);
        return animation;
    }

    private static void Time(KeyFrameAnimation animation, MotionStep step)
    {
        animation.Duration = step.Duration;
        animation.DelayTime = step.Delay;
        animation.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
    }
}
