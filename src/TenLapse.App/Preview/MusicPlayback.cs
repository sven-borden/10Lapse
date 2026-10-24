using System.ComponentModel;
using Microsoft.UI.Dispatching;
using TenLapse.Presentation.Music;
using TenLapse.Presentation.Preview;
using TenLapse.Presentation.Services;
using TenLapse.Services;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace TenLapse.Preview;

/// <summary>
/// The preview's music (#50): a media player for the song, Presentation's <see cref="MusicPreview"/> keeping it in step
/// with the player, and the timer that asks it to about 20 times a second while the preview plays (the fades' steps).
/// </summary>
internal sealed class MusicPlayback : IDisposable
{
    private static readonly TimeSpan _interval = TimeSpan.FromMilliseconds(50);

    private readonly PlayerViewModel _player;
    private readonly MediaPlayerMusicOutput _output;
    private readonly MusicPreview _preview;
    private readonly DispatcherQueueTimer _timer;
    private bool _disposed;

    public MusicPlayback(PlayerViewModel player, DispatcherQueue queue, IErrorSink errors)
    {
        _player = player;
        _output = new MediaPlayerMusicOutput(queue, errors);
        _preview = new MusicPreview(player, _output, errors);
        _timer = queue.CreateTimer();
        _timer.Interval = _interval;
        _timer.Tick += OnTick;
        _player.PropertyChanged += OnPlayerPropertyChanged;
        Follow();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _player.PropertyChanged -= OnPlayerPropertyChanged;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _preview.Dispose();
        _output.Dispose();
    }

    private void OnTick(DispatcherQueueTimer sender, object args) => _preview.Update();

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.IsPlaying))
        {
            Follow();
        }
    }

    private void Follow()
    {
        if (_disposed)
        {
            return;
        }

        if (_player.IsPlaying)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }
}

/// <summary>
/// The speaker <see cref="MusicPreview"/> plays the song through (#50): a <see cref="MediaPlayer"/> kept out of the system's
/// media controls (the song is the preview's, not a track the media keys or the volume flyout should name). A song is
/// opened from its path in the background; <see cref="IsReady"/> once the player has opened it. A song that does not open
/// is logged and stays silent: the Music section says when its file is missing, and the export says when it cannot read it.
/// </summary>
internal sealed class MediaPlayerMusicOutput : IMusicOutput
{
    private readonly MediaPlayer _media;
    private readonly DispatcherQueue _queue;
    private readonly IErrorSink _errors;
    private int _generation;
    private bool _ready;
    private bool _playing;
    private bool _disposed;

    public MediaPlayerMusicOutput(DispatcherQueue queue, IErrorSink errors)
    {
        _queue = queue;
        _errors = errors;
        _media = new MediaPlayer
        {
            AutoPlay = false,
            IsLoopingEnabled = false,
            AudioCategory = MediaPlayerAudioCategory.Media,
        };
        _media.CommandManager.IsEnabled = false;
        _media.MediaOpened += OnMediaOpened;
        _media.MediaFailed += OnMediaFailed;
    }

    /// <inheritdoc />
    public bool IsReady => _ready;

    /// <inheritdoc />
    public bool IsPlaying => _playing;

    /// <inheritdoc />
    public TimeSpan Position => _media.PlaybackSession.Position;

    /// <inheritdoc />
    public double Volume
    {
        get => _media.Volume;
        set => _media.Volume = Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1);
    }

    /// <inheritdoc />
    public void Load(string? path)
    {
        var generation = ++_generation;
        _ready = false;
        Pause();
        _media.Source = null;
        if (path is not null)
        {
            _ = OpenAsync(path, generation);
        }
    }

    /// <inheritdoc />
    public void Play(TimeSpan position)
    {
        _media.PlaybackSession.Position = position;
        _media.Play();
        _playing = true;
    }

    /// <inheritdoc />
    public void Seek(TimeSpan position) => _media.PlaybackSession.Position = position;

    /// <inheritdoc />
    public void Pause()
    {
        if (_playing)
        {
            _playing = false;
            _media.Pause();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _generation++;
        _media.MediaOpened -= OnMediaOpened;
        _media.MediaFailed -= OnMediaFailed;
        _media.Dispose();
    }

    // The file is opened off the UI thread; a newer Load (or Dispose) meanwhile wins.
    private async Task OpenAsync(string path, int generation)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            if (generation == _generation && !_disposed)
            {
                _media.Source = MediaSource.CreateFromStorageFile(file);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            _errors.Report(exception, MusicText.PreviewFailed);
        }
    }

    // Raised on a media thread.
    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        var generation = _generation;
        _queue.TryEnqueueGuarded(
            _errors,
            () =>
            {
                if (generation == _generation && !_disposed && _media.Source is not null)
                {
                    _ready = true;
                }
            },
            MusicText.PreviewFailed);
    }

    // Raised on a media thread: logged; the preview plays on in silence.
    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) =>
        _errors.Report(new InvalidOperationException($"The music can't be played in the preview: {args.Error} {args.ErrorMessage}", args.ExtendedErrorCode), MusicText.PreviewFailed);
}
