using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TenLapse.Presentation.Look;

namespace TenLapse.Shell;

/// <summary>The Look section of the inspector (#45; see the comment at the top of LookPanel.xaml). The shell hosts it in its Editing state.</summary>
public sealed partial class LookPanel : UserControl
{
    // Cancel had focus as the measuring stopped and Measure is about to show: it takes focus then.
    private bool _focusMeasure;

    public LookPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>The panel's view model, for x:Bind. Set once, before the panel is loaded.</summary>
    public LookPanelViewModel? ViewModel { get; set; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    // Cancel goes with the progress (pressed, or the measuring ended): keyboard focus moves on to Measure when the photos
    // left show beside it, else to the switch, rather than falling back to the window. Measure goes as the measuring
    // starts: focus moves to its Cancel (or the switch).
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(LookPanelViewModel.IsMeasuring) when !viewModel.IsMeasuring && CancelMeasuringButton.FocusState != FocusState.Unfocused:
                // The view model raises HasUnmeasured next, which shows Measure; until then it is collapsed.
                if (viewModel.HasUnmeasured)
                {
                    _focusMeasure = true;
                }
                else
                {
                    DeflickerSwitch.Focus(FocusState.Programmatic);
                }

                break;
            case nameof(LookPanelViewModel.HasUnmeasured) when viewModel.HasUnmeasured && _focusMeasure:
                _focusMeasure = false;
                MeasureButton.Focus(FocusState.Programmatic);
                break;
            case nameof(LookPanelViewModel.HasUnmeasured) when !viewModel.HasUnmeasured && MeasureButton.FocusState != FocusState.Unfocused:
                _ = viewModel.IsMeasuring ? CancelMeasuringButton.Focus(FocusState.Programmatic) : DeflickerSwitch.Focus(FocusState.Programmatic);
                break;
        }
    }
}
