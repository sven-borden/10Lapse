using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;
using TenLapse.Core.Projects;
using TenLapse.Presentation;
using TenLapse.Presentation.Preview;
using Windows.Foundation;
using Windows.System;

namespace TenLapse.Preview;

/// <summary>
/// Crop and pan's framing over the preview (#46; see the comment at the top of FramingOverlay.xaml). It places itself over
/// the gate, as <see cref="PreviewLayout.Gate"/> puts it, and turns drags and keys into the view model's edits, in shares
/// of the frame (<see cref="FramingDrag"/>); the view model writes them to the project.
/// </summary>
public sealed partial class FramingOverlay : UserControl
{
    private const double HandleSize = 12;

    private CropViewModel? _crop;
    private Rect _gate;

    // The drag running: the framing and the pointer where it began, and the corner it resizes by (null moves).
    private Framing _dragStart;
    private Point _pointerStart;
    private FramingCorner? _corner;
    private bool _dragging;

    public FramingOverlay()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Place();
        Box.PointerPressed += (element, e) => BeginDrag((UIElement)element, e, null);
        Box.KeyDown += OnBoxKeyDown;
        Hook(TopLeftHandle, FramingCorner.TopLeft);
        Hook(TopRightHandle, FramingCorner.TopRight);
        Hook(BottomLeftHandle, FramingCorner.BottomLeft);
        Hook(BottomRightHandle, FramingCorner.BottomRight);
        foreach (var element in new UIElement[] { Box, TopLeftHandle, TopRightHandle, BottomLeftHandle, BottomRightHandle })
        {
            element.PointerMoved += OnPointerMoved;
            element.PointerReleased += (sender, e) => EndDrag((UIElement)sender, e);
            element.PointerCaptureLost += (_, _) => _dragging = false;
        }
    }

    /// <summary>Follows <paramref name="crop"/> from now on. Set once.</summary>
    internal void Attach(CropViewModel crop)
    {
        _crop = crop;
        crop.EditingChanged += (_, _) => Place();
        Place();
    }

    private void Hook(Rectangle handle, FramingCorner corner) =>
        handle.PointerPressed += (element, e) => BeginDrag((UIElement)element, e, corner);

    // Shows the framing over the gate while the view model edits one; collapsed otherwise.
    private void Place()
    {
        if (_crop is not { IsEditing: true } crop)
        {
            Visibility = Visibility.Collapsed;
            _dragging = false;
            return;
        }

        var gate = PreviewLayout.Gate(ActualWidth, ActualHeight, crop.VideoSize);
        if (gate.IsEmpty)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;
        _gate = new Rect(gate.X, gate.Y, gate.Width, gate.Height);
        var framing = crop.EditedFraming;
        var box = new Rect(_gate.X + (framing.Left * _gate.Width), _gate.Y + (framing.Top * _gate.Height), framing.Size * _gate.Width, framing.Size * _gate.Height);
        GateGeometry.Rect = _gate;
        FramingGeometry.Rect = box;
        Canvas.SetLeft(Box, box.X);
        Canvas.SetTop(Box, box.Y);
        Box.Width = box.Width;
        Box.Height = box.Height;
        AutomationProperties.SetName(Box, crop.FramingName);
        PlaceHandle(TopLeftHandle, box.Left, box.Top);
        PlaceHandle(TopRightHandle, box.Right, box.Top);
        PlaceHandle(BottomLeftHandle, box.Left, box.Bottom);
        PlaceHandle(BottomRightHandle, box.Right, box.Bottom);
    }

    private static void PlaceHandle(Rectangle handle, double x, double y)
    {
        Canvas.SetLeft(handle, x - (HandleSize / 2));
        Canvas.SetTop(handle, y - (HandleSize / 2));
    }

    private void BeginDrag(UIElement element, PointerRoutedEventArgs e, FramingCorner? corner)
    {
        if (_crop is not { IsEditing: true } crop)
        {
            return;
        }

        _dragStart = crop.EditedFraming;
        _pointerStart = e.GetCurrentPoint(Surface).Position;
        _corner = corner;
        _dragging = element.CapturePointer(e.Pointer);
        Box.Focus(FocusState.Pointer);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || _crop is not { } crop || _gate.Width <= 0 || _gate.Height <= 0)
        {
            return;
        }

        var at = e.GetCurrentPoint(Surface).Position;
        crop.SetEditedFraming(_corner is { } corner
            ? FramingDrag.Resized(_dragStart, corner, (at.X - _gate.X) / _gate.Width, (at.Y - _gate.Y) / _gate.Height)
            : FramingDrag.Moved(_dragStart, (at.X - _pointerStart.X) / _gate.Width, (at.Y - _pointerStart.Y) / _gate.Height));
        e.Handled = true;
    }

    private void EndDrag(UIElement element, PointerRoutedEventArgs e)
    {
        if (_dragging)
        {
            element.ReleasePointerCapture(e.Pointer);
        }

        _dragging = false;
    }

    // The arrows move the framing (Shift further), plus and minus zoom it; handled, so the transport's arrows (step a
    // frame) do not run as well.
    private void OnBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_crop is not { IsEditing: true } crop)
        {
            return;
        }

        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var step = shift ? CropViewModel.LargeNudgeStep : CropViewModel.NudgeStep;
        switch (e.Key)
        {
            case VirtualKey.Left:
                crop.Nudge(-step, 0);
                break;
            case VirtualKey.Right:
                crop.Nudge(step, 0);
                break;
            case VirtualKey.Up:
                crop.Nudge(0, -step);
                break;
            case VirtualKey.Down:
                crop.Nudge(0, step);
                break;
            case VirtualKey.Add or (VirtualKey)187:
                crop.ZoomBy(CropViewModel.ZoomStep);
                break;
            case VirtualKey.Subtract or (VirtualKey)189:
                crop.ZoomBy(1 / CropViewModel.ZoomStep);
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
