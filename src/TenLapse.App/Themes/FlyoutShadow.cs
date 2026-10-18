using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Shapes;

namespace TenLapse.Themes;

/// <summary>
/// The Flyout shadow of DESIGN.md (Elevation) for anything that floats over content: InfoBars over the stage
/// (#28), the export panel's overlays (#24), the first-launch sample frame (#29). ThemeShadow throws on Windows
/// App SDK 2.5.1 (docs/STATUS.md, Decisions), so this draws the spec's two layers, ambient then key, as
/// Composition drop shadows from the ShadowFlyout* color tokens and the ElevationFlyout* numbers.
/// </summary>
/// <remarks>
/// Put a <see cref="Shape"/> (a <see cref="Rectangle"/> with the floating element's corner radius and any opaque
/// fill) in the same layout cell right behind the floating element, so it always has its size and shape, and call
/// <see cref="Attach"/> on it. The shadow follows its size and the theme, and goes with it when it leaves the
/// tree. In high contrast the shadow colors are transparent, so nothing is drawn.
/// </remarks>
public sealed class FlyoutShadow
{
    private readonly Shape _caster;
    private readonly ContainerVisual _container;
    private readonly DropShadow _ambient;
    private readonly DropShadow _key;

    private FlyoutShadow(Shape caster)
    {
        _caster = caster;
        var compositor = ElementCompositionPreview.GetElementVisual(caster).Compositor;
        var mask = caster.GetAlphaMask();
        _container = compositor.CreateContainerVisual();
        _ambient = AddLayer(compositor, mask, ThemeTokens.Number("ElevationFlyoutBlur"), ThemeTokens.Number("ElevationFlyoutOffsetY"));
        _key = AddLayer(compositor, mask, ThemeTokens.Number("ElevationFlyoutKeyBlur"), ThemeTokens.Number("ElevationFlyoutKeyOffsetY"));
        ElementCompositionPreview.SetElementChildVisual(caster, _container);

        Resize();
        Recolor();
        caster.SizeChanged += (_, _) => Resize();
        caster.ActualThemeChanged += (_, _) => Recolor();
    }

    /// <summary>Casts the Flyout shadow from <paramref name="caster"/> (see the remarks on <see cref="FlyoutShadow"/>).</summary>
    public static FlyoutShadow Attach(Shape caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new FlyoutShadow(caster);
    }

    /// <summary>Takes the colors again, for a theme change the element does not see (high contrast on or off).</summary>
    public void Recolor()
    {
        _ambient.Color = ThemeTokens.Color("ShadowFlyoutColor", _caster);
        _key.Color = ThemeTokens.Color("ShadowFlyoutKeyColor", _caster);
    }

    private SpriteVisual AddLayerVisual(Compositor compositor, DropShadow shadow)
    {
        var sprite = compositor.CreateSpriteVisual();
        sprite.Shadow = shadow;
        _container.Children.InsertAtTop(sprite);
        return sprite;
    }

    private DropShadow AddLayer(Compositor compositor, CompositionBrush mask, double blur, double offsetY)
    {
        var shadow = compositor.CreateDropShadow();
        shadow.Mask = mask;
        shadow.BlurRadius = (float)blur;
        shadow.Offset = new Vector3(0, (float)offsetY, 0);
        AddLayerVisual(compositor, shadow);
        return shadow;
    }

    private void Resize()
    {
        var size = new Vector2((float)_caster.ActualWidth, (float)_caster.ActualHeight);
        _container.Size = size;
        foreach (var child in _container.Children)
        {
            child.Size = size;
        }
    }
}
