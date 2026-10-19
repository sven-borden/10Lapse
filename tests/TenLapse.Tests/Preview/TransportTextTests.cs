using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Presentation;
using TenLapse.Presentation.Preview;
using TenLapse.Presentation.Services;
using TenLapse.Tests.Presentation;

namespace TenLapse.Tests.Preview;

public sealed class TransportTextTests
{
    [Theory]
    [InlineData(TransportKey.Space, false, false, KeyFocus.Free, TransportAction.TogglePlay)]
    [InlineData(TransportKey.Left, false, false, KeyFocus.Free, TransportAction.StepBack)]
    [InlineData(TransportKey.Right, false, false, KeyFocus.Free, TransportAction.StepForward)]
    [InlineData(TransportKey.Left, true, false, KeyFocus.Free, TransportAction.StepBackTen)]
    [InlineData(TransportKey.Right, true, false, KeyFocus.Free, TransportAction.StepForwardTen)]
    [InlineData(TransportKey.Home, false, false, KeyFocus.Free, TransportAction.First)]
    [InlineData(TransportKey.End, false, false, KeyFocus.Free, TransportAction.Last)]
    [InlineData(TransportKey.Other, false, false, KeyFocus.Free, TransportAction.None)]
    // Typing: every key stays in the text box.
    [InlineData(TransportKey.Space, false, false, KeyFocus.TextInput, TransportAction.None)]
    [InlineData(TransportKey.Left, true, false, KeyFocus.TextInput, TransportAction.None)]
    [InlineData(TransportKey.Home, false, false, KeyFocus.TextInput, TransportAction.None)]
    // A list or a segmented control keeps its arrows; Space, Home and End still play and jump.
    [InlineData(TransportKey.Left, false, false, KeyFocus.ArrowControl, TransportAction.None)]
    [InlineData(TransportKey.Right, true, false, KeyFocus.ArrowControl, TransportAction.None)]
    [InlineData(TransportKey.Space, false, false, KeyFocus.ArrowControl, TransportAction.TogglePlay)]
    [InlineData(TransportKey.End, false, false, KeyFocus.ArrowControl, TransportAction.Last)]
    // Ctrl, Alt or Windows with a key belongs to someone else (Alt+Left is Back).
    [InlineData(TransportKey.Left, false, true, KeyFocus.Free, TransportAction.None)]
    [InlineData(TransportKey.Space, false, true, KeyFocus.Free, TransportAction.None)]
    [InlineData(TransportKey.Space, true, false, KeyFocus.Free, TransportAction.None)]
    public void TheTransportKeysAndWhoseTheyAre(TransportKey key, bool shift, bool other, KeyFocus focus, TransportAction expected)
    {
        Assert.Equal(expected, TransportKeys.Resolve(key, shift, other, focus));
    }

    [Theory]
    [InlineData(312, 742, "0312 / 0742", "Frame 312 of 742")]
    [InlineData(1, 1, "0001 / 0001", "Frame 1 of 1")]
    [InlineData(7, 12_345, "00007 / 12345", "Frame 7 of 12345")]
    public void TheCounterIsZeroPaddedToTheTotal(int number, int total, string counter, string name)
    {
        Assert.Equal(counter, ProjectText.PreviewCounter(number, total));
        Assert.Equal(name, ProjectText.PreviewCounterName(number, total));
    }

    [Fact]
    public void TheCounterRejectsAFrameOutsideTheClip()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectText.PreviewCounter(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectText.PreviewCounter(11, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectText.PreviewCounterName(1, 0));
    }

    [Fact]
    public void TheTimeCodeIsTheFramesStartAndTheClipLength()
    {
        var rate = new Fraction(30, 1);

        Assert.Equal("00:10.40 / 00:24.73", ProjectText.PreviewTimecode(FrameTiming.FrameStart(312, rate), FrameTiming.Duration(742, rate)));
        Assert.Equal("Time 00:10.40 of 00:24.73", ProjectText.PreviewTimecodeName(FrameTiming.FrameStart(312, rate), FrameTiming.Duration(742, rate)));
    }

    [Fact]
    public void ThePreviewsProblemsReadAsSentences()
    {
        Assert.Equal(ProjectText.PreviewFrameUnreadableOne, ProjectText.PreviewFrameUnreadable(1));
        Assert.StartsWith("3 photos can't be read", ProjectText.PreviewFrameUnreadable(3), StringComparison.Ordinal);
        Assert.StartsWith("1,200 photos", ProjectText.PreviewFrameUnreadable(1200), StringComparison.Ordinal);
        Assert.Equal("IMG_4402.JPG can't be read", ProjectText.PreviewPlaceholder("IMG_4402.JPG"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectText.PreviewFrameUnreadable(0));
    }
}

public sealed class PreviewProblemsTests
{
    [Fact]
    public void UnreadablePhotosBecomeOneBarThatALaterFlushReplaces()
    {
        var infoBars = new RecordingInfoBars();
        var log = new FakeErrorSink();
        var problems = new PreviewProblems(infoBars, log);

        Assert.True(problems.FrameFailed(Failure(@"C:\Photos\IMG_0001.JPG", SkipReason.FileNotFound)));
        Assert.False(problems.FrameFailed(Failure(@"C:\Photos\IMG_0002.JPG", SkipReason.Truncated)));
        Assert.False(problems.FrameFailed(Failure(@"C:\Photos\IMG_0001.JPG", SkipReason.FileNotFound)));
        problems.Flush();

        var first = Assert.Single(infoBars.Shown);
        Assert.Equal(InfoBarSeverity.Caution, first.Request.Severity);
        Assert.Equal(ProjectText.PreviewFrameUnreadable(2), first.Request.Message);
        Assert.Equal(["IMG_0001.JPG", "IMG_0002.JPG"], first.Request.Details.Select(detail => detail.Text));
        Assert.Equal(SkippedFileText.Reason(SkipReason.Truncated), first.Request.Details[1].Note);
        Assert.Equal(3, log.Reported.Count);

        // Nothing new: nothing shown. A new one: the longer list replaces the bar.
        problems.Flush();
        Assert.Single(infoBars.Shown);
        Assert.True(problems.FrameFailed(Failure(@"C:\Photos\IMG_0003.JPG", SkipReason.AccessDenied)));
        problems.Flush();
        Assert.Equal(2, infoBars.Shown.Count);
        Assert.Equal([first], infoBars.Dismissed);
        Assert.Equal(3, infoBars.Shown[1].Request.Details.Count);
    }

    [Fact]
    public void ACacheThatCannotBeWrittenIsToldOnceAndLoggedEachTime()
    {
        var infoBars = new RecordingInfoBars();
        var log = new FakeErrorSink();
        var problems = new PreviewProblems(infoBars, log);

        problems.CacheFailed(new IOException("The disk is full."));
        problems.CacheFailed(new IOException("The disk is full."));

        var bar = Assert.Single(infoBars.Shown);
        Assert.Equal(ProjectText.PreviewCacheFailed, bar.Request.Message);
        Assert.Equal(2, log.Reported.Count);
    }

    [Fact]
    public void ANewProjectStartsANewList()
    {
        var infoBars = new RecordingInfoBars();
        var problems = new PreviewProblems(infoBars, new FakeErrorSink());
        problems.FrameFailed(Failure(@"C:\Photos\IMG_0001.JPG", SkipReason.FileNotFound));
        problems.Flush();

        problems.Reset();
        Assert.True(problems.FrameFailed(Failure(@"C:\Photos\IMG_0001.JPG", SkipReason.FileNotFound)));
        problems.Flush();

        Assert.Equal(ProjectText.PreviewFrameUnreadable(1), infoBars.Shown[1].Request.Message);
        Assert.Empty(infoBars.Dismissed);
    }

    private static Core.Preview.FrameLoadFailure Failure(string path, SkipReason reason) =>
        new(new SkippedFile(path, reason, "test"));

    private sealed class RecordingInfoBars : IInfoBarService
    {
        private readonly InfoBarQueue _queue = new(new FakeErrorSink());

        public List<InfoBarTicket> Shown { get; } = [];

        public List<InfoBarTicket> Dismissed { get; } = [];

        public InfoBarTicket Show(InfoBarRequest request)
        {
            var ticket = _queue.Show(request);
            Shown.Add(ticket);
            return ticket;
        }

        public void Dismiss(InfoBarTicket ticket)
        {
            Dismissed.Add(ticket);
            _queue.Dismiss(ticket);
        }
    }
}
