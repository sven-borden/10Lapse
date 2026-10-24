using System.ComponentModel;
using System.Diagnostics;
using TenLapse.Core.Preview;
using TenLapse.Core.Projects;
using TenLapse.Presentation.Preview;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation.Music;

/// <summary>
/// The music under the preview (#50): while the player plays, the song plays from where the video is in it, at the
/// loudness of its fades, and stops when the player stops; a seek, a loop back to the start or a change of the project
/// moves it with the picture. It plays only what the export would carry: no song for a GIF, silence past the song's end.
/// UI thread.
/// </summary>
/// <remarks>
/// The player's clock is the master: the song is placed from <see cref="PlaybackClock.Read"/> and moved back into step
/// when it drifts more than <see cref="Tolerance"/>, but not again within <see cref="Settle"/> of a move, so a media player
/// still finishing a seek is not chased. The view calls <see cref="Update"/> about 20 times a second while playing, which
/// is also how often the fades' loudness steps; the player's own changes call it at once.
/// </remarks>
public sealed class MusicPreview : IDisposable
{
    /// <summary>How far the song may drift from the picture before it is moved back.</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(120);

    /// <summary>How long after a move the drift is left alone.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(600);

    private readonly PlayerViewModel _player;
    private readonly IMusicOutput _output;
    private readonly IErrorSink _errors;
    private readonly Func<long> _now;
    private string? _loaded;
    private PlaybackClock? _clock;
    private long _loop = -1;
    private long _movedAt;
    private bool _disposed;

    /// <param name="player">The preview's player, whose clock the song follows.</param>
    /// <param name="output">The speaker.</param>
    /// <param name="errors">A failure of the speaker is logged, never shown: the picture plays on.</param>
    /// <param name="timestamp">The clock the player's runs on (<see cref="Stopwatch.GetTimestamp"/>); tests pass the player's.</param>
    public MusicPreview(PlayerViewModel player, IMusicOutput output, IErrorSink errors, Func<long>? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(errors);
        _player = player;
        _output = output;
        _errors = errors;
        _now = timestamp ?? Stopwatch.GetTimestamp;
        _player.StateChanged += OnPlayerChanged;
        _player.PropertyChanged += OnPlayerPropertyChanged;
        Update();
    }

    /// <summary>True while the player plays: the view's timer runs only then.</summary>
    public bool IsPlaying => _player.IsPlaying;

    /// <summary>Puts the song where the picture is (see the remarks). Never throws: the speaker's failure is logged.</summary>
    public void Update()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            Follow();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _errors.Report(exception, MusicText.PreviewFailed);
        }
    }

    /// <summary>Stops following the player and silences the speaker; the caller disposes the speaker.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _player.StateChanged -= OnPlayerChanged;
        _player.PropertyChanged -= OnPlayerPropertyChanged;
        try
        {
            _output.Pause();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _errors.Report(exception, MusicText.PreviewFailed);
        }
    }

    private void OnPlayerChanged(object? sender, EventArgs e) => Update();

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.IsPlaying))
        {
            Update();
        }
    }

    private void Follow()
    {
        var state = _player.State;
        var snapshot = state.Snapshot;
        var music = snapshot.Music;
        var path = music.HasMusic && snapshot.Output.Format != VideoFormat.Gif ? music.Path : null;
        if (!string.Equals(path, _loaded, StringComparison.OrdinalIgnoreCase))
        {
            _loaded = path;
            _clock = null;
            _output.Load(path);
        }

        if (state.Clock is not { } clock || path is null || !_output.IsReady || clock.Length <= 0)
        {
            Silence();
            return;
        }

        var now = _now();
        var reading = clock.Read(now);
        var loop = (long)(reading.ClipTicks / clock.Length);
        var within = (long)(reading.ClipTicks % clock.Length);

        // The song plays to its end as the panel knows it; a stale length is corrected by the file's own end.
        var played = music.PlayedLength(clock.Length, music.Length > TimeSpan.Zero ? music.Length.Ticks : long.MaxValue);
        if (within >= played)
        {
            Silence();
            return;
        }

        _output.Volume = music.GainAt(within, played);
        var song = TimeSpan.FromTicks(music.SongTimeAt(within));
        var jumped = !ReferenceEquals(clock, _clock) || loop != _loop;
        _clock = clock;
        _loop = loop;
        if (!_output.IsPlaying)
        {
            _output.Play(song);
            _movedAt = now;
            return;
        }

        // A seek or a loop moves the song at once; a project change that keeps the time (the clock is anchored again at
        // the frame on screen) does not, as long as the song is in step.
        var settled = (now - _movedAt) * TimeSpan.TicksPerSecond / clock.Frequency >= Settle.Ticks;
        if ((jumped || settled) && (_output.Position - song).Duration() > Tolerance)
        {
            _output.Seek(song);
            _movedAt = now;
        }
    }

    private void Silence()
    {
        if (_output.IsPlaying)
        {
            _output.Pause();
        }
    }
}
