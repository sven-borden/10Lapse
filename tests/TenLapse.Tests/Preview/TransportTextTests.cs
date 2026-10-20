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
    [InlineData(TransportKey.Space, KeyFocus.Free, TransportAction.None)]
    [InlineData(TransportKey.Space, KeyFocus.ArrowControl, TransportAction.None)]
    [InlineData(TransportKey.Left, KeyFocus.Free, TransportAction.StepBack)]
    [InlineData(TransportKey.Right, KeyFocus.Free, TransportAction.StepForward)]
    [InlineData(TransportKey.End, KeyFocus.Free, TransportAction.Last)]
    public void AHeldSpaceDoesNotRepeatButTheArrowsDo(TransportKey key, KeyFocus focus, TransportAction expected)
    {
        Assert.Equal(expected, TransportKeys.Resolve(key, shift: false, otherModifier: false, focus, repeat: true));
    }

    [Theory]
    [InlineData(FocusedControl.Other, KeyFocus.Free)]
    [InlineData(FocusedControl.TextBox, KeyFocus.TextInput)]
    [InlineData(FocusedControl.PasswordBox, KeyFocus.TextInput)]
    [InlineData(FocusedControl.RichEditBox, KeyFocus.TextInput)]
    [InlineData(FocusedControl.AutoSuggestBox, KeyFocus.TextInput)]
    [InlineData(FocusedControl.NumberBox, KeyFocus.TextInput)]
    [InlineData(FocusedControl.EditableComboBox, KeyFocus.TextInput)]
    [InlineData(FocusedControl.None, KeyFocus.Free)]
    [InlineData(FocusedControl.ComboBox, KeyFocus.ArrowControl)]
    [InlineData(FocusedControl.ComboBoxItem, KeyFocus.ArrowControl)]
    [InlineData(FocusedControl.RangeBase, KeyFocus.ArrowControl)]
    [InlineData(FocusedControl.ListViewBase, KeyFocus.ArrowControl)]
    [InlineData(FocusedControl.ListBox, KeyFocus.ArrowControl)]
    [InlineData(FocusedControl.Selector, KeyFocus.ArrowControl)]
    [InlineData(FocusedControl.RadioButton, KeyFocus.ArrowControl)]
    [InlineData(FocusedControl.RadioButtons, KeyFocus.ArrowControl)]
    [InlineData(FocusedControl.ToggleSwitch, KeyFocus.ArrowControl)]
    [InlineData(FocusedControl.ScrollViewer, KeyFocus.ArrowControl)]
    [InlineData(FocusedControl.MenuItem, KeyFocus.ArrowControl)]
    public void EachFocusedControlKeepsTheKeysItUses(FocusedControl control, KeyFocus expected)
    {
        Assert.Equal(expected, TransportKeys.Classify(control));
    }

    [Fact]
    public void EveryKindOfFocusedControlIsClassified()
    {
        // A new kind added without a rule would fall to Free and lose its arrows to the transport.
        var free = Enum.GetValues<FocusedControl>().Where(control => TransportKeys.Classify(control) == KeyFocus.Free);
        Assert.Equal([FocusedControl.Other, FocusedControl.None], free);
    }

    [Theory]
    // A 16:9 video in a wider stage: full height, centred.
    [InlineData(1000, 400, 1920, 1080, 144, 0, 711, 400)]
    // In a taller stage: full width, centred.
    [InlineData(800, 900, 1920, 1080, 0, 225, 800, 450)]
    // A portrait video.
    [InlineData(900, 600, 1080, 1920, 281, 0, 338, 600)]
    // Odd sizes land on whole DIPs.
    [InlineData(1001, 333, 1280, 720, 204, 0, 592, 333)]
    public void TheGateIsAsLargeAsFitsAtTheVideosShapeCentredOnWholeDips(double width, double height, int videoWidth, int videoHeight, double x, double y, double gateWidth, double gateHeight)
    {
        var gate = PreviewLayout.Gate(width, height, new PixelSize(videoWidth, videoHeight));

        Assert.Equal(new StageRect(x, y, gateWidth, gateHeight), gate);
        Assert.False(gate.IsEmpty);
    }

    [Theory]
    [InlineData(0, 400, 1920, 1080)]
    [InlineData(600, -1, 1920, 1080)]
    [InlineData(double.NaN, 400, 1920, 1080)]
    public void WithoutRoomThereIsNoGate(double width, double height, int videoWidth, int videoHeight)
    {
        Assert.True(PreviewLayout.Gate(width, height, new PixelSize(videoWidth, videoHeight)).IsEmpty);
    }

    [Fact]
    public void WithoutAVideoSizeThereIsNoGate()
    {
        Assert.True(PreviewLayout.Gate(600, 400, default).IsEmpty);
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

        Assert.Equal("00:10.40 / 00:24.73", ProjectText.TimecodeCaption(FrameTiming.FrameStart(312, rate), FrameTiming.Duration(742, rate)));
        Assert.Equal("Time 00:10.40 of 00:24.73", ProjectText.PreviewTimecodeName(FrameTiming.FrameStart(312, rate), FrameTiming.Duration(742, rate)));
    }

    [Fact]
    public void ThePreviewsProblemsReadAsSentences()
    {
        Assert.StartsWith("1 photo can't be read, so the preview shows a placeholder for it.", ProjectText.PreviewFrameUnreadable(1), StringComparison.Ordinal);
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
