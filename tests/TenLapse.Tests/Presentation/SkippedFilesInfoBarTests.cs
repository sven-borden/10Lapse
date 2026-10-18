using TenLapse.Core.Projects;
using TenLapse.Presentation;
using TenLapse.Presentation.Services;

namespace TenLapse.Tests.Presentation;

public sealed class SkippedFilesInfoBarTests
{
    [Fact]
    public void NothingSkippedRaisesNothing() => Assert.Null(SkippedFilesInfoBar.Create([]));

    [Fact]
    public void OnePhotoReadsInTheSingular()
    {
        var request = SkippedFilesInfoBar.Create([new SkippedFile(@"C:\Photos\IMG_4417.HEIC", SkipReason.EmptyFile, "Open: 0 bytes.")]);

        Assert.NotNull(request);
        Assert.Equal("1 photo couldn't be read and was skipped.", request.Message);
        Assert.Equal(InfoBarSeverity.Caution, request.Severity);
        Assert.Equal(new[] { new InfoBarDetail("IMG_4417.HEIC", "Empty file (0 bytes)") }, request.Details);
    }

    [Fact]
    public void SeveralPhotosAreOneInfoBarWithALineEachInOrder()
    {
        var request = SkippedFilesInfoBar.Create(
        [
            new SkippedFile(@"C:\Photos\IMG_4402.HEIC", SkipReason.Truncated, "Decode: ends early."),
            new SkippedFile(@"C:\Photos\IMG_4417.HEIC", SkipReason.EmptyFile, ""),
            new SkippedFile(@"D:\Card\DCIM\IMG_0001.jpg", SkipReason.FileInUse, "Open: ERROR_SHARING_VIOLATION"),
        ]);

        Assert.NotNull(request);
        Assert.Equal("3 photos couldn't be read and were skipped.", request.Message);
        Assert.True(request.HasDetails);
        Assert.Null(request.Action);
        InfoBarDetail[] expected =
        [
            new("IMG_4402.HEIC", "Damaged file: it ends early"),
            new("IMG_4417.HEIC", "Empty file (0 bytes)"),
            new("IMG_0001.jpg", "In use by another app"),
        ];
        Assert.Equal(expected, request.Details);
    }

    [Fact]
    public void ALargeCountIsGrouped()
    {
        var skipped = Enumerable.Range(0, 1204).Select(i => new SkippedFile($@"C:\Photos\IMG_{i:0000}.jpg", SkipReason.DecodeFailed, ""));

        var request = SkippedFilesInfoBar.Create(skipped);

        Assert.Equal("1,204 photos couldn't be read and were skipped.", request?.Message);
        Assert.Equal(1204, request?.Details.Count);
    }

    [Fact]
    public void TheSameSkipsReadTheSameSoTheQueueFoldsThem()
    {
        SkippedFile[] skipped = [new(@"C:\Photos\a.jpg", SkipReason.DecodeFailed, "x"), new(@"C:\Photos\b.jpg", SkipReason.Other, "y")];

        Assert.True(SkippedFilesInfoBar.Create(skipped)!.ReadsLike(SkippedFilesInfoBar.Create(skipped)!));
    }

    [Fact]
    public void EveryReasonHasItsOwnShortText()
    {
        var texts = Enum.GetValues<SkipReason>().Select(SkippedFileText.Reason).ToArray();

        Assert.All(texts, text => Assert.False(string.IsNullOrWhiteSpace(text)));
        Assert.All(texts, text => Assert.False(text.EndsWith('.'), $"\"{text}\" is a list note, not a sentence"));
        Assert.Equal(texts.Length, texts.Distinct(StringComparer.Ordinal).Count());
        Assert.Throws<ArgumentOutOfRangeException>(() => SkippedFileText.Reason((SkipReason)99));
    }

    [Fact]
    public void ThePrototypesWordsAreKept()
    {
        Assert.Equal("Damaged file: it ends early", SkippedFileText.Reason(SkipReason.Truncated));
        Assert.Equal("Empty file (0 bytes)", SkippedFileText.Reason(SkipReason.EmptyFile));
    }

    [Fact]
    public void ANullEntryIsRefused() =>
        Assert.Throws<ArgumentException>(() => SkippedFilesInfoBar.Create([null!]));
}
