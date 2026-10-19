using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TenLapse.Presentation;
using TenLapse.Presentation.Preview;
using TenLapse.Presentation.Services;

namespace TenLapse.Preview;

/// <summary>
/// The transport row (#19): play button, scrubber, frame counter and time code, bound to the <see cref="PlayerViewModel"/>.
/// While playing it moves the counter on every UI frame (<see cref="PlayerViewModel.Tick"/>); the picture itself is the
/// render thread's (<see cref="PreviewView"/>).
/// </summary>
public sealed partial class TransportBar : UserControl
{
    private IErrorPresenter? _errors;
    private bool _ticking;

    public TransportBar()
    {
        InitializeComponent();
        Unloaded += (_, _) => StopTicking();
        Loaded += (_, _) => FollowPlaying();
    }

    /// <summary>The player, for x:Bind; null until <see cref="Attach"/>.</summary>
    public PlayerViewModel? Player { get; private set; }

    /// <summary>Binds to <paramref name="player"/>.</summary>
    internal void Attach(PlayerViewModel player, IErrorPresenter errors)
    {
        Detach();
        Player = player;
        _errors = errors;
        player.PropertyChanged += OnPlayerPropertyChanged;
        ScrubberBar.Attach(player, errors);
        Bindings.Update();
        FollowPlaying();
    }

    /// <summary>Puts the keyboard focus on Play (#38: after the first photos land, or after an export with no Export button).</summary>
    internal bool FocusPlay() => PlayButton.Focus(FocusState.Programmatic);

    /// <summary>Lets go of the player.</summary>
    internal void Detach()
    {
        StopTicking();
        ScrubberBar.Detach();
        if (Player is not null)
        {
            Player.PropertyChanged -= OnPlayerPropertyChanged;
        }

        Player = null;
    }

    /// <summary>The play icon shows while paused.</summary>
    public static Visibility PlayIconVisibility(bool playing) => playing ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>The pause icon shows while playing.</summary>
    public static Visibility PauseIconVisibility(bool playing) => playing ? Visibility.Visible : Visibility.Collapsed;

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.IsPlaying))
        {
            FollowPlaying();
        }
    }

    private void FollowPlaying()
    {
        if (Player is { IsPlaying: true } && IsLoaded)
        {
            if (!_ticking)
            {
                _ticking = true;
                CompositionTarget.Rendering += OnRendering;
            }
        }
        else
        {
            StopTicking();
        }
    }

    private void StopTicking()
    {
        if (_ticking)
        {
            _ticking = false;
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private void OnRendering(object? sender, object e)
    {
        if (Player is { } player && _errors is { } errors)
        {
            errors.Guard(player.Tick, ProjectText.PreviewTickFailed);
        }
    }
}
