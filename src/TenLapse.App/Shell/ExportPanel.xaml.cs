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

    // The check and title animation, kept so the next finished state starts undrawn (a stopped storyboard lets go of the
    // values it held; a finished one holds them over the local values).
    private Storyboard? _arrival;

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
    /// with <see cref="RatingPrompt.IsShown"/>.
    /// </summary>
    internal void AttachRating(RatingPrompt rating)
    {
        ArgumentNullException.ThrowIfNull(rating);
        _rating = rating;
        RatingYesButton.Command = rating.YesCommand;
        RatingNoButton.Command = rating.NoCommand;
        rating.PropertyChanged += OnRatingPropertyChanged;
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

        Gate.Detach();
    }

    /// <summary>The running part is on screen: focus goes to its title, "Exporting Harbor sunset.mp4", which Enter cannot act on.</summary>
    internal bool FocusRunning() => RunningStop.Focus(FocusState.Programmatic);

    /// <summary>The finished part is coming: focus goes to Play the video.</summary>
    internal bool FocusFinished() => PlayButton.Focus(FocusState.Programmatic);

    /// <summary>
    /// The finished part enters: the check draws in (500 ms Enter after 200 ms; at once with animations off) and the title
    /// rises 6 px.
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

        // The rating question's card rises in once the check has drawn; with animations off it fades in short at once.
        if (RatingCard.Visibility == Visibility.Visible)
        {
            ArriveRatingCard(_arrival);
        }

        if (_arrival.Children.Count > 0)
        {
            _arrival.Begin();
        }
    }

    // Unseen until its delay has passed, then it rises 6 px and fades in, as the title does.
    private void ArriveRatingCard(Storyboard arrival)
    {
        var card = MotionTokens.Plan(MotionCatalogue.FinishedRating);
        var end = card.Delay + card.Duration;
        if (card.Moves)
        {
            var shift = new TranslateTransform { Y = MotionSpec.TitleRise };
            RatingCard.RenderTransform = shift;
            var rise = new DoubleAnimationUsingKeyFrames();
            rise.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(card.Delay), Value = MotionSpec.TitleRise });
            rise.KeyFrames.Add(MotionTokens.KeyFrame(0, end, card.Easing));
            Storyboard.SetTarget(rise, shift);
            Storyboard.SetTargetProperty(rise, nameof(TranslateTransform.Y));
            arrival.Children.Add(rise);
        }

        if (card.Animates)
        {
            var fade = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 0 });
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(card.Delay), Value = 0 });
            fade.KeyFrames.Add(MotionTokens.KeyFrame(1, end, card.Easing));
            Storyboard.SetTarget(fade, RatingCard);
            Storyboard.SetTargetProperty(fade, nameof(UIElement.Opacity));
            arrival.Children.Add(fade);
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
    }

    // The card shows with the question (its heading announced) and goes with an answer or with the finished state. An
    // answer collapses the button that has the focus, which would drop it to the window's first control: it goes to
    // Back to editing, the stop before the card.
    private void OnRatingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RatingPrompt.IsShown) || _rating is not { } rating || _log is not { } log)
        {
            return;
        }

        log.RunLogged(
            () =>
            {
                if (rating.IsShown)
                {
                    RatingCard.Visibility = Visibility.Visible;
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
