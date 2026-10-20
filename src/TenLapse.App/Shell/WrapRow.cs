using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace TenLapse.Shell;

/// <summary>
/// A row of buttons that wraps (#114): its children sit side by side, left to right, <see cref="Spacing"/> apart, and a
/// child that would pass the row's width starts a new line under them. A translation that makes two buttons too wide
/// for a 280 px panel (German, French) puts them one under the other instead of cutting them off. Collapsed children
/// take no place.
/// </summary>
public sealed partial class WrapRow : Panel
{
    /// <summary>The <see cref="Spacing"/> property.</summary>
    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(WrapRow), new PropertyMetadata(0d, OnSpacingChanged));

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

        var (width, height) = Lay(availableSize.Width, arrange: false);
        return new Size(width, height);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        Lay(finalSize.Width, arrange: true);
        return finalSize;
    }

    // Places the children line by line within the width; returns the size they take.
    private (double Width, double Height) Lay(double available, bool arrange)
    {
        double x = 0, y = 0, line = 0, widest = 0;
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            var size = child.DesiredSize;
            if (x > 0 && x + Spacing + size.Width > available)
            {
                widest = Math.Max(widest, x);
                y += line + Spacing;
                x = 0;
                line = 0;
            }

            var left = x > 0 ? x + Spacing : 0;
            if (arrange)
            {
                child.Arrange(new Rect(left, y, Math.Min(size.Width, Math.Max(0, available)), size.Height));
            }

            x = left + size.Width;
            line = Math.Max(line, size.Height);
        }

        return (Math.Max(widest, x), y + line);
    }

    private static void OnSpacingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((WrapRow)sender).InvalidateMeasure();
}
