using TenLapse.Core.Projects;
using TenLapse.Core.Timing;

namespace TenLapse.Tests.Timing;

/// <summary>
/// The frame sequence (#49): which photo each video frame shows with holds, plays and a boomerang, against a reference
/// built the slow way (the frames listed one by one), over every small case and long runs, and the timing that follows.
/// </summary>
public sealed class FrameSequenceTests
{
    // The sequence written out frame by frame, as the issue describes it: the first photo held, the plays (out and back
    // for a boomerang, never showing a turning photo twice in a row), the last frame held.
    private static List<int> Reference(int photos, int holdStart, int holdEnd, int plays, bool boomerang)
    {
        var core = new List<int>();
        if (photos == 0)
        {
            return core;
        }

        for (var play = 0; play < plays; play++)
        {
            for (var photo = 0; photo < photos; photo++)
            {
                if (play == 0 || photo > 0 || !boomerang || photos < 2)
                {
                    core.Add(photo);
                }
            }

            if (boomerang && photos >= 2)
            {
                for (var photo = photos - 2; photo >= 0; photo--)
                {
                    core.Add(photo);
                }
            }
        }

        var frames = Enumerable.Repeat(core[0], holdStart).ToList();
        frames.AddRange(core);
        frames.AddRange(Enumerable.Repeat(core[^1], holdEnd));
        return frames;
    }

    public static TheoryData<int, int, int, int, bool> SmallCases()
    {
        var cases = new TheoryData<int, int, int, int, bool>();
        foreach (var photos in new[] { 0, 1, 2, 3, 7 })
        foreach (var holdStart in new[] { 0, 1, 5 })
        foreach (var holdEnd in new[] { 0, 3 })
        foreach (var plays in new[] { 1, 2, 5 })
        foreach (var boomerang in new[] { false, true })
        {
            cases.Add(photos, holdStart, holdEnd, plays, boomerang);
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(SmallCases))]
    public void EveryFrameShowsThePhotoTheReferenceLists(int photos, int holdStart, int holdEnd, int plays, bool boomerang)
    {
        var sequence = new FrameSequence(photos, holdStart, holdEnd, plays, boomerang);
        var reference = Reference(photos, holdStart, holdEnd, plays, boomerang);

        Assert.Equal(reference.Count, sequence.Length);
        Assert.Equal(reference, Enumerable.Range(0, sequence.Length).Select(sequence.PhotoAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => sequence.PhotoAt(sequence.Length));
        Assert.Throws<ArgumentOutOfRangeException>(() => sequence.PhotoAt(-1));
        if (photos > 0)
        {
            Assert.Equal(reference[^1], sequence.LastPhoto);
        }
    }

    [Theory]
    [MemberData(nameof(SmallCases))]
    public void ShowingsDirectionsAndCoverageAgreeWithTheFrames(int photos, int holdStart, int holdEnd, int plays, bool boomerang)
    {
        var sequence = new FrameSequence(photos, holdStart, holdEnd, plays, boomerang);
        var frames = Reference(photos, holdStart, holdEnd, plays, boomerang);

        // Every showing (a run of one photo) starts where ShowingsOf says, and nowhere else (one photo has one long run).
        for (var photo = 0; photo < photos; photo++)
        {
            var runs = Enumerable.Range(0, frames.Count).Where(i => frames[i] == photo && (i == 0 || frames[i - 1] != photo)).ToList();
            var showings = sequence.ShowingsOf(photo);
            Assert.All(showings, start => Assert.Equal(photo, frames[start]));
            Assert.All(runs, start => Assert.Contains(start, showings));
            Assert.Equal(runs[0], sequence.FirstPositionOf(photo));
            Assert.True(showings.Count <= (2 * plays) + 1);
        }

        // Covered photos: the photos the first frames show, always 0 to k - 1 (the first pass goes forward).
        for (var done = 0; done <= frames.Count; done++)
        {
            var shown = frames.Take(done).Distinct().Count();
            Assert.Equal(done == 0 ? 0 : frames.Take(done).Max() + 1, shown);
            Assert.Equal(shown, sequence.PhotosCovered(done));
        }

        // Direction: back exactly where a boomerang's next frame shows the photo before (a forward play's wrap to photo 0
        // is still forward).
        for (var i = holdStart; i + 1 < holdStart + sequence.CoreLength; i++)
        {
            if (frames[i + 1] != frames[i] && (boomerang || frames[i + 1] > frames[i]))
            {
                Assert.Equal(frames[i + 1] > frames[i] ? 1 : -1, sequence.DirectionAt(i));
            }
        }

        Assert.All(Enumerable.Range(0, holdStart), i => Assert.Equal(1, sequence.DirectionAt(i)));
    }

    [Fact]
    public void ABoomerangNeverShowsAPhotoTwiceInARowOutsideItsHolds()
    {
        foreach (var photos in new[] { 2, 3, 10, 742 })
        foreach (var plays in new[] { 1, 3, 5 })
        {
            var sequence = new FrameSequence(photos, 0, 0, plays, boomerang: true);
            var frames = Enumerable.Range(0, sequence.Length).Select(sequence.PhotoAt).ToList();
            Assert.All(frames.Zip(frames.Skip(1)), pair => Assert.Equal(1, Math.Abs(pair.First - pair.Second)));
            Assert.Equal(0, frames[0]);
            Assert.Equal(0, frames[^1]);
            Assert.Equal(plays, frames.Count(photo => photo == photos - 1));
            Assert.Equal((plays * ((2 * photos) - 2)) + 1, sequence.Length);
        }
    }

    [Fact]
    public void APlainSequenceIsThePhotosOnce()
    {
        var sequence = FrameSequence.Plain(742);

        Assert.True(sequence.IsIdentity);
        Assert.Equal(742, sequence.Length);
        Assert.All(Enumerable.Range(0, 742), i => Assert.Equal(i, sequence.PhotoAt(i)));
        Assert.Equal(0, FrameSequence.Plain(0).Length);
        Assert.Equal(0, new FrameSequence(0, 10, 10, 3, true).Length);
    }

    [Fact]
    public void ALongRunMatchesTheReferenceFrameByFrame()
    {
        // 5,000 photos, five boomerang plays and holds: 50,000 frames and more, every one checked.
        var sequence = new FrameSequence(5_000, 90, 120, 5, boomerang: true);
        var reference = Reference(5_000, 90, 120, 5, boomerang: true);

        Assert.Equal(reference.Count, sequence.Length);
        for (var i = 0; i < reference.Count; i++)
        {
            if (sequence.PhotoAt(i) != reference[i])
            {
                Assert.Fail($"Frame {i} shows {sequence.PhotoAt(i)}, expected {reference[i]}.");
            }
        }
    }

    [Fact]
    public void ASequenceTooLongForAnIndexThrowsInsteadOfWrapping()
    {
        Assert.Throws<OverflowException>(() => new FrameSequence(int.MaxValue / 2, 0, 0, 5, boomerang: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameSequence(10, 0, 0, 0, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameSequence(10, -1, 0, 1, false));
    }

    [Theory]
    [InlineData(24, 1)]
    [InlineData(25, 1)]
    [InlineData(30000, 1001)]
    [InlineData(30, 1)]
    [InlineData(60, 1)]
    [InlineData(1, 2)]
    [InlineData(1, 10)]
    [InlineData(7, 3)]
    public void AHoldIsTheNearestWholeFrameAndAtLeastOne(long numerator, long denominator)
    {
        var rate = new Fraction(numerator, denominator);
        Assert.Equal(0, SequenceSettings.HoldFrames(TimeSpan.Zero, rate));
        for (var tenths = 1; tenths <= 100; tenths++)
        {
            var hold = TimeSpan.FromTicks(tenths * 1_000_000L);
            var frames = SequenceSettings.HoldFrames(hold, rate);
            var exact = (decimal)tenths / 10 * numerator / denominator;

            // Nearest, half up, and never zero for a hold the user set.
            Assert.Equal(Math.Max(1, (int)decimal.Floor(exact + 0.5m)), frames);

            // So the hold lasts within half a frame of what was asked, or one frame when that is longer (exact ticks).
            var lasts = FrameTiming.Duration(frames, rate);
            var frame = FrameTiming.Duration(1, rate);
            Assert.True(Math.Abs(lasts - hold.Ticks) <= Math.Max((frame / 2) + 1, frames == 1 ? frame : 0), $"{tenths / 10.0} s at {rate}: {frames} frames, {lasts} ticks");
        }
    }

    [Theory]
    [InlineData(24, 1)]
    [InlineData(25, 1)]
    [InlineData(30000, 1001)]
    [InlineData(30, 1)]
    [InlineData(60, 1)]
    [InlineData(1, 2)]
    [InlineData(1, 10)]
    public void TheVideosFramesStartAtTheirIndexAndTheLengthIsTheLastFramesEnd(long numerator, long denominator)
    {
        var rate = new Fraction(numerator, denominator);
        var settings = new SequenceSettings(TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2), 3, boomerang: true);
        var sequence = settings.Resolve(600, rate);

        // Exact timing (#10): the length is FrameStart of the frame count, and each frame's start comes from its index, so
        // the starts never drift however long the video (a sum of durations equals the length exactly).
        Assert.Equal(FrameTiming.FrameStart(sequence.Length, rate), sequence.Duration(rate));
        long sum = 0;
        for (var i = 0; i < sequence.Length; i++)
        {
            Assert.Equal(sum, FrameTiming.FrameStart(i, rate));
            sum += FrameTiming.FrameDuration(i, rate);
        }

        Assert.Equal(sequence.Duration(rate), sum);
    }

    [Fact]
    public void TheSettingsKeepTheirRanges()
    {
        Assert.True(SequenceSettings.Default.IsPlain);
        Assert.Throws<ArgumentOutOfRangeException>(() => SequenceSettings.Default with { Plays = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => SequenceSettings.Default with { Plays = 6 });
        Assert.Throws<ArgumentOutOfRangeException>(() => SequenceSettings.Default with { HoldStart = TimeSpan.FromSeconds(10.1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => SequenceSettings.Default with { HoldEnd = TimeSpan.FromSeconds(-0.1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => SequenceSettings.Default with { HoldEnd = TimeSpan.FromMilliseconds(150) });
        Assert.Equal(TimeSpan.FromSeconds(10), (SequenceSettings.Default with { HoldStart = TimeSpan.FromSeconds(10) }).HoldStart);

        // A GIF loops by itself: it plays the photos once, and keeps everything else.
        var looped = new SequenceSettings(TimeSpan.FromSeconds(1), TimeSpan.Zero, 4, boomerang: true);
        Assert.Equal(looped with { Plays = 1 }, looped.For(VideoFormat.Gif));
        Assert.Same(looped, looped.For(VideoFormat.Mp4H264));
    }
}
