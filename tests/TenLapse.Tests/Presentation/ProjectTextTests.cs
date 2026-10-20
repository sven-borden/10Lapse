using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Presentation;

namespace TenLapse.Tests.Presentation;

public sealed class ProjectTextTests
{
    // The input is frames per second (Per second), seconds (Length) or seconds per photo (Per photo).
    [Theory]
    [InlineData(742, 30, 1, SpeedMode.PerSecond, "742 photos → 24.7 s at 30 fps")]
    [InlineData(720, 24, 1, SpeedMode.PerSecond, "720 photos → 30.0 s at 24 fps")]
    [InlineData(5000, 30000, 1001, SpeedMode.PerSecond, "5,000 photos → 2 min 47 s at 29.97 fps")]
    [InlineData(0, 30, 1, SpeedMode.PerSecond, "0 photos → 0.0 s at 30 fps")]
    [InlineData(1, 30, 1, SpeedMode.PerSecond, "1 photo → 0.0 s at 30 fps")]
    [InlineData(3, 30, 1, SpeedMode.PerSecond, "3 photos → 0.1 s at 30 fps")]
    [InlineData(5000, 30, 1, SpeedMode.PerSecond, "5,000 photos → 2 min 47 s at 30 fps")]
    [InlineData(742, 30, 1, SpeedMode.Length, "742 photos → 30.0 s at 24.73 fps")]
    [InlineData(0, 30, 1, SpeedMode.Length, "0 photos → 0.0 s at 10 s per photo")]
    [InlineData(1, 30, 1, SpeedMode.Length, "1 photo → 10.0 s at 10 s per photo")]
    [InlineData(3, 30, 1, SpeedMode.Length, "3 photos → 30.0 s at 10 s per photo")]
    [InlineData(5000, 30, 1, SpeedMode.Length, "5,000 photos → 41.7 s at 120 fps")]
    [InlineData(40, 1, 2, SpeedMode.PerPhoto, "40 photos → 20.0 s at 0.5 s per photo")]
    [InlineData(0, 1, 2, SpeedMode.PerPhoto, "0 photos → 0.0 s at 0.5 s per photo")]
    [InlineData(1, 1, 2, SpeedMode.PerPhoto, "1 photo → 0.5 s at 0.5 s per photo")]
    [InlineData(3, 1, 2, SpeedMode.PerPhoto, "3 photos → 1.5 s at 0.5 s per photo")]
    [InlineData(5000, 1, 2, SpeedMode.PerPhoto, "5,000 photos → 41 min 40 s at 0.5 s per photo")]
    [InlineData(10, 2, 1, SpeedMode.PerPhoto, "10 photos → 20.0 s at 2 s per photo")]
    public void TheTimingReadoutReadsAsTheProductSaysIt(int count, int numerator, int denominator, SpeedMode mode, string expected)
    {
        var input = new Fraction(numerator, denominator);
        var timing = mode switch
        {
            SpeedMode.PerSecond => TimingSettings.PerSecond(input),
            SpeedMode.Length => TimingSettings.Length(input),
            _ => TimingSettings.PerPhoto(input),
        };

        var (headline, detail) = ProjectText.TimingReadoutParts(count, timing);
        Assert.Equal(expected, ProjectText.Sentences(headline, detail));
    }

    [Theory]
    [InlineData(0, "This project's settings will be cleared.")]
    [InlineData(1, "This project's photo and settings will be cleared. The photo file stays on your PC.")]
    [InlineData(5000, "This project's 5,000 photos and settings will be cleared. The photo files stay on your PC.")]
    public void TheNewProjectQuestionSaysWhatWillBeCleared(int count, string expected)
    {
        var confirmation = ProjectText.NewProjectConfirmation(count);

        Assert.Equal("Start a new project?", confirmation.Title);
        Assert.Equal(expected, confirmation.Message);
    }

    [Fact]
    public void EverySentenceIsOneFormatStringWithItsOwnPlaceholders()
    {
        // Whole sentences; a counted one is a plural family whose forms keep the count's placeholder (#114).
        Assert.Equal("{0} photo → {1}", ProjectText.TimingHeadline.For(1));
        Assert.Equal("{0} photos → {1}", ProjectText.TimingHeadline.For(742));
        Assert.Equal("{0} – {1}", ProjectText.WindowTitleSaved);
        Assert.Equal("● {0} – {1}", ProjectText.WindowTitleUnsaved);
        Assert.Equal("This project's {0} photos and settings will be cleared. The photo files stay on your PC.", ProjectText.NewProjectMessage.For(2));
        Assert.DoesNotContain("{", ProjectText.NewProjectMessage.For(1));
        Assert.DoesNotContain("{", ProjectText.NewProjectMessageNoPhotos);

        var confirmation = ProjectText.NewProjectConfirmation(2);
        Assert.Equal(ProjectText.NewProjectTitle, confirmation.Title);
        Assert.Equal(ProjectText.NewProjectConfirm, confirmation.ConfirmText);
        Assert.Equal(ProjectText.Cancel, confirmation.CancelText);
    }

    [Theory]
    [InlineData("Timelapse", false, false, "10Lapse")]
    [InlineData("Harbor sunset", true, false, "Harbor sunset – 10Lapse")]
    [InlineData("Harbor sunset", true, true, "● Harbor sunset – 10Lapse")]
    [InlineData("Timelapse", false, true, "● Timelapse – 10Lapse")]
    public void TheWindowTitleNamesTheProjectOnceThereIsOne(string name, bool hasFrames, bool unsaved, string expected)
    {
        Assert.Equal(expected, ProjectText.WindowTitle(name, hasFrames, unsaved));
    }

    [Theory]
    [InlineData(742, 30, 1, SpeedMode.PerSecond, "742 photos → 24.7 s", "at 30 fps")]
    [InlineData(1, 30, 1, SpeedMode.PerSecond, "1 photo → 0.0 s", "at 30 fps")]
    [InlineData(5000, 30000, 1001, SpeedMode.PerSecond, "5,000 photos → 2 min 47 s", "at 29.97 fps")]
    [InlineData(742, 30, 1, SpeedMode.Length, "742 photos → 30.0 s", "at 24.73 fps")]
    [InlineData(40, 1, 2, SpeedMode.PerPhoto, "40 photos → 20.0 s", "at 0.5 s per photo")]
    public void TheTimingReadoutComesInTwoPartsThatJoinToTheWholeReadout(int count, int numerator, int denominator, SpeedMode mode, string headline, string detail)
    {
        var input = new Fraction(numerator, denominator);
        var timing = mode switch
        {
            SpeedMode.PerSecond => TimingSettings.PerSecond(input),
            SpeedMode.Length => TimingSettings.Length(input),
            _ => TimingSettings.PerPhoto(input),
        };

        var parts = ProjectText.TimingReadoutParts(count, timing);

        Assert.Equal((headline, detail), parts);
        Assert.Equal($"{headline} {detail}", ProjectText.Sentences(parts.Headline, parts.Detail));
    }

    [Fact]
    public void TheReadoutPartsTakeACultureAndUseTheCurrentOneWithout()
    {
        var timing = TimingSettings.PerSecond(new Fraction(30000, 1001));
        var german = System.Globalization.CultureInfo.GetCultureInfo("de-DE");

        Assert.Equal(("1.234 photos → 41,2 s", "at 29,97 fps"), ProjectText.TimingReadoutParts(1234, timing, german));
        Assert.Equal(("1,234 photos → 41.2 s", "at 29.97 fps"), ProjectText.TimingReadoutParts(1234, timing));
        Assert.Equal(("1 photo → 0,5 s", "at 0,5 s per photo"), ProjectText.TimingReadoutParts(1, TimingSettings.PerPhoto(new Fraction(1, 2)), german));

        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = german;
            Assert.Equal(("1.234 photos → 41,2 s", "at 29,97 fps"), ProjectText.TimingReadoutParts(1234, timing));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void CountsSizesAndTimesFollowTheRegionalFormat()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("Adding 1.240 of 5.000 photos…", ProjectText.ImportReadingProgress(1240, 5000));
            Assert.Equal("1.240 photos are only in OneDrive. Download them now? About 2,4 GB.", ProjectText.CloudDownload(1240, 2_600_000_000).Message);
            Assert.Equal("IMG_0312.JPG · 19:26:05", ProjectText.FilmstripFileCaption("IMG_0312.JPG", new DateTime(2026, 10, 20, 19, 26, 5)));

            // The zero-padded counters and the time codes stay invariant on purpose.
            Assert.Equal("Frame 1312 of 5000", ProjectText.FrameCaption(1312, 5000));
            Assert.Equal("00:10.40 / 00:24.73", ProjectText.TimecodeCaption(104_000_000, 247_300_000));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }

    [Theory]
    [InlineData(1, 1, "This photo needs the HEIF Image Extensions (free) from Microsoft.")]
    [InlineData(12, 1, "These 12 photos need the HEIF Image Extensions (free) from Microsoft.")]
    [InlineData(1, 2, "This photo needs 2 extensions from Microsoft. Start with the HEIF Image Extensions (free).")]
    [InlineData(8, 2, "These 8 photos need 2 extensions from Microsoft. Start with the HEIF Image Extensions (free).")]
    public void TheExtensionSentencesAreWholeAndCountTheExtensions(int photos, int extensions, string expected)
    {
        PhotoCodecExtension[] needed =
        [
            new("HEIF Image Extensions", "9PMMSR1CGPWG"),
            new("HEVC Video Extensions", "9NMZLZ57R3T7", HasFee: true, IsCodec: true),
        ];

        Assert.Equal(expected, ProjectText.CodecNeeded(photos, needed[..extensions]));
        Assert.Equal("Last, get the HEVC Video Extensions (small fee). Once it's installed, restart 10Lapse and add the photos again.", ProjectText.CodecNext(needed[1], isLast: true));
        Assert.Equal("Next, get the HEVC Video Extensions (small fee).", ProjectText.CodecNext(needed[1], isLast: false));
    }

    [Fact]
    public void SentencesQuotingALabelTakeItFromItsOwnString()
    {
        Assert.Contains(ProjectText.IncludeSubfolders, ProjectText.ImportNothingFoundSubfolders, StringComparison.Ordinal);
        Assert.Contains(ProjectText.AddPhotos, ProjectText.ImportNothingFoundSubfolders, StringComparison.Ordinal);
        Assert.Equal("Removed 3 frames from the project. The photos stay on your PC. Ctrl+Z puts them back.", ProjectText.FilmstripRemoved(3));
        Assert.Equal("Removed 1 frame from the project. The photo stays on your PC. Ctrl+Z puts it back.", ProjectText.FilmstripRemoved(1));
        Assert.Equal("Wait. Then go.", ProjectText.Sentences("Wait.", null, string.Empty, "Then go."));
        Assert.Equal(string.Empty, ProjectText.Sentences());
    }
}
