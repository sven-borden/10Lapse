using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Imaging;
using TenLapse.Presentation;
using TenLapse.Presentation.Motion;
using TenLapse.Presentation.Services;
using TenLapse.Services;
using TenLapse.Themes;
using Windows.UI.ViewManagement;

namespace TenLapse.Shell;

/// <summary>
/// When Windows switches between light and dark, the app crossfades instead of flashing (motion spec, Theme: colors
/// crossfade over 250 ms linear; photos never change). At once with animations off, and in high contrast.
/// </summary>
/// <remarks>
/// <para>
/// To have the old look to fade from, the shell holds its theme itself: the shell's <c>RequestedTheme</c> is set to the
/// Windows mode, so XAML's own switch does not reach it. When Windows' colors change, the shell's window is captured as it
/// is, the picture covers it (taking no input), the theme switches under it, and the picture fades out. The capture leaves
/// out the Mica backdrop and the preview's swap chain, which show the new look at once; the preview's photo is the same in
/// both themes.
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
    private readonly UISettings _settings = new();
    private int _generation;

    /// <param name="shell">The element whose theme the app's look follows (the shell view).</param>
    /// <param name="content">What is captured (the shell's root grid, under the snapshot).</param>
    /// <param name="snapshot">An image over everything, collapsed and not hit-testable, that shows the capture.</param>
    /// <param name="dispatcher">The UI thread's queue.</param>
    /// <param name="log">Where a failure goes (log only).</param>
    /// <param name="follows">False when the theme is fixed (Debug <c>--theme=</c>).</param>
    public ThemeCrossfade(FrameworkElement shell, UIElement content, Image snapshot, DispatcherQueue dispatcher, IErrorSink log, bool follows)
    {
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

        if (ThemeTokens.IsHighContrast)
        {
            _shell.RequestedTheme = ElementTheme.Default;
            return;
        }

        var target = WindowsTheme();
        if (_shell.RequestedTheme == ElementTheme.Default)
        {
            // Back from high contrast: no old look of this kind to fade from.
            _shell.RequestedTheme = target;
        }
        else if (_shell.RequestedTheme != target)
        {
            _ = SwitchAsync(target);
        }
    }

    /// <summary>The theme now, Light or Dark.</summary>
    public ElementTheme Current => _shell.ActualTheme;

    /// <summary>Crossfades to <paramref name="target"/>. Never throws: a failure is logged and the theme switches anyway.</summary>
    public async Task SwitchAsync(ElementTheme target)
    {
        var generation = ++_generation;
        var step = MotionTokens.Plan(MotionCatalogue.ThemeChange);
        var captured = false;
        try
        {
            if (step.Animates && _content is FrameworkElement { ActualWidth: > 0, ActualHeight: > 0 })
            {
                var picture = new RenderTargetBitmap();
                await picture.RenderAsync(_content);
                if (generation != _generation)
                {
                    return;
                }

                _snapshot.Source = picture;
                Visual().StopAnimation(nameof(Microsoft.UI.Composition.Visual.Opacity));
                Visual().Opacity = 1f;
                _snapshot.Visibility = Visibility.Visible;
                captured = true;
            }
        }
        catch (Exception exception)
        {
            _log.Report(exception, ProjectText.MotionFailed);
        }
        finally
        {
            if (generation == _generation)
            {
                _shell.RequestedTheme = target;
            }
        }

        if (captured)
        {
            FadeOut(step, generation);
        }
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
        Visual().Opacity = 1f;
    }

    // Windows' app mode: a dark background color means dark mode.
    private ElementTheme WindowsTheme()
    {
        var background = _settings.GetColorValue(UIColorType.Background);
        return background.R + background.G + background.B < 384 ? ElementTheme.Dark : ElementTheme.Light;
    }
}
