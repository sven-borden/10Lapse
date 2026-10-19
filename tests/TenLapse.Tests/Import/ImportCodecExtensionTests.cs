using TenLapse.Core.Import;
using TenLapse.Core.Projects;
using TenLapse.Media.Decoding;
using TenLapse.Presentation.Services;

namespace TenLapse.Tests.Import;

/// <summary>#14 through the import: photos Windows cannot decode for want of an extension, and what the user is told.</summary>
public sealed class ImportCodecExtensionTests
{
    private static ScriptedImportSource Source(int heic, int broken, params PhotoCodecExtension[] needed)
    {
        var frames = Enumerable.Range(1, heic).Select(i => ScriptedImportSource.Photo($"IMG_{i:0000}.HEIC"))
            .Concat(Enumerable.Range(1, broken).Select(i => ScriptedImportSource.Photo($"BAD_{i:0000}.JPG")))
            .Concat([ScriptedImportSource.Photo("OK_0001.JPG")])
            .ToList();
        return new ScriptedImportSource(frames)
        {
            Read = frame => Path.GetFileName(frame.SourcePath) switch
            {
                var name when name.StartsWith("IMG_", StringComparison.Ordinal) =>
                    FrameReading.Failed(frame, new SkippedFile(frame.SourcePath, SkipReason.NeedsExtension, "Open: missing.", needed)),
                var name when name.StartsWith("BAD_", StringComparison.Ordinal) =>
                    FrameReading.Failed(frame, new SkippedFile(frame.SourcePath, SkipReason.DecodeFailed, "Damaged.")),
                _ => FrameReading.Read(frame with { PixelSize = new PixelSize(4, 3) }),
            },
            ReadBatch = 50,
        };
    }

    [Fact]
    public async Task ThreeHundredHeicPhotosWithoutTheExtensionAreOneInfoBarNotThreeHundred()
    {
        using var harness = ImportHarness.With(Source(300, 0, KnownPhotoFormats.HeifExtension));

        await harness.ImportAsync(@"C:\Scripted");

        var bar = Assert.Single(harness.InfoBars.Shown);
        Assert.Equal("These 300 photos need the free HEIF Image Extensions from Microsoft.", bar.Message);
        Assert.Equal("Get it from the Store", bar.ActionLabel);
        Assert.Equal(300, harness.Project.SkippedFiles.Length);
        Assert.Single(harness.Project.Frames);
        Assert.Empty(harness.RealFailures());
    }

    [Fact]
    public async Task DamagedPhotosKeepTheirOwnListAndThePromptStaysSeparate()
    {
        using var harness = ImportHarness.With(Source(12, 2, KnownPhotoFormats.HeifExtension));

        await harness.ImportAsync(@"C:\Scripted");

        Assert.Equal(2, harness.InfoBars.Shown.Count);
        var list = harness.InfoBars.Shown.Single(bar => bar.HasDetails);
        Assert.Equal("2 photos couldn't be read and were skipped.", list.Message);
        Assert.All(list.Details, detail => Assert.StartsWith("BAD_", detail.Text, StringComparison.Ordinal));
        var prompt = harness.InfoBars.Shown.Single(bar => !bar.HasDetails);
        Assert.Equal("These 12 photos need the free HEIF Image Extensions from Microsoft.", prompt.Message);
    }

    [Fact]
    public async Task EveryImportAsksWindowsAgainSoAnInstalledExtensionIsFoundWithoutARestart()
    {
        using var first = ImportHarness.With(Source(5, 0, KnownPhotoFormats.HeifExtension));
        await first.ImportAsync(@"C:\Scripted");
        Assert.Equal(1, first.Codecs.Refreshed);
        Assert.Single(first.InfoBars.Shown);

        // The user installed the extension and adds the folder again: the import refreshes before it reads, and the
        // photos that now read leave nothing to say.
        var fixedSource = new ScriptedImportSource([.. Enumerable.Range(1, 5).Select(i => ScriptedImportSource.Photo($"IMG_{i:0000}.HEIC"))]);
        using var second = ImportHarness.With(fixedSource);
        await second.ImportAsync(@"C:\Scripted");
        await second.ImportAsync(@"C:\Scripted");

        Assert.Equal(2, second.Codecs.Refreshed);
        Assert.DoesNotContain(second.InfoBars.Shown, bar => bar.ActionLabel is not null);
        Assert.Equal(5, second.Project.Frames.Length);
    }

    [Fact]
    public async Task WhenBothHeifAndHevcAreMissingTheImportRaisesTheOneInfoBarThatNamesBoth()
    {
        using var harness = ImportHarness.With(Source(8, 0, KnownPhotoFormats.HeifExtension, KnownPhotoFormats.HevcExtension));
        harness.Codecs.Missing.Add(KnownPhotoFormats.HevcExtension);

        await harness.ImportAsync(@"C:\Scripted");

        var bar = Assert.Single(harness.InfoBars.Shown);
        Assert.Equal(
            "These 8 photos need two extensions from Microsoft: the free HEIF Image Extensions, then the HEVC Video Extensions (a small fee).",
            bar.Message);
    }

    [Fact]
    public async Task ThePressedButtonOpensTheStoreThroughTheLauncherAndNeverMakesAnError()
    {
        using var harness = ImportHarness.With(Source(3, 0, KnownPhotoFormats.RawExtension));
        await harness.ImportAsync(@"C:\Scripted");
        var bar = Assert.Single(harness.InfoBars.Shown);

        await harness.Ui.RunAsync(() => bar.Action!());

        Assert.Equal([KnownPhotoFormats.RawExtension], harness.Codecs.Opened);
        Assert.Empty(harness.RealFailures());
    }
}
