using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using TenLapse.Core.Preview;
using TenLapse.Presentation.Export;
using TenLapse.Presentation.Motion;
using TenLapse.Presentation.Rating;
using TenLapse.Presentation.Services;
using TenLapse.Themes;
using Windows.Graphics.Imaging;

namespace TenLapse.Shell;

/// <summary>
/// The export panel and the finished state (see the comment at the top of ExportPanel.xaml). The shell swaps it in and
/// out; this view keeps the film gate moving with the progress, draws the check in, raises what Narrator should hear and
/// puts focus where the user continues.
/// </summary>
public sealed partial class ExportPanel : UserControl
{
    private IErrorSink? _log;
    private RatingPrompt? _rating;
    private Services.WindowExportFeedback? _window;

    // The check and title animation, kept so the next finished state starts undrawn (a stopped storyboard lets go of the
    // values it held; a finished one holds them over the local values).
    private Storyboard? _arrival;

    // The rating card (#139): true from the finished part's entrance until it leaves; the wait for the card's entrance
    // delay; and the card's own rise, kept for the same reason as _arrival.
    private bool _finishedOnScreen;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _ratingWait;
    private Storyboard? _ratingArrival;

    public ExportPanel()
    {
        InitializeComponent();
    }

    /// <summary>The view model, for x:Bind. Set once, before the panel is loaded.</summary>
    public ExportViewModel? ViewModel { get; private set; }

    /// <summary>The running export's part, which the shell swaps in.</summary>
    internal UIElement Running => RunningScroll;

    /// <summary>The finished state's part, which the shell swaps in.</summary>
    internal UIElement Finished => FinishedScroll;

    /// <summary>The narrow layout (#36): the head and the details of each part side by side, or stacked.</summary>
    internal void SetNarrow(bool narrow) =>
        VisualStateManager.GoToState(this, narrow ? "Narrow" : "Wide", useTransitions: false);

    /// <summary>Connects the panel to its view model and to the filmstrip's thumbnails, which the film gate shows.</summary>
    internal void Attach(ExportViewModel viewModel, ThumbnailCache<SoftwareBitmap>? thumbnails, IErrorSink log)
    {
        ViewModel = viewModel;
        _log = log;
        Gate.Attach(thumbnails, log);
        viewModel.Announced += OnAnnounced;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// Connects the rating question's card (#139) to its view model: the buttons' commands, and the card shows and goes
    /// with <see cref="RatingPrompt.IsShown"/>. A due card arrives (<see cref="RatingPrompt.Arrive"/>) only while the
    /// finished part is on screen and <paramref name="window"/> is in front, once its entrance delay has passed; behind or
    /// minimized, it waits for the window to come to the front.
    /// </summary>
    internal void AttachRating(RatingPrompt rating, Services.WindowExportFeedback window)
    {
        ArgumentNullException.ThrowIfNull(rating);
        ArgumentNullException.ThrowIfNull(window);
        _rating = rating;
        _window = window;
        RatingYesButton.Command = rating.YesCommand;
        RatingNoButton.Command = rating.NoCommand;
        rating.PropertyChanged += OnRatingPropertyChanged;
        window.FrontChanged += OnWindowFrontChanged;
        RatingCard.Visibility = rating.IsShown ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Lets go of the view model and stops the gate (the window closes).</summary>
    internal void Detach()
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.Announced -= OnAnnounced;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (_rating is { } rating)
        {
            rating.PropertyChanged -= OnRatingPropertyChanged;
        }

        if (_window is { } window)
        {
            window.FrontChanged -= OnWindowFrontChanged;
        }

        _ratingWait?.Stop();
        Gate.Detach();
    }

    /// <summary>The running part is on screen: focus goes to its title, "Exporting Harbor sunset.mp4", which Enter cannot act on.</summary>
    internal bool FocusRunning() => RunningStop.Focus(FocusState.Programmatic);

    /// <summary>The finished part is coming: focus goes to Play the video.</summary>
    internal bool FocusFinished() => PlayButton.Focus(FocusState.Programmatic);

    /// <summary>
    /// The finished part enters: the check draws in (500 ms Enter after 200 ms; at once with animations off) and the title
    /// rises 6 px. Focus is on Play already (the swap moved it before the entrance). A due rating card starts its wait.
    /// </summary>
    internal void ArriveFinished()
    {
        _arrival?.Stop();
        _arrival = null;
        FinishedHeading.Opacity = 1;
        FinishedHeading.RenderTransform = null;
        _arrival = new Storyboard();

        // The check: drawn at once with animations off.
        var check = MotionTokens.Plan(MotionCatalogue.FinishedCheck);
        if (check.Moves)
        {
            CheckStroke.StrokeDashOffset = CheckDash;
            var draw = new DoubleAnimationUsingKeyFrames { EnableDependentAnimation = true, BeginTime = check.Delay };
            draw.KeyFrames.Add(MotionTokens.KeyFrame(0, check.Duration, check.Easing));
            Storyboard.SetTarget(draw, CheckStroke);
            Storyboard.SetTargetProperty(draw, nameof(Microsoft.UI.Xaml.Shapes.Shape.StrokeDashOffset));
            _arrival.Children.Add(draw);
        }
        else
        {
            CheckStroke.StrokeDashOffset = 0;
        }

        // The title rises 6 px and fades in (the canvas's ent: opacity and movement together); with animations off it only
        // fades in.
        var title = MotionTokens.Plan(MotionCatalogue.FinishedTitle);
        if (title.Moves)
        {
            var shift = new Microsoft.UI.Xaml.Media.TranslateTransform { Y = MotionSpec.TitleRise };
            FinishedHeading.RenderTransform = shift;
            var rise = new DoubleAnimationUsingKeyFrames { BeginTime = title.Delay };
            rise.KeyFrames.Add(MotionTokens.KeyFrame(0, title.Duration, title.Easing));
            Storyboard.SetTarget(rise, shift);
            Storyboard.SetTargetProperty(rise, nameof(Microsoft.UI.Xaml.Media.TranslateTransform.Y));
            _arrival.Children.Add(rise);
        }

        if (title.Animates)
        {
            var fade = new DoubleAnimationUsingKeyFrames { BeginTime = title.Delay, FillBehavior = FillBehavior.Stop };
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 0 });
            fade.KeyFrames.Add(MotionTokens.KeyFrame(1, title.Duration, title.Easing));
            Storyboard.SetTarget(fade, FinishedHeading);
            Storyboard.SetTargetProperty(fade, nameof(UIElement.Opacity));
            _arrival.Children.Add(fade);
        }

        if (_arrival.Children.Count > 0)
        {
            _arrival.Begin();
        }

        _finishedOnScreen = true;
        WaitForRatingCard();
    }

    // A due rating card arrives once the check has drawn (its entrance delay; at once with animations off), while the
    // finished part is on screen and the window is in front. Behind or minimized, nothing starts: the window coming to the
    // front (OnWindowFrontChanged) starts the wait then. Nothing is recorded until the card arrives.
    private void WaitForRatingCard()
    {
        if (!_finishedOnScreen || _rating is not { IsDue: true } || _window is not { InFront: true } || _ratingWait is { IsRunning: true })
        {
            return;
        }

        var delay = MotionTokens.Plan(MotionCatalogue.FinishedRating).Delay;
        if (delay <= TimeSpan.Zero)
        {
            ArriveRating();
            return;
        }

        if (_ratingWait is null)
        {
            _ratingWait = DispatcherQueue.CreateTimer();
            _ratingWait.IsRepeating = false;
            _ratingWait.Tick += OnRatingWaitTick;
        }

        _ratingWait.Interval = delay;
        _ratingWait.Start();
    }

    private void OnRatingWaitTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args) =>
        _log?.RunLogged(
            () =>
            {
                sender.Stop();
                ArriveRating();
            },
            RatingText.PromptFailed);

    // Checked again as the wait ends: the window may have gone behind meanwhile, and then the card waits for it again.
    private void ArriveRating()
    {
        if (_finishedOnScreen && _window is { InFront: true } && _rating is { IsDue: true } rating)
        {
            rating.Arrive();
        }
    }

    private void OnWindowFrontChanged(object? sender, EventArgs e) => _log?.RunLogged(WaitForRatingCard, RatingText.PromptFailed);

    // The card rises 6 px and fades in, as the title does; with animations off it only fades in short.
    private void ArriveRatingCard()
    {
        _ratingArrival?.Stop();
        _ratingArrival = new Storyboard();
        var card = MotionTokens.Plan(MotionCatalogue.FinishedRating);
        if (card.Moves)
        {
            var shift = new TranslateTransform { Y = MotionSpec.TitleRise };
            RatingCard.RenderTransform = shift;
            var rise = new DoubleAnimationUsingKeyFrames();
            rise.KeyFrames.Add(MotionTokens.KeyFrame(0, card.Duration, card.Easing));
            Storyboard.SetTarget(rise, shift);
            Storyboard.SetTargetProperty(rise, nameof(TranslateTransform.Y));
            _ratingArrival.Children.Add(rise);
        }

        if (card.Animates)
        {
            var fade = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 0 });
            fade.KeyFrames.Add(MotionTokens.KeyFrame(1, card.Duration, card.Easing));
            Storyboard.SetTarget(fade, RatingCard);
            Storyboard.SetTargetProperty(fade, nameof(UIElement.Opacity));
            _ratingArrival.Children.Add(fade);
        }

        if (_ratingArrival.Children.Count > 0)
        {
            _ratingArrival.Begin();
        }
    }

    /// <summary>The finished part left: the animation lets go of its end values, and the check is undrawn for next time.</summary>
    internal void ResetFinished()
    {
        _arrival?.Stop();
        _arrival = null;
        CheckStroke.StrokeDashOffset = CheckDash;
        FinishedHeading.RenderTransform = null;
        FinishedHeading.Opacity = 1;
        LeaveFinished();
    }

    // The rating card's wait ends and its rise lets go: a card that has not arrived does not arrive later.
    private void LeaveFinished()
    {
        _finishedOnScreen = false;
        _ratingWait?.Stop();
        _ratingArrival?.Stop();
        _ratingArrival = null;
        RatingCard.RenderTransform = null;
        RatingCard.Opacity = 1;
    }

    // The stroke's dash in units of its thickness (28 px / 3 px), as in ExportPanel.xaml.
    private const double CheckDash = 9.34;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ViewModel is not { } viewModel || _log is not { } log)
        {
            return;
        }

        if (e.PropertyName is nameof(ExportViewModel.GateFraction) or nameof(ExportViewModel.ExportFrames) or nameof(ExportViewModel.FramesText))
        {
            log.RunLogged(() => Gate.Show(viewModel.ExportFrames, viewModel.GateFraction), ExportText.ProgressFailed);
        }

        // The finished state left (a new export does not go through ResetFinished): the rating card's wait ends with it.
        if (e.PropertyName == nameof(ExportViewModel.State) && !viewModel.IsFinished && _finishedOnScreen)
        {
            log.RunLogged(LeaveFinished, RatingText.PromptFailed);
        }
    }

    // A due card waits for its arrival (WaitForRatingCard). It shows as it arrives, rising in, with its heading
    // announced then (after the focus moved to Play, and once the card can be seen), and goes with an answer or with the
    // finished state. An answer collapses the button that has the focus, which would drop it to the window's first
    // control: it goes to Back to editing, the stop before the card.
    private void OnRatingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_rating is not { } rating || _log is not { } log)
        {
            return;
        }

        if (e.PropertyName == nameof(RatingPrompt.IsDue))
        {
            log.RunLogged(
                () =>
                {
                    if (rating.IsDue)
                    {
                        WaitForRatingCard();
                    }
                    else
                    {
                        _ratingWait?.Stop();
                    }
                },
                RatingText.PromptFailed);
            return;
        }

        if (e.PropertyName != nameof(RatingPrompt.IsShown))
        {
            return;
        }

        log.RunLogged(
            () =>
            {
                if (rating.IsShown)
                {
                    RatingCard.Visibility = Visibility.Visible;
                    ArriveRatingCard();
                    Announcer.Announce(this, RatingText.Question, AutomationNotificationProcessing.All, "Rating");
                    return;
                }

                var hadFocus = HasFocusWithin(RatingCard);
                RatingCard.Visibility = Visibility.Collapsed;
                if (hadFocus)
                {
                    BackButton.Focus(FocusState.Programmatic);
                }
            },
            RatingText.PromptFailed);
    }

    private bool HasFocusWithin(UIElement container)
    {
        var focused = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        for (var node = focused; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node == container)
            {
                return true;
            }
        }

        return false;
    }

    // Narrator hears the start, every ten percent at most, and the end (the view model decides when).
    private void OnAnnounced(object? sender, string message) =>
        Announcer.Announce(this, message, AutomationNotificationProcessing.MostRecent, "Export");
}
