using System.Diagnostics;
using System.Globalization;
using TenLapse.Core.Projects;
using TenLapse.Tests.Fixtures;
using TenLapse.Tests.Ordering;

namespace TenLapse.Tests.Import;

/// <summary>
/// The issue's bar (#12): a big folder shows its first frames in under 2 s and finishes without freezing the UI thread.
/// The gate runs it on 1,000 photos; set <c>TENLAPSE_IMPORT_PERF_COUNT=5000</c> for the issue's full size.
/// </summary>
public sealed class ImportPerformanceTests : IDisposable
{
    private const int GateCount = 1_000;

    private readonly string _folder = Directory.CreateTempSubdirectory("10lapse-perf-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task ABigFolderShowsItsFirstFramesAtOnceAndNeverHoldsTheUiThread()
    {
        var count = int.TryParse(Environment.GetEnvironmentVariable("TENLAPSE_IMPORT_PERF_COUNT"), CultureInfo.InvariantCulture, out var n) && n > 0 ? n : GateCount;
        var written = Stopwatch.StartNew();
        var start = new DateTime(2026, 6, 21, 19, 0, 0);
        WritePhotos(count, start);
        written.Stop();

        using var harness = ImportHarness.WithPhotoImporter();
        var clock = new Stopwatch();
        TimeSpan? firstFrames = null;
        TimeSpan? allListed = null;
        harness.Ui.Run(() =>
        {
            harness.Project.Changed += (_, e) =>
            {
                if (e.Frames?.Kind == FrameChangeKind.Added)
                {
                    firstFrames ??= clock.Elapsed;
                    if (e.Snapshot.Frames.Length == count)
                    {
                        allListed = clock.Elapsed;
                    }
                }
            };
            return true;
        });

        // A heartbeat: how long a small piece of work waits for the UI thread while the import runs.
        using var stop = new CancellationTokenSource();
        var heartbeat = Task.Run(async () =>
        {
            var worst = TimeSpan.Zero;
            while (!stop.IsCancellationRequested)
            {
                var asked = Stopwatch.GetTimestamp();
                await harness.Ui.InvokeAsync(() => true);
                var waited = Stopwatch.GetElapsedTime(asked);
                worst = waited > worst ? waited : worst;
                await Task.Delay(5, CancellationToken.None);
            }

            return worst;
        });

        clock.Start();
        await harness.ImportAsync(_folder);
        clock.Stop();
        await stop.CancelAsync();
        var worstWait = await heartbeat;

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{count:N0} photos (written in {written.Elapsed.TotalSeconds:0.00} s): first frames {firstFrames?.TotalMilliseconds:0} ms, all listed {allListed?.TotalMilliseconds:0} ms, " +
            $"done {clock.Elapsed.TotalSeconds:0.00} s; longest UI step {harness.Ui.Longest.TotalMilliseconds:0.0} ms, worst UI wait {worstWait.TotalMilliseconds:0.0} ms.");

        Assert.Equal(count, harness.Project.Frames.Length);
        Assert.All(harness.Project.Frames, frame => Assert.Equal(new PixelSize(96, 64), frame.PixelSize));
        Assert.Equal(SortMode.CaptureTime, harness.Project.SortMode);
        Assert.Equal(start.AddSeconds(5 * (count - 1)), harness.Project.Frames[^1].CaptureTime);
        Assert.NotNull(firstFrames);
        Assert.True(firstFrames < TimeSpan.FromSeconds(2), $"The first frames took {firstFrames}.");
        Assert.True(harness.Ui.Longest < TimeSpan.FromMilliseconds(250), $"One UI step took {harness.Ui.Longest}.");
        Assert.True(worstWait < TimeSpan.FromMilliseconds(500), $"The UI thread was held for {worstWait}.");
        Assert.Empty(harness.InfoBars.Shown);
    }

    // Copies of a 96x64 fixture JPEG, each with its own Exif capture time five seconds after the one before. The
    // names run against the times (the last name is the first shot), so the import has to sort by time.
    private void WritePhotos(int count, DateTime start)
    {
        var photo = File.ReadAllBytes(Path.Combine(PhotoFixtureCatalogue.Directory, "gray.jpg"));
        var app0End = 4 + ((photo[4] << 8) | photo[5]);
        Parallel.For(0, count, i =>
        {
            var exif = ExifFiles.Jpeg(new ExifFiles.Tags(Original: ExifFiles.Stamp(start.AddSeconds(5 * i))));
            var app1 = exif.AsSpan(2, exif.Length - 4);
            var bytes = new byte[photo.Length + app1.Length];
            photo.AsSpan(0, app0End).CopyTo(bytes);
            app1.CopyTo(bytes.AsSpan(app0End));
            photo.AsSpan(app0End).CopyTo(bytes.AsSpan(app0End + app1.Length));
            File.WriteAllBytes(Path.Combine(_folder, $"IMG_{count - i:00000}.jpg"), bytes);
        });
    }
}
