using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TenLapse.Presentation.Look;

namespace TenLapse.Shell;

/// <summary>The Look section of the inspector (#45; see the comment at the top of LookPanel.xaml). The shell hosts it in its Editing state.</summary>
public sealed partial class LookPanel : UserControl
{
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

    // Cancel goes with the progress (pressed, or the measuring ended): keyboard focus moves on to the switch rather than
    // falling back to the window.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LookPanelViewModel.IsMeasuring)
            && ViewModel is { IsMeasuring: false }
            && CancelMeasuringButton.FocusState != FocusState.Unfocused)
        {
            DeflickerSwitch.Focus(FocusState.Programmatic);
        }
    }
}
