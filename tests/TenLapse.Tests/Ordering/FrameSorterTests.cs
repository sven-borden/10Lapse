using System.Diagnostics;
using TenLapse.Core.Ordering;
using TenLapse.Core.Projects;

namespace TenLapse.Tests.Ordering;

public static class FrameSorterTests
{
    private static readonly DateTime _base = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Unspecified);

    private static Frame Photo(string path, DateTime? taken = null, DateTime? modified = null) =>
        new(path, 1_000, modified ?? new DateTime(2026, 5, 2, 8, 0, 0, DateTimeKind.Utc)) { CaptureTime = taken };

    private static string[] Names(IEnumerable<Frame> frames) => [.. frames.Select(frame => frame.FileName)];

    // Camera A (Canon) shoots at :00, :10, :20; camera B (Sony) at :05, :15, :25.
    private static Frame[] TwoCameras(bool inFolderOrder = true)
    {
        Frame[] a =
        [
            Photo(@"C:\Trip\Canon\IMG_0001.jpg", _base),
            Photo(@"C:\Trip\Canon\IMG_0002.jpg", _base.AddSeconds(10)),
            Photo(@"C:\Trip\Canon\IMG_0003.jpg", _base.AddSeconds(20)),
        ];
        Frame[] b =
        [
            Photo(@"C:\Trip\Sony\DSC09990.jpg", _base.AddSeconds(5)),
            Photo(@"C:\Trip\Sony\DSC09991.jpg", _base.AddSeconds(15)),
            Photo(@"C:\Trip\Sony\DSC09992.jpg", _base.AddSeconds(25)),
        ];
        return inFolderOrder ? [.. a, .. b] : [.. b, .. a];
    }

    [Fact]
    public static void TwoCamerasWithDifferentNamingInterleaveByCaptureTime()
    {
        var sorted = FrameSorter.Sort(TwoCameras(), SortMode.CaptureTime);

        Assert.Equal(["IMG_0001.jpg", "DSC09990.jpg", "IMG_0002.jpg", "DSC09991.jpg", "IMG_0003.jpg", "DSC09992.jpg"], Names(sorted));
        Assert.Equal(Names(sorted), Names(FrameSorter.Sort(TwoCameras(inFolderOrder: false), SortMode.CaptureTime)));
    }

    [Fact]
    public static void TheSameSetByNameGroupsTheCameras()
    {
        var sorted = FrameSorter.Sort(TwoCameras(), SortMode.Name);

        Assert.Equal(["DSC09990.jpg", "DSC09991.jpg", "DSC09992.jpg", "IMG_0001.jpg", "IMG_0002.jpg", "IMG_0003.jpg"], Names(sorted));
    }

    [Fact]
    public static void TheDefaultIsCaptureTimeOnlyWhenEveryFrameHasOne()
    {
        var timed = TwoCameras();
        Assert.Equal(SortMode.CaptureTime, FrameSorter.DefaultMode(timed));

        var oneMissing = timed.ToArray();
        oneMissing[2] = Photo(@"C:\Trip\Canon\IMG_0003.jpg");
        Assert.Equal(SortMode.Name, FrameSorter.DefaultMode(oneMissing));

        Assert.Equal(SortMode.Name, FrameSorter.DefaultMode([]));
    }

    [Fact]
    public static void WhenOneFileHasNoTimeTheDefaultFallsBackToNameOrder()
    {
        var frames = TwoCameras().ToArray();
        frames[2] = Photo(@"C:\Trip\Canon\IMG_0003.jpg");

        var mode = FrameSorter.DefaultMode(frames);
        var sorted = FrameSorter.Sort(frames, mode);

        Assert.Equal(SortMode.Name, mode);
        Assert.Equal(["DSC09990.jpg", "DSC09991.jpg", "DSC09992.jpg", "IMG_0001.jpg", "IMG_0002.jpg", "IMG_0003.jpg"], Names(sorted));
    }

    [Fact]
    public static void ChoosingCaptureTimeWithSomeFramesUntimedPutsThemLastInNameOrder()
    {
        Frame[] frames =
        [
            Photo(@"C:\Trip\IMG_0010.jpg"),
            Photo(@"C:\Trip\IMG_0002.jpg", _base.AddSeconds(30)),
            Photo(@"C:\Trip\IMG_0009.jpg"),
            Photo(@"C:\Trip\IMG_0003.jpg", _base),
        ];

        Assert.Equal(["IMG_0003.jpg", "IMG_0002.jpg", "IMG_0009.jpg", "IMG_0010.jpg"], Names(FrameSorter.Sort(frames, SortMode.CaptureTime)));
    }

    [Fact]
    public static void BurstShotsInOneSecondOrderBySubSecond()
    {
        // A burst whose names run opposite to the clock: the time wins.
        Frame[] frames =
        [
            Photo(@"C:\Burst\IMG_0001.jpg", _base.AddMilliseconds(900)),
            Photo(@"C:\Burst\IMG_0002.jpg", _base.AddMilliseconds(30)),
            Photo(@"C:\Burst\IMG_0003.jpg", _base.AddMilliseconds(499)),
            Photo(@"C:\Burst\IMG_0004.jpg", _base.AddMilliseconds(120)),
            Photo(@"C:\Burst\IMG_0005.jpg", _base.AddTicks(1)),
        ];

        Assert.Equal(["IMG_0005.jpg", "IMG_0002.jpg", "IMG_0004.jpg", "IMG_0003.jpg", "IMG_0001.jpg"], Names(FrameSorter.Sort(frames, SortMode.CaptureTime)));
    }

    [Fact]
    public static void TiesBreakByNameThenByPathThenByInputPosition()
    {
        Frame[] frames =
        [
            Photo(@"C:\b\IMG_10.jpg", _base),
            Photo(@"C:\b\IMG_9.jpg", _base),
            Photo(@"C:\a\IMG_9.jpg", _base),
            Photo(@"C:\Folder 10\IMG_9.jpg", _base),
            Photo(@"C:\Folder 2\IMG_9.jpg", _base),
        ];

        var sorted = FrameSorter.Sort(frames, SortMode.CaptureTime);

        Assert.Equal(
            [@"C:\a\IMG_9.jpg", @"C:\b\IMG_9.jpg", @"C:\Folder 2\IMG_9.jpg", @"C:\Folder 10\IMG_9.jpg", @"C:\b\IMG_10.jpg"],
            sorted.Select(frame => frame.SourcePath));
    }

    [Fact]
    public static void ByNameUsesTheFileNameNotTheFolderAndExplorerOrder()
    {
        Frame[] frames =
        [
            Photo(@"C:\z\IMG_10.jpg"),
            Photo(@"C:\a\IMG_9.jpg"),
            Photo(@"C:\m\img_2.JPG"),
            Photo(@"C:\m\IMG_0001.jpg"),
        ];

        Assert.Equal(["IMG_0001.jpg", "img_2.JPG", "IMG_9.jpg", "IMG_10.jpg"], Names(FrameSorter.Sort(frames, SortMode.Name)));
    }

    [Fact]
    public static void DateModifiedOrdersByWriteTimeThenByName()
    {
        var early = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Frame[] frames =
        [
            Photo(@"C:\p\IMG_3.jpg", modified: early.AddDays(2)),
            Photo(@"C:\p\IMG_2.jpg", modified: early),
            Photo(@"C:\p\IMG_10.jpg", modified: early.AddDays(1)),
            Photo(@"C:\p\IMG_1.jpg", modified: early.AddDays(1)),
        ];

        Assert.Equal(["IMG_2.jpg", "IMG_1.jpg", "IMG_10.jpg", "IMG_3.jpg"], Names(FrameSorter.Sort(frames, SortMode.DateModified)));
    }

    [Fact]
    public static void ManualKeepsTheGivenOrder()
    {
        var frames = TwoCameras(inFolderOrder: false);

        Assert.Equal(frames, FrameSorter.Sort(frames, SortMode.Manual));
    }

    [Fact]
    public static void EmptyAndSingleListsAndUnknownModes()
    {
        Assert.Empty(FrameSorter.Sort([], SortMode.Name));
        var one = Photo(@"C:\p\IMG_1.jpg");
        Assert.Equal([one], FrameSorter.Sort([one], SortMode.CaptureTime));
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameSorter.Sort([one], (SortMode)99));
        Assert.Throws<ArgumentNullException>(() => FrameSorter.Sort(null!, SortMode.Name));
    }

    [Theory]
    [InlineData(SortMode.CaptureTime)]
    [InlineData(SortMode.Name)]
    [InlineData(SortMode.DateModified)]
    public static void SortingIsStableAndAlwaysAPermutation(SortMode mode)
    {
        var random = new Random(15);
        var frames = Enumerable.Range(0, 400)
            .Select(i => Photo(
                $@"C:\Shoot\{random.Next(3)}\IMG_{random.Next(1, 40)}.jpg",
                random.Next(5) == 0 ? null : _base.AddSeconds(random.Next(0, 30)),
                new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(random.Next(0, 20))))
            .GroupBy(frame => frame.SourcePath)
            .Select(group => group.First())
            .ToArray();

        var sorted = FrameSorter.Sort(frames, mode);
        var again = FrameSorter.Sort(sorted.Reverse(), mode);

        Assert.Equal(frames.Length, sorted.Length);
        Assert.Equal(frames.Select(f => f.Id).Order(), sorted.Select(f => f.Id).Order());
        Assert.Equal(sorted, again);
    }

    [Fact]
    public static void FiveThousandFramesSortQuickly()
    {
        var random = new Random(150);
        var frames = Enumerable.Range(0, 5000)
            .Select(i => Photo($@"C:\Photos\{(i % 2 == 0 ? "Canon" : "Sony")}\{(i % 2 == 0 ? "IMG_" : "DSC")}{i:00000}.jpg", _base.AddMilliseconds(random.Next(0, 3_000_000))))
            .ToArray();

        var watch = Stopwatch.StartNew();
        var byTime = FrameSorter.Sort(frames, SortMode.CaptureTime);
        var byName = FrameSorter.Sort(frames, SortMode.Name);
        watch.Stop();

        TestContext.Current.SendDiagnosticMessage($"Sorting 5,000 frames by time and by name took {watch.Elapsed.TotalMilliseconds:N0} ms.");
        Assert.Equal(5000, byTime.Length);
        Assert.Equal(byTime.Select(f => f.CaptureTime).Order(), byTime.Select(f => f.CaptureTime));
        Assert.True(byName.Select(f => f.FileName).SequenceEqual(byName.Select(f => f.FileName).Order(NaturalNameComparer.Instance)));
        Assert.True(watch.Elapsed.TotalSeconds < 5, $"took {watch.Elapsed.TotalSeconds:N1} s");
    }

    [Fact]
    public static void TwoCameraFilesOnDiskOrderByTheirExifTimesEndToEnd()
    {
        var folder = Directory.CreateTempSubdirectory("tenlapse-twocams-");
        try
        {
            // Canon names run IMG_0001..., Sony DSC09990...; the clocks interleave them, and the file write times
            // are all the same so only EXIF can order them.
            var shots = new (string Name, string Taken, string SubSecond)[]
            {
                ("IMG_0001.jpg", "2026:05:01 10:00:00", "00"),
                ("DSC09990.jpg", "2026:05:01 10:00:05", "40"),
                ("IMG_0002.jpg", "2026:05:01 10:00:10", "00"),
                ("DSC09991.jpg", "2026:05:01 10:00:15", "40"),
                ("DSC09992.jpg", "2026:05:01 10:00:15", "90"),
            };
            var written = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
            var frames = new List<Frame>();
            foreach (var (name, taken, sub) in shots.Reverse())
            {
                var path = Path.Combine(folder.FullName, name);
                File.WriteAllBytes(path, ExifFiles.Jpeg(new ExifFiles.Tags(Original: taken, SubSecOriginal: sub), paddingBytes: 2000));
                File.SetLastWriteTimeUtc(path, written);
                frames.Add(new Frame(path, new FileInfo(path).Length, written) { CaptureTime = CaptureTimeReader.ReadFile(path)?.Time });
            }

            var project = new Project();
            project.AddFrames(frames);
            var mode = project.ApplyDefaultSort();

            Assert.Equal(SortMode.CaptureTime, mode);
            Assert.Equal(SortMode.CaptureTime, project.SortMode);
            Assert.Equal(shots.Select(shot => shot.Name), project.Frames.Select(frame => frame.FileName));

            // One file loses its EXIF time: the whole set falls back to name order.
            var stripped = project.Frames[0];
            File.WriteAllBytes(stripped.SourcePath, ExifFiles.Jpeg(new ExifFiles.Tags()));
            project.UpdateFrames([stripped with { CaptureTime = CaptureTimeReader.ReadFile(stripped.SourcePath)?.Time }]);
            Assert.Equal(SortMode.Name, project.ApplyDefaultSort());
            Assert.Equal(["DSC09990.jpg", "DSC09991.jpg", "DSC09992.jpg", "IMG_0001.jpg", "IMG_0002.jpg"], project.Frames.Select(frame => frame.FileName));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
