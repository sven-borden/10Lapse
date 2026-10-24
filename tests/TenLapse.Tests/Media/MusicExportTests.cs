using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Audio;
using TenLapse.Media.Export;
using TenLapse.Media.Inspection;
using Windows.UI;
using static TenLapse.Tests.Media.MediaSkips;
using static TenLapse.Tests.Media.WaveFiles;

namespace TenLapse.Tests.Media;

/// <summary>
/// The music under an MP4 (#50), read back from the file the export wrote: the song from its start point, faded in and
/// out, as long as the video, at the rate the AAC encoder takes, with the video's frames untouched.
/// </summary>
/// <remarks>
/// The AAC round trip shifts the sound by the encoder's priming (about 2,100 samples, under 50 ms) and blurs a sine's level
/// a little, so the checks measure 50 ms windows away from the edges they are about, with margins for both.
/// </remarks>
[Collection(nameof(RealEncoderCollection))]
public sealed class MusicExportTests : IDisposable
{
    private static readonly Fraction _rate = new(30, 1);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TenLapse.Tests", Guid.NewGuid().ToString("N"));

    public MusicExportTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A reader still closing the file; the temp folder is cleaned up by Windows eventually.
        }
    }

    [Fact]
    public async Task TheSongPlaysFromItsStartPointFadedInAndOutUnderTheWholeVideo()
    {
        // The song is loud for its first 1.5 s and quiet after; starting 1 s in, the video's first half second is loud,
        // the rest quiet. A 0.2 s fade in and a 0.5 s fade out, under a 2 s video.
        SkipWithoutH264Encoder();
        var song = Write(Path.Combine(_directory, "song.wav"), 44_100, 2, new Tone(1.5, 440, 0.5), new Tone(8.5, 440, 0.1));
        var music = new MusicSettings(song, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.5));
        var path = Path.Combine(_directory, "music.mp4");

        await ExportAsync(path, frames: 60, music);

        var (rate, sound) = ReadSound(path);
        Assert.Equal(44_100, rate);
        Assert.InRange(sound.Length / 2, (int)(1.98 * rate), (int)(2.1 * rate));
        Assert.InRange(Rms(sound, rate, 0, 0.05), 0, 0.05);
        Assert.InRange(Rms(sound, rate, 0.3, 0.05), 0.28, 0.42);
        Assert.InRange(Rms(sound, rate, 1.0, 0.05), 0.055, 0.085);
        Assert.InRange(Rms(sound, rate, (sound.Length / 2.0 / rate) - 0.06, 0.05), 0, 0.03);

        // The picture is as it is without music: every frame at its exact time.
        var info = VideoFileReader.Read(path);
        Assert.Equal(60, info.FrameCount);
        Assert.Equal(Enumerable.Range(0, 60).Select(i => FrameTiming.FrameStart(i, _rate)), info.SampleTimes);
    }

    [Fact]
    public async Task ASongThatEndsFirstLeavesTheRestOfTheVideoSilent()
    {
        // A 1 s mono song at 48 kHz under a 2 s video, no fades: it plays on both sides, then silence to the video's end.
        SkipWithoutH264Encoder();
        var song = Write(Path.Combine(_directory, "short.wav"), 48_000, 1, new Tone(1, 440, 0.5));
        var music = new MusicSettings(song, TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
        var path = Path.Combine(_directory, "short.mp4");

        await ExportAsync(path, frames: 60, music);

        var (rate, sound) = ReadSound(path);
        Assert.Equal(48_000, rate);
        Assert.InRange(sound.Length / 2, (int)(1.98 * rate), (int)(2.1 * rate));
        Assert.InRange(Rms(sound, rate, 0.5, 0.05), 0.3, 0.42);
        Assert.InRange(RightRms(sound, rate, 0.5, 0.05), 0.3, 0.42);
        Assert.InRange(Rms(sound, rate, 1.5, 0.05), 0, 0.01);
    }

    [Fact]
    public async Task ASongAtARateTheEncoderDoesNotTakeIsResampledTo48kHzAtItsPitch()
    {
        SkipWithoutH264Encoder();
        var song = Write(Path.Combine(_directory, "low.wav"), 22_050, 2, new Tone(5, 1000, 0.5));
        var music = new MusicSettings(song, TimeSpan.FromSeconds(5), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
        var path = Path.Combine(_directory, "low.mp4");

        await ExportAsync(path, frames: 60, music);

        var (rate, sound) = ReadSound(path);
        Assert.Equal(48_000, rate);
        Assert.InRange(RisingCrossings(sound, rate, 0.5, 1.0), 990, 1010);
        Assert.InRange(Rms(sound, rate, 1.0, 0.05), 0.3, 0.42);
    }

    [Fact]
    public async Task ASongThatCannotBeReadFailsTheExportBeforeTheFileExists()
    {
        SkipWithoutH264Encoder();
        var missing = Path.Combine(_directory, "gone.mp3");
        var path = Path.Combine(_directory, "silent.mp4");

        var exception = await Assert.ThrowsAsync<VideoExportException>(() => ExportAsync(path, frames: 10, new MusicSettings(missing, TimeSpan.FromSeconds(3), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero)));

        Assert.Equal(VideoExportFailure.MusicUnreadable, exception.Failure);
        Assert.Equal(missing, exception.SourcePath);
        Assert.False(File.Exists(path));
        Assert.Equal(TenLapse.Presentation.Music.MusicText.ExportFailed, new TenLapse.Services.VideoExportErrorMapper().Map(exception)!.Text);
    }

    [Fact]
    public void AnMp4CarriesTheProjectsMusicAndAGifNone()
    {
        var music = MusicSettings.Default with { Path = @"C:\Music\Sunrise.mp3", Length = TimeSpan.FromMinutes(3) };
        var frames = Enumerable.Range(0, 90).Select(i => new Frame($@"C:\p\{i:D3}.jpg", 1, DateTime.UtcNow));
        var mp4 = Golden.GoldenExport.Snapshot(frames, TimingSettings.PerSecond(_rate), FrameAspect.Ratio16x9, FitMode.Fill, music: music);
        var gif = Golden.GoldenExport.Snapshot(frames, TimingSettings.PerSecond(_rate), FrameAspect.Ratio16x9, FitMode.Fill, VideoFormat.Gif, music: music);

        Assert.Equal(new MusicTrack(music, FrameTiming.Duration(90, _rate)), ProjectExporter.SettingsFor(mp4).Music);
        Assert.Null(ProjectExporter.SettingsFor(gif).Music);
        Assert.Null(ProjectExporter.SettingsFor(Golden.GoldenExport.Snapshot(frames, TimingSettings.PerSecond(_rate), FrameAspect.Ratio16x9, FitMode.Fill)).Music);
    }

    [Fact]
    public void TheMixerFillsTheVideoAndFadesTheSong()
    {
        // No encoder needed: the PCM the encoder would get. A 0.5 s song under a 1 s video with a 0.1 s fade out.
        var song = Write(Path.Combine(_directory, "mix.wav"), 44_100, 2, new Tone(0.5, 440, 0.5));
        var music = new MusicSettings(song, TimeSpan.FromSeconds(0.5), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(0.1));
        using var mixer = new MusicMixer(new MusicTrack(music, FrameTiming.TicksPerSecond));

        var pcm = new short[2 * 44_100 * 2];
        var rendered = 0;
        int count;
        while ((count = mixer.Render(pcm.AsSpan(rendered * 2))) > 0)
        {
            rendered += count;
        }

        Assert.Equal(44_100, mixer.SampleRate);
        Assert.Equal(44_100, rendered);
        var sound = pcm.Take(rendered * 2).Select(value => value / 32768f).ToArray();
        Assert.InRange(Rms(sound, 44_100, 0.2, 0.05), 0.33, 0.37);
        Assert.InRange(Rms(sound, 44_100, 0.45, 0.04), 0.001, 0.12);
        Assert.Equal(0.0, Rms(sound, 44_100, 0.5, 0.5));
    }

    [Fact]
    public void AFileWithoutSoundIsRefusedWhenChosen()
    {
        var song = Write(Path.Combine(_directory, "probe.wav"), 44_100, 2, new Tone(3, 440, 0.5));
        Assert.Equal(TimeSpan.FromSeconds(3), MusicFile.Read(song).Length);

        var text = Path.Combine(_directory, "notes.mp3");
        File.WriteAllText(text, "not a song");
        Assert.Throws<InvalidDataException>(() => MusicFile.Read(text));
        Assert.Throws<InvalidDataException>(() => MusicFile.Read(Path.Combine(_directory, "nowhere.mp3")));
    }

    private Task ExportAsync(string path, int frames, MusicSettings music) =>
        ExportOrSkipAsync(
            new SolidFrames(frames, 320, 180, Color.FromArgb(255, 40, 90, 160)),
            path,
            new VideoExportSettings(320, 180, _rate) { Music = new MusicTrack(music, FrameTiming.Duration(frames, _rate)) });

    // The file's first audio stream, decoded whole, as interleaved stereo.
    private static (int Rate, float[] Sound) ReadSound(string path)
    {
        using var decoder = new MusicDecoder(path);
        var sound = new List<float>();
        var piece = new float[8192];
        int frames;
        while ((frames = decoder.Read(piece)) > 0)
        {
            sound.AddRange(piece.Take(frames * 2));
        }

        return (decoder.SampleRate, [.. sound]);
    }

    private static double RightRms(float[] stereo, int rate, double start, double length) =>
        Rms([.. stereo.Skip(1)], rate, start, length);
}
