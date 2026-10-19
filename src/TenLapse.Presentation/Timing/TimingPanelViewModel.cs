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
    private double _lengthFraction;
    private int _modeIndex;
    private bool _noticeAppeared;
    private bool _editing;
    private bool _disposed;

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

    /// <summary>The unit after the box: "fps", "s" or "s per photo".</summary>
    public string ValueUnit => Mode switch
    {
        SpeedMode.PerSecond => TimingText.UnitFramesPerSecond,
        SpeedMode.Length => TimingText.UnitLength,
        _ => TimingText.UnitSecondsPerPhoto,
    };

    /// <summary>The mode's value as the box shows it, in the user's culture ("29,97" in German): the stored value, never the typed text.</summary>
    public string ValueText => _valueText;

    /// <summary>The first line of the readout: "742 photos → 24.7 s".</summary>
    public string Headline => _headline;

    /// <summary>The second line: "at 30 fps", "at 24.73 fps" or "at 2 s per photo".</summary>
    public string Detail => _detail;

    /// <summary>The whole readout in one sentence: "742 photos → 24.7 s at 30 fps".</summary>
    public string Readout => $"{_headline} {_detail}";

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

    private SpeedMode Mode => _modes[_modeIndex];

    private TimingField Field => Mode switch
    {
        SpeedMode.PerSecond => TimingField.FramesPerSecond,
        SpeedMode.Length => TimingField.LengthSeconds,
        _ => TimingField.SecondsPerPhoto,
    };

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
        // The speed and the photo count (the readout and Length mode's rate depend on it); nothing else.
        if ((e.Changes & (ProjectChanges.Frames | ProjectChanges.Timing)) != 0)
        {
            Refresh();
        }
    }

    private void OnProjectReplaced(object? sender, ProjectReplacedEventArgs e)
    {
        _project.Changed -= OnProjectChanged;
        _project = e.Current.Model;
        _project.Changed += OnProjectChanged;
        Message = string.Empty;
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
        var text = _message.Length > 0 ? $"{_message} {Readout}" : Readout;
        if (_noticeAppeared && _notice.Length > 0)
        {
            text = $"{text} {_notice}";
        }

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

                var (headline, detail) = ProjectText.TimingReadoutParts(count, timing, culture);
                var readoutChanged = headline != _headline || detail != _detail;
                _headline = headline;
                _detail = detail;
                if (readoutChanged)
                {
                    OnPropertyChanged(nameof(Headline));
                    OnPropertyChanged(nameof(Detail));
                    OnPropertyChanged(nameof(Readout));
                }

                var rate = timing.EffectiveRate(count);
                SetProperty(ref _lengthFraction, Math.Min(1, (double)FrameTiming.Duration(count, rate.Rate) / FrameTiming.TicksPerSecond / BarSeconds), nameof(LengthFraction));
                var hadNotice = _notice.Length > 0;
                Notice = count > 0 && timing.Mode == SpeedMode.Length ? NoticeFor(rate, timing, count, culture) : string.Empty;
                if (!hadNotice && _notice.Length > 0)
                {
                    // It appears when the photos are added or removed too, where a screen reader would otherwise miss it.
                    _noticeAppeared = true;
                    if (!_editing)
                    {
                        Announced?.Invoke(this, $"{_notice} {Readout}");
                        _noticeAppeared = false;
                    }
                }
            },
            TimingText.RefreshFailed);

    private static string NoticeFor(PlaybackRate rate, TimingSettings timing, int count, CultureInfo culture)
    {
        if (!rate.IsClamped)
        {
            return string.Empty;
        }

        var target = TimingInput.Describe(TimingField.LengthSeconds, timing.LengthSeconds, culture);
        var actual = TimingFormat.Duration(FrameTiming.Duration(count, rate.Rate), culture);
        return rate.Clamp == RateClamp.Max
            ? TimingText.Format(culture, TimingText.LengthTooShort, target, TimingFormat.FrameRate(FrameRates.Max, culture), actual)
            : TimingText.Format(culture, TimingText.LengthTooLong, target, TimingFormat.SecondsPerPhoto(FrameRates.Min, culture), actual);
    }

    private static string PresetLabel(SpeedMode mode, Fraction preset, CultureInfo culture)
    {
        var number = TimingFormat.Decimal(preset, culture);
        return mode switch
        {
            SpeedMode.PerSecond => number,
            SpeedMode.Length => TimingText.Format(culture, TimingText.LengthPreset, number),
            _ => $"{number} {TimingWords.Second}",
        };
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
