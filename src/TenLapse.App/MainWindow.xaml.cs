using Microsoft.UI.Xaml;
using TenLapse.Core;

namespace TenLapse;

/// <summary>The single application window.</summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = Product.Name;
    }
}
