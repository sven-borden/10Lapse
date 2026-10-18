using TenLapse.Core.Import;
using TenLapse.Core.Projects;
using TenLapse.Media.Import;
using TenLapse.Tests.Fixtures;

namespace TenLapse.Tests.Import;

/// <summary>Media's importer on its own: what it lists from folders and files, and what reading makes of the fixtures.</summary>
public sealed class PhotoImporterTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("10lapse-import-").FullName;

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task TheFixtureFolderListsEveryFileAndReadingKeepsSixteenAndSkipsFourWithTheirReasons()
    {
        var importer = new PhotoImporter();
        var listed = await ListAsync(importer, new ImportRequest([PhotoFixtureCatalogue.Directory], IncludeSubfolders: false));

        Assert.Equal(PhotoFixtureCatalogue.All.Count, listed.Frames.Count);
        Assert.Empty(listed.Skipped);

        var readings = new List<FrameReading>();
        await foreach (var batch in importer.ReadAsync(listed.Frames, TestContext.Current.CancellationToken))
        {
            readings.AddRange(batch);
        }

        Assert.Equal(listed.Frames.Select(frame => frame.Id).Order(), readings.Select(reading => reading.Frame.Id).Order());
        var expected = PhotoFixtureCatalogue.All.ToDictionary(fixture => fixture.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var reading in readings)
        {
            var fixture = expected[reading.Frame.FileName];
            Assert.Equal(fixture.Reason, reading.Skipped?.Reason);
            if (reading.Succeeded)
            {
                Assert.Equal(fixture.Size, reading.Frame.PixelSize);
            }
        }

        Assert.Equal(16, readings.Count(reading => reading.Succeeded));
        Assert.Equal(
            ["empty.jpg", "no-eoi.jpg", "text.heic", "truncated.jpg"],
            readings.Where(reading => !reading.Succeeded).Select(reading => reading.Frame.FileName).Order(StringComparer.Ordinal));

        var dated = Assert.Single(readings, reading => reading.Frame.FileName == "exif-date.jpg").Frame;
        Assert.Equal(new DateTime(2024, 6, 15, 14, 30, 45, 123), dated.CaptureTime);
    }

    [Fact]
    public async Task ARawFileWithAJpegOfTheSameNameInTheSameFolderIsLeftOut()
    {
        Touch("DSC_0001.NEF", "DSC_0001.JPG", "DSC_0002.NEF", "DSC_0003.dng", "DSC_0003.heic", "other/DSC_0002.jpg");

        var listed = await ListAsync(new PhotoImporter(Importable) { IsReadable = Importable }, new ImportRequest([_root], IncludeSubfolders: false));

        // DSC_0002.NEF has its JPEG in another folder, so it stays.
        Assert.Equal(["DSC_0001.JPG", "DSC_0002.NEF", "DSC_0003.heic"], Names(listed.Frames));
    }

    [Fact]
    public async Task ARawFileStaysWhenThisPcCannotDecodeItsPartner()
    {
        Touch("IMG_0001.CR3", "IMG_0001.HEIF", "IMG_0002.CR3", "IMG_0002.JPG");

        // A codec set without HEIF: the HEIF is listed (it will come back as needing an extension), and the CR3 stays.
        var importer = new PhotoImporter(path => Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".cr3" or ".heif")
        {
            IsReadable = path => Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase),
        };

        var listed = await ListAsync(importer, new ImportRequest([_root], IncludeSubfolders: false));

        Assert.Equal(["IMG_0001.CR3", "IMG_0001.HEIF", "IMG_0002.JPG"], Names(listed.Frames));
    }

    [Fact]
    public async Task SubfoldersAreSearchedOnlyWhenAskedAndHiddenOnesNever()
    {
        Touch("IMG_2.jpg", "IMG_10.jpg", "b/IMG_1.jpg", "a/deeper/IMG_1.jpg", "a/IMG_1.jpg", "notes.txt");
        var hidden = Directory.CreateDirectory(Path.Combine(_root, "hidden"));
        File.WriteAllBytes(Path.Combine(hidden.FullName, "IMG_9.jpg"), [1]);
        hidden.Attributes |= FileAttributes.Hidden;
        File.WriteAllBytes(Path.Combine(_root, "IMG_3.jpg"), [1]);
        File.SetAttributes(Path.Combine(_root, "IMG_3.jpg"), FileAttributes.Hidden);

        var flat = await ListAsync(Importer(), new ImportRequest([_root], IncludeSubfolders: false));
        var deep = await ListAsync(Importer(), new ImportRequest([_root], IncludeSubfolders: true));

        Assert.Equal(["IMG_2.jpg", "IMG_10.jpg"], Names(flat.Frames));
        Assert.Equal(
            [Path.Combine(_root, "IMG_2.jpg"), Path.Combine(_root, "IMG_10.jpg"), Path.Combine(_root, "a", "IMG_1.jpg"), Path.Combine(_root, "a", "deeper", "IMG_1.jpg"), Path.Combine(_root, "b", "IMG_1.jpg")],
            deep.Frames.Select(frame => frame.SourcePath));
    }

    [Fact]
    public async Task AJunctionBackToItsOwnFolderIsNotFollowedSoEachPhotoIsListedOnceAndTheWalkEnds()
    {
        Touch("IMG_1.jpg", "a/IMG_2.jpg");
        var loop = Path.Combine(_root, "a", "loop");
        MakeJunction(loop, _root);
        try
        {
            Assert.True(File.Exists(Path.Combine(loop, "IMG_1.jpg")));

            var listing = ListAsync(Importer(), new ImportRequest([_root], IncludeSubfolders: true));
            var listed = await listing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.Equal([Path.Combine(_root, "IMG_1.jpg"), Path.Combine(_root, "a", "IMG_2.jpg")], listed.Frames.Select(frame => frame.SourcePath));
        }
        finally
        {
            // Removes the link, never what it points at.
            Directory.Delete(loop);
        }
    }

    [Fact]
    public async Task FilesAreTakenAsGivenUnsupportedOnesAreLeftOutAndMissingOnesAreSkipped()
    {
        Touch("IMG_1.jpg", "IMG_2.jpg", "notes.txt");
        var missing = Path.Combine(_root, "gone.jpg");
        var file = new FileInfo(Path.Combine(_root, "IMG_2.jpg"));

        var listed = await ListAsync(
            Importer(),
            new ImportRequest([Path.Combine(_root, "IMG_2.jpg"), Path.Combine(_root, "notes.txt"), missing, Path.Combine(_root, "IMG_2.jpg")], IncludeSubfolders: false));

        var frame = Assert.Single(listed.Frames);
        Assert.Equal(file.FullName, frame.SourcePath);
        Assert.True(frame.Matches(file.Length, file.LastWriteTimeUtc));
        var skipped = Assert.Single(listed.Skipped);
        Assert.Equal(missing, skipped.Path);
        Assert.Equal(SkipReason.FileNotFound, skipped.Reason);
    }

    [Fact]
    public async Task TheFirstBatchIsSmallSoTheFirstFramesShowAtOnce()
    {
        Touch([.. Enumerable.Range(1, 25).Select(i => $"IMG_{i:0000}.jpg")]);
        var importer = new PhotoImporter(Importable, firstListBatch: 4, listBatch: 10);

        var sizes = new List<int>();
        await foreach (var batch in importer.ListAsync(new ImportRequest([_root], false), TestContext.Current.CancellationToken))
        {
            sizes.Add(batch.Frames.Count);
        }

        Assert.Equal([4, 10, 10, 1], sizes);
    }

    [Fact]
    public async Task ReadingHandsEveryFrameOutOnceInBatches()
    {
        Touch([.. Enumerable.Range(1, 30).Select(i => $"IMG_{i:0000}.jpg")]);
        var importer = new PhotoImporter(Importable, readers: 4, readBatch: 7);
        var listed = await ListAsync(importer, new ImportRequest([_root], false));

        var batches = new List<IReadOnlyList<FrameReading>>();
        await foreach (var batch in importer.ReadAsync(listed.Frames, TestContext.Current.CancellationToken))
        {
            batches.Add(batch);
        }

        // One-byte files are no JPEGs: each comes back skipped, none is lost or doubled.
        Assert.Equal(30, batches.Sum(batch => batch.Count));
        Assert.Equal(30, batches.SelectMany(batch => batch).Select(reading => reading.Frame.Id).Distinct().Count());
        Assert.All(batches.SelectMany(batch => batch), reading => Assert.False(reading.Succeeded));
        Assert.All(batches, batch => Assert.InRange(batch.Count, 1, 7));
    }

    [Fact]
    public async Task AFolderThatIsNotThereIsSkipped()
    {
        var gone = Path.Combine(_root, "card");

        var listed = await ListAsync(Importer(), new ImportRequest([gone], IncludeSubfolders: true));

        Assert.Empty(listed.Frames);
        Assert.Equal(SkipReason.FileNotFound, Assert.Single(listed.Skipped).Reason);
    }

    private static bool Importable(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".nef" or ".dng" or ".heic";

    private static PhotoImporter Importer() => new(Importable);

    private static List<string> Names(IEnumerable<Frame> frames) => [.. frames.Select(frame => frame.FileName)];

    private static async Task<(List<Frame> Frames, List<SkippedFile> Skipped)> ListAsync(PhotoImporter importer, ImportRequest request)
    {
        var frames = new List<Frame>();
        var skipped = new List<SkippedFile>();
        await foreach (var batch in importer.ListAsync(request, TestContext.Current.CancellationToken))
        {
            frames.AddRange(batch.Frames);
            skipped.AddRange(batch.Skipped);
        }

        return (frames, skipped);
    }

    // A directory junction (no administrator rights needed, unlike a symbolic link).
    private static void MakeJunction(string link, string target)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", link, target])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }

    private void Touch(params string[] names)
    {
        foreach (var name in names)
        {
            var path = Path.Combine(_root, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [1]);
        }
    }
}
