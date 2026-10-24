using TenLapse.Core.Projects;
using TenLapse.Core.Timing;

namespace TenLapse.Tests.Projects;

/// <summary>
/// The music settings (#50): how long the song plays under the video, the loudness of its fades, where in the song each
/// moment of the video is, and what a value out of range does.
/// </summary>
public sealed class MusicSettingsTests
{
    private const long Second = FrameTiming.TicksPerSecond;
    private static readonly MusicSettings _song = new(@"C:\Music\Sunrise.mp3", TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));

    [Fact]
    public void TheSongPlaysUntilTheVideoOrTheSongEnds()
    {
        // 50 s of song are left after the start: a 20 s video takes 20 of them, a 90 s video all 50.
        Assert.Equal(20 * Second, _song.PlayedLength(20 * Second, 60 * Second));
        Assert.Equal(50 * Second, _song.PlayedLength(90 * Second, 60 * Second));

        // A start past the song's end plays nothing; no song plays nothing.
        Assert.Equal(0, (_song with { Start = TimeSpan.FromSeconds(70) }).PlayedLength(20 * Second, 60 * Second));
        Assert.Equal(0, MusicSettings.Default.PlayedLength(20 * Second, 60 * Second));
    }

    [Fact]
    public void TheMusicRisesAndFallsOnSquareCurvesAndIsSilentOutside()
    {
        var played = 20 * Second;

        Assert.Equal(0.0, _song.GainAt(0, played));
        Assert.Equal(0.25, _song.GainAt(Second, played), 9);
        Assert.Equal(1.0, _song.GainAt(10 * Second, played));
        Assert.Equal(0.25, _song.GainAt(18 * Second, played), 9);
        Assert.Equal(0.0, _song.GainAt(played, played));
        Assert.Equal(0.0, _song.GainAt(-1, played));

        // Fades that overlap in a short video meet below full.
        Assert.True(_song.GainAt(Second, 5 * Second / 2) < 0.25);

        // Without fades the music is full from the first moment to the last.
        var flat = _song with { FadeIn = TimeSpan.Zero, FadeOut = TimeSpan.Zero };
        Assert.Equal(1.0, flat.GainAt(0, played));
        Assert.Equal(1.0, flat.GainAt(played - 1, played));
    }

    [Fact]
    public void EachMomentOfTheVideoIsTheStartPlusItsTimeIntoTheSong() =>
        Assert.Equal(13 * Second, _song.SongTimeAt(3 * Second));

    [Fact]
    public void AValueOutOfRangeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _song with { FadeIn = TimeSpan.FromSeconds(10.1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => _song with { FadeOut = TimeSpan.FromSeconds(-1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => _song with { Start = TimeSpan.FromTicks(-1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => _song with { Length = TimeSpan.FromTicks(-1) });
        Assert.Throws<ArgumentException>(() => _song with { Path = @"Music\Sunrise.mp3" });
        Assert.Throws<ArgumentException>(() => _song with { Path = string.Empty });
        Assert.Equal(MusicSettings.MaxFade, (_song with { FadeIn = MusicSettings.MaxFade }).FadeIn);
        Assert.False(MusicSettings.Default.HasMusic);
        Assert.True(_song.HasMusic);
    }

    [Fact]
    public void TheEditIsOneChangeAndTheSnapshotHasIt()
    {
        var project = new Project();
        var changes = new List<ProjectChanges>();
        project.Changed += (_, e) => changes.Add(e.Changes);

        project.Music = _song;
        project.Music = _song;

        Assert.Equal(_song, project.Snapshot.Music);
        Assert.Single(changes);
        Assert.Equal(ProjectChanges.Music, changes[0] & ~ProjectChanges.Dirty);
        Assert.True(project.Snapshot.IsDirty);
        Assert.Throws<ArgumentNullException>(() => project.Music = null!);
        Assert.Equal(MusicSettings.Default, new Project().Music);
    }

    [Fact]
    public void AProjectFileStoresTheSongBesideItsProjectByARelativePath()
    {
        Assert.Equal(@"Music\Sunrise.mp3", SavedMusic.Relative(@"D:\Trip\Music\Sunrise.mp3", @"D:\Trip"));
        Assert.Equal(@"..\Music\Sunrise.mp3", SavedMusic.Relative(@"D:\Music\Sunrise.mp3", @"D:\Trip"));
        Assert.Null(SavedMusic.Relative(@"C:\Music\Sunrise.mp3", @"D:\Trip"));
        Assert.Null(SavedMusic.Relative(null, @"D:\Trip"));
        Assert.Null(SavedMusic.Relative(@"D:\Music\Sunrise.mp3", null));
    }
}
