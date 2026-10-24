using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TenLapse.Core.Projects;
using TenLapse.Presentation.Preview;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation;

/// <summary>
/// Crop and pan (#46), in the Frame section: the switch, what the preview shows (the video, or the whole frame with the
/// start or end framing over it to adjust), the zoom and Reset of the framing adjusted, and how the move eases. Every
/// change is one write to <see cref="Project.Crop"/>; the preview and the export take the framing from the next snapshot.
/// The framing drawn over the preview edits through <see cref="SetEditedFraming"/>, <see cref="Nudge"/> and
/// <see cref="ZoomBy"/>. UI thread.
/// </summary>
/// <remarks>
/// What the preview shows is this view model's alone, never saved and never exported, like the Look panel's Before and
/// After: choosing Start pauses the player on the video's first frame and End on its last, so each framing is set on the
/// photo it frames; playing goes back to Video, so Play always shows the move; turning crop and pan off goes back to
/// Video too. The framings stay in the project while it is off.
/// </remarks>
public sealed class CropViewModel : ObservableObject, IDisposable
{
    /// <summary>What an arrow key moves the framing by, as a share of the frame.</summary>
    public const double NudgeStep = 0.01;

    /// <summary>What Shift with an arrow key moves it by.</summary>
    public const double LargeNudgeStep = 0.1;

    /// <summary>What plus or minus zooms it by, as a factor.</summary>
    public const double ZoomStep = 1.1;

    // The parts of the Video, Start and End switch.
    private const int ShowsVideo = 0;
    private const int ShowsStart = 1;
    private const int ShowsEnd = 2;

    private static readonly string[] _showLabels = [CropText.ShowVideo, CropText.ShowStart, CropText.ShowEnd];

    private static readonly CropEasing[] _easings = [CropEasing.Smooth, CropEasing.Linear];

    private static readonly string[] _easingLabels = [CropText.EasingSmooth, CropText.EasingLinear];

    private readonly ShellViewModel _shell;
    private readonly PlayerViewModel? _player;
    private readonly IErrorPresenter _errors;
    private Project _project;
    private CropSettings _crop = CropSettings.Default;
    private int _showIndex;
    private bool _disposed;

    /// <param name="shell">The shell whose current project crop and pan edits.</param>
    /// <param name="player">The preview's player, which Start and End pause and move to the first and last frame; null for none.</param>
    /// <param name="errors">The error path: a failed edit is shown and logged.</param>
    public CropViewModel(ShellViewModel shell, PlayerViewModel? player, IErrorPresenter errors)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(errors);
        _shell = shell;
        _player = player;
        _errors = errors;
        ResetCommand = errors.Command(Reset, CropText.ChangeFailed);
        _project = shell.CurrentProject.Model;
        _project.Changed += OnProjectChanged;
        _shell.ProjectReplaced += OnProjectReplaced;
        if (_player is not null)
        {
            _player.PropertyChanged += OnPlayerPropertyChanged;
        }

        Refresh();
    }

    /// <summary>
    /// Raised when what the framing over the preview shows changed: whether it shows (<see cref="IsEditing"/>), which
    /// framing, the framing itself, or the video's shape it is drawn in.
    /// </summary>
    public event EventHandler? EditingChanged;

    /// <summary>Crop and pan on or off. Setting it edits the project.</summary>
    public bool IsOn
    {
        get => _crop.Enabled;
        set
        {
            if (value != _crop.Enabled)
            {
                Edit(crop => crop with { Enabled = value });
            }
        }
    }

    /// <summary>Video, Start, End: the parts of the switch that picks what the preview shows.</summary>
    public IReadOnlyList<string> ShowLabels => _showLabels;

    /// <summary>0 for the video, 1 to adjust the start framing, 2 the end framing. Two-way with the switch; the preview's alone.</summary>
    public int ShowIndex
    {
        get => _showIndex;
        set
        {
            if (value is >= ShowsVideo and <= ShowsEnd && value != _showIndex)
            {
                Show(value);
                if (value != ShowsVideo && _player is { } player)
                {
                    // Each framing is set on the photo it frames.
                    player.Pause();
                    player.SeekPosition(value == ShowsStart ? 0 : player.LastPosition);
                }
            }
        }
    }

    /// <summary>True while the preview shows the whole frame with a framing over it to adjust: on, and Start or End chosen.</summary>
    public bool IsEditing => _crop.Enabled && _showIndex != ShowsVideo;

    /// <summary>True while the framing adjusted is the start framing (false for the end one).</summary>
    public bool EditsStart => _showIndex == ShowsStart;

    /// <summary>The framing adjusted: the start or the end framing; the whole frame while <see cref="IsEditing"/> is false.</summary>
    public Framing EditedFraming => !IsEditing ? Framing.Whole : EditsStart ? _crop.Start : _crop.End;

    /// <summary>The framing's screen reader name: "Start framing" or "End framing".</summary>
    public string FramingName => EditsStart ? CropText.FramingStartName : CropText.FramingEndName;

    /// <summary>The video's width and height, the shape the framing is drawn in.</summary>
    public PixelSize VideoSize => _project.Snapshot.VideoSize;

    /// <summary>The framing adjusted's zoom, 1 to 4, as the slider's value. Setting it zooms about the framing's centre.</summary>
    public double Zoom
    {
        get => EditedFraming.Zoom;
        set
        {
            if (IsEditing && double.IsFinite(value) && value > 0 && Math.Abs(value - Zoom) > 1e-9)
            {
                var edited = EditedFraming;
                SetEditedFraming(Framing.Clamped(1 / value, edited.CenterX, edited.CenterY));
            }
        }
    }

    /// <summary>The smallest zoom, the slider's minimum.</summary>
    public double MinZoom => 1;

    /// <summary>The largest zoom, the slider's maximum.</summary>
    public double MaxZoom => 1 / Framing.MinSize;

    /// <summary>The zoom as text, "1.5×".</summary>
    public string ZoomText => CropText.Zoom(Zoom);

    /// <summary>The zoom slider's screen reader name, for the framing adjusted.</summary>
    public string ZoomName => EditsStart ? CropText.ZoomStartName : CropText.ZoomEndName;

    /// <summary>Gives the framing adjusted the whole frame again.</summary>
    public IRelayCommand ResetCommand { get; }

    /// <summary>Smooth, Linear.</summary>
    public IReadOnlyList<string> EasingLabels => _easingLabels;

    /// <summary>The project's easing. Setting it edits the project.</summary>
    public int EasingIndex
    {
        get => Array.IndexOf(_easings, _crop.Easing);
        set
        {
            if (value >= 0 && value < _easings.Length && _easings[value] != _crop.Easing)
            {
                Edit(crop => crop with { Easing = _easings[value] });
            }
        }
    }

    /// <summary>Puts <paramref name="framing"/> in place of the framing adjusted (a drag on the preview). Nothing unless <see cref="IsEditing"/>.</summary>
    public void SetEditedFraming(Framing framing)
    {
        if (IsEditing && framing != EditedFraming)
        {
            var start = EditsStart;
            Edit(crop => start ? crop with { Start = framing } : crop with { End = framing });
        }
    }

    /// <summary>Moves the framing adjusted by <paramref name="dx"/> and <paramref name="dy"/> shares of the frame (the arrow keys).</summary>
    public void Nudge(double dx, double dy) => SetEditedFraming(FramingDrag.Moved(EditedFraming, dx, dy));

    /// <summary>Zooms the framing adjusted by <paramref name="factor"/> about its centre (plus and minus).</summary>
    public void ZoomBy(double factor) => SetEditedFraming(FramingDrag.Zoomed(EditedFraming, factor));

    /// <summary>Stops following the project and the player.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _project.Changed -= OnProjectChanged;
        _shell.ProjectReplaced -= OnProjectReplaced;
        if (_player is not null)
        {
            _player.PropertyChanged -= OnPlayerPropertyChanged;
        }
    }

    private void Reset() => SetEditedFraming(Framing.Whole);

    private void Show(int index)
    {
        _showIndex = index;
        OnPropertyChanged(nameof(ShowIndex));
        RaiseEditing();
    }

    // Play shows the move: back to the video.
    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.IsPlaying) && _player is { IsPlaying: true } && _showIndex != ShowsVideo)
        {
            Show(ShowsVideo);
        }
    }

    private void OnProjectChanged(object? sender, ProjectChangedEventArgs e)
    {
        if ((e.Changes & ProjectChanges.Crop) != 0)
        {
            Refresh();
        }
        else if ((e.Changes & (ProjectChanges.Frames | ProjectChanges.FrameSettings | ProjectChanges.Output)) != 0)
        {
            // The video's shape, which the framing is drawn in.
            OnPropertyChanged(nameof(VideoSize));
            EditingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnProjectReplaced(object? sender, ProjectReplacedEventArgs e)
    {
        _project.Changed -= OnProjectChanged;
        _project = e.Current.Model;
        _project.Changed += OnProjectChanged;
        _showIndex = ShowsVideo;
        OnPropertyChanged(nameof(ShowIndex));
        Refresh();
    }

    // A closed project (the window is going away) takes no edits. A control that showed a value the project did not take
    // (the write failed) goes back to the project's.
    private void Edit(Func<CropSettings, CropSettings> change)
    {
        _errors.Guard(
            () =>
            {
                if (!_project.IsClosed)
                {
                    _project.Crop = change(_project.Crop);
                }
            },
            CropText.ChangeFailed);
        Refresh();
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(EasingIndex));
        OnPropertyChanged(nameof(Zoom));
    }

    private void Refresh() =>
        _errors.Guard(
            () =>
            {
                var crop = _project.Snapshot.Crop;
                if (crop == _crop)
                {
                    return;
                }

                var wasOn = _crop.Enabled;
                _crop = crop;
                if (wasOn && !crop.Enabled)
                {
                    // Off: the next time it is on, the preview starts on the video.
                    _showIndex = ShowsVideo;
                    OnPropertyChanged(nameof(ShowIndex));
                }

                OnPropertyChanged(nameof(IsOn));
                OnPropertyChanged(nameof(EasingIndex));
                RaiseEditing();
            },
            CropText.RefreshFailed);

    private void RaiseEditing()
    {
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(EditsStart));
        OnPropertyChanged(nameof(EditedFraming));
        OnPropertyChanged(nameof(FramingName));
        OnPropertyChanged(nameof(Zoom));
        OnPropertyChanged(nameof(ZoomText));
        OnPropertyChanged(nameof(ZoomName));
        EditingChanged?.Invoke(this, EventArgs.Empty);
    }
}
