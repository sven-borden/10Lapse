using TenLapse.Core.Projects;
using TenLapse.Presentation;
using TenLapse.Presentation.Music;
using TenLapse.Presentation.Preview;
using TenLapse.Presentation.Services;
using TenLapse.Tests.Projects;

namespace TenLapse.Tests.Presentation;

/// <summary>
/// The Music section (#50): Add music reads the file before it joins the project and refuses one that does not play; the
/// sliders write whole tenths within their ranges; the line under them says how the song fits the video; Remove keeps the
/// fades; a missing file and a GIF are said. And the preview's music: it plays in step with the player, at the fades'
/// loudness, and stops with it.
/// </summary>
public sealed class MusicPanelTests
{
    private const string Song = @"C:\Music\Sunrise.mp3";

    // 300 photos at the default 30 fps: a 10 s video.
    private static (ShellViewModel Shell, Project Project, FakeMusicFiles Files, FakeInfoBarService InfoBars, FakeErrorSink Errors, MusicPanelViewModel Panel) Harness(Action<FakeMusicFiles>? setup = null)
    {
        var errors = new FakeErrorSink();
        var shell = new ShellViewModel(new FakeDialogService(), errors);
        var project = shell.CurrentProject.Model;
        project.AddFrames(TestFrames.Folder("a", 300));
        var files = new FakeMusicFiles();
        setup?.Invoke(files);
        var infoBars = new FakeInfoBarService();
        return (shell, project, files, infoBars, errors, new MusicPanelViewModel(shell, files, infoBars, errors));
    }

    [Fact]
    public void ANewProjectHasNoMusic()
    {
        var (_, project, _, _, _, panel) = Harness();

        Assert.False(panel.HasMusic);
        Assert.True(panel.ShowsEmpty);
        Assert.False(panel.ShowsControls);
        Assert.False(panel.ShowsFit);
        Assert.False(panel.ShowsGifSilent);
        Assert.False(panel.IsMissing);
        Assert.Equal(MusicSettings.Default, project.Music);
    }

    [Fact]
    public async Task AddMusicReadsTheSongAndStartsAtItsBeginningWithTheFadesSet()
    {
        var (_, project, files, _, _, panel) = Harness();
        files.Add(Song, TimeSpan.FromMinutes(3));
        files.Picked = Song;
        panel.FadeInSeconds = 3;

        await panel.AddCommand.ExecuteAsync(null);

        Assert.Equal(new MusicSettings(Song, TimeSpan.FromMinutes(3), TimeSpan.Zero, TimeSpan.FromSeconds(3), MusicSettings.Default.FadeOut), project.Music);
        Assert.True(panel.HasMusic);
        Assert.False(panel.ShowsEmpty);
        Assert.True(panel.ShowsControls);
        Assert.Equal("Sunrise.mp3", panel.FileName);
        Assert.Equal(MusicText.Length(TimeSpan.FromMinutes(3)), panel.LengthText);
        Assert.Equal(MusicText.Song("Sunrise.mp3", TimeSpan.FromMinutes(3)), panel.SongName);
        Assert.Equal(180, panel.StartMaximum);
        Assert.Equal(MusicText.FitsVideo, panel.FitText);
        Assert.Null(files.StartFolder);

        // Replace opens in the song's folder.
        files.Picked = null;
        await panel.AddCommand.ExecuteAsync(null);
        Assert.Equal(@"C:\Music", files.StartFolder);
        Assert.Equal(Song, project.Music.Path);
    }

    [Fact]
    public async Task AFileThatDoesNotPlayIsRefusedWithAnInfoBar()
    {
        var (_, project, files, infoBars, errors, panel) = Harness();
        files.Picked = @"C:\Music\notes.mp3";

        await panel.AddCommand.ExecuteAsync(null);

        Assert.Equal(MusicSettings.Default, project.Music);
        var shown = Assert.Single(infoBars.Shown);
        Assert.Equal(InfoBarSeverity.Critical, shown.Severity);
        Assert.Equal(MusicText.UnreadableFile("notes.mp3"), shown.Message);
        Assert.Contains(errors.Reported, report => report.Context == MusicText.ChooseFailed);
        Assert.Empty(errors.Shown);
        Assert.True(panel.AddCommand.CanExecute(null));
    }

    [Fact]
    public void TheSlidersWriteWholeTenthsWithinTheirRanges()
    {
        var (_, project, _, _, _, panel) = Harness(files => files.Add(Song, TimeSpan.FromSeconds(30)));
        project.Music = MusicSettings.Default with { Path = Song, Length = TimeSpan.FromSeconds(30) };

        panel.StartSeconds = 12.34;
        panel.FadeInSeconds = 10.6;
        panel.FadeOutSeconds = double.NaN;

        Assert.Equal(TimeSpan.FromSeconds(12.3), project.Music.Start);
        Assert.Equal(MusicSettings.MaxFade, project.Music.FadeIn);
        Assert.Equal(MusicSettings.Default.FadeOut, project.Music.FadeOut);
        Assert.Equal(MusicText.Length(TimeSpan.FromSeconds(12.3)), panel.StartText);
        Assert.Equal(MusicText.Fade(MusicSettings.MaxFade), panel.FadeInText);

        panel.StartSeconds = 90;
        Assert.Equal(TimeSpan.FromSeconds(30), project.Music.Start);
    }

    [Fact]
    public void TheLineUnderTheSlidersSaysHowTheSongFitsTheVideo()
    {
        var (_, project, _, _, _, panel) = Harness(files => files.Add(Song, TimeSpan.FromSeconds(6)));

        // A 6 s song under the 10 s video ends 4 s early; started 2 s in, 6 s early; started at its end, it plays nothing.
        project.Music = MusicSettings.Default with { Path = Song, Length = TimeSpan.FromSeconds(6) };
        Assert.Equal(MusicText.EndsEarlyBy(TimeSpan.FromSeconds(4)), panel.FitText);
        Assert.True(panel.ShowsFit);

        panel.StartSeconds = 2;
        Assert.Equal(MusicText.EndsEarlyBy(TimeSpan.FromSeconds(6)), panel.FitText);

        panel.StartSeconds = 6;
        Assert.Equal(MusicText.StartsAfterEnd, panel.FitText);

        // A longer video: the change of the photos is followed too.
        project.Music = project.Music with { Start = TimeSpan.Zero, Length = TimeSpan.FromSeconds(20) };
        Assert.Equal(MusicText.FitsVideo, panel.FitText);
        project.AddFrames(TestFrames.Folder("b", 450));
        Assert.Equal(MusicText.EndsEarlyBy(TimeSpan.FromSeconds(5)), panel.FitText);
    }

    [Fact]
    public void RemoveTakesTheSongOutAndKeepsTheFades()
    {
        var (_, project, _, _, _, panel) = Harness(files => files.Add(Song, TimeSpan.FromSeconds(30)));
        project.Music = new MusicSettings(Song, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(6));

        panel.RemoveCommand.Execute(null);

        Assert.Equal(MusicSettings.Default with { FadeIn = TimeSpan.FromSeconds(0.5), FadeOut = TimeSpan.FromSeconds(6) }, project.Music);
        Assert.False(panel.HasMusic);
        Assert.True(panel.ShowsEmpty);
    }

    [Fact]
    public void AMissingFileIsSaidInPlaceOfTheSliders()
    {
        var (_, project, files, _, _, panel) = Harness();

        project.Music = MusicSettings.Default with { Path = Song, Length = TimeSpan.FromSeconds(30) };

        Assert.True(panel.IsMissing);
        Assert.False(panel.ShowsControls);
        Assert.False(panel.ShowsFit);
        Assert.Equal(MusicText.MissingFile("Sunrise.mp3"), panel.MissingText);

        // The section looks once per song, not on every edit.
        var asked = files.ExistsAsked;
        project.Music = project.Music with { FadeIn = TimeSpan.Zero };
        Assert.Equal(asked, files.ExistsAsked);
    }

    [Fact]
    public void AGifSaysItHasNoSound()
    {
        var (_, project, _, _, _, panel) = Harness(files => files.Add(Song, TimeSpan.FromSeconds(30)));
        project.Music = MusicSettings.Default with { Path = Song, Length = TimeSpan.FromSeconds(30) };
        Assert.False(panel.ShowsGifSilent);

        project.Output = project.Output with { Format = VideoFormat.Gif };

        Assert.True(panel.ShowsGifSilent);
    }

    [Fact]
    public void ANewProjectStartsWithoutMusic()
    {
        var (shell, project, _, _, _, panel) = Harness(files => files.Add(Song, TimeSpan.FromSeconds(30)));
        project.Music = MusicSettings.Default with { Path = Song, Length = TimeSpan.FromSeconds(30) };
        Assert.True(panel.HasMusic);

        Assert.True(shell.ReplaceWithoutAsking(() => new Project()));

        Assert.False(panel.HasMusic);
        panel.FadeInSeconds = 4;
        Assert.Equal(TimeSpan.FromSeconds(4), shell.CurrentProject.Model.Music.FadeIn);
        Assert.Equal(TimeSpan.FromSeconds(30), project.Music.Length);
    }

    [Fact]
    public void ThePreviewPlaysTheSongFromWhereTheVideoIsAtItsFadesLoudness()
    {
        var (player, project, output, preview, clock) = PreviewHarness();
        project.Music = new MusicSettings(Song, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        preview.Update();
        Assert.Equal(Song, output.Loaded);
        Assert.False(output.IsPlaying);

        player.Play();
        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(output.Plays));
        Assert.Equal(0.0, output.Volume);

        // One second in, in step: no move; the fade in is a quarter of the way up.
        clock.Now = 1000;
        output.Position = TimeSpan.FromSeconds(21.05);
        preview.Update();
        Assert.Empty(output.Seeks);
        Assert.Equal(0.25, output.Volume, 9);

        // Drifted 300 ms: moved back.
        clock.Now = 2000;
        output.Position = TimeSpan.FromSeconds(22.3);
        preview.Update();
        Assert.Equal(TimeSpan.FromSeconds(22), Assert.Single(output.Seeks));

        // A seek on the scrubber moves it at once; a pause stops it.
        player.SeekPosition(150);
        Assert.Equal(TimeSpan.FromSeconds(25), output.Seeks[^1]);
        player.Pause();
        Assert.False(output.IsPlaying);
    }

    [Fact]
    public void ThePreviewIsSilentForAGifPastTheSongsEndAndUntilTheSongIsReady()
    {
        var (player, project, output, preview, clock) = PreviewHarness();
        output.Ready = false;
        project.Music = new MusicSettings(Song, TimeSpan.FromSeconds(3), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
        player.Play();
        Assert.Empty(output.Plays);

        output.Ready = true;
        preview.Update();
        Assert.Single(output.Plays);

        // The 3 s song under the 10 s video: silent from 3 s.
        clock.Now = 4000;
        preview.Update();
        Assert.False(output.IsPlaying);

        // A GIF has no sound: nothing is loaded.
        project.Output = project.Output with { Format = VideoFormat.Gif };
        preview.Update();
        Assert.Null(output.Loaded);
    }

    private static (PlayerViewModel Player, Project Project, FakeMusicOutput Output, MusicPreview Preview, FakeClock Clock) PreviewHarness()
    {
        var errors = new FakeErrorSink();
        var shell = new ShellViewModel(new FakeDialogService(), errors);
        var project = shell.CurrentProject.Model;
        project.AddFrames(TestFrames.Folder("a", 300));
        var clock = new FakeClock();
        var player = new PlayerViewModel(shell, errors, () => clock.Now, 1000);
        var output = new FakeMusicOutput();
        return (player, project, output, new MusicPreview(player, output, errors, () => clock.Now), clock);
    }

    private sealed class FakeClock
    {
        public long Now { get; set; }
    }

    private sealed class FakeMusicFiles : IMusicFiles
    {
        private readonly Dictionary<string, TimeSpan> _songs = new(StringComparer.OrdinalIgnoreCase);

        public string? Picked { get; set; }

        public string? StartFolder { get; private set; }

        public int ExistsAsked { get; private set; }

        public void Add(string path, TimeSpan length) => _songs[path] = length;

        public Task<string?> PickAsync(string? startFolder)
        {
            StartFolder = startFolder;
            return Task.FromResult(Picked);
        }

        public Task<MusicFileInfo> ProbeAsync(string path) =>
            _songs.TryGetValue(path, out var length)
                ? Task.FromResult(new MusicFileInfo(length))
                : Task.FromException<MusicFileInfo>(new InvalidDataException("Not a song."));

        public bool Exists(string path)
        {
            ExistsAsked++;
            return _songs.ContainsKey(path);
        }
    }

    private sealed class FakeMusicOutput : IMusicOutput
    {
        public bool Ready { get; set; } = true;

        public string? Loaded { get; private set; }

        public List<TimeSpan> Plays { get; } = [];

        public List<TimeSpan> Seeks { get; } = [];

        public bool IsReady => Ready && Loaded is not null;

        public bool IsPlaying { get; private set; }

        public TimeSpan Position { get; set; }

        public double Volume { get; set; } = 1;

        public void Load(string? path)
        {
            Loaded = path;
            IsPlaying = false;
        }

        public void Play(TimeSpan position)
        {
            Plays.Add(position);
            Position = position;
            IsPlaying = true;
        }

        public void Seek(TimeSpan position)
        {
            Seeks.Add(position);
            Position = position;
        }

        public void Pause() => IsPlaying = false;

        public void Dispose()
        {
        }
    }
}
