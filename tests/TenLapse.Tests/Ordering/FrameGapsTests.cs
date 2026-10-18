using System.Diagnostics;
using TenLapse.Core.Ordering;
using TenLapse.Core.Projects;

namespace TenLapse.Tests.Ordering;

public static class FrameGapsTests
{
    private static readonly DateTime _base = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Unspecified);

    private static Frame Photo(string name, DateTime? taken = null) =>
        new($@"C:\Photos\{name}", 1_000, new DateTime(2026, 5, 2, 8, 0, 0, DateTimeKind.Utc)) { CaptureTime = taken };

    private static Frame[] Named(params string[] names) => [.. names.Select(name => Photo(name))];

    /// <summary>Frames <paramref name="count"/> shots apart by <paramref name="seconds"/> from the base time, named IMG_0001...</summary>
    private static Frame[] Steady(int count, double seconds = 5, int first = 1) =>
        [.. Enumerable.Range(0, count).Select(i => Photo($"IMG_{first + i:0000}.jpg", _base.AddSeconds(i * seconds)))];

    [Fact]
    public static void AJumpInNumberingIsAGapOfTheMissingNumbers()
    {
        var gaps = FrameGaps.Detect(Named("IMG_0040.jpg", "IMG_0041.jpg", "IMG_0045.jpg", "IMG_0046.jpg"));

        var gap = Assert.Single(gaps);
        Assert.Equal(1, gap.AfterIndex);
        Assert.Equal(FrameGapKinds.Numbering, gap.Kinds);
        Assert.Equal(3, gap.MissingFrames);
        Assert.Null(gap.Interval);
        Assert.Null(gap.TypicalInterval);
        Assert.Equal(0, gap.EstimatedMissingByTime);
    }

    [Fact]
    public static void ConsecutiveNumbersAreNoGapWhateverTheWidth()
    {
        Assert.Empty(FrameGaps.Detect(Named("IMG_0008.jpg", "IMG_0009.jpg", "IMG_0010.jpg", "IMG_0011.jpg")));
        Assert.Empty(FrameGaps.Detect(Named("a8.jpg", "a9.jpg", "a10.jpg")));
        Assert.Empty(FrameGaps.Detect(Named("IMG_9998.jpg", "IMG_9999.jpg", "IMG_10000.jpg")));
    }

    [Fact]
    public static void ACounterThatWrapsIsNoGap()
    {
        Assert.Empty(FrameGaps.Detect(Named("IMG_9998.jpg", "IMG_9999.jpg", "IMG_0001.jpg", "IMG_0002.jpg")));
        Assert.Empty(FrameGaps.Detect(Named("DSC09999.jpg", "DSC00001.jpg")));

        // Past the wrap the numbers skipped are counted from 1.
        var gap = Assert.Single(FrameGaps.Detect(Named("IMG_9999.jpg", "IMG_0005.jpg")));
        Assert.Equal(0, gap.AfterIndex);
        Assert.Equal(4, gap.MissingFrames);
    }

    [Fact]
    public static void NumbersThatDoNotRiseAreNoGap()
    {
        // A shot repeated, a RAW and its JPEG, a second folder starting over, a manual order.
        Assert.Empty(FrameGaps.Detect(Named("IMG_0005.jpg", "IMG_0005.jpg", "IMG_0005.CR2", "IMG_0001.jpg")));
        Assert.Empty(FrameGaps.Detect(Named("IMG_0010.jpg", "IMG_0009.jpg", "IMG_0008.jpg")));
    }

    [Fact]
    public static void TwoCamerasWithDifferentPrefixesAreSeparateSequences()
    {
        // No gap between IMG_0041 and DSC_0100 and none inside either camera when they interleave.
        Assert.Empty(FrameGaps.Detect(Named("IMG_0041.jpg", "DSC_0100.jpg", "IMG_0042.jpg", "DSC_0101.jpg", "IMG_0043.jpg")));

        // A jump inside one camera is found even with the other camera's frames between: the marker goes in front of the frame that jumped.
        var gap = Assert.Single(FrameGaps.Detect(Named("IMG_0041.jpg", "DSC_0100.jpg", "IMG_0042.jpg", "DSC_0101.jpg", "IMG_0045.jpg")));
        Assert.Equal(3, gap.AfterIndex);
        Assert.Equal(2, gap.MissingFrames);
    }

    [Fact]
    public static void TheExtensionAndTheCaseOfTheNameDoNotSplitASequence()
    {
        var gap = Assert.Single(FrameGaps.Detect(Named("IMG_0001.CR2", "img_0002.jpg", "IMG_0003.heic", "Img_0005.JPG")));
        Assert.Equal(2, gap.AfterIndex);
        Assert.Equal(1, gap.MissingFrames);
    }

    [Fact]
    public static void NamesWithoutANumberAndVeryLongRunsAreSkipped()
    {
        Assert.Empty(FrameGaps.Detect(Named("sunrise.jpg", "sunset.jpg", "IMG_0001.jpg", "IMG_0002.jpg")));
        Assert.Empty(FrameGaps.Detect(Named("x1234567890123456789012.jpg", "x1234567890123456789099.jpg")));
    }

    [Fact]
    public static void TheLastRunOfDigitsIsTheCounter()
    {
        // A date in the name is part of the prefix; only the trailing counter moves.
        Assert.Empty(FrameGaps.Detect(Named("20260501_0001.jpg", "20260501_0002.jpg", "20260502_0001.jpg")));
        var gap = Assert.Single(FrameGaps.Detect(Named("20260501_0001.jpg", "20260501_0004.jpg")));
        Assert.Equal(2, gap.MissingFrames);
    }

    [Theory]
    [InlineData("IMG_20240101_123456.jpg", "IMG_20240101_123512.jpg", "IMG_20240101_123530.jpg")]
    [InlineData("PXL_20240101_123456789.jpg", "PXL_20240101_123512345.jpg", "PXL_20240101_123530001.jpg")]
    [InlineData("20240101_123456.jpg", "20240101_123512.jpg", "20240101_123530.jpg")]
    [InlineData("VID_20240101_123456.mp4", "VID_20240101_123512.mp4", "VID_20240101_123530.mp4")]
    [InlineData("20240101_123456(1).jpg", "20240101_123512(2).jpg", "20240101_123530(7).jpg")]
    [InlineData("20240101123456.jpg", "20240101123512.jpg", "20240101123530.jpg")]
    [InlineData("Screenshot 2024-01-01 123456.png", "Screenshot 2024-01-01 123512.png", "Screenshot 2024-01-01 123530.png")]
    [InlineData("IMG-20240101-123456.jpg", "IMG-20240101-123512.jpg", "IMG-20240101-123530.jpg")]
    public static void TimestampNamesAreNotNumbered(string first, string second, string third)
    {
        // The last run of digits is a time of day (or a seconds counter), not a frame counter: no gap at any seam.
        Assert.Empty(FrameGaps.Detect(Named(first, second, third)));
    }

    [Fact]
    public static void ACounterNextToADateIsStillNumbered()
    {
        // Only a time-like run turns numbering off: a short counter after a date is a counter.
        var gap = Assert.Single(FrameGaps.Detect(Named("IMG_20240101_0041.jpg", "IMG_20240101_0045.jpg")));
        Assert.Equal(3, gap.MissingFrames);

        // And a plain 4 to 5 digit camera counter is flagged as before.
        gap = Assert.Single(FrameGaps.Detect(Named("IMG_0041.jpg", "IMG_0045.jpg")));
        Assert.Equal(3, gap.MissingFrames);
        gap = Assert.Single(FrameGaps.Detect(Named("DSC12341.jpg", "DSC12345.jpg")));
        Assert.Equal(3, gap.MissingFrames);
    }

    [Fact]
    public static void AnOutlierIntervalIsAGapWithTheTypicalInterval()
    {
        var frames = Steady(10, seconds: 5).Concat(Steady(10, seconds: 5, first: 11).Select((frame, i) => Photo(frame.FileName, _base.AddSeconds(45 + 60 + (i * 5))))).ToArray();

        var gap = Assert.Single(FrameGaps.Detect(frames));

        Assert.Equal(9, gap.AfterIndex);
        Assert.Equal(FrameGapKinds.CaptureInterval, gap.Kinds);
        Assert.Equal(0, gap.MissingFrames);
        Assert.Equal(TimeSpan.FromSeconds(60), gap.Interval);
        Assert.Equal(TimeSpan.FromSeconds(5), gap.TypicalInterval);
        Assert.Equal(11, gap.EstimatedMissingByTime);
    }

    [Fact]
    public static void JitterBurstsAndSlowDriftAreNotGaps()
    {
        // 4 to 6 seconds apart.
        var random = new Random(15);
        var at = _base;
        var jittery = Enumerable.Range(1, 40).Select(i => Photo($"IMG_{i:0000}.jpg", at = at.AddSeconds(4 + random.NextDouble() * 2))).ToArray();
        Assert.Empty(FrameGaps.Detect(jittery));

        // A burst: ten shots a tenth of a second apart, a pause of 0.9 s, ten more. Under the one-second floor.
        at = _base;
        var burst = Enumerable.Range(1, 20).Select(i => Photo($"IMG_{i:0000}.jpg", at = at.AddSeconds(i == 11 ? 0.9 : 0.1))).ToArray();
        Assert.Empty(FrameGaps.Detect(burst));

        // A sunset: the interval grows 20 percent a shot.
        at = _base;
        var interval = 1.0;
        var drifting = Enumerable.Range(1, 30).Select(i => Photo($"IMG_{i:0000}.jpg", at = at.AddSeconds(interval *= 1.2))).ToArray();
        Assert.Empty(FrameGaps.Detect(drifting));
    }

    [Fact]
    public static void ThreeTimesTheMedianIsTheLine()
    {
        Frame[] Series(double jump) =>
        [
            .. Enumerable.Range(0, 8).Select(i => Photo($"IMG_{i + 1:0000}.jpg", _base.AddSeconds(i * 10))),
            .. Enumerable.Range(0, 8).Select(i => Photo($"IMG_{i + 9:0000}.jpg", _base.AddSeconds((7 * 10) + jump + (i * 10)))),
        ];

        Assert.Empty(FrameGaps.Detect(Series(30)));
        Assert.Single(FrameGaps.Detect(Series(31)));
    }

    [Fact]
    public static void TooFewNeighboursOrMissingTimesNeverFlag()
    {
        // Three shots: only two intervals, fewer than the three needed to call one an outlier.
        Assert.Empty(FrameGaps.Detect([Photo("IMG_0001.jpg", _base), Photo("IMG_0002.jpg", _base.AddSeconds(5)), Photo("IMG_0003.jpg", _base.AddSeconds(500))]));

        // The same long step without times on the frames around it.
        var untimed = Steady(10).Select(frame => Photo(frame.FileName)).ToArray();
        Assert.Empty(FrameGaps.Detect(untimed));

        // Out of time order (a manual order or two cameras sorted by name): negative steps are not intervals.
        var reversed = Steady(12).Reverse().ToArray();
        Assert.Empty(FrameGaps.Detect(reversed));
    }

    [Fact]
    public static void AGapNearTheStartIsFoundFromTheSideThatHasNeighbours()
    {
        var frames = new[] { 0.0, 60.0 }.Concat(Enumerable.Range(1, 8).Select(i => 60.0 + (i * 5)))
            .Select((seconds, i) => Photo($"IMG_{i + 1:0000}.jpg", _base.AddSeconds(seconds)))
            .ToArray();

        var gap = Assert.Single(FrameGaps.Detect(frames));
        Assert.Equal(0, gap.AfterIndex);
        Assert.Equal(TimeSpan.FromSeconds(5), gap.TypicalInterval);
    }

    [Fact]
    public static void TwoGapsNextToEachOtherAreBothFound()
    {
        var seconds = new List<double>();
        var t = 0.0;
        for (var i = 0; i < 30; i++)
        {
            t += i is 10 or 13 ? 120 : 5;
            seconds.Add(t);
        }

        var frames = seconds.Select((s, i) => Photo($"IMG_{i + 1:0000}.jpg", _base.AddSeconds(s))).ToArray();

        Assert.Equal([9, 12], FrameGaps.Detect(frames).Select(gap => gap.AfterIndex));
    }

    [Fact]
    public static void NumberingAndIntervalAtOnePlaceAreOneMarkerWithBothKinds()
    {
        var frames = Steady(10, seconds: 5)
            .Concat(Enumerable.Range(0, 10).Select(i => Photo($"IMG_{i + 16:0000}.jpg", _base.AddSeconds(45 + 300 + (i * 5)))))
            .ToArray();

        var gap = Assert.Single(FrameGaps.Detect(frames));

        Assert.Equal(9, gap.AfterIndex);
        Assert.Equal(FrameGapKinds.Numbering | FrameGapKinds.CaptureInterval, gap.Kinds);
        Assert.Equal(5, gap.MissingFrames);
        Assert.Equal(TimeSpan.FromSeconds(300), gap.Interval);
    }

    [Fact]
    public static void InterleavedCamerasAreJudgedOnTheirOrderedTimes()
    {
        // Canon every 10 s and Sony every 10 s offset by 5 s: merged in time order the steps are all 5 s. One Canon shot
        // (IMG_0004) is missing: the step is 10 s, only twice the median and so no interval gap, but Canon's numbers jump.
        var frames = new List<Frame>();
        for (var i = 0; i < 12; i++)
        {
            if (i != 3)
            {
                frames.Add(Photo($"IMG_{i + 1:0000}.jpg", _base.AddSeconds(i * 10)));
            }

            frames.Add(Photo($"DSC{9990 + i:00000}.jpg", _base.AddSeconds((i * 10) + 5)));
        }

        var sorted = FrameSorter.Sort(frames, SortMode.CaptureTime);
        var gap = Assert.Single(FrameGaps.Detect(sorted));

        Assert.Equal(FrameGapKinds.Numbering, gap.Kinds);
        Assert.Equal(1, gap.MissingFrames);

        // The marker is in front of the frame whose number jumped.
        Assert.Equal("IMG_0005.jpg", sorted[gap.AfterIndex + 1].FileName);
    }

    [Fact]
    public static void EmptyAndSingleListsHaveNoGaps()
    {
        Assert.Empty(FrameGaps.Detect([]));
        Assert.Empty(FrameGaps.Detect(Named("IMG_0001.jpg")));
        Assert.Throws<ArgumentNullException>(() => FrameGaps.Detect(null!));
    }

    [Fact]
    public static void FiveThousandFramesAreScannedQuickly()
    {
        var frames = Steady(5000, seconds: 2);
        var watch = Stopwatch.StartNew();
        var gaps = FrameGaps.Detect(frames);
        watch.Stop();

        TestContext.Current.SendDiagnosticMessage($"Gap detection over 5,000 frames took {watch.Elapsed.TotalMilliseconds:N1} ms.");
        Assert.Empty(gaps);
        Assert.True(watch.Elapsed.TotalSeconds < 2);
    }
}
