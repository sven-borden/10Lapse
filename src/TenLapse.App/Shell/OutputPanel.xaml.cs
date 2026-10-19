using Microsoft.UI.Xaml.Controls;
using TenLapse.Presentation;

namespace TenLapse.Shell;

/// <summary>The Output section of the inspector (see the comment at the top of OutputPanel.xaml). The shell hosts it in its Editing state.</summary>
public sealed partial class OutputPanel : UserControl
{
    public OutputPanel() => InitializeComponent();

    /// <summary>The panel's view model, for x:Bind. Set once, before the panel is loaded.</summary>
    public OutputPanelViewModel? ViewModel { get; set; }
}
