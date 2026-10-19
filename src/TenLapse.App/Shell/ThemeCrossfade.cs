using System.Numerics;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TenLapse.Media;
using TenLapse.Presentation;
using TenLapse.Presentation.Motion;
using TenLapse.Presentation.Services;
using TenLapse.Services;
using TenLapse.Themes;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace TenLapse.Shell;

/// <summary>
/// When Windows switches between light and dark, the app crossfades instead of flashing (motion spec, Theme: colors
/// crossfade over 250 ms linear; photos never change). The same fade with animations off (it moves nothing); at once in high contrast.
/// </summary>
/// <remarks>
/// <para>
/// To have the old look to fade from, the shell holds its theme itself: the shell's <c>RequestedTheme</c> is set to the
/// Windows mode, so XAML's own switch does not reach it. When Windows' colors change, the shell's window is captured as it
/// is, the picture covers it (taking no input), the theme switches under it, and the picture fades out. The capture leaves
/// out the Mica backdrop, which shows the new look at once, and draws the swap chains (the preview, the premiere's video)
/// as the surface behind them. So the picture gets a hole wherever a photo is (the preview's gate, the premiere's video),
/// and the live photos show through it unchanged for the whole crossfade.
/// </para>
/// <para>
/// In high contrast the shell's theme goes back to Default, so the system's high contrast colors apply as before. With
/// Debug <c>--theme=</c> the theme stays the one asked for. A capture that fails is logged and the theme switches all the
/// same.
/// </para>
/// </remarks>
internal sealed class ThemeCrossfade
{
    private readonly FrameworkElement _shell;
    private readonly UIElement _content;
    private readonly Image _snapshot;
    private readonly DispatcherQueue _dispatcher;
    private readonly IErrorSink _log;
    private readonly bool _follows;
    private readonly Func<IEnumerable<PhotoArea>> _photos;
    private readonly UISettings _settings = new();
    private int _generation;

    /// <param name="shell">The element whose theme the app's look follows (the shell view).</param>
    /// <param name="content">What is captured (the shell's root grid, under the snapshot).</param>
    /// <param name="snapshot">An image over everything, collapsed and not hit-testable, that shows the capture.</param>
    /// <param name="dispatcher">The UI thread's queue.</param>
    /// <param name="log">Where a failure goes (log only).</param>
    /// <param name="follows">False when the theme is fixed (Debug <c>--theme=</c>).</param>
    /// <param name="photos">Where photos are on screen now (in swap chains, which the capture cannot see): left out of it.</param>
    public ThemeCrossfade(FrameworkElement shell, UIElement content, Image snapshot, DispatcherQueue dispatcher, IErrorSink log, bool follows, Func<IEnumerable<PhotoArea>> photos)
    {
        _photos = photos;
        _shell = shell;
        _content = content;
        _snapshot = snapshot;
        _dispatcher = dispatcher;
        _log = log;
        _follows = follows;
        if (follows)
        {
            _shell.RequestedTheme = ThemeTokens.IsHighContrast ? ElementTheme.Default : WindowsTheme();
        }
    }

    /// <summary>Windows' colors changed (UI thread): follows its light or dark mode, crossfading.</summary>
    public void Follow()
    {
        if (!_follows)
        {
            return;
        }

        // Both high contrast changes are at once, and overtake a crossfade still capturing (its end would pin its theme).
        if (ThemeTokens.IsHighContrast)
        {
            Clear(++_generation);
            _shell.RequestedTheme = ElementTheme.Default;
            return;
        }

        var target = WindowsTheme();
        if (_shell.RequestedTheme == ElementTheme.Default)
        {
            // Back from high contrast: no old look of this kind to fade from.
            Clear(++_generation);
            _shell.RequestedTheme = target;
        }
        else if (_shell.RequestedTheme != target)
        {
            _ = SwitchAsync(target);
        }
    }

    /// <summary>The theme now, Light or Dark.</summary>
    public ElementTheme Current => _shell.ActualTheme;

    /// <summary>
    /// Crossfades to <paramref name="target"/>. A failure is logged, the picture goes, and the theme switches all the same;
    /// a newer switch, or a high contrast change, overtakes this one.
    /// </summary>
    public async Task SwitchAsync(ElementTheme target)
    {
        var generation = ++_generation;
        var step = MotionTokens.Plan(MotionCatalogue.ThemeChange);
        try
        {
            if (step.Animates && _content is FrameworkElement { ActualWidth: > 0, ActualHeight: > 0 } content)
            {
                var picture = new RenderTargetBitmap();
                await picture.RenderAsync(content);
                if (generation != _generation)
                {
                    return;
                }

                _snapshot.Source = picture;
                var visual = Visual();
                visual.StopAnimation(nameof(Microsoft.UI.Composition.Visual.Opacity));
                visual.Opacity = 1f;
                visual.Clip = PhotosCutOut(visual.Compositor, content);
                _snapshot.Visibility = Visibility.Visible;
                _shell.RequestedTheme = target;
                FadeOut(step, generation);
                return;
            }
        }
        catch (Exception exception)
        {
            _log.Report(exception, ProjectText.MotionFailed);
            Clear(generation);
        }

        if (generation == _generation)
        {
            _shell.RequestedTheme = target;
        }
    }

    // The whole picture but the photos on screen: a clip with a hole for each (the gate's rounded corners), so the live
    // photos show through. Null when no photo is on screen.
    private CompositionGeometricClip? PhotosCutOut(Compositor compositor, FrameworkElement content)
    {
        var holes = _photos().Where(area => IsShown(area.Element, content)).ToList();
        if (holes.Count == 0)
        {
            return null;
        }

        var device = GraphicsDevice.Shared;
        var picture = CanvasGeometry.CreateRectangle(device, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        foreach (var area in holes)
        {
            var place = area.Element.TransformToVisual(content).TransformBounds(area.Bounds);
            using var hole = CanvasGeometry.CreateRoundedRectangle(device, place, area.Radius, area.Radius);
            var cut = picture.CombineWith(hole, Matrix3x2.Identity, CanvasGeometryCombine.Exclude);
            picture.Dispose();
            picture = cut;
        }

        return compositor.CreateGeometricClip(compositor.CreatePathGeometry(new CompositionPath(picture)));
    }

    // True when the element and everything above it up to the shell's root shows (a collapsed screen hides its photos).
    private static bool IsShown(UIElement element, UIElement root)
    {
        for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is UIElement { Visibility: Visibility.Collapsed })
            {
                return false;
            }

            if (ReferenceEquals(node, root))
            {
                return true;
            }
        }

        return false;
    }

    private Visual Visual() => ElementCompositionPreview.GetElementVisual(_snapshot);

    private void FadeOut(MotionStep step, int generation)
    {
        var visual = Visual();
        var compositor = visual.Compositor;
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 1f);
        fade.InsertKeyFrame(1f, 0f, MotionTokens.CreateEasing(compositor, step.Easing));
        fade.Duration = step.Duration;
        visual.StartAnimation(nameof(Microsoft.UI.Composition.Visual.Opacity), fade);
        batch.End();
        batch.Completed += (_, _) => _dispatcher.TryEnqueueGuarded(_log, () => Clear(generation), ProjectText.MotionFailed);
    }

    private void Clear(int generation)
    {
        if (generation != _generation)
        {
            return;
        }

        _snapshot.Visibility = Visibility.Collapsed;
        _snapshot.Source = null;
        var visual = Visual();
        visual.StopAnimation(nameof(Microsoft.UI.Composition.Visual.Opacity));
        visual.Opacity = 1f;
        visual.Clip = null;
    }

    // Windows' app mode: a dark background color means dark mode.
    private ElementTheme WindowsTheme()
    {
        var background = _settings.GetColorValue(UIColorType.Background);
        return background.R + background.G + background.B < 384 ? ElementTheme.Dark : ElementTheme.Light;
    }
}

/// <summary>Where a photo is on screen, drawn by a swap chain that a capture of the window cannot see.</summary>
/// <param name="Element">The element that holds it.</param>
/// <param name="Bounds">The photo's rectangle, in the element's DIPs.</param>
/// <param name="Radius">The radius of its corners, in DIPs.</param>
internal readonly record struct PhotoArea(UIElement Element, Rect Bounds, float Radius);
