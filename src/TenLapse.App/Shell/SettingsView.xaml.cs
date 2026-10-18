using Microsoft.UI.Xaml.Controls;
using TenLapse.Presentation;

namespace TenLapse.Shell;

/// <summary>The Settings page (see the comment at the top of SettingsView.xaml). The shell shows it in its Settings state.</summary>
public sealed partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    /// <summary>The page's view model, for x:Bind. Set once, before the page is loaded.</summary>
    public SettingsViewModel? ViewModel { get; set; }
}
