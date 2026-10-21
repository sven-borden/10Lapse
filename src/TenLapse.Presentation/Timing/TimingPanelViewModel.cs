using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation;

/// <summary>
/// The Timing section of the inspector (#17): the speed mode (Per second, Length, Per photo), the presets and the
/// box for a custom value of the current mode, and the readout "742 photos → 24.7 s at 30 fps". Every change goes
/// through <see cref="Project.Timing"/>, the one place the speed lives, so the preview and the export take it
/// from the next snapshot and nothing else is rebuilt. Each mode keeps its own value (<see cref="TimingSettings"/>),
/// so switching modes and back returns to what the user had. All of it runs on the UI thread, the one thread
/// that may change a <see cref="Project"/>.
/// </summary>
/// <remarks>
/// Nothing is changed silently. A value typed outside the range or with more decimals than the box keeps is
/// stored in range and rounded, and <see cref="Message"/> says what became of it; text that is not a number is
/// refused with a sentence and the box goes back to the stored value (see <see cref="TimingInput"/>). In Length
/// mode a target the photos cannot reach at 1 to 120 fps is played at the nearest limit, and <see cref="Notice"/>
/// says so for as long as it holds. The numbers everywhere in the panel (the box, the presets, the readout and these
/// sentences) are written in the user's culture, so one sentence never mixes a dot and a comma.
/// </remarks>
public sealed class TimingPanelViewModel : ObservableObject, IDisposable
{
    private const double BarSeconds = 60;

    private static readonly SpeedMode[] _modes = [SpeedMode.PerSecond, SpeedMode.Length, SpeedMode.PerPhoto];

    private static readonly string[] _modeLabels = [TimingText.ModePerSecond, TimingText.ModeLength, TimingText.ModePerPhoto];

    private static readonly Fraction[] _lengthPresets = [new(10, 1), new(15, 1), new(30, 1), new(60, 1)];

    private static readonly Fraction[] _photoPresets = [new(1, 4), new(1, 2), new(1, 1), new(2, 1), new(5, 1)];

    private static readonly string[] _directionLabels = [TimingText.DirectionForward, TimingText.DirectionBoomerang];

    private readonly ShellViewModel _shell;
    private readonly IErrorPresenter _errors;
    private readonly CultureInfo? _culture;
    private Project _project;
    private string[] _presetLabels = [];
    private int _selectedPresetIndex = -1;
    private string _valueText = string.Empty;
    private string _headline = string.Empty;
    private string _detail = string.Empty;
    private string _message = string.Empty;
    private string _notice = string.Empty;
    private string _sequenceMessage = string.Empty;
    private string _holdStartText = string.Empty;
    private string _holdEndText = string.Empty;
    private string[] _playsLabels = [];
    private SequenceSettings _sequence = SequenceSettings.Default;
    private bool _isGif;
    private double _lengthFraction;
    private int _modeIndex;
    private bool _noticeAppeared;
    private bool _editing;
    private bool _disposed;
    private object? _shownTiming;
    private int _shownCount = -1;

    /// <summary>Follows the shell's current project, also across <c>New project</c>.</summary>
    /// <param name="shell">The shell whose <see cref="ShellViewModel.CurrentProject"/> the panel edits.</param>
    /// <param name="errors">The error path: a failed edit is shown and logged.</param>
    /// <param name="culture">The culture of the numbers in the box, the presets and the sentences; the current culture when null.</param>
    public TimingPanelViewModel(ShellViewModel shell, IErrorPresenter errors, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(errors);
        _shell = shell;
        _errors = errors;
        _culture = culture;
        _project = shell.CurrentProject.Model;
        _project.Changed += OnProjectChanged;
        _shell.ProjectReplaced += OnProjectReplaced;
        Refresh();
    }

    /// <summary>Raised on the UI thread after the user changed the speed or typed something the panel refused or corrected: the sentence a screen reader should say (the message, then the readout).</summary>
    public event EventHandler<string>? Announced;

    /// <summary>
    /// Raised after the readout changed because the speed changed (motion spec, Retime: the new length rises into place and
    /// the bar follows), not because photos came or went. Never for the first readout or a new project.
    /// </summary>
    public event EventHandler? Retimed;

    private CultureInfo Culture => _culture ?? CultureInfo.CurrentCulture;

    /// <summary>Per second, Length, Per photo.</summary>
    public IReadOnlyList<string> ModeLabels => _modeLabels;

    /// <summary>The mode in use. Setting it edits the project; each mode keeps its own value.</summary>
    public int SelectedModeIndex
    {
        get => _modeIndex;
        set
        {
            if (value >= 0 && value < _modes.Length && value != _modeIndex)
            {
                Message = string.Empty;
                var before = _project.Timing;
                Edit(timing => timing with { Mode = _modes[value] });
                AnnounceIfChanged(before);
            }
        }
    }

    /// <summary>Name of the presets for a screen reader: "Frames per second", "Video length" or "Seconds per photo".</summary>
    public string PresetsName => Mode switch
    {
        SpeedMode.PerSecond => TimingText.PresetsFramesPerSecond,
        SpeedMode.Length => TimingText.PresetsLength,
        _ => TimingText.PresetsSecondsPerPhoto,
    };

    /// <summary>The presets of the current mode: 12 to 60 fps; 10 to 60 s; a quarter of a second to 5 s per photo.</summary>
    public IReadOnlyList<string> PresetLabels => _presetLabels;

    /// <summary>The preset that equals the mode's value, or -1 for a custom value. Setting it stores that preset.</summary>
    public int SelectedPresetIndex
    {
        get => _selectedPresetIndex;
        set
        {
            var presets = Presets(Mode);
            if (value >= 0 && value < presets.Count && value != _selectedPresetIndex)
            {
                Message = string.Empty;
                var before = _project.Timing;
                Apply(Field, presets[value]);
                AnnounceIfChanged(before);
            }
        }
    }

    /// <summary>The caption above the box, which is also its name: "Custom frames per second", "Custom length in seconds", "Custom seconds per photo".</summary>
    public string ValueLabel => Mode switch
    {
        SpeedMode.PerSecond => TimingText.CustomFramesPerSecond,
        SpeedMode.Length => TimingText.CustomLength,
        _ => TimingText.CustomSecondsPerPhoto,
    };

    /// <summary>The unit after the box: "fps", "s" or "s per photo"; the first two are the units the box accepts when typed.</summary>
    public string ValueUnit => Mode switch
    {
        SpeedMode.PerSecond => TimingWords.FramesPerSecond,
        SpeedMode.Length => TimingWords.Second,
        _ => TimingText.UnitSecondsPerPhoto,
    };

    /// <summary>The length bar's left end, "0 s", in the panel's culture.</summary>
    public string BarStart => TimingFormat.WholeSeconds(0, Culture);

    /// <summary>The length bar's middle, "30 s": half a minute.</summary>
    public string BarMiddle => TimingFormat.WholeSeconds((long)BarSeconds / 2, Culture);

    /// <summary>The length bar's right end, "60 s": a minute, the longest video it shows.</summary>
    public string BarEnd => TimingFormat.WholeSeconds((long)BarSeconds, Culture);

    /// <summary>The mode's value as the box shows it, in the user's culture ("29,97" in German): the stored value, never the typed text.</summary>
    public string ValueText => _valueText;

    /// <summary>The first line of the readout: "742 photos → 24.7 s".</summary>
    public string Headline => _headline;

    /// <summary>The second line: "at 30 fps", "at 24.73 fps" or "at 2 s per photo".</summary>
    public string Detail => _detail;

    /// <summary>The whole readout in one sentence: "742 photos → 24.7 s at 30 fps" (<see cref="ProjectText.SentenceJoin"/>).</summary>
    public string Readout => ProjectText.Sentences(_headline, _detail);

    /// <summary>What the panel did with the last entry (refused, held to the range, rounded); empty when it was used as typed or after another change.</summary>
    public string Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
            }
        }
    }

    /// <summary>True when <see cref="Message"/> has something to show.</summary>
    public bool HasMessage => _message.Length > 0;

    /// <summary>
    /// Length mode only: the target cannot be reached at 1 to 120 fps with this many photos, so the video is as long as
    /// the nearest limit makes it. Empty otherwise. It follows the photos, so it appears and goes as they are added or removed.
    /// </summary>
    public string Notice
    {
        get => _notice;
        private set
        {
            if (SetProperty(ref _notice, value))
            {
                OnPropertyChanged(nameof(HasNotice));
            }
        }
    }

    /// <summary>True when <see cref="Notice"/> has something to show.</summary>
    public bool HasNotice => _notice.Length > 0;

    /// <summary>The video's length against a minute, 0 to 1, for the bar under the readout.</summary>
    public double LengthFraction => _lengthFraction;

    /// <summary>Forward, Boomerang (#49).</summary>
    public IReadOnlyList<string> DirectionLabels => _directionLabels;

    /// <summary>0 forward, 1 boomerang. Setting it edits the project.</summary>
    public int SelectedDirectionIndex
    {
        get => _sequence.Boomerang ? 1 : 0;
        set
        {
            if (value is 0 or 1 && (value == 1) != _sequence.Boomerang)
            {
                EditSequence(sequence => sequence with { Boomerang = value == 1 });
            }
        }
    }

    /// <summary>The play counts, 1 to 5, in the user's digits.</summary>
    public IReadOnlyList<string> PlaysLabels => _playsLabels;

    /// <summary>The play count's index (plays − 1). Setting it edits the project.</summary>
    public int SelectedPlaysIndex
    {
        get => _sequence.Plays - SequenceSettings.MinPlays;
        set
        {
            var plays = value + SequenceSettings.MinPlays;
            if (plays is >= SequenceSettings.MinPlays and <= SequenceSettings.MaxPlays && plays != _sequence.Plays)
            {
                EditSequence(sequence => sequence with { Plays = plays });
            }
        }
    }

    /// <summary>
    /// True while the output is a video: the play counts show. A GIF loops by itself and plays the photos once
    /// (<see cref="SequenceSettings.For"/>), so the row is absent and <see cref="PlaysGifNote"/> says why.
    /// </summary>
    public bool ShowsPlays => !_isGif;

    /// <summary>True while the output is a GIF: the note in place of the play counts.</summary>
    public bool ShowsPlaysGifNote => _isGif;

    /// <summary>"A GIF plays over and over by itself, …": why the play counts are not there.</summary>
    public string PlaysGifNote => TimingText.PlaysGif;

    /// <summary>The hold before the photos play, as its box shows it ("1.5"): the stored value, never the typed text.</summary>
    public string HoldStartText => _holdStartText;

    /// <summary>The hold after the photos played, as its box shows it.</summary>
    public string HoldEndText => _holdEndText;

    /// <summary>What the hold boxes did with the last entry (refused, held to the range, rounded); empty otherwise.</summary>
    public string SequenceMessage
    {
        get => _sequenceMessage;
        private set
        {
            if (SetProperty(ref _sequenceMessage, value))
            {
                OnPropertyChanged(nameof(HasSequenceMessage));
            }
        }
    }

    /// <summary>True when <see cref="SequenceMessage"/> has something to show.</summary>
    public bool HasSequenceMessage => _sequenceMessage.Length > 0;

    /// <summary>Takes what was typed in the hold-at-start box (Enter, or the box lost focus).</summary>
    public void CommitHoldStart(string? text) => CommitHold(text, _holdStartText, nameof(HoldStartText), (sequence, hold) => sequence with { HoldStart = hold });

    /// <summary>Takes what was typed in the hold-at-end box.</summary>
    public void CommitHoldEnd(string? text) => CommitHold(text, _holdEndText, nameof(HoldEndText), (sequence, hold) => sequence with { HoldEnd = hold });

    /// <summary>
    /// Takes entries still in the hold boxes before a shortcut acts (#38), as <see cref="CommitPending"/> does for the speed
    /// box. True when there was one.
    /// </summary>
    public bool CommitPendingHolds(string? startText, string? endText)
    {
        var pending = false;
        if (!string.Equals(startText?.Trim(), _holdStartText, StringComparison.Ordinal))
        {
            CommitHoldStart(startText);
            pending = true;
        }

        if (!string.Equals(endText?.Trim(), _holdEndText, StringComparison.Ordinal))
        {
            CommitHoldEnd(endText);
            pending = true;
        }

        return pending;
    }

    private SpeedMode Mode => _modes[_modeIndex];

    private TimingField Field => Mode switch
    {
        SpeedMode.PerSecond => TimingField.FramesPerSecond,
        SpeedMode.Length => TimingField.LengthSeconds,
        _ => TimingField.SecondsPerPhoto,
    };

    /// <summary>
    /// Takes an entry still in the box before a shortcut acts (#38): Ctrl+E or Ctrl+N run without moving the focus, so the
    /// box has not committed on LostFocus yet, and the export would use the old value (a click on Export would have
    /// moved the focus, and committed, first). True when there was an entry; text equal to the stored value is none, and
    /// raises nothing.
    /// </summary>
    /// <param name="text">The box's text.</param>
    public bool CommitPending(string? text)
    {
        if (string.Equals(text?.Trim(), _valueText, StringComparison.Ordinal))
        {
            return false;
        }

        CommitValue(text);
        return true;
    }

    /// <summary>
    /// Takes what the user typed in the box (when it loses focus or Enter is pressed). A valid number is stored, held to
    /// the range and rounded to what the box keeps, with <see cref="Message"/> saying so; anything else is refused with a
    /// sentence. Either way <see cref="ValueText"/> is raised again, so the box shows the stored value. Text that equals
    /// what the box already shows changes nothing.
    /// </summary>
    /// <param name="text">The box's text.</param>
    public void CommitValue(string? text)
    {
        if (string.Equals(text?.Trim(), _valueText, StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(ValueText));
            return;
        }

        var entry = TimingInput.Parse(Field, text, Culture);
        Message = entry.Message;
        var before = _project.Timing;
        if (entry.IsValid)
        {
            Apply(Field, entry.Value);
        }

        // The box goes back to the stored value, also when nothing changed, so a refused entry does not stay on screen.
        OnPropertyChanged(nameof(ValueText));
        if (entry.Message.Length > 0)
        {
            Announce();
        }
        else
        {
            AnnounceIfChanged(before);
        }
    }

    /// <summary>Stops following the project.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _project.Changed -= OnProjectChanged;
        _shell.ProjectReplaced -= OnProjectReplaced;
    }

    private IReadOnlyList<Fraction> Presets(SpeedMode mode) => mode switch
    {
        SpeedMode.PerSecond => FrameRates.Presets,
        SpeedMode.Length => _lengthPresets,
        _ => _photoPresets,
    };

    private void OnProjectChanged(object? sender, ProjectChangedEventArgs e)
    {
        // The speed, the photo count (the readout and Length mode's rate depend on it), the sequence (#49), and the output
        // (a GIF plays the photos once); nothing else.
        if ((e.Changes & (ProjectChanges.Frames | ProjectChanges.Timing | ProjectChanges.Sequence | ProjectChanges.Output)) != 0)
        {
            Refresh();
        }
    }

    private void CommitHold(string? text, string shown, string property, Func<SequenceSettings, TimeSpan, SequenceSettings> change)
    {
        if (string.Equals(text?.Trim(), shown, StringComparison.Ordinal))
        {
            OnPropertyChanged(property);
            return;
        }

        var entry = HoldInput.Parse(text, Culture);
        SequenceMessage = entry.Message;
        var before = _project.Sequence;
        if (entry.IsValid)
        {
            EditSequence(sequence => change(sequence, entry.Value), announce: false);
        }

        // The box goes back to the stored value, also when nothing changed, so a refused entry does not stay on screen.
        OnPropertyChanged(property);
        if (entry.Message.Length > 0 || _project.Sequence != before)
        {
            Announced?.Invoke(this, ProjectText.Sentences(_sequenceMessage, Readout, _noticeAppeared ? _notice : null));
            _noticeAppeared = false;
        }
    }

    // Writes the sequence (#49) to the project and says what the video came to; a closed project takes no edits.
    private void EditSequence(Func<SequenceSettings, SequenceSettings> change, bool announce = true)
    {
        var before = _project.Sequence;
        if (announce)
        {
            SequenceMessage = string.Empty;
        }

        _editing = true;
        try
        {
            _errors.Guard(
                () =>
                {
                    if (!_project.IsClosed)
                    {
                        _project.Sequence = change(_project.Sequence);
                    }
                },
                TimingText.SequenceChangeFailed);
        }
        finally
        {
            _editing = false;
        }

        // A control that showed the pick but did not keep it (the write failed) goes back to the project's value.
        OnPropertyChanged(nameof(SelectedDirectionIndex));
        OnPropertyChanged(nameof(SelectedPlaysIndex));
        if (announce && _project.Sequence != before)
        {
            Announced?.Invoke(this, ProjectText.Sentences(Readout, _noticeAppeared ? _notice : null));
            _noticeAppeared = false;
        }
    }

    private void OnProjectReplaced(object? sender, ProjectReplacedEventArgs e)
    {
        _project.Changed -= OnProjectChanged;
        _project = e.Current.Model;
        _project.Changed += OnProjectChanged;
        Message = string.Empty;
        SequenceMessage = string.Empty;
        Refresh();
    }

    // Writes one mode's value to the project, in range already (the presets and TimingInput keep it so).
    private void Apply(TimingField field, Fraction value) =>
        Edit(timing => field switch
        {
            TimingField.FramesPerSecond => timing with { FramesPerSecond = value },
            TimingField.LengthSeconds => timing with { LengthSeconds = value },
            _ => timing with { SecondsPerPhoto = value },
        });

    // A closed project (the window is going away) takes no edits.
    private void Edit(Func<TimingSettings, TimingSettings> change)
    {
        // Refresh runs inside the write; what it finds (a new Notice) is announced with the edit, not on its own.
        _editing = true;
        try
        {
            _errors.Guard(
                () =>
                {
                    if (!_project.IsClosed)
                    {
                        _project.Timing = change(_project.Timing);
                    }
                },
                TimingText.ChangeFailed);
        }
        finally
        {
            _editing = false;
        }

        // A control that showed the pick but did not keep it (the write failed) goes back to the project's value.
        OnPropertyChanged(nameof(SelectedModeIndex));
        OnPropertyChanged(nameof(SelectedPresetIndex));
        OnPropertyChanged(nameof(ValueText));
    }

    // A pick that did not change the speed (the write failed, or the same value again) says nothing.
    private void AnnounceIfChanged(TimingSettings before)
    {
        if (_project.Timing != before)
        {
            Announce();
        }
    }

    private void Announce()
    {
        var text = ProjectText.Sentences(_message, Readout, _noticeAppeared ? _notice : null);

        _noticeAppeared = false;
        Announced?.Invoke(this, text);
    }

    private void Refresh() =>
        _errors.Guard(
            () =>
            {
                var snapshot = _project.Snapshot;
                var timing = snapshot.Timing;
                var count = snapshot.Frames.Length;

                var modeIndex = Array.IndexOf(_modes, timing.Mode);
                if (modeIndex != _modeIndex)
                {
                    _modeIndex = modeIndex;
                    OnPropertyChanged(nameof(SelectedModeIndex));
                    OnPropertyChanged(nameof(PresetsName));
                    OnPropertyChanged(nameof(ValueLabel));
                    OnPropertyChanged(nameof(ValueUnit));
                }

                var culture = Culture;
                var presets = Presets(timing.Mode);
                var labels = presets.Select(preset => PresetLabel(timing.Mode, preset, culture)).ToArray();
                if (!labels.SequenceEqual(_presetLabels))
                {
                    _presetLabels = labels;
                    OnPropertyChanged(nameof(PresetLabels));
                }

                var value = timing.Mode switch
                {
                    SpeedMode.PerSecond => timing.FramesPerSecond,
                    SpeedMode.Length => timing.LengthSeconds,
                    _ => timing.SecondsPerPhoto,
                };
                SetProperty(ref _selectedPresetIndex, IndexOf(presets, value), nameof(SelectedPresetIndex));
                SetProperty(ref _valueText, TimingInput.Number(value, culture), nameof(ValueText));

                RefreshSequence(snapshot, culture);
                var sequence = snapshot.EffectiveSequence;
                var (headline, detail) = ProjectText.TimingReadoutParts(count, timing, sequence, culture);
                var readoutChanged = headline != _headline || detail != _detail;
                _headline = headline;
                _detail = detail;
                if (readoutChanged)
                {
                    OnPropertyChanged(nameof(Headline));
                    OnPropertyChanged(nameof(Detail));
                    OnPropertyChanged(nameof(Readout));
                }

                // A new speed or a new sequence (#49) retimes the video; photos coming or going do not.
                var shown = (timing, sequence);
                var retimed = readoutChanged && _shownTiming is not null && count == _shownCount && !Equals(_shownTiming, shown);
                _shownTiming = shown;
                _shownCount = count;
                var rate = snapshot.EffectiveRate;
                SetProperty(ref _lengthFraction, Math.Min(1, (double)snapshot.Duration / FrameTiming.TicksPerSecond / BarSeconds), nameof(LengthFraction));

                // After the bar's new length, so the view animates from the length it showed to this one.
                if (retimed)
                {
                    Retimed?.Invoke(this, EventArgs.Empty);
                }
                var hadNotice = _notice.Length > 0;
                Notice = count > 0 && timing.Mode == SpeedMode.Length ? NoticeFor(rate, timing, snapshot.Duration, culture) : string.Empty;
                if (!hadNotice && _notice.Length > 0)
                {
                    // It appears when the photos are added or removed too, where a screen reader would otherwise miss it.
                    _noticeAppeared = true;
                    if (!_editing)
                    {
                        Announced?.Invoke(this, ProjectText.Sentences(_notice, Readout));
                        _noticeAppeared = false;
                    }
                }
            },
            TimingText.RefreshFailed);

    // The sequence's controls (#49) show what the project has: the direction, the plays (absent for a GIF) and the holds.
    private void RefreshSequence(ProjectSnapshot snapshot, CultureInfo culture)
    {
        var sequence = snapshot.Sequence;
        var labels = Enumerable.Range(SequenceSettings.MinPlays, SequenceSettings.MaxPlays - SequenceSettings.MinPlays + 1)
            .Select(plays => plays.ToString(culture))
            .ToArray();
        if (!labels.SequenceEqual(_playsLabels))
        {
            _playsLabels = labels;
            OnPropertyChanged(nameof(PlaysLabels));
        }

        var changed = sequence != _sequence;
        _sequence = sequence;
        if (changed)
        {
            OnPropertyChanged(nameof(SelectedDirectionIndex));
            OnPropertyChanged(nameof(SelectedPlaysIndex));
        }

        SetProperty(ref _holdStartText, HoldInput.Number(sequence.HoldStart, culture), nameof(HoldStartText));
        SetProperty(ref _holdEndText, HoldInput.Number(sequence.HoldEnd, culture), nameof(HoldEndText));
        var isGif = snapshot.Output.Format == VideoFormat.Gif;
        if (isGif != _isGif)
        {
            _isGif = isGif;
            OnPropertyChanged(nameof(ShowsPlays));
            OnPropertyChanged(nameof(ShowsPlaysGifNote));
        }
    }

    private static string NoticeFor(PlaybackRate rate, TimingSettings timing, long duration, CultureInfo culture)
    {
        if (!rate.IsClamped)
        {
            return string.Empty;
        }

        var target = TimingInput.Describe(TimingField.LengthSeconds, timing.LengthSeconds, culture);
        var actual = TimingFormat.Duration(duration, culture);
        return rate.Clamp == RateClamp.Max
            ? TimingText.Format(culture, TimingText.LengthTooShort, target, TimingFormat.FrameRate(FrameRates.Max, culture), actual)
            : TimingText.Format(culture, TimingText.LengthTooLong, target, TimingFormat.SecondsPerPhoto(FrameRates.Min, culture), actual);
    }

    private static string PresetLabel(SpeedMode mode, Fraction preset, CultureInfo culture)
    {
        // Per second presets are bare numbers ("24"), the others seconds ("30 s", "0.25 s").
        return mode == SpeedMode.PerSecond ? TimingFormat.Decimal(preset, culture) : TimingFormat.Seconds(preset, culture);
    }

    private static int IndexOf(IReadOnlyList<Fraction> presets, Fraction value)
    {
        for (var i = 0; i < presets.Count; i++)
        {
            if (presets[i] == value)
            {
                return i;
            }
        }

        return -1;
    }
}
