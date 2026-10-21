using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using TenLapse.Presentation.Export;
using TenLapse.Presentation.Motion;
using TenLapse.Presentation.Preview;
using TenLapse.Presentation.Services;
using TenLapse.Services;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;

namespace TenLapse.Shell;

/// <summary>
/// The pill and the premiere over the preview (see the comment at the top of ExportPreviewOverlay.xaml).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the file plays, not the frames.</b> The finished state could replay the project through the preview's renderer
/// (cheap, the same picture), but playing the MP4 that was just written proves the file: it opens, decodes and runs at its
/// rate on this PC. If it cannot play (a codec Windows lacks), the preview plays the frames instead and the log says why.
/// </para>
/// <para>
/// The player holds the file open; an export to the same name would fail its early check (<c>OutputFileInUse</c>). So the
/// video goes, and the file is let go of, as soon as the finished state does: before the next Save dialog opens.
/// </para>
/// </remarks>
public sealed partial class ExportPreviewOverlay : UserControl
{
    private ExportViewModel? _export;
    private PlayerViewModel? _player;
    private IErrorPresenter? _errors;
    private IErrorSink? _log;
    private IInfoBarService? _infoBars;
    private MediaPlayer? _media;
    private MediaSource? _source;
    private int _premiere;
    private bool _playing;
    private bool _arrived;

    public ExportPreviewOverlay()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Guarded(Place);
    }

    /// <summary>Follows the export and the preview's player; <paramref name="infoBars"/> says when the video can't play here.</summary>
    internal void Attach(ExportViewModel export, PlayerViewModel player, IErrorPresenter errors, IErrorSink log, IInfoBarService infoBars)
    {
        _export = export;
        _player = player;
        _errors = errors;
        _log = log;
        _infoBars = infoBars;
        export.PropertyChanged += OnExportPropertyChanged;
        export.PremiereRequested += OnPremiereRequested;
        player.PropertyChanged += OnPlayerPropertyChanged;
        player.StateChanged += OnPlayerStateChanged;
    }

    /// <summary>Stops the video and lets go of everything (the window closes).</summary>
    /// <summary>Where the premiere's video is, while it plays (the theme crossfade leaves it out of its picture); else null.</summary>
    internal PhotoArea? PhotoOnScreen()
    {
        FrameworkElement shown = Gif.Visibility == Visibility.Visible ? Gif : Video;
        return _playing && shown.Visibility == Visibility.Visible && shown.ActualWidth > 0 && shown.ActualHeight > 0
            ? new PhotoArea(shown, new Rect(0, 0, shown.ActualWidth, shown.ActualHeight), 0f)
            : null;
    }

    internal void Detach()
    {
        if (_export is { } export)
        {
            export.PropertyChanged -= OnExportPropertyChanged;
            export.PremiereRequested -= OnPremiereRequested;
        }

        if (_player is { } player)
        {
            player.PropertyChanged -= OnPlayerPropertyChanged;
            player.StateChanged -= OnPlayerStateChanged;
        }

        // The player is not disposed: the element still holds it while the window tears down, and disposing it under the
        // element ended the process with a stowed exception (or hung it) on close. Without a source it holds nothing.
        StopPremiere();
        if (_media is { } media)
        {
            media.MediaFailed -= OnMediaFailed;
            _media = null;
        }
    }

    // An event handler that throws would end the process (a stowed exception): logged instead. Nothing runs before Attach.
    private void Guarded(Action action) => _log?.RunLogged(action, ExportText.PremiereFailed);

    private void OnExportPropertyChanged(object? sender, PropertyChangedEventArgs e) => Guarded(() => FollowExport(e.PropertyName));

    private void FollowExport(string? property)
    {
        if (_export is not { } export)
        {
            return;
        }

        switch (property)
        {
            case nameof(ExportViewModel.IsExporting):
                Pill.Visibility = export.IsExporting ? Visibility.Visible : Visibility.Collapsed;
                Place();
                break;
            case nameof(ExportViewModel.RenderingText):
                PillText.Text = export.RenderingText;
                break;
            case nameof(ExportViewModel.IsFinished):
                _arrived = export.IsFinished;
                if (!export.IsFinished)
                {
                    StopPremiere();
                }

                break;
        }
    }

    // The first request after the export ends waits the motion spec's 1.1 s (the check draws first); Play starts at once.
    private void OnPremiereRequested(object? sender, string path)
    {
        var wait = _arrived;
        _arrived = false;
        if (_errors is { } errors)
        {
            _ = errors.GuardAsync(() => PremiereAsync(path, wait), ExportText.PremiereFailed);
        }
    }

    private async Task PremiereAsync(string path, bool wait)
    {
        var ticket = ++_premiere;
        if (wait)
        {
            await Task.Delay(Themes.MotionTokens.Plan(MotionCatalogue.FinishedPremiere).Duration);
        }

        if (ticket != _premiere || _export is not { IsFinished: true } || _player is not { } player)
        {
            return;
        }

        StopPremiere();
        ticket = ++_premiere;
        player.Pause();
        if (string.Equals(Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase))
        {
            await PremiereGifAsync(path, ticket);
            return;
        }

        var file = await StorageFile.GetFileFromPathAsync(path);
        if (ticket != _premiere || _export is not { IsFinished: true })
        {
            return;
        }

        if (_media is null)
        {
            _media = new MediaPlayer { AutoPlay = false, IsMuted = true };
            _media.MediaFailed += OnMediaFailed;
        }

        _source = MediaSource.CreateFromStorageFile(file);
        _media.Source = _source;
        Video.SetMediaPlayer(_media);
        Place();
        Video.Visibility = Visibility.Visible;
        _playing = true;
        _media.Play();
    }

    // An animated GIF (#51) plays through XAML's own decoder, from a copy in memory, so the file is not held open (an
    // export to the same name must be able to replace it). A GIF that will not decode falls back as a video does.
    private async Task PremiereGifAsync(string path, int ticket)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        if (ticket != _premiere || _export is not { IsFinished: true })
        {
            return;
        }

        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(bytes.AsBuffer());
        stream.Seek(0);
        var image = new BitmapImage { AutoPlay = true };
        image.ImageFailed += OnGifFailed;
        await image.SetSourceAsync(stream);
        if (ticket != _premiere || _export is not { IsFinished: true })
        {
            image.ImageFailed -= OnGifFailed;
            return;
        }

        Gif.Source = image;
        Place();
        Gif.Visibility = Visibility.Visible;
        _playing = true;
    }

    private void OnGifFailed(object sender, ExceptionRoutedEventArgs e) =>
        Guarded(() => FallBackToFrames(new InvalidOperationException($"The exported GIF can't be shown in the preview: {e.ErrorMessage}")));

    // The video goes and the file is let go of.
    private void StopPremiere()
    {
        _premiere++;
        if (!_playing)
        {
            return;
        }

        _playing = false;
        if (Gif.Source is BitmapImage gif)
        {
            gif.ImageFailed -= OnGifFailed;
            gif.Stop();
        }

        Gif.Source = null;
        Gif.Visibility = Visibility.Collapsed;
        Video.Visibility = Visibility.Collapsed;
        if (_media is { } media)
        {
            media.Pause();
            media.Source = null;
        }

        _source?.Dispose();
        _source = null;
    }

    // The user took the transport (played, scrubbed, stepped): the preview is theirs again.
    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_playing && e.PropertyName is nameof(PlayerViewModel.IsPlaying) or nameof(PlayerViewModel.CurrentFrame))
        {
            Guarded(StopPremiere);
        }
    }

    // A new shape or size moves the gate.
    private void OnPlayerStateChanged(object? sender, EventArgs e) => Guarded(Place);

    // Raised on a media thread: back on the UI thread, the preview plays the frames instead, and a Caution InfoBar says the
    // video is saved but did not play here (never silent: the user would take the frames for the file).
    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        var log = _log;
        if (log is null)
        {
            return;
        }

        log.Report(new InvalidOperationException($"The exported video can't be played in the preview: {args.Error} {args.ErrorMessage}", args.ExtendedErrorCode), ExportText.PremiereFailed);
        DispatcherQueue.TryEnqueueGuarded(log, FallBackToFrames, ExportText.PremiereFailed);
    }

    // On the UI thread: the preview plays the frames instead of the file, and a Caution InfoBar says the file is saved but
    // did not play here.
    private void FallBackToFrames(Exception reason)
    {
        _log?.Report(reason, ExportText.PremiereFailed);
        FallBackToFrames();
    }

    private void FallBackToFrames()
    {
        if (!_playing || _player is not { } player)
        {
            return;
        }

        StopPremiere();
        player.Seek(0);
        player.Play();
        _infoBars?.Show(Presentation.Services.InfoBarSeverity.Caution, ExportText.PremiereCantPlay);
    }

    // The pill sits 12 px inside the gate's top-left corner; the video covers the gate exactly.
    private void Place()
    {
        if (_player is not { } player || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var gate = PreviewLayout.Gate(ActualWidth, ActualHeight, player.State.Snapshot.VideoSize);
        if (gate.IsEmpty)
        {
            return;
        }

        Pill.Margin = new Thickness(gate.X + 12, gate.Y + 12, 0, 0);
        Video.Margin = new Thickness(gate.X, gate.Y, 0, 0);
        Video.Width = gate.Width;
        Video.Height = gate.Height;
        Gif.Margin = Video.Margin;
        Gif.Width = gate.Width;
        Gif.Height = gate.Height;
    }
}
