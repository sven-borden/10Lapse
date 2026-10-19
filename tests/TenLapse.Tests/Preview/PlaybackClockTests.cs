using TenLapse.Core.Preview;
using TenLapse.Core.Timing;

namespace TenLapse.Tests.Preview;

public sealed class PlaybackClockTests
{
    // Timestamps in 100 ns ticks, so a timestamp is a clip time.
    private const long Frequency = FrameTiming.TicksPerSecond;
    private const long Second = FrameTiming.TicksPerSecond;

    public static TheoryData<int, int> Rates => new()
    {
        { 30, 1 },
        { 30000, 1001 },
        { 24000, 1001 },
        { 25, 1 },
        { 60, 1 },
        { 1, 2 },
    };

    [Theory]
    [MemberData(nameof(Rates))]
    public void EveryReadingIsTheFrameTheIndexMathGivesForItsMomentOverAnHour(int num, int den)
    {
        var rate = new Fraction(num, den);
        const int Count = 742;
        const long Start = 5_000_000_000;
        var clock = new PlaybackClock(rate, Count, 0, Start, Frequency);
        var length = FrameTiming.Duration(Count, rate);
        var random = new Random(19);

        // A render loop that wakes at uneven moments (jittered vblanks, a hitch now and then) for an hour.
        for (long elapsed = 0; elapsed < 3600 * Second; elapsed += 100_000 + random.Next(400_000))
        {
            var expected = (int)FrameTiming.FrameAt(elapsed % length, rate);
            Assert.Equal(expected, clock.FrameAt(Start + elapsed));
        }

        // Exactly an hour in, still exact: nothing was accumulated.
        Assert.Equal((int)FrameTiming.FrameAt((3600 * Second) % length, rate), clock.FrameAt(Start + (3600 * Second)));
    }

    [Fact]
    public void ALateDrawSkipsFramesInsteadOfShowingThemLate()
    {
        var rate = new Fraction(30, 1);
        var clock = new PlaybackClock(rate, 1000, 0, 0, Frequency);

        // Draws every 50 ms (20 per second) at 30 fps: frames are dropped, and each draw shows the frame of its moment.
        var shown = new List<int>();
        for (long t = 0; t <= 10 * Second; t += 500_000)
        {
            shown.Add(clock.FrameAt(t));
        }

        Assert.Equal(300, shown[^1]);
        Assert.All(shown.Zip(shown.Skip(1)), pair => Assert.InRange(pair.Second - pair.First, 1, 2));
    }

    [Fact]
    public void StartsAtItsFrameAndLoops()
    {
        var rate = new Fraction(30, 1);
        var clock = new PlaybackClock(rate, 90, 60, 1_000, Frequency);

        Assert.Equal(60, clock.FrameAt(1_000));
        Assert.Equal(60, clock.FrameAt(0));
        Assert.Equal(61, clock.FrameAt(1_000 + FrameTiming.FrameDuration(60, rate)));

        // 30 frames on, the clip (90 frames, 3 s) is at its end and starts again.
        var loop = clock.Read(1_000 + Second);
        Assert.Equal(0, loop.Frame);
        Assert.Equal(90, loop.Occurrence);
        Assert.Equal(89, clock.Read(1_000 + Second - 1).Frame);
        Assert.Equal(3 * Second, (long)clock.Length);
    }

    [Fact]
    public void AReadingSaysWhenItsFrameBegan()
    {
        var rate = new Fraction(30000, 1001);
        var clock = new PlaybackClock(rate, 500, 10, 7_777, Frequency);
        for (long t = 7_777; t < 7_777 + (40 * Second); t += 1_234_567)
        {
            var reading = clock.Read(t);
            var began = clock.TimestampAt(reading.OccurrenceStart);
            var next = clock.TimestampAt(reading.OccurrenceStart + FrameTiming.FrameDuration(reading.Frame, rate));
            Assert.InRange(t, began, next - 1);
        }
    }

    [Fact]
    public void WorksInStopwatchUnits()
    {
        // A 3 MHz counter (Stopwatch on some machines): one second of counts is 30 frames at 30 fps.
        var clock = new PlaybackClock(new Fraction(30, 1), 100, 0, 0, 3_000_000);

        Assert.Equal(30, clock.FrameAt(3_000_000));
        Assert.Equal(29, clock.FrameAt(2_999_999));
    }

    [Fact]
    public void RejectsWhatCannotPlay()
    {
        var rate = new Fraction(30, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaybackClock(rate, 0, 0, 0, Frequency));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaybackClock(rate, 10, 10, 0, Frequency));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaybackClock(rate, 10, -1, 0, Frequency));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaybackClock(rate, 10, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaybackClock(default, 10, 0, 0, Frequency));
    }
}
