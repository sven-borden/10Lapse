using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TenLapse.Presentation.Music;

namespace TenLapse.Shell;

/// <summary>The Music section of the inspector (#50; see the comment at the top of MusicPanel.xaml). The shell hosts it in its Editing state.</summary>
public sealed partial class MusicPanel : UserControl
{
    public MusicPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>The section's view model, for x:Bind. Set once, before the panel is loaded.</summary>
    public MusicPanelViewModel? ViewModel { get; set; }

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

    // The button pressed goes with its row: Add music becomes the song's row, Remove the empty one. Keyboard focus moves on to
    // the button that takes its place (Replace, or Add music) rather than falling back to the window.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MusicPanelViewModel.HasMusic) || ViewModel is not { } viewModel)
        {
            return;
        }

        if (viewModel.HasMusic && AddButton.FocusState != FocusState.Unfocused)
        {
            ReplaceButton.Focus(FocusState.Programmatic);
        }
        else if (!viewModel.HasMusic && (RemoveButton.FocusState != FocusState.Unfocused || ReplaceButton.FocusState != FocusState.Unfocused))
        {
            AddButton.Focus(FocusState.Programmatic);
        }
    }
}
