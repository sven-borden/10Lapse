using System.ComponentModel;
using Microsoft.UI.Xaml;
using TenLapse.Presentation;

namespace TenLapse;

/// <summary>The single application window.</summary>
public sealed partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        InitializeComponent();

        // Window is not a DependencyObject, so its Title cannot take a binding: follow the view model by hand.
        Title = ViewModel.Title;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    /// <summary>The window's view model, for x:Bind as the shell grows (#28).</summary>
    public ShellViewModel ViewModel { get; }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.Title))
        {
            Title = ViewModel.Title;
        }
    }
}
