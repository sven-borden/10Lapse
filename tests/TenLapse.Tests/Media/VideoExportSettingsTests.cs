using TenLapse.Core.Timing;
using TenLapse.Media.Export;

namespace TenLapse.Tests.Media;

/// <summary>The export settings' defaults: how much it decodes ahead on this kind of PC.</summary>
public sealed class VideoExportSettingsTests
{
    private const long GiB = 1L << 30;

    private static readonly VideoExportSettings _settings = new(1920, 1080, new Fraction(30, 1));

    [Fact]
    public void ADedicatedGpuDecodesEightAheadAndATightPcFour()
    {
        var threads = Math.Clamp(Environment.ProcessorCount / 2, 1, 6);

        Assert.Equal((8, threads), _settings.DecodeFor(tightMemory: false));
        Assert.Equal((4, Math.Min(threads, 3)), _settings.DecodeFor(tightMemory: true));
    }

    [Fact]
    public void SetValuesWinAndTheThreadsNeverExceedTheDepth()
    {
        Assert.Equal((5, 2), (_settings with { DecodeAhead = 5, DecodeThreads = 2 }).DecodeFor(tightMemory: true));
        Assert.Equal((2, 2), (_settings with { DecodeAhead = 2, DecodeThreads = 6 }).DecodeFor(tightMemory: false));
        Assert.Equal((1, 1), (_settings with { DecodeAhead = 0, DecodeThreads = 0 }).DecodeFor(tightMemory: false));
    }

    [Fact]
    public void MemoryIsTightOnAnIntegratedGpuOrWithEightGigabytesOfRamOrLess()
    {
        Assert.True(VideoExportSettings.IsTightMemory(unifiedMemory: true, 64 * GiB));
        Assert.True(VideoExportSettings.IsTightMemory(unifiedMemory: false, (8 * GiB) - (300L << 20)));
        Assert.True(VideoExportSettings.IsTightMemory(unifiedMemory: false, 8 * GiB));
        Assert.False(VideoExportSettings.IsTightMemory(unifiedMemory: false, 12 * GiB));
        Assert.False(VideoExportSettings.IsTightMemory(unifiedMemory: false, (16 * GiB) - (400L << 20)));
    }
}
