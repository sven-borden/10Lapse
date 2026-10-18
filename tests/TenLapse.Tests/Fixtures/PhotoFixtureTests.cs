using TenLapse.Core.Projects;
using TenLapse.Media.Decoding;
using TenLapse.Tests.Media;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace TenLapse.Tests.Fixtures;

/// <summary>
/// Every file of the fixture folder (committed in <c>Fixtures/Photos</c>, or made at run time by
/// <see cref="RuntimeFixtures"/>) through <see cref="PhotoDecoder"/> at a 1080p cover size: nothing throws, each
/// problem file comes back with the reason <c>Fixtures/README.md</c> states, each good file decodes with the right
/// size, orientation and colours. The export side is in <see cref="PhotoFixtureExportTests"/>.
/// </summary>
public sealed class PhotoFixtureTests : IDisposable
{
    private static readonly PhotoDecodeOptions _cover = PhotoDecodeOptions.Cover(new PixelSize(1920, 1080));

    private readonly RuntimeFixtures _runtime = new();

    public static TheoryData<string> Names => PhotoFixtureCatalogue.Names();

    public void Dispose() => _runtime.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void TheFolderHoldsExactlyTheCataloguedFilesAndTheReadmeNamesEachOne()
    {
        var onDisk = Directory.EnumerateFiles(PhotoFixtureCatalogue.Directory).Select(Path.GetFileName).Order().ToArray();
        var catalogued = PhotoFixtureCatalogue.All.Select(fixture => fixture.Name).Order().ToArray();
        var readme = File.ReadAllText(PhotoFixtureCatalogue.Readme);

        Assert.Equal(catalogued, onDisk);
        foreach (var name in catalogued)
        {
            Assert.Contains($"`{name}`", readme, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task EachCommittedFileReadsAndDecodesOrReportsItsReason(string name)
    {
        var fixture = PhotoFixtureCatalogue.Get(name);
        Assert.SkipWhen(fixture.Skip is not null, fixture.Skip ?? string.Empty);

        var info = await PhotoDecoder.ReadInfoAsync(fixture.Path, Token);
        var result = await PhotoDecoder.DecodeAsync(fixture.Path, _cover, Token);

        if (fixture.Reason is { } reason)
        {
            Assert.False(info.Succeeded, $"{name} read as {info.Value}");
            Assert.False(result.Succeeded, $"{name} decoded");
            Assert.Equal(reason, info.Failure.Reason);
            Assert.Equal(reason, result.Failure.Reason);
            Assert.False(string.IsNullOrWhiteSpace(result.Failure.Detail));
            var skipped = result.Failure.ToSkippedFile();
            Assert.Equal((fixture.Path, reason), (skipped.Path, skipped.Reason));
            return;
        }

        Assert.True(info.Succeeded, $"{name}: {info.Failure?.Detail}");
        Assert.Equal(fixture.Size, info.Value.Size);
        Assert.Equal(fixture.Orientation, info.Value.Orientation);
        Assert.Equal(fixture.Frames, info.Value.FrameCount);
        Assert.True(result.Succeeded, $"{name}: {result.Failure?.Detail}");
        using var photo = result.Value;
        Assert.Equal(fixture.Size, photo.Size);
        Assert.Equal(BitmapPixelFormat.Bgra8, photo.Pixels.BitmapPixelFormat);
        Assert.Equal(BitmapAlphaMode.Premultiplied, photo.Pixels.BitmapAlphaMode);
        AssertQuadrants(photo, fixture);
    }

    [Fact]
    public async Task OnlyTheRedMarkerMovesWithTheOrientation()
    {
        // The same four colours, shown four ways: the red top-left marker lands in the corner the orientation names.
        var corners = new Dictionary<int, int> { [1] = 0, [3] = 3, [6] = 1, [8] = 2 };
        foreach (var (orientation, corner) in corners)
        {
            var path = PhotoFixtureCatalogue.Get($"orientation-{orientation}.jpg").Path;
            var result = await PhotoDecoder.DecodeAsync(path, _cover, Token);

            Assert.True(result.Succeeded, result.Failure?.Detail);
            using var photo = result.Value;
            for (var quadrant = 0; quadrant < 4; quadrant++)
            {
                Assert.True(
                    PhotoFixtures.IsRed(PhotoFixtures.Quadrant(photo, quadrant)) == (quadrant == corner),
                    $"orientation {orientation}: quadrant {quadrant} is {PhotoFixtures.Quadrant(photo, quadrant)}");
            }
        }
    }

    [Fact]
    public async Task TheExifDateAndSubSecondsOfAPhotoCanBeReadWithWindowsOwnProperties()
    {
        // What #11 will order by: the fixture really carries DateTimeOriginal and SubSecTimeOriginal.
        await using var file = new FileStream(PhotoFixtureCatalogue.Get("exif-date.jpg").Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var stream = file.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(Token);

        var properties = await decoder.BitmapProperties.GetPropertiesAsync(["System.Photo.DateTaken", "/app1/ifd/exif/{ushort=37521}"]).AsTask(Token);

        // The property system folds the sub-second field into the date (it reads 45.12 s here: hundredths).
        var taken = ((DateTimeOffset)properties["System.Photo.DateTaken"].Value).DateTime;
        Assert.InRange(taken, new DateTime(2024, 6, 15, 14, 30, 45), new DateTime(2024, 6, 15, 14, 30, 45).AddMilliseconds(200));
        Assert.Equal("123", properties["/app1/ifd/exif/{ushort=37521}"].Value);
    }

    [Fact]
    public async Task AFileAnotherStreamHoldsWithFileShareNoneIsFileInUse()
    {
        var path = await _runtime.LockedAsync();

        await AssertFailsAsync(path, SkipReason.FileInUse);
    }

    [Fact]
    public async Task AFileDeletedAfterItWasListedIsFileNotFound()
    {
        var path = await _runtime.DeletedAfterListingAsync();

        await AssertFailsAsync(path, SkipReason.FileNotFound);
    }

    [Fact]
    public async Task ALongUnicodePathPastMaxPathDecodes()
    {
        var path = await _runtime.LongUnicodeAsync();

        await AssertDecodesAsync(path, new PixelSize(64, 32));
    }

    [Fact]
    public async Task AReadOnlyFileDecodes()
    {
        var path = await _runtime.ReadOnlyAsync();

        await AssertDecodesAsync(path, new PixelSize(64, 32));
    }

    [Fact]
    public async Task AHundredMegapixelJpegReadsInstantlyAndDecodesAtTheCoverSize()
    {
        var path = await RuntimeFixtures.HugeJpegAsync();

        var info = await PhotoDecoder.ReadInfoAsync(path, Token);
        var result = await PhotoDecoder.DecodeAsync(path, _cover, Token);

        Assert.True(info.Succeeded, info.Failure?.Detail);
        Assert.Equal(new PixelSize(RuntimeFixtures.HugeSide, RuntimeFixtures.HugeSide), info.Value.Size);
        Assert.True(result.Succeeded, result.Failure?.Detail);
        using var photo = result.Value;
        // 10000x10000 covering 1920x1080 is 1920 wide; the crop trims rows later, the decode keeps them all.
        Assert.Equal(new PixelSize(1920, 1920), photo.Size);
        Assert.True(PhotoFixtures.IsRed(PhotoFixtures.Quadrant(photo, 0)), $"top-left is {PhotoFixtures.Quadrant(photo, 0)}");
        Assert.False(PhotoFixtures.IsRed(PhotoFixtures.Quadrant(photo, 3)));
    }

    [Fact]
    public async Task ImportingTheWholeFolderReportsEveryProblemFileWithItsReasonAndThrowsNothing()
    {
        // What #12's import does with a folder: filter by extension, read each file's header, collect the failures.
        var locked = await _runtime.LockedAsync();
        var readOnly = await _runtime.ReadOnlyAsync();
        var longPath = await _runtime.LongUnicodeAsync();
        var listed = Directory.EnumerateFiles(PhotoFixtureCatalogue.Directory)
            .Concat([locked, readOnly, longPath])
            .Where(PhotoCodecs.IsReadable)
            .ToList();

        var skipped = new List<SkippedFile>();
        var imported = new List<string>();
        foreach (var path in listed)
        {
            var info = await PhotoDecoder.ReadInfoAsync(path, Token);
            if (info.Succeeded)
            {
                imported.Add(Path.GetFileName(path));
            }
            else
            {
                skipped.Add(info.Failure.ToSkippedFile());
            }
        }

        Assert.Equal(PhotoFixtureCatalogue.All.Count + 3, listed.Count);
        var expected = PhotoFixtureCatalogue.All.Where(fixture => !fixture.Decodes).ToDictionary(fixture => Path.GetFileName(fixture.Path), fixture => fixture.Reason!.Value);
        expected["locked.jpg"] = SkipReason.FileInUse;
        Assert.Equal(
            expected.OrderBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value)),
            skipped.Select(file => (Path.GetFileName(file.Path), file.Reason)).OrderBy(pair => pair.Item1));
        Assert.Equal(PhotoFixtureCatalogue.All.Count(fixture => fixture.Decodes) + 2, imported.Count);
    }

    private static async Task AssertFailsAsync(string path, SkipReason reason)
    {
        var info = await PhotoDecoder.ReadInfoAsync(path, Token);
        var result = await PhotoDecoder.DecodeAsync(path, _cover, Token);

        Assert.False(info.Succeeded, $"{Path.GetFileName(path)} read as {info.Value}");
        Assert.False(result.Succeeded, $"{Path.GetFileName(path)} decoded");
        Assert.Equal(reason, info.Failure.Reason);
        Assert.Equal(reason, result.Failure.Reason);
        Assert.Equal(path, result.Failure.Path);
        Assert.False(string.IsNullOrWhiteSpace(result.Failure.Detail));
    }

    private static async Task AssertDecodesAsync(string path, PixelSize size)
    {
        var info = await PhotoDecoder.ReadInfoAsync(path, Token);
        var result = await PhotoDecoder.DecodeAsync(path, _cover, Token);

        Assert.True(info.Succeeded, info.Failure?.Detail);
        Assert.Equal(size, info.Value.Size);
        Assert.True(result.Succeeded, result.Failure?.Detail);
        using var photo = result.Value;
        Assert.Equal(size, photo.Size);
        Assert.True(PhotoFixtures.IsRed(PhotoFixtures.Quadrant(photo, 0)));
    }

    private static void AssertQuadrants(DecodedPhoto photo, PhotoFixture fixture)
    {
        var bytes = PhotoFixtures.Bgra(photo);
        var expected = fixture.Quadrants!;
        for (var quadrant = 0; quadrant < 4; quadrant++)
        {
            var x = quadrant % 2 == 0 ? photo.Size.Width / 4 : photo.Size.Width * 3 / 4;
            var y = quadrant < 2 ? photo.Size.Height / 4 : photo.Size.Height * 3 / 4;
            var offset = ((y * photo.Size.Width) + x) * 4;
            var actual = new Rgba(bytes[offset + 2], bytes[offset + 1], bytes[offset], bytes[offset + 3]);
            var want = expected[quadrant];
            var off = new[] { actual.R - want.R, actual.G - want.G, actual.B - want.B, actual.A - want.A }.Max(Math.Abs);
            Assert.True(off <= fixture.Tolerance, $"{fixture.Name}, quadrant {quadrant}: {actual}, expected {want} within {fixture.Tolerance}");
        }
    }
}
