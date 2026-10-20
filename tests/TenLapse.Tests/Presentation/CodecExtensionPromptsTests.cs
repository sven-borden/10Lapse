using System.Runtime.InteropServices;
using TenLapse.Core.Projects;
using TenLapse.Media.Decoding;
using TenLapse.Presentation;
using TenLapse.Presentation.Services;

namespace TenLapse.Tests.Presentation;

/// <summary>#14: the InfoBars that send the user to the Microsoft Store for the HEIF, HEVC and Raw extensions.</summary>
public sealed class CodecExtensionPromptsTests
{
    private static readonly PhotoCodecExtension _heif = KnownPhotoFormats.HeifExtension;
    private static readonly PhotoCodecExtension _hevc = KnownPhotoFormats.HevcExtension;
    private static readonly PhotoCodecExtension _raw = KnownPhotoFormats.RawExtension;

    private readonly FakeCodecExtensions _codecs = new();
    private readonly FakeInfoBarService _infoBars = new();
    private readonly FakeErrorSink _errors = new();

    private CodecExtensionPrompts Prompts() => new(_codecs, _infoBars, _errors);

    private static SkippedFile Heic(int number, params PhotoCodecExtension[] needed) =>
        new($@"C:\Photos\IMG_{number:0000}.HEIC", SkipReason.NeedsExtension, "Open: missing.", needed);

    [Fact]
    public void ThreeHundredHeicPhotosAreOneInfoBarWithTheCountAndOneButton()
    {
        var skipped = Enumerable.Range(1, 300).Select(i => Heic(i, _heif)).ToList();

        var requests = Prompts().Create(skipped);

        var request = Assert.Single(requests);
        Assert.Equal("These 300 photos need HEIF Image Extension (free) from Microsoft.", request.Message);
        Assert.Equal(InfoBarSeverity.Caution, request.Severity);
        Assert.Equal("Get it from the Store", request.ActionLabel);
        Assert.NotNull(request.Action);

        // Shown through the service: one bar, not 300.
        Prompts().Show(Prompts().Plan(skipped));
        Assert.Single(_infoBars.Shown);
    }

    [Fact]
    public void TheCountIsGroupedAndASinglePhotoReadsInTheSingular()
    {
        var thousands = Prompts().Create(Enumerable.Range(1, 1204).Select(i => Heic(i, _heif)));
        var one = Prompts().Create([Heic(1, _heif)]);

        Assert.Equal("These 1,204 photos need HEIF Image Extension (free) from Microsoft.", Assert.Single(thousands).Message);
        Assert.Equal("This photo needs HEIF Image Extension (free) from Microsoft.", Assert.Single(one).Message);
    }

    [Fact]
    public void RawPhotosNeedTheRawImageExtension()
    {
        var skipped = Enumerable.Range(1, 14).Select(i => new SkippedFile($@"D:\DCIM\DSC_{i:0000}.NEF", SkipReason.NeedsExtension, "", [_raw]));

        var request = Assert.Single(Prompts().Create(skipped));

        Assert.Equal("These 14 photos need Raw Image Extension (free) from Microsoft.", request.Message);
        Assert.Equal("Get it from the Store", request.ActionLabel);
    }

    [Fact]
    public void AHeicPhotoWhoseDecoderIsThereButWhoseCodecIsNotNeedsOnlyTheHevcExtension()
    {
        _codecs.Missing.Add(_hevc);
        var request = Assert.Single(Prompts().Create(Enumerable.Range(1, 12).Select(i => Heic(i, _hevc))));

        Assert.Equal("These 12 photos need HEVC Video Extensions (paid) from Microsoft.", request.Message);
        Assert.DoesNotContain("free", request.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileClassifiedAsMissingHevcOnAPcThatHasHevcIsNotPromptedAndStaysInTheGenericList()
    {
        // The classifier's "component missing" codes are broad (a damaged or 10-bit HEIC can raise them): when the PC
        // has the codec, the file is unreadable for another reason, and a paid upsell would be false.
        var file = Heic(1, _hevc);

        var plan = Prompts().Plan([file]);

        Assert.Empty(plan.Requests);
        Assert.Same(file, Assert.Single(plan.NotCovered));
        Assert.Equal([_hevc], _codecs.Asked);
    }

    [Fact]
    public void ThePlanSplitsTheFilesByTheSameDecisionTheInfoBarsAreMadeFrom()
    {
        var needsHevc = Heic(1, _hevc);
        var hasHevc = Heic(2, _hevc);
        var heifMissing = Heic(3, _heif);
        var damaged = new SkippedFile(@"C:\Photosad.jpg", SkipReason.DecodeFailed, "Damaged.");
        _codecs.Missing.Add(_hevc);

        var plan = Prompts().Plan([needsHevc, heifMissing, damaged]);

        Assert.Equal(2, plan.Requests.Count);
        Assert.Equal([damaged], plan.NotCovered);

        // The HEVC codec appears on the PC: the same file is now not covered.
        _codecs.Missing.Clear();
        var later = Prompts().Plan([hasHevc, heifMissing]);
        Assert.Equal([hasHevc], later.NotCovered);
        Assert.Equal("This photo needs HEIF Image Extension (free) from Microsoft.", Assert.Single(later.Requests).Message);
    }

    [Fact]
    public void WhenTheDecoderIsMissingAndThePcHasHevcOnlyTheDecoderIsAsked()
    {
        // The HEIF decoder is missing; HEVC is on this PC (not in Missing), so the second extension is not needed.
        var request = Assert.Single(Prompts().Create(Enumerable.Range(1, 12).Select(i => Heic(i, _heif, _hevc))));

        Assert.Equal("These 12 photos need HEIF Image Extension (free) from Microsoft.", request.Message);
        Assert.Equal([_hevc], _codecs.Asked.Distinct());
    }

    [Fact]
    public void WhenBothAreMissingOneInfoBarNamesBoth()
    {
        _codecs.Missing.Add(_hevc);

        var request = Assert.Single(Prompts().Create(Enumerable.Range(1, 12).Select(i => Heic(i, _heif, _hevc))));

        Assert.Equal(
            "These 12 photos need 2 extensions from Microsoft. Start with HEIF Image Extension (free).",
            request.Message);
        Assert.Equal("Get it from the Store", request.ActionLabel);
    }

    [Fact]
    public void ThePcIsAskedOncePerExtensionNotOncePerPhoto()
    {
        _codecs.Missing.Add(_hevc);

        Prompts().Create(Enumerable.Range(1, 300).Select(i => Heic(i, _heif, _hevc)));

        Assert.Single(_codecs.Asked);
    }

    [Fact]
    public void HeicAndRawTogetherAreTwoInfoBarsInTheOrderTheyAppear()
    {
        SkippedFile[] skipped = [Heic(1, _heif), new(@"C:\Photos\DSC_1.CR2", SkipReason.NeedsExtension, "", [_raw]), Heic(2, _heif), new(@"C:\Photos\DSC_2.CR2", SkipReason.NeedsExtension, "", [_raw])];

        var requests = Prompts().Create(skipped);

        Assert.Equal(
            ["These 2 photos need HEIF Image Extension (free) from Microsoft.", "These 2 photos need Raw Image Extension (free) from Microsoft."],
            requests.Select(request => request.Message));
    }

    [Fact]
    public void BrokenFilesAndFilesWithAnUnknownCauseAreNotCoveredByThePrompts()
    {
        SkippedFile[] skipped =
        [
            new(@"C:\Photos\text.heic", SkipReason.UnsupportedFormat, "Not a HEIF file."),
            new(@"C:\Photos\cut.heic", SkipReason.DecodeFailed, "Ends early."),
            new(@"C:\Photos\empty.heic", SkipReason.EmptyFile, ""),
            new(@"C:\Photos\unknown.heic", SkipReason.NeedsExtension, "Cause not known."),
        ];

        Assert.Empty(Prompts().Create(skipped));
        Assert.All(skipped.Take(3), file => Assert.False(CodecExtensionPrompts.Covers(file)));
        Assert.False(CodecExtensionPrompts.Covers(skipped[3]));
    }

    [Fact]
    public async Task TheButtonOpensTheStorePageOfTheExtensionAndLeavesNoErrorBehind()
    {
        var request = Assert.Single(Prompts().Create([Heic(1, _heif)]));

        await request.Action!();

        Assert.Equal([_heif], _codecs.Opened);
        Assert.Empty(_infoBars.Shown);
        Assert.Empty(_errors.Reported);
        Assert.Empty(_errors.Shown);
    }

    [Fact]
    public async Task PressingTheButtonForTwoExtensionsRaisesTheNextOne()
    {
        _codecs.Missing.Add(_hevc);
        var first = Assert.Single(Prompts().Create([Heic(1, _heif, _hevc)]));

        await first.Action!();

        var next = Assert.Single(_infoBars.Shown);
        Assert.Equal(
            "Finally, get HEVC Video Extensions (paid). When the install finishes, restart 10Lapse and add the photos again.",
            next.Message);
        Assert.True(next.ShowsNext);
        Assert.Equal("Get it from the Store", next.ActionLabel);

        await next.Action!();

        Assert.Equal([_heif, _hevc], _codecs.Opened);
        Assert.Single(_infoBars.Shown);
    }

    [Fact]
    public async Task AStoreThatCannotBeOpenedSaysWhichExtensionToSearchFor()
    {
        _codecs.OpenResult = _ => false;
        var request = Assert.Single(Prompts().Create([new SkippedFile(@"C:\DCIM\DSC_1.NEF", SkipReason.NeedsExtension, "", [_raw])]));

        await request.Action!();

        var bar = Assert.Single(_infoBars.Shown);
        Assert.Equal(InfoBarSeverity.Caution, bar.Severity);
        Assert.Equal(
            "Couldn't open the Microsoft Store. Search the Store for Raw Image Extension and install the extension, then restart 10Lapse and add the photos again.",
            bar.Message);
    }

    [Fact]
    public async Task AStoreLaunchThatThrowsIsLoggedAndTheUserGetsTheSameFallback()
    {
        var failure = new COMException("No handler for ms-windows-store.", unchecked((int)0x80004005));
        _codecs.OpenResult = _ => throw failure;
        var request = Assert.Single(Prompts().Create([Heic(1, _heif)]));

        await request.Action!();

        var logged = Assert.Single(_errors.Reported);
        Assert.Same(failure, logged.Exception);
        Assert.Equal(ProjectText.CodecStoreOpenFailed, logged.Context);
        var bar = Assert.Single(_infoBars.Shown);
        Assert.Equal(InfoBarSeverity.Caution, bar.Severity);
        Assert.Contains("Search the Store for HEIF Image Extension", bar.Message, StringComparison.Ordinal);
        Assert.Contains("restart 10Lapse", bar.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DismissingTheFirstOfTwoBarsLeavesTheOtherWithItsWorkingButton()
    {
        var queue = new InfoBarQueue(_errors);
        SkippedFile[] skipped = [Heic(1, _heif), new(@"C:\Photos\DSC_1.CR2", SkipReason.NeedsExtension, "", [_raw])];
        var plan = Prompts().Plan(skipped);
        var tickets = plan.Requests.Select(request => queue.Show(request)).ToList();
        Assert.Same(tickets[0], queue.Current);

        queue.Dismiss(tickets[0]);

        Assert.Same(tickets[1], queue.Current);
        await queue.RunActionAsync(tickets[1]);
        Assert.Equal([_raw], _codecs.Opened);
        Assert.Null(queue.Current);
    }

    [Fact]
    public void ASecondImportWithADifferentCountAddsItsOwnBarAndAnIdenticalOneFolds()
    {
        var queue = new InfoBarQueue(_errors);
        var three = queue.Show(Assert.Single(Prompts().Create(Enumerable.Range(1, 3).Select(i => Heic(i, _heif)))));

        // The same photos again: the same words, so no second bar.
        Assert.Same(three, queue.Show(Assert.Single(Prompts().Create(Enumerable.Range(1, 3).Select(i => Heic(i, _heif))))));
        Assert.Equal(0, queue.WaitingCount);

        // Other photos: the count differs, so it is its own bar behind the first, which keeps its count.
        var five = queue.Show(Assert.Single(Prompts().Create(Enumerable.Range(1, 5).Select(i => Heic(i, _heif)))));
        Assert.NotSame(three, five);
        Assert.Equal(1, queue.WaitingCount);
        Assert.Equal("These 3 photos need HEIF Image Extension (free) from Microsoft.", queue.Current!.Request.Message);
        queue.Dismiss(three);
        Assert.Equal("These 5 photos need HEIF Image Extension (free) from Microsoft.", queue.Current!.Request.Message);
    }

    [Fact]
    public void DismissingTheTwoExtensionBarWithoutPressingItsButtonRaisesNoFollowUp()
    {
        _codecs.Missing.Add(_hevc);
        var queue = new InfoBarQueue(_errors);
        var ticket = queue.Show(Assert.Single(Prompts().Create([Heic(1, _heif, _hevc)])));

        queue.Dismiss(ticket);

        Assert.Null(queue.Current);
        Assert.Empty(_codecs.Opened);
    }

    [Fact]
    public async Task TheFollowUpOfTheChainedButtonShowsAtOnceAheadOfTheBarsAlreadyWaiting()
    {
        _codecs.Missing.Add(_hevc);
        var queue = new InfoBarQueue(_errors);
        var first = queue.Show(Assert.Single(new CodecExtensionPrompts(_codecs, queue, _errors).Create([Heic(1, _heif, _hevc)])));
        queue.Show(InfoBarSeverity.Neutral, "Export cancelled.");

        await queue.RunActionAsync(first);

        Assert.StartsWith("Finally, get HEVC Video Extensions", queue.Current!.Request.Message, StringComparison.Ordinal);
        Assert.Equal(1, queue.WaitingCount);
        await queue.RunActionAsync(queue.Current);
        Assert.Equal([_heif, _hevc], _codecs.Opened);
        Assert.Equal("Export cancelled.", queue.Current!.Request.Message);
    }

    [Fact]
    public void TheStoreProductIdsAreTheVerifiedOnes()
    {
        // Checked on apps.microsoft.com on 2026-10-19 (see the PR): the title of each page matches.
        Assert.Equal("9PMMSR1CGPWG", KnownPhotoFormats.HeifExtension.StoreProductId);
        Assert.Equal("9NMZLZ57R3T7", KnownPhotoFormats.HevcExtension.StoreProductId);
        Assert.Equal("9NCTDW2W1BH8", KnownPhotoFormats.RawExtension.StoreProductId);
        Assert.True(KnownPhotoFormats.HevcExtension.HasFee);
        Assert.False(KnownPhotoFormats.HeifExtension.HasFee);
        Assert.False(KnownPhotoFormats.RawExtension.HasFee);
    }

    [Fact]
    public void TheInfoBarHasNoDetailList()
    {
        // No detail list: an InfoBar has one button, and the button is the point.
        var request = Assert.Single(Prompts().Create([Heic(1, _heif)]));
        Assert.False(request.HasDetails);
    }
}
