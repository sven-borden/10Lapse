using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TenLapse.Presentation;
using TenLapse.Presentation.Services;
using TenLapse.Themes;

namespace TenLapse.Shell;

/// <summary>
/// The app shell: title bar, command bar, the Empty and Editing body with its named regions, and the floating
/// InfoBar layer (see the comment at the top of ShellView.xaml). <see cref="MainWindow"/> hosts it and draws the
/// title bar into the window frame.
/// </summary>
public sealed partial class ShellView : UserControl
{
    private readonly InfoBarPresenter _infoBars;
    private readonly FlyoutShadow _infoBarShadow;

    /// <param name="viewModel">The shell view model.</param>
    /// <param name="infoBars">The InfoBars to float over the stage.</param>
    public ShellView(ShellViewModel viewModel, InfoBarQueue infoBars)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(infoBars);
        ViewModel = viewModel;
        InitializeComponent();

        _infoBarShadow = FlyoutShadow.Attach(InfoBarShadowCaster);
        _infoBars = new InfoBarPresenter(infoBars, InfoBarLayer, ShellInfoBar, InfoBarDetails, InfoBarDetailList);
    }

    /// <summary>The shell's view model, for x:Bind.</summary>
    public ShellViewModel ViewModel { get; }

    /// <summary>The title bar strip, which the window makes its drag region.</summary>
    public UIElement TitleBar => AppTitleBar;

    /// <summary>Keeps the title bar content clear of the system caption buttons (DIPs on each side).</summary>
    public void SetTitleBarInsets(double left, double right)
    {
        TitleBarLeftInset.Width = new GridLength(Math.Max(0, left));
        TitleBarRightInset.Width = new GridLength(Math.Max(0, right));
    }

    /// <summary>Paints the Mica token behind the shell where the system has no Mica backdrop.</summary>
    public void ShowMicaFallback() => MicaFallback.Visibility = Visibility.Visible;

    /// <summary>Takes the theme again for what follows it by hand (high contrast switched, say).</summary>
    public void RefreshTheme() => _infoBarShadow.Recolor();

    /// <summary>Stops following the InfoBar queue (the window closed).</summary>
    public void Detach() => _infoBars.Detach();
}
