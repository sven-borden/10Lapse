using System.Globalization;
using TenLapse.Core.Ordering;

namespace TenLapse.Tests.Ordering;

/// <summary>Real files from cameras and apps, cut to their metadata (sources and licences in <c>Ordering/Fixtures/README.md</c>).</summary>
public static class CaptureTimeFixtureTests
{
    private static string Folder => Path.Combine(AppContext.BaseDirectory, "Ordering", "Fixtures");

    /// <summary>Each fixture with the time exiftool and MetadataExtractor read from the whole file, and its offset when there is one.</summary>
    private static readonly (string File, string Time, string? Offset)[] _fixtures =
    [
        ("jpeg-apple-iphone-6-plus.jpg", "2014-10-31 13:32:37.539", null),
        ("nef-nikon-d90-big-endian.nef", "2011-04-25 01:55:46", null),
        ("nef-nikon-d850.nef", "2017-10-15 17:35:20.41", "01:00"),
        ("dng-apple-iphone-12-pro.dng", "2020-12-29 14:24:45.7", "-05:00"),
        ("cr3-canon-eos-m200.cr3", "2025-08-09 13:27:20.94", "11:00"),
        ("heic-apple-iphone-img-2927.heic", "2020-05-04 18:39:11.644", "-04:00"),
        ("raf-fujifilm-finepix-s5500.raf", "2013-02-09 10:32:22", null),
        ("png-ztxt-raw-profile-issue-316.png", "2012-08-23 17:42:23.61", null),
    ];

    public static TheoryData<string, string, string?> Fixtures
    {
        get
        {
            var data = new TheoryData<string, string, string?>();
            foreach (var (file, time, offset) in _fixtures)
            {
                data.Add(file, time, offset);
            }

            return data;
        }
    }

    public static TheoryData<string> FixtureFiles => [.. _fixtures.Select(fixture => fixture.File)];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public static void ReadsTheCaptureTimeOfARealFile(string file, string time, string? offset)
    {
        var reading = CaptureTimeReader.ReadFile(Path.Combine(Folder, file));

        Assert.True(reading is not null, $"no capture time in {file}");
        Assert.Equal(DateTime.Parse(time, CultureInfo.InvariantCulture), reading.Value.Time);
        Assert.Equal(offset is null ? null : TimeSpan.Parse(offset, CultureInfo.InvariantCulture), reading.Value.UtcOffset);
        Assert.Equal(CaptureTimeSource.DateTimeOriginal, reading.Value.Source);
    }

    [Fact]
    public static void TheFolderHoldsExactlyTheListedFilesAndTheReadmeNamesEachOne()
    {
        var listed = _fixtures.Select(fixture => fixture.File).Order(StringComparer.Ordinal).ToArray();
        var present = Directory.EnumerateFiles(Folder).Select(Path.GetFileName).Where(name => name != "README.md").Order(StringComparer.Ordinal).ToArray();
        var readme = File.ReadAllText(Path.Combine(Folder, "README.md"));

        Assert.Equal(listed, present);
        Assert.All(listed, file => Assert.Contains($"`{file}`", readme, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public static void EveryTruncationAndCorruptionOfARealFileStaysCheap(string file)
    {
        var bytes = File.ReadAllBytes(Path.Combine(Folder, file));
        long most = 0;
        for (var length = 0; length < bytes.Length; length += 1 + (bytes.Length / 4000))
        {
            most = Math.Max(most, CaptureTimeReaderTests.AllocatedByRead(bytes[..length], out _));
        }

        var random = new Random(85);
        for (var i = 0; i < 300; i++)
        {
            var copy = (byte[])bytes.Clone();
            for (var edit = 0; edit < 2; edit++)
            {
                copy[random.Next(Math.Min(copy.Length, 4096))] = (byte)random.Next(256);
            }

            most = Math.Max(most, CaptureTimeReaderTests.AllocatedByRead(copy, out _));
        }

        Assert.True(most < CaptureTimeReaderTests.AllocationLimitPerRead, $"{file}: one read allocated {most:N0} bytes");
    }
}
