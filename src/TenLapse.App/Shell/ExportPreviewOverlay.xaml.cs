using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TenLapse.Presentation.Export;
using TenLapse.Presentation.Preview;
using TenLapse.Presentation.Services;
using TenLapse.Services;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

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
    private static readonly TimeSpan _premiereDelay = TimeSpan.FromMilliseconds(1100);

    private ExportViewModel? _export;
    private PlayerViewModel? _player;
    private IErrorPresenter? _errors;
    private IErrorSink? _log;
    private MediaPlayer? _media;
    private MediaSource? _source;
    private int _premiere;
    private bool _playing;
    private bool _arrived;

    public ExportPreviewOverlay()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Place();
    }

    /// <summary>Follows the export and the preview's player.</summary>
    internal void Attach(ExportViewModel export, PlayerViewModel player, IErrorPresenter errors, IErrorSink log)
    {
        _export = export;
        _player = player;
        _errors = errors;
        _log = log;
        export.PropertyChanged += OnExportPropertyChanged;
        export.PremiereRequested += OnPremiereRequested;
        player.PropertyChanged += OnPlayerPropertyChanged;
        player.StateChanged += OnPlayerStateChanged;
    }

    /// <summary>Stops the video and lets go of everything (the window closes).</summary>
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

        StopPremiere();
        if (_media is { } media)
        {
            media.MediaFailed -= OnMediaFailed;
            media.Dispose();
            _media = null;
        }
    }

    private void OnExportPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_export is not { } export)
        {
            return;
        }

        switch (e.PropertyName)
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
            await Task.Delay(_premiereDelay);
        }

        if (ticket != _premiere || _export is not { IsFinished: true } || _player is not { } player)
        {
            return;
        }

        StopPremiere();
        ticket = ++_premiere;
        player.Pause();
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

    // The video goes and the file is let go of.
    private void StopPremiere()
    {
        _premiere++;
        if (!_playing)
        {
            return;
        }

        _playing = false;
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
            StopPremiere();
        }
    }

    // A new shape or size moves the gate.
    private void OnPlayerStateChanged(object? sender, EventArgs e) => Place();

    // Raised on a media thread: back on the UI thread, the preview plays the frames instead.
    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        var log = _log;
        if (log is null)
        {
            return;
        }

        log.Report(new InvalidOperationException($"The exported video can't be played in the preview: {args.Error} {args.ErrorMessage}", args.ExtendedErrorCode), ExportText.PremiereFailed);
        DispatcherQueue.TryEnqueueGuarded(
            log,
            () =>
            {
                if (!_playing || _player is not { } player)
                {
                    return;
                }

                StopPremiere();
                player.Seek(0);
                player.Play();
            },
            ExportText.PremiereFailed);
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
    }
}
