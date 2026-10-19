using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.System;
using TenLapse.Themes;

namespace TenLapse.Shell;

/// <summary>
/// A row of equal segments with one selected (see the comment at the top of SegmentedControl.xaml). Set
/// <see cref="Items"/> to the labels and bind <see cref="SelectedIndex"/> two-way.
/// </summary>
public sealed partial class SegmentedControl : UserControl
{
    /// <summary>The labels, as an <c>IReadOnlyList&lt;string&gt;</c> (or any <c>IEnumerable&lt;string&gt;</c>).</summary>
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items),
        typeof(object),
        typeof(SegmentedControl),
        new PropertyMetadata(null, (d, _) => ((SegmentedControl)d).Rebuild()));

    /// <summary>The selected segment, or -1 for none.</summary>
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex),
        typeof(int),
        typeof(SegmentedControl),
        new PropertyMetadata(-1, (d, _) => ((SegmentedControl)d).Sync(animate: true)));

    private readonly List<RadioButton> _buttons = [];
    private readonly string _group = $"Segments{Guid.NewGuid():N}";
    private bool _syncing;
    private Storyboard? _slide;
    private bool _pillPlaced;
    private DropShadow? _shadow;
    private SpriteVisual? _shadowSprite;

    public SegmentedControl()
    {
        InitializeComponent();
        Inner.SizeChanged += (_, _) => Sync(animate: false);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>The segment labels.</summary>
    public object? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    /// <summary>The selected segment, or -1 for none.</summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    private void Rebuild()
    {
        Segments.Children.Clear();
        Segments.ColumnDefinitions.Clear();
        _buttons.Clear();

        var labels = Items is IEnumerable<string> list ? list.ToList() : [];
        for (var i = 0; i < labels.Count; i++)
        {
            Segments.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var text = new TextBlock { Text = labels[i], TextWrapping = TextWrapping.NoWrap };
            Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
            var button = new RadioButton
            {
                Style = (Style)Resources["SegmentStyle"],
                GroupName = _group,
                Tag = i,
                Content = text,
            };
            AutomationProperties.SetName(button, labels[i]);
            button.Checked += OnChecked;
            button.KeyDown += OnKeyDown;
            Grid.SetColumn(button, i);
            Segments.Children.Add(button);
            _buttons.Add(button);
        }

        _pillPlaced = false;
        Sync(animate: false);
    }

    // Shows the selection: the checked segment, which one Tab enters on, and the pill.
    private void Sync(bool animate)
    {
        var selected = SelectedIndex;
        _syncing = true;
        try
        {
            for (var i = 0; i < _buttons.Count; i++)
            {
                _buttons[i].IsChecked = i == selected;
                _buttons[i].IsTabStop = i == (selected >= 0 && selected < _buttons.Count ? selected : 0);
            }
        }
        finally
        {
            _syncing = false;
        }

        var hasPill = selected >= 0 && selected < _buttons.Count && Inner.ActualWidth > 0;
        Pill.Visibility = hasPill ? Visibility.Visible : Visibility.Collapsed;
        if (!hasPill)
        {
            _pillPlaced = false;
            return;
        }

        var width = Inner.ActualWidth / _buttons.Count;
        Pill.Width = width;
        Pill.Height = Inner.ActualHeight;
        MovePill(selected * width,animate && _pillPlaced && IsLoaded && AnimationsEnabled);
        _pillPlaced = true;
    }

    private static bool AnimationsEnabled =>
#if DEBUG
        MotionTokens.AnimationsEnabled && !Debugging.DebugLaunch.ReduceMotion;
#else
        MotionTokens.AnimationsEnabled;
#endif

    private void MovePill(double x, bool animate)
    {
        // Wherever the pill is drawn now, which is part way along while an earlier slide still runs.
        var from = PillShift.X;
        _slide?.Stop();
        _slide = null;

        // The local value is always the end value. The slide only draws the way there and stops holding anything, so
        // a slide that is cut short or never finishes cannot leave the pill in the wrong place.
        PillShift.X = x;
        if (!animate || from == x)
        {
            return;
        }

        // 250 ms Move (DESIGN.md, Segmented control).
        var slide = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        slide.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = from });
        slide.KeyFrames.Add(new SplineDoubleKeyFrame
        {
            KeyTime = MotionTokens.Reveal,
            Value = x,
            KeySpline = new KeySpline
            {
                ControlPoint1 = new Point(MotionTokens.Move.ControlPoint1.X, MotionTokens.Move.ControlPoint1.Y),
                ControlPoint2 = new Point(MotionTokens.Move.ControlPoint2.X, MotionTokens.Move.ControlPoint2.Y),
            },
        });
        Storyboard.SetTarget(slide, PillShift);
        Storyboard.SetTargetProperty(slide, nameof(TranslateTransform.X));
        _slide = new Storyboard();
        _slide.Children.Add(slide);
        _slide.Begin();
    }

    private void OnChecked(object sender, RoutedEventArgs e)
    {
        if (!_syncing && sender is RadioButton { Tag: int index })
        {
            SelectedIndex = index;
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: int index } || _buttons.Count == 0)
        {
            return;
        }

        var rtl = FlowDirection == FlowDirection.RightToLeft;
        int? target = e.Key switch
        {
            VirtualKey.Left => index + (rtl ? 1 : -1),
            VirtualKey.Right => index + (rtl ? -1 : 1),
            VirtualKey.Up => index - 1,
            VirtualKey.Down => index + 1,
            VirtualKey.Home => 0,
            VirtualKey.End => _buttons.Count - 1,
            _ => null,
        };
        if (target is not { } next)
        {
            return;
        }

        // The control moves both, so the platform's own handling of the arrow keys adds nothing.
        e.Handled = true;
        next = Math.Clamp(next, 0, _buttons.Count - 1);
        SelectedIndex = next;
        _buttons[next].Focus(FocusState.Keyboard);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_shadow is not null)
        {
            return;
        }

        // The Pill shadow of DESIGN.md: 0 1px 2px, drawn from the caster rectangle's shape.
        var compositor = ElementCompositionPreview.GetElementVisual(PillShadowCaster).Compositor;
        _shadow = compositor.CreateDropShadow();
        _shadow.Mask = PillShadowCaster.GetAlphaMask();
        _shadow.BlurRadius = (float)ThemeTokens.Number("ElevationPillBlur");
        _shadow.Offset = new Vector3(0, (float)ThemeTokens.Number("ElevationPillOffsetY"), 0);
        _shadow.Color = ThemeTokens.Color("ShadowPillColor", this);
        _shadowSprite = compositor.CreateSpriteVisual();
        _shadowSprite.Shadow = _shadow;
        ElementCompositionPreview.SetElementChildVisual(PillShadowCaster, _shadowSprite);
        PillShadowCaster.SizeChanged += OnCasterSizeChanged;
        ActualThemeChanged += OnActualThemeChanged;
        ResizeShadow();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_shadow is null)
        {
            return;
        }

        PillShadowCaster.SizeChanged -= OnCasterSizeChanged;
        ActualThemeChanged -= OnActualThemeChanged;
        ElementCompositionPreview.SetElementChildVisual(PillShadowCaster, null);
        _shadowSprite?.Dispose();
        _shadow.Mask?.Dispose();
        _shadow.Dispose();
        _shadowSprite = null;
        _shadow = null;
    }

    private void OnCasterSizeChanged(object sender, SizeChangedEventArgs e) => ResizeShadow();

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        if (_shadow is not null)
        {
            _shadow.Color = ThemeTokens.Color("ShadowPillColor", this);
        }
    }

    private void ResizeShadow()
    {
        if (_shadowSprite is not null)
        {
            _shadowSprite.Size = new Vector2((float)PillShadowCaster.ActualWidth, (float)PillShadowCaster.ActualHeight);
        }
    }
}
