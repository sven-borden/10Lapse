using TenLapse.Core.Projects;
using TenLapse.Media.Encoding;
using TenLapse.Media.Export;
using static TenLapse.Tests.Media.EncoderCapabilitiesTests;

namespace TenLapse.Tests.Media;

/// <summary>The #9 cache file: round trip, invalidation by key, and bad files ignored. No GPU involved.</summary>
public sealed class EncoderCacheTests : IDisposable
{
    private static readonly EncoderMft[] _encoders =
    [
        new(VideoFormat.Mp4H264, "GPU H.264 Encoder MFT", true, new Guid("60f44560-5a20-4857-bfef-d29773cb8040")),
        new(VideoFormat.Mp4H264, "H264 Encoder MFT", false, new Guid("6ca50344-051a-4ded-9779-a43305165e35")),
        new(VideoFormat.Mp4Hevc, "HEVCVideoExtensionEncoder", false, Guid.Empty),
    ];

    private static readonly DecoderMft[] _decoders = [HevcDecoder];

    private static readonly EncoderCacheKey _key = new(
        EncoderCacheKey.CurrentSchema,
        "VEN_10DE&DEV_2803&SUBSYS_171719DA&REV_A1",
        "NVIDIA GeForce RTX 4060 Ti",
        "32.0.16.1714",
        "10.0.26300.9711",
        EncoderCacheKey.EncoderList(_encoders),
        EncoderCacheKey.DecoderList(_decoders));

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TenLapse.Tests", Guid.NewGuid().ToString("N"));

    private string CachePath => Path.Combine(_directory, "nested", "encoders.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static EncoderCapabilities Sample() => new(
        _encoders,
        [
            Probe(VideoFormat.Mp4H264, 3840, 2160, 60, EncoderKind.Software),
            Probe(VideoFormat.Mp4H264, 2560, 1440, 60, EncoderKind.Hardware),
            Probe(VideoFormat.Mp4Hevc, 3840, 2160, 60, EncoderKind.None, VideoExportFailure.SettingsRejected),
        ],
        TimeSpan.FromMilliseconds(1082.5),
        decoders: _decoders);

    [Fact]
    public void AWrittenResultReadsBackTheSame()
    {
        var written = Sample();
        EncoderCapabilitiesCache.Write(CachePath, _key, written);

        var read = EncoderCapabilitiesCache.TryRead(CachePath, _key);

        Assert.NotNull(read);
        Assert.Equal(written.Encoders, read.Encoders);
        Assert.Equal(written.Probes, read.Probes);
        Assert.Equal(written.ProbeDuration, read.ProbeDuration);
        Assert.Equal(written.Decoders, read.Decoders);
        Assert.True(read.CanPlayBack(VideoFormat.Mp4Hevc));
        Assert.Null(read.DeviceFailure);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(CachePath)!, "*.tmp"));
    }

    [Fact]
    public void AnyChangeToTheKeyMissesTheCache()
    {
        EncoderCapabilitiesCache.Write(CachePath, _key, Sample());
        EncoderMft[] withHevc = [.. _encoders, new(VideoFormat.Mp4Hevc, "GPU HEVC Encoder MFT", true, Guid.NewGuid())];

        EncoderCacheKey[] changed =
        [
            _key with { Schema = EncoderCacheKey.CurrentSchema + 1 },
            _key with { Adapter = "VEN_8086&DEV_A780&SUBSYS_00000000&REV_04" },
            _key with { Driver = "32.0.16.1720" },
            _key with { OsBuild = "10.0.26300.9800" },
            _key with { Encoders = EncoderCacheKey.EncoderList(withHevc) },
            _key with { Encoders = EncoderCacheKey.EncoderList(_encoders[..2]) },
            _key with { Decoders = EncoderCacheKey.DecoderList([]) },
            _key with { Decoders = EncoderCacheKey.DecoderList([.. _decoders, new(VideoFormat.Mp4Hevc, "GPU HEVC Decoder", true, Guid.NewGuid())]) },
        ];

        foreach (var key in changed)
        {
            Assert.Null(EncoderCapabilitiesCache.TryRead(CachePath, key));
        }

        Assert.NotNull(EncoderCapabilitiesCache.TryRead(CachePath, _key with { }));
    }

    [Fact]
    public void TheEncoderListIgnoresOrder() =>
        Assert.Equal(EncoderCacheKey.EncoderList(_encoders), EncoderCacheKey.EncoderList(_encoders.Reverse()));

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"key":null,"encoders":[],"probes":[],"probeTicks":1}""")]
    [InlineData("""{"key":{"schema":1,"adapter":"a","adapterName":"b","driver":"c","osBuild":"d","encoders":"e"},"encoders":[],"probes":[{"format":"Mp4Av1","width":1,"height":1,"framesPerSecond":30,"encoder":"Hardware","encoderName":null,"failure":null,"milliseconds":1}],"probeTicks":1}""")]
    [InlineData("""{"key":{"schema":2,"adapter":"a","adapterName":"b","driver":"c","osBuild":"d","encoders":"e","decoders":"f"},"encoders":[],"probes":[],"probeTicks":1}""")]
    public void ABadFileIsIgnoredAndRewritten(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        File.WriteAllText(CachePath, content);

        Assert.Null(EncoderCapabilitiesCache.TryRead(CachePath, _key));

        EncoderCapabilitiesCache.Write(CachePath, _key, Sample());
        Assert.NotNull(EncoderCapabilitiesCache.TryRead(CachePath, _key));
    }

    [Fact]
    public void AProbeRowWithAnImpossibleSizeIsIgnored()
    {
        EncoderCapabilitiesCache.Write(CachePath, _key, Sample());
        File.WriteAllText(CachePath, File.ReadAllText(CachePath).Replace("\"width\": 2560", "\"width\": -2", StringComparison.Ordinal));

        Assert.Null(EncoderCapabilitiesCache.TryRead(CachePath, _key));
    }

    [Fact]
    public void AMissingOrLockedFileIsAMiss()
    {
        Assert.Null(EncoderCapabilitiesCache.TryRead(CachePath, _key));

        EncoderCapabilitiesCache.Write(CachePath, _key, Sample());
        using (new FileStream(CachePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Null(EncoderCapabilitiesCache.TryRead(CachePath, _key));
        }
    }

    [Fact]
    public void AProvisionalResultIsNotCached()
    {
        var provisional = new EncoderCapabilities(_encoders, [], TimeSpan.Zero, VideoExportFailure.NoGraphicsDevice);

        Assert.Throws<ArgumentException>(() => EncoderCapabilitiesCache.Write(CachePath, _key, provisional));
        Assert.False(File.Exists(CachePath));
    }
}
