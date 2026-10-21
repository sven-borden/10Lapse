using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TenLapse.Core.Preview;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation.Preview;

/// <summary>
/// What the player shows, for the render thread: the project as one snapshot, the photo and the video frame while paused,
/// and the clock while playing. Immutable; <see cref="PlayerViewModel.State"/> swaps in a new one on every change.
/// </summary>
/// <param name="Snapshot">The project: frames, timing, sequence and frame settings, all from one moment.</param>
/// <param name="Frame">The photo (project frame) on screen while paused (and where playback started).</param>
/// <param name="Position">The video frame on screen while paused: a position in <see cref="Sequence"/> (#49).</param>
/// <param name="Clock">The playback clock while playing, over the video's frames; null while paused.</param>
/// <param name="Version">Grows by one with each new state, so the render thread can tell that something changed.</param>
public sealed record PlayerState(ProjectSnapshot Snapshot, int Frame, int Position, PlaybackClock? Clock, long Version)
{
    /// <summary>The video's frames (#49): which photo each one shows, holds, plays and boomerang included.</summary>
    public FrameSequence Sequence { get; } = Snapshot.Playback;

    /// <summary>True while playing.</summary>
    public bool IsPlaying => Clock is not null;

    /// <summary>The photo (project frame) that belongs on screen at <paramref name="timestamp"/>.</summary>
    public int FrameAt(long timestamp) => Clock is { } clock ? PhotoAt(clock.FrameAt(timestamp)) : Frame;

    /// <summary>The photo video frame <paramref name="position"/> shows (the clock's <see cref="PlaybackReading.Frame"/> is a video frame).</summary>
    public int PhotoAt(int position) =>
        Sequence.Length == 0 ? 0 : Sequence.PhotoAt(Math.Clamp(position, 0, Sequence.Length - 1));

    /// <summary>Which way the photos run at video frame <paramref name="position"/>: 1 forward, -1 on a boomerang's way back.</summary>
    public int DirectionAt(int position) => Sequence.DirectionAt(position);
}

/// <summary>
/// The preview's transport (#19): play and pause at the project's rate, looping; seek, step and the ends; the frame
/// counter and the time code. It plays the video's frames (#49, <see cref="ProjectSnapshot.Playback"/>: holds, plays,
/// boomerang), so the clock, the time code and the scrubber are on the video's timeline (<see cref="Position"/>), while
/// the photo on screen (<see cref="CurrentFrame"/>), the counter, the filmstrip's playhead and the arrow keys stay in
/// photos. Lives on the UI thread and follows the shell's current project through its snapshots,
/// so frames added, removed or reordered and a New project reach the player in one piece. The render thread reads
/// <see cref="State"/> and draws; this view model never waits on it.
/// </summary>
/// <remarks>
/// Every time comes from a <see cref="PlaybackClock"/> and <see cref="FrameTiming"/>: the counter follows the clock on
/// each UI frame (<see cref="Tick"/>), and the render thread asks the same clock, so they agree. A change of the
/// project while playing re-anchors the clock at the moment the frame on screen began, so playback keeps its pace, and
/// the playhead stays on the same photo when frames before it come or go. The counter and time code names Narrator
/// reads follow at most once a second while playing, so playback does not flood it. A minimized or hidden window
/// pauses playback and plays on when it shows again (<see cref="SetWindowShown"/>).
/// </remarks>
public sealed partial class PlayerViewModel : ObservableObject, IDisposable
{
    private readonly ShellViewModel _shell;
    private readonly IErrorPresenter _errors;
    private readonly Func<long> _now;
    private readonly long _frequency;
    private Project _project;
    private PlaybackClock? _clock;
    private PlayerState _state;
    private long _version;
    private bool _disposed;
    private bool _windowShown = true;
    private bool _resumeWhenShown;
    private long _namesUpdatedAt;

    /// <summary>Follows <paramref name="shell"/>'s current project. On the UI thread.</summary>
    /// <param name="shell">The shell, for its project, New project and the Settings page.</param>
    /// <param name="errors">The error path for the commands.</param>
    /// <param name="timestamp">The clock (<see cref="Stopwatch.GetTimestamp"/>); tests pass their own.</param>
    /// <param name="frequency">Its units per second (<see cref="Stopwatch.Frequency"/> when 0).</param>
    public PlayerViewModel(ShellViewModel shell, IErrorPresenter errors, Func<long>? timestamp = null, long frequency = 0)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentOutOfRangeException.ThrowIfNegative(frequency);
        _shell = shell;
        _errors = errors;
        _now = timestamp ?? Stopwatch.GetTimestamp;
        _frequency = frequency > 0 ? frequency : Stopwatch.Frequency;
        _project = shell.CurrentProject.Model;
        _state = new PlayerState(_project.Snapshot, 0, 0, null, 0);
        CounterText = string.Empty;
        TimecodeText = string.Empty;
        CounterName = string.Empty;
        TimecodeName = string.Empty;
        PlayPauseCommand = errors.Command(TogglePlay, ProjectText.PreviewPlayFailed);

        _project.Changed += OnProjectChanged;
        shell.ProjectReplaced += OnProjectReplaced;
        shell.PropertyChanged += OnShellPropertyChanged;
        Load(_project.Snapshot, frame: 0, position: 0);
    }

    /// <summary>A new <see cref="State"/>: the render thread draws again. Raised on the UI thread.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Frames in the project.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastFrame), nameof(HasFrames))]
    public partial int FrameCount { get; private set; }

    /// <summary>The photo (project frame) on screen, 0-based. Follows the clock while playing.</summary>
    [ObservableProperty]
    public partial int CurrentFrame { get; private set; }

    /// <summary>The video frame on screen (#49), 0-based: where the scrubber and the time code are. Follows the clock while playing.</summary>
    [ObservableProperty]
    public partial int Position { get; private set; }

    /// <summary>Frames in the video (#49): the photos with their holds, plays and way back. Equal to <see cref="FrameCount"/> when there are none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastPosition))]
    public partial int PositionCount { get; private set; }

    /// <summary>True while playing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseName), nameof(PlayPauseTip))]
    public partial bool IsPlaying { get; private set; }

    /// <summary>"0312 / 0742".</summary>
    [ObservableProperty]
    public partial string CounterText { get; private set; }

    /// <summary>"00:10.40 / 00:24.73".</summary>
    [ObservableProperty]
    public partial string TimecodeText { get; private set; }

    /// <summary>"Frame 312 of 742" for Narrator; while playing it follows at most once a second.</summary>
    [ObservableProperty]
    public partial string CounterName { get; private set; }

    /// <summary>"Time 00:10.40 of 00:24.73" for Narrator; while playing it follows at most once a second.</summary>
    [ObservableProperty]
    public partial string TimecodeName { get; private set; }

    /// <summary>True when there is something to play.</summary>
    public bool HasFrames => FrameCount > 0;

    /// <summary>The last photo's index (0 without frames).</summary>
    public int LastFrame => Math.Max(0, FrameCount - 1);

    /// <summary>The video's last frame, the scrubber's maximum (0 without frames).</summary>
    public int LastPosition => Math.Max(0, PositionCount - 1);

    /// <summary>The play button's name: "Play" or "Pause".</summary>
    public string PlayPauseName => IsPlaying ? ProjectText.PreviewPause : ProjectText.PreviewPlay;

    /// <summary>The play button's tooltip, with its key: "Play (Space)" or "Pause (Space)" (#38).</summary>
    public string PlayPauseTip => Keyboard.KeyText.Tip(PlayPauseName, Keyboard.KeyText.PlayKeys);

    /// <summary>The play button.</summary>
    public IRelayCommand PlayPauseCommand { get; }

    /// <summary>What the render thread draws. Safe on any thread.</summary>
    public PlayerState State => Volatile.Read(ref _state);

    /// <summary>The playback clock while playing; null while paused.</summary>
    public PlaybackClock? Clock => _clock;

    /// <summary>Plays from the frame on screen, looping, at the project's effective rate.</summary>
    public void Play()
    {
        if (_disposed || IsPlaying || FrameCount == 0 || _shell.IsSettingsOpen || !_windowShown)
        {
            return;
        }

        _clock = NewClock(State, Position, _now());
        IsPlaying = true;
        UpdateTexts();
        Publish();
    }

    /// <summary>Stops on the frame the clock has reached.</summary>
    public void Pause()
    {
        _resumeWhenShown = false;
        if (!IsPlaying)
        {
            return;
        }

        var position = _clock!.FrameAt(_now());
        _clock = null;
        IsPlaying = false;
        Show(position);
        UpdateTexts();
        Publish();
    }

    /// <summary>Play or pause (Space, the play button).</summary>
    public void TogglePlay()
    {
        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    /// <summary>
    /// Shows photo <paramref name="frame"/> (clamped to the project): its showing nearest to the video frame on screen (#49:
    /// a held, repeated or boomeranged photo shows more than once), so a click in the strip stays in the same play.
    /// Playback goes on from there. Nothing moves when that photo is already on screen and the player is paused.
    /// </summary>
    public void Seek(int frame)
    {
        if (_disposed || FrameCount == 0)
        {
            return;
        }

        frame = Math.Clamp(frame, 0, FrameCount - 1);
        if (!IsPlaying && frame == CurrentFrame)
        {
            return;
        }

        SeekPosition(State.Sequence.NearestPositionOf(frame, IsPlaying ? _clock!.FrameAt(_now()) : Position));
    }

    /// <summary>Shows video frame <paramref name="position"/> (clamped to the video): the scrubber's seek. Playback goes on from there.</summary>
    public void SeekPosition(int position)
    {
        if (_disposed || FrameCount == 0)
        {
            return;
        }

        position = Math.Clamp(position, 0, Math.Max(0, PositionCount - 1));
        if (IsPlaying)
        {
            _clock = NewClock(State, position, _now());
        }
        else if (position == Position)
        {
            return;
        }

        Show(position);
        UpdateTexts();
        Publish();
    }

    /// <summary>False while the window is minimized or hidden (see <see cref="SetWindowShown"/>).</summary>
    public bool IsWindowShown => _windowShown;

    /// <summary>
    /// The window was minimized or hidden (false), or shows again (true). Hiding pauses playback, as the first-launch
    /// timelapse stops in the background, so nothing decodes and draws unseen; showing plays on when hiding paused it.
    /// </summary>
    public void SetWindowShown(bool shown)
    {
        if (_disposed || shown == _windowShown)
        {
            return;
        }

        if (!shown)
        {
            var playing = IsPlaying;
            Pause();
            _resumeWhenShown = playing;
            _windowShown = false;
            return;
        }

        _windowShown = true;
        if (_resumeWhenShown)
        {
            _resumeWhenShown = false;
            Play();
        }
    }

    /// <summary>Pauses and moves <paramref name="frames"/> photos (negative is back), stopping at the first and last photo.</summary>
    public void Step(int frames)
    {
        Pause();
        Seek(CurrentFrame + frames);
    }

    /// <summary>Does what a transport key asks; false when the action is <see cref="TransportAction.None"/>.</summary>
    public bool Handle(TransportAction action)
    {
        switch (action)
        {
            case TransportAction.TogglePlay:
                TogglePlay();
                return true;
            case TransportAction.StepBack:
                Step(-1);
                return true;
            case TransportAction.StepForward:
                Step(1);
                return true;
            case TransportAction.StepBackTen:
                Step(-10);
                return true;
            case TransportAction.StepForwardTen:
                Step(10);
                return true;
            case TransportAction.First:
                SeekPosition(0);
                return true;
            case TransportAction.Last:
                SeekPosition(PositionCount - 1);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Moves the counter to the clock's frame. The view calls it on each UI frame while playing.</summary>
    public void Tick()
    {
        if (_clock is null)
        {
            return;
        }

        var position = _clock.FrameAt(_now());
        if (position != Position)
        {
            Show(position);
            UpdateTexts();
        }
    }

    /// <summary>The frame (a video frame for the scrubber) at <paramref name="fraction"/> of the scrubber's width (0 to 1), as a drag or a click places it.</summary>
    public static int FrameAtFraction(double fraction, int frameCount)
    {
        if (frameCount <= 1 || double.IsNaN(fraction))
        {
            return 0;
        }

        return (int)Math.Round(Math.Clamp(fraction, 0, 1) * (frameCount - 1), MidpointRounding.AwayFromZero);
    }

    /// <summary>Where frame <paramref name="frame"/> sits on the scrubber (0 to 1).</summary>
    public static double FractionOfFrame(int frame, int frameCount) =>
        frameCount <= 1 ? 0 : Math.Clamp((double)frame / (frameCount - 1), 0, 1);

    /// <summary>Stops following the shell and the project.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _project.Changed -= OnProjectChanged;
        _shell.ProjectReplaced -= OnProjectReplaced;
        _shell.PropertyChanged -= OnShellPropertyChanged;
        _clock = null;
    }

    private void OnProjectChanged(object? sender, ProjectChangedEventArgs e) =>
        _errors.Guard(() => Follow(e.Snapshot), ProjectText.PreviewProjectFailed);

    private void OnProjectReplaced(object? sender, ProjectReplacedEventArgs e) =>
        _errors.Guard(
            () =>
            {
                _project.Changed -= OnProjectChanged;
                _project = e.Current.Model;
                _project.Changed += OnProjectChanged;
                _clock = null;
                IsPlaying = false;
                Load(_project.Snapshot, frame: 0, position: 0);
            },
            ProjectText.PreviewProjectFailed);

    // Playback stops on the Settings page (#26): the preview is covered, and nothing should play unseen.
    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.IsSettingsOpen) && _shell.IsSettingsOpen)
        {
            _errors.Guard(Pause, ProjectText.PreviewPlayFailed);
        }
    }

    // A change of the project: playing on from the frame on screen, at its pace. The playhead stays on the same photo
    // when it is still in the clip (frames before it removed, added or moved, review m7); when it left, the index
    // stays, clamped to the clip. On the video's timeline (#49) it goes to that photo's showing nearest to where it was,
    // so a change of the holds or the plays keeps it in the same pass; an unchanged sequence keeps the very frame.
    private void Follow(ProjectSnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        var previous = State;
        var before = previous.Snapshot.Frames;
        if (_clock is { } clock)
        {
            var now = _now();
            var reading = clock.Read(now);
            var photo = previous.PhotoAt(reading.Frame);
            var frame = Place(before, photo, snapshot);
            var (position, moved) = PlaceOnTimeline(previous.Sequence, reading.Frame, frame, frame != photo, snapshot);
            Load(snapshot, frame, position, playing: true, frameBegan: clock.TimestampAt(reading.OccurrenceStart), moved: moved);
        }
        else
        {
            var frame = Place(before, CurrentFrame, snapshot);
            var (position, _) = PlaceOnTimeline(previous.Sequence, Position, frame, frame != CurrentFrame, snapshot);
            Load(snapshot, frame, position);
        }
    }

    // Where photo `frame` goes on the new snapshot's timeline, coming from `position` on the old one; and whether the
    // playhead moved (a new clock then starts where the frame on screen began).
    private static (int Position, bool Moved) PlaceOnTimeline(FrameSequence before, int position, int frame, bool photoMoved, ProjectSnapshot snapshot)
    {
        var after = snapshot.Playback;
        if (after.Length == 0)
        {
            return (0, true);
        }

        frame = Math.Clamp(frame, 0, after.PhotoCount - 1);
        if (!photoMoved && after == before && position >= 0 && position < after.Length)
        {
            return (position, false);
        }

        return (after.NearestPositionOf(frame, position), true);
    }

    // Where the photo at index in before sits in snapshot; the index itself when it is not there.
    private static int Place(System.Collections.Immutable.ImmutableArray<Frame> before, int index, ProjectSnapshot snapshot)
    {
        if (index < 0 || index >= before.Length)
        {
            return index;
        }

        var id = before[index].Id;
        var frames = snapshot.Frames;
        if (index < frames.Length && frames[index].Id == id)
        {
            return index;
        }

        for (var i = 0; i < frames.Length; i++)
        {
            if (frames[i].Id == id)
            {
                return i;
            }
        }

        return index;
    }

    // frame: the photo on screen; position: the video frame, on snapshot's timeline (the photo's first frame when not given).
    private void Load(ProjectSnapshot snapshot, int frame, int? position = null, bool playing = false, long? frameBegan = null, bool moved = false)
    {
        var count = snapshot.Frames.Length;
        var sequence = snapshot.Playback;
        FrameCount = count;
        PositionCount = sequence.Length;
        frame = count == 0 ? 0 : Math.Clamp(frame, 0, count - 1);
        var at = count == 0 ? 0 : Math.Clamp(position ?? sequence.FirstPositionOf(frame), 0, sequence.Length - 1);
        if (count == 0 || !playing)
        {
            _clock = null;
            IsPlaying = false;
        }
        else
        {
            var previous = _clock;
            var rate = snapshot.EffectiveRate.Rate;

            // The same clip at the same rate keeps its clock (an import filling in sizes, a rename); anything else is
            // anchored where the frame on screen began, so the change does not stretch or shorten that frame.
            if (previous is null || moved || previous.Rate != rate || previous.FrameCount != sequence.Length)
            {
                _clock = new PlaybackClock(rate, sequence.Length, at, frameBegan ?? _now(), _frequency);
            }
        }

        CurrentFrame = frame;
        Position = at;
        _state = new PlayerState(snapshot, frame, at, _clock, _version);
        UpdateTexts();
        Publish(snapshot);
    }

    private PlaybackClock NewClock(PlayerState state, int position, long now) =>
        new(state.Snapshot.EffectiveRate.Rate, state.Sequence.Length, position, now, _frequency);

    // The video frame on screen, and the photo it shows.
    private void Show(int position)
    {
        Position = position;
        CurrentFrame = State.PhotoAt(position);
    }

    private void UpdateTexts()
    {
        var count = FrameCount;
        if (count == 0)
        {
            CounterText = TimecodeText = CounterName = TimecodeName = string.Empty;
            return;
        }

        // The counter is in photos ("0312 / 0742", as the strip's caption); the time code on the video's timeline.
        var rate = _clock?.Rate ?? State.Snapshot.EffectiveRate.Rate;
        var start = FrameTiming.FrameStart(Position, rate);
        var length = FrameTiming.Duration(PositionCount, rate);
        CounterText = ProjectText.PreviewCounter(CurrentFrame + 1, count);
        TimecodeText = ProjectText.TimecodeCaption(start, length);

        // Narrator's names: always while paused; while playing at most once a second, so they are never far behind
        // (a name change on text that has no focus is not read out, so this costs nothing in speech).
        var now = _now();
        if (!IsPlaying || now - _namesUpdatedAt >= _frequency)
        {
            _namesUpdatedAt = now;
            CounterName = ProjectText.PreviewCounterName(CurrentFrame + 1, count);
            TimecodeName = ProjectText.PreviewTimecodeName(start, length);
        }
    }

    private void Publish(ProjectSnapshot? snapshot = null)
    {
        _version++;
        Volatile.Write(ref _state, new PlayerState(snapshot ?? State.Snapshot, CurrentFrame, Position, _clock, _version));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
