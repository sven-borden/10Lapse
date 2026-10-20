using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TenLapse.Presentation;
using Windows.Foundation;

namespace TenLapse.Shell;

/// <summary>
/// A row of buttons that wraps (#114): its children sit side by side, left to right, <see cref="Spacing"/> apart, and a
/// child that would pass the row's width starts a new line under them. A translation that makes two buttons too wide
/// for a 280 px panel (German, French) puts them one under the other instead of cutting them off. Collapsed children
/// take no place. The rules are <see cref="WrapLayout"/>'s: arrange keeps the lines measure broke while they still fit,
/// so a row measured at an unbounded width (an Auto column) and arranged a hair narrower does not move a child to a
/// line measure never reserved.
/// </summary>
public sealed partial class WrapRow : Panel
{
    /// <summary>The <see cref="Spacing"/> property.</summary>
    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(WrapRow), new PropertyMetadata(0d, OnSpacingChanged));

    private WrapResult? _measured;

    /// <summary>The gap between two children, across and down.</summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        }

        _measured = WrapLayout.Lay(Sizes(Visible()), Spacing, availableSize.Width);
        return new Size(_measured.Width, _measured.Height);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var visible = Visible();
        var layout = WrapLayout.ForArrange(_measured, Sizes(visible), Spacing, finalSize.Width);
        for (var i = 0; i < visible.Count; i++)
        {
            var (width, height) = layout.Sizes[i];
            var (left, top) = layout.Positions[i];
            visible[i].Arrange(new Rect(left, top, Math.Min(width, Math.Max(0, finalSize.Width)), height));
        }

        return finalSize;
    }

    private List<UIElement> Visible() => [.. Children.Where(child => child.Visibility != Visibility.Collapsed)];

    private static List<(double Width, double Height)> Sizes(List<UIElement> children) =>
        [.. children.Select(child => (child.DesiredSize.Width, child.DesiredSize.Height))];

    private static void OnSpacingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((WrapRow)sender).InvalidateMeasure();
}
