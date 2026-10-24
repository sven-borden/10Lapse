using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TenLapse.Core.Projects;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation.Music;

/// <summary>
/// The Music section of the inspector (#50): Add music, then the song with Replace and Remove, where the video starts in
/// it, the fades in and out, and how the song fits the video. Every change is one write to <see cref="Project.Music"/>;
/// the export and the preview's music take it from the next snapshot. UI thread.
/// </summary>
/// <remarks>
/// A chosen file is read before it joins the project (its length, and one decoded piece, so a missing codec is said at
/// once): one that does not play is not taken, and an InfoBar says so. Remove takes the song out of the project only; the
/// file stays on the disk, and the fades stay for the next song. A song whose file is gone (moved, a drive not connected)
/// stays in the project, and the section says so in place of the controls until it is replaced or removed. The section
/// looks for the file only when the project's song changes, never on every edit.
/// </remarks>
public sealed class MusicPanelViewModel : ObservableObject, IDisposable
{
    /// <summary>The slider steps: a tenth of a second for the start, half a second for the fades.</summary>
    public const double StartStep = 0.1;

    /// <inheritdoc cref="StartStep"/>
    public const double FadeStep = 0.5;

    private readonly ShellViewModel _shell;
    private readonly IMusicFiles _files;
    private readonly IInfoBarService _infoBars;
    private readonly IErrorPresenter _errors;
    private Project _project;
    private MusicSettings _music = MusicSettings.Default;
    private long _videoDuration;
    private bool _isGif;
    private string? _checkedPath;
    private bool _missing;
    private bool _choosing;
    private bool _disposed;

    /// <param name="shell">The shell whose current project the section edits.</param>
    /// <param name="files">The file picker and the reader of a chosen file.</param>
    /// <param name="infoBars">Where a chosen file that does not play is said.</param>
    /// <param name="errors">The error path: a failed edit is shown and logged.</param>
    public MusicPanelViewModel(ShellViewModel shell, IMusicFiles files, IInfoBarService infoBars, IErrorPresenter errors)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(infoBars);
        ArgumentNullException.ThrowIfNull(errors);
        _shell = shell;
        _files = files;
        _infoBars = infoBars;
        _errors = errors;
        AddCommand = errors.AsyncCommand(ChooseAsync, MusicText.ChooseFailed, () => !_choosing);
        RemoveCommand = errors.Command(Remove, MusicText.ChangeFailed);
        _project = shell.CurrentProject.Model;
        _project.Changed += OnProjectChanged;
        _shell.ProjectReplaced += OnProjectReplaced;
        Refresh();
    }

    /// <summary>True when the project has a song: the song's row shows in place of Add music.</summary>
    public bool HasMusic => _music.HasMusic;

    /// <summary>True while there is no song: the hint and Add music show.</summary>
    public bool ShowsEmpty => !HasMusic;

    /// <summary>The song's file name, "Sunrise.mp3".</summary>
    public string FileName => Path.GetFileName(_music.Path) ?? string.Empty;

    /// <summary>The song's length, "3 min 42 s".</summary>
    public string LengthText => HasMusic ? MusicText.Length(_music.Length) : string.Empty;

    /// <summary>The song's screen reader name, "Sunrise.mp3, 3 min 42 s long".</summary>
    public string SongName => HasMusic ? MusicText.Song(FileName, _music.Length) : string.Empty;

    /// <summary>True when the song's file is not where the project says.</summary>
    public bool IsMissing => HasMusic && _missing;

    /// <summary>"Sunrise.mp3 can't be found…", while it is missing.</summary>
    public string MissingText => IsMissing ? MusicText.MissingFile(FileName) : string.Empty;

    /// <summary>True when the start and the fades show: a song whose file is there.</summary>
    public bool ShowsControls => HasMusic && !_missing;

    /// <summary>The start slider's end: the song's length in seconds (at least one step, so the slider has a range).</summary>
    public double StartMaximum => Math.Max(StartStep, _music.Length.TotalSeconds);

    /// <summary>Where the video starts in the song, in seconds, as the slider's value; whole tenths. Setting it edits the project.</summary>
    public double StartSeconds
    {
        get => _music.Start.TotalSeconds;
        set
        {
            var start = Tenths(value, 0, StartMaximum, _music.Start);
            if (start != _music.Start)
            {
                Edit(music => music with { Start = start });
            }
        }
    }

    /// <summary>The start as text, "12.5 s".</summary>
    public string StartText => MusicText.Length(_music.Start);

    /// <summary>The fades' slider end, in seconds.</summary>
    public static double FadeMaximum => MusicSettings.MaxFade.TotalSeconds;

    /// <summary>The fade in, in seconds, as the slider's value; whole tenths. Setting it edits the project.</summary>
    public double FadeInSeconds
    {
        get => _music.FadeIn.TotalSeconds;
        set
        {
            var fade = Tenths(value, 0, FadeMaximum, _music.FadeIn);
            if (fade != _music.FadeIn)
            {
                Edit(music => music with { FadeIn = fade });
            }
        }
    }

    /// <summary>The fade in as text, "1.5 s".</summary>
    public string FadeInText => MusicText.Fade(_music.FadeIn);

    /// <summary>The fade out, in seconds, as the slider's value; whole tenths. Setting it edits the project.</summary>
    public double FadeOutSeconds
    {
        get => _music.FadeOut.TotalSeconds;
        set
        {
            var fade = Tenths(value, 0, FadeMaximum, _music.FadeOut);
            if (fade != _music.FadeOut)
            {
                Edit(music => music with { FadeOut = fade });
            }
        }
    }

    /// <summary>The fade out as text, "2.0 s".</summary>
    public string FadeOutText => MusicText.Fade(_music.FadeOut);

    /// <summary>How the song fits the video: under all of it, ending early, or starting past its end. Empty without a known length or a video.</summary>
    public string FitText
    {
        get
        {
            if (!ShowsControls || _music.Length <= TimeSpan.Zero || _videoDuration <= 0)
            {
                return string.Empty;
            }

            var left = _music.Length - _music.Start;
            if (left <= TimeSpan.Zero)
            {
                return MusicText.StartsAfterEnd;
            }

            return left.Ticks < _videoDuration ? MusicText.EndsEarlyBy(TimeSpan.FromTicks(_videoDuration - left.Ticks)) : MusicText.FitsVideo;
        }
    }

    /// <summary>True when <see cref="FitText"/> has something to say.</summary>
    public bool ShowsFit => FitText.Length > 0;

    /// <summary>True while the output is a GIF and there is a song: the section says a GIF has no sound.</summary>
    public bool ShowsGifSilent => HasMusic && _isGif;

    /// <summary>Add music, and Replace: the file dialog, then the file is read and joins the project.</summary>
    public IAsyncRelayCommand AddCommand { get; }

    /// <summary>Takes the song out of the project (never off the disk).</summary>
    public IRelayCommand RemoveCommand { get; }

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

    // The dialog opens in the song's folder (the Music folder when there is no song).
    private async Task ChooseAsync()
    {
        var path = await _files.PickAsync(Path.GetDirectoryName(_music.Path));
        if (path is null || _disposed)
        {
            return;
        }

        SetChoosing(true);
        try
        {
            MusicFileInfo info;
            try
            {
                info = await _files.ProbeAsync(path);
            }
            catch (InvalidDataException exception)
            {
                _errors.Report(new InvalidDataException(MusicText.UnreadableLog(path, exception.Message), exception), MusicText.ChooseFailed);
                _infoBars.Show(InfoBarSeverity.Critical, MusicText.UnreadableFile(Path.GetFileName(path)));
                return;
            }

            // A new song starts at its beginning; the fades the user set stay.
            Edit(music => new MusicSettings(path, info.Length, TimeSpan.Zero, music.FadeIn, music.FadeOut));
        }
        finally
        {
            SetChoosing(false);
        }
    }

    private void SetChoosing(bool choosing)
    {
        _choosing = choosing;
        AddCommand.NotifyCanExecuteChanged();
    }

    // The fades stay for the next song.
    private void Remove() => Edit(music => MusicSettings.Default with { FadeIn = music.FadeIn, FadeOut = music.FadeOut });

    private void OnProjectChanged(object? sender, ProjectChangedEventArgs e) => Refresh();

    private void OnProjectReplaced(object? sender, ProjectReplacedEventArgs e)
    {
        _project.Changed -= OnProjectChanged;
        _project = e.Current.Model;
        _project.Changed += OnProjectChanged;
        Refresh();
    }

    // A closed project (the window is going away) takes no edits. A control that showed a value the project did not take
    // (the write failed) goes back to the project's.
    private void Edit(Func<MusicSettings, MusicSettings> change)
    {
        _errors.Guard(
            () =>
            {
                if (!_project.IsClosed)
                {
                    _project.Music = change(_project.Music);
                }
            },
            MusicText.ChangeFailed);
        Refresh(force: true);
    }

    // Every property follows the project at once (an empty name refreshes every binding); HasMusic is raised by name too,
    // for the view's focus.
    private void Refresh(bool force = false) =>
        _errors.Guard(
            () =>
            {
                var snapshot = _project.Snapshot;
                var music = snapshot.Music;
                var hadMusic = HasMusic;
                var duration = snapshot.Duration;
                var isGif = snapshot.Output.Format == VideoFormat.Gif;
                if (!string.Equals(music.Path, _checkedPath, StringComparison.OrdinalIgnoreCase))
                {
                    _checkedPath = music.Path;
                    _missing = music.Path is { } path && !_files.Exists(path);
                    force = true;
                }

                if (!force && music == _music && duration == _videoDuration && isGif == _isGif)
                {
                    return;
                }

                _music = music;
                _videoDuration = duration;
                _isGif = isGif;
                OnPropertyChanged(string.Empty);
                if (hadMusic != HasMusic)
                {
                    OnPropertyChanged(nameof(HasMusic));
                }
            },
            MusicText.RefreshFailed);

    // A slider's seconds as whole tenths within its range; a value that is not a number keeps `current`.
    private static TimeSpan Tenths(double seconds, double min, double max, TimeSpan current) =>
        double.IsFinite(seconds)
            ? TimeSpan.FromTicks((long)Math.Round(Math.Clamp(seconds, min, max) * 10, MidpointRounding.AwayFromZero) * (TimeSpan.TicksPerSecond / 10))
            : current;
}
