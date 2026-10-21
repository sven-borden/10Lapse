using TenLapse.Core.Projects;
using TenLapse.Core.Timing;

namespace TenLapse.Tests.Projects;

/// <summary>The project's sequence group (#49): the snapshot's video, Length mode over the whole video, and the edit.</summary>
public sealed class SequenceSettingsTests
{
    private static readonly SequenceSettings _boomerang = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 2, boomerang: true);

    private static ProjectSnapshot Snapshot(int photos, TimingSettings timing, SequenceSettings sequence, VideoFormat format = VideoFormat.Mp4H264)
    {
        var project = new Project(timing, FrameSettings.Default, OutputSettings.Default with { Format = format }, SortMode.Manual, sequence);
        project.AddFrames(TestFrames.Folder("a", photos));
        return project.Snapshot;
    }

    [Fact]
    public void TheSnapshotsVideoIsTheResolvedSequenceAndItsLength()
    {
        // 100 photos at 30 fps, a boomerang played twice, 1 s held before and 2 s after: 30 + 2 × 198 + 1 + 60 frames.
        var snapshot = Snapshot(100, TimingSettings.PerSecond(new Fraction(30, 1)), _boomerang);

        Assert.Equal(new FrameSequence(100, 30, 60, 2, true), snapshot.Playback);
        Assert.Equal(30 + 397 + 60, snapshot.Playback.Length);
        Assert.Equal(FrameTiming.Duration(487, new Fraction(30, 1)), snapshot.Duration);
        Assert.Equal(snapshot.Playback.Duration(snapshot.EffectiveRate.Rate), snapshot.Duration);
    }

    [Fact]
    public void APlainProjectPlaysItsPhotosOnceAsBefore()
    {
        var snapshot = Snapshot(742, TimingSettings.PerSecond(new Fraction(30, 1)), SequenceSettings.Default);

        Assert.True(snapshot.Playback.IsIdentity);
        Assert.Equal(FrameTiming.Duration(742, new Fraction(30, 1)), snapshot.Duration);
        foreach (var target in new[] { 10, 15, 30, 60 })
        {
            var timing = TimingSettings.Length(new Fraction(target, 1));
            Assert.Equal(timing.EffectiveRate(742), Snapshot(742, timing, SequenceSettings.Default).EffectiveRate);
        }
    }

    [Theory]
    [InlineData(10, 120)]
    [InlineData(15, 742)]
    [InlineData(30, 742)]
    [InlineData(60, 300)]
    [InlineData(95, 2000)]
    public void LengthModeTargetsTheWholeVideoHoldsIncluded(int target, int photos)
    {
        foreach (var sequence in new[]
        {
            _boomerang,
            SequenceSettings.Default with { Plays = 3 },
            SequenceSettings.Default with { HoldEnd = TimeSpan.FromSeconds(2.5) },
            new SequenceSettings(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.5), 5, boomerang: true),
        })
        {
            var snapshot = Snapshot(photos, TimingSettings.Length(new Fraction(target, 1)), sequence);
            var rate = snapshot.EffectiveRate;
            if (rate.IsClamped)
            {
                continue;
            }

            // Within a frame of the target: the holds are whole frames at the rate the rest of the photos need.
            var frame = FrameTiming.Duration(1, rate.Rate);
            var error = Math.Abs(snapshot.Duration - (target * FrameTiming.TicksPerSecond));
            Assert.True(error <= frame + 10_000, $"{photos} photos in {target} s as {sequence}: {snapshot.Duration} ticks at {rate.Rate}, {error} off.");
        }
    }

    [Fact]
    public void LengthModeCannotReachATargetTheHoldsAloneFill()
    {
        var holds = new SequenceSettings(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), 1, boomerang: false);

        Assert.Equal(new PlaybackRate(FrameRates.Max, RateClamp.Max), Snapshot(100, TimingSettings.Length(new Fraction(10, 1)), holds).EffectiveRate);
        Assert.Equal(new PlaybackRate(FrameRates.Max, RateClamp.Max), Snapshot(100, TimingSettings.Length(new Fraction(5, 1)), holds).EffectiveRate);
        Assert.Equal(RateClamp.None, Snapshot(100, TimingSettings.Length(new Fraction(15, 1)), holds).EffectiveRate.Clamp);
        Assert.Equal(new PlaybackRate(FrameRates.Min, RateClamp.Min), Snapshot(0, TimingSettings.Length(new Fraction(15, 1)), holds).EffectiveRate);
    }

    [Fact]
    public void AGifPlaysThePhotosOnceAndKeepsTheRest()
    {
        var snapshot = Snapshot(100, TimingSettings.PerSecond(new Fraction(30, 1)), _boomerang, VideoFormat.Gif);

        Assert.Equal(_boomerang, snapshot.Sequence);
        Assert.Equal(_boomerang with { Plays = 1 }, snapshot.EffectiveSequence);
        Assert.Equal(new FrameSequence(100, 30, 60, 1, true), snapshot.Playback);
    }

    [Fact]
    public void ChangingTheSequenceIsOneEditAndTheSameValueIsNone()
    {
        var project = new Project();
        var changes = new List<ProjectChanges>();
        project.Changed += (_, e) => changes.Add(e.Changes);

        project.Sequence = _boomerang;
        project.Sequence = _boomerang with { };

        Assert.Equal([ProjectChanges.Sequence | ProjectChanges.Dirty], changes);
        Assert.Equal(_boomerang, project.Snapshot.Sequence);
        Assert.Throws<ArgumentNullException>(() => project.Sequence = null!);

        // A project made with a sequence (an opened file's) is clean.
        var opened = new Project(TimingSettings.Default, FrameSettings.Default, OutputSettings.Default, SortMode.Name, _boomerang);
        Assert.False(opened.Snapshot.IsDirty);
        Assert.Equal(0, opened.Version);
        Assert.Equal(_boomerang, opened.Sequence);
    }
}
