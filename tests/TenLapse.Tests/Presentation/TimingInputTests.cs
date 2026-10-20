using System.Globalization;
using TenLapse.Core.Timing;
using TenLapse.Presentation;

namespace TenLapse.Tests.Presentation;

/// <summary>What the Timing panel's box makes of typed text (#17): parsing, NTSC, decimals, bounds and the sentences.</summary>
public sealed class TimingInputTests
{
    private static readonly CultureInfo _en = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo _de = CultureInfo.GetCultureInfo("de-DE");
    private static readonly CultureInfo _fr = CultureInfo.GetCultureInfo("fr-FR");

    private static TimingEntry Fps(string? text, CultureInfo? culture = null) => TimingInput.Parse(TimingField.FramesPerSecond, text, culture ?? _en);

    private static TimingEntry Length(string? text) => TimingInput.Parse(TimingField.LengthSeconds, text, _en);

    private static TimingEntry Photo(string? text) => TimingInput.Parse(TimingField.SecondsPerPhoto, text, _en);

    [Theory]
    [InlineData("24", 24, 1)]
    [InlineData("1", 1, 1)]
    [InlineData("120", 120, 1)]
    [InlineData("12.5", 25, 2)]
    [InlineData("12,5", 25, 2)]
    [InlineData(" 30 fps ", 30, 1)]
    [InlineData("30FPS", 30, 1)]
    [InlineData("25/2", 25, 2)]
    public void AFrameRateIsReadAsTyped(string text, int numerator, int denominator)
    {
        var entry = Fps(text);

        Assert.True(entry.IsValid);
        Assert.Equal(new Fraction(numerator, denominator), entry.Value);
        Assert.Equal(string.Empty, entry.Message);
    }

    [Theory]
    [InlineData("29.97")]
    [InlineData("29,97")]
    [InlineData("29.970")]
    [InlineData("30000/1001")]
    public void ThirtyFpsOnNtscIsTheExactRational(string text)
    {
        var entry = Fps(text);

        Assert.Equal(new Fraction(30000, 1001), entry.Value);
        Assert.Equal(string.Empty, entry.Message);
    }

    [Theory]
    [InlineData("59.94", 60000, 1001)]
    [InlineData("23,98", 24000, 1001)]
    [InlineData("119.88", 120000, 1001)]
    public void OtherNtscRatesAreExactAndNeverRounded(string text, int numerator, int denominator)
    {
        var entry = Fps(text);

        Assert.True(entry.IsValid);
        Assert.Equal(new Fraction(numerator, denominator), entry.Value);
        Assert.Equal(string.Empty, entry.Message);
    }

    [Fact]
    public void ANearMissOfAWholeRateIsNotSnappedToNtsc()
    {
        // 29.99 is 2999/100; only entries within 0.01 of an NTSC rate snap.
        Assert.Equal(new Fraction(2999, 100), Fps("29.99").Value);
        Assert.Equal(new Fraction(30, 1), Fps("30").Value);
    }

    [Theory]
    [InlineData("37.4829", "37.48", 3748, 100)]
    [InlineData("37,4829", "37.48", 3748, 100)]
    [InlineData("12.3456", "12.35", 1235, 100)]
    [InlineData("100/3", "33.33", 3333, 100)]
    public void MoreDecimalsThanTheBoxKeepsAreRoundedAndSaid(string text, string shown, int numerator, int denominator)
    {
        var entry = Fps(text);

        Assert.True(entry.IsValid);
        Assert.Equal(new Fraction(numerator, denominator), entry.Value);
        Assert.Equal($"Rounded to {shown} fps.", entry.Message);
    }

    [Theory]
    [InlineData("0", 1)]
    [InlineData("0.5", 1)]
    [InlineData("0,001", 1)]
    [InlineData("0.00001", 1)]
    [InlineData("121", 120)]
    [InlineData("200", 120)]
    [InlineData("5000000000", 120)]
    [InlineData("99999999999999999999999999999999", 120)]
    public void AFrameRateOutsideOneToOneHundredTwentyIsHeldToTheBoundAndSaid(string text, int bound)
    {
        var entry = Fps(text);

        Assert.True(entry.IsValid);
        Assert.Equal(new Fraction(bound, 1), entry.Value);
        Assert.Equal($"Frame rates run from 1 to 120 fps, so {text} is now {bound} fps.", entry.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("1e3")]
    [InlineData("1.000,5")]
    [InlineData("1,2,3")]
    [InlineData(".")]
    [InlineData("fps")]
    [InlineData("30 frames")]
    [InlineData("1/0")]
    public void TextThatIsNotANumberIsRefusedWithASentence(string? text)
    {
        var entry = Fps(text);

        Assert.False(entry.IsValid);
        Assert.Equal("Type a frame rate from 1 to 120 fps, like 24 or 29.97.", entry.Message);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void ADecimalCommaWorksInGermanAndFrenchAndTheSentencesUseIt(string name)
    {
        var culture = CultureInfo.GetCultureInfo(name);

        Assert.Equal(new Fraction(30000, 1001), Fps("29,97", culture).Value);
        Assert.Equal(new Fraction(25, 2), Fps("12,5", culture).Value);

        var rounded = Fps("37,4829", culture);
        Assert.Equal(new Fraction(3748, 100), rounded.Value);
        Assert.Equal("Rounded to 37,48 fps.", rounded.Message);
    }

    [Theory]
    [InlineData("29.98", "Using the NTSC rate 29.97 fps (30000/1001).")]
    [InlineData("29.975", "Using the NTSC rate 29.97 fps (30000/1001).")]
    [InlineData("29,98", "Using the NTSC rate 29.97 fps (30000/1001).")]
    [InlineData("23.976", "Using the NTSC rate 23.98 fps (24000/1001).")]
    [InlineData("59.95 fps", "Using the NTSC rate 59.94 fps (60000/1001).")]
    public void AnEntryThatSnapsToNtscWithOtherDigitsSaysSo(string text, string message)
    {
        var entry = Fps(text);

        Assert.True(entry.IsValid);
        Assert.Equal(message, entry.Message);
        Assert.Equal(1001, entry.Value.Denominator);
    }

    [Theory]
    [InlineData("29.97")]
    [InlineData("29,97")]
    [InlineData("29.970")]
    [InlineData("23.98")]
    [InlineData("59.94")]
    [InlineData("119.88")]
    [InlineData("30000/1001")]
    [InlineData("30")]
    public void AnEntryThatIsWhatTheBoxShowsStaysSilent(string text)
    {
        var entry = Fps(text);

        Assert.True(entry.IsValid);
        Assert.Equal(string.Empty, entry.Message);
    }

    [Fact]
    public void TheNtscSentenceUsesTheCulturesDecimalSeparator()
    {
        Assert.Equal("Using the NTSC rate 29,97 fps (30000/1001).", Fps("29,98", _de).Message);
        Assert.Equal("Using the NTSC rate 23,98 fps (24000/1001).", Fps("23,976", _fr).Message);
    }

    [Theory]
    [InlineData("1,000")]
    [InlineData("1.000")]
    [InlineData("12,345")]
    [InlineData("12.345")]
    [InlineData("100.000")]
    public void OneSeparatorAndExactlyThreeDigitsIsRefusedNotGuessed(string text)
    {
        foreach (var entry in new[] { Fps(text), Length(text), Photo(text) })
        {
            Assert.False(entry.IsValid);
            Assert.Equal("Type 1000 without a separator, or 1.5 for one and a half.", entry.Message);
        }
    }

    [Fact]
    public void TheThousandsSentenceWritesItsExampleInTheCulture()
    {
        Assert.Equal("Type 1000 without a separator, or 1,5 for one and a half.", Fps("1.000", _de).Message);
        Assert.Equal("Type 1000 without a separator, or 1,5 for one and a half.", TimingInput.Parse(TimingField.LengthSeconds, "1,000", _fr).Message);
    }

    [Theory]
    [InlineData("0.125")]
    [InlineData(".125")]
    [InlineData("0,125")]
    [InlineData("1.25")]
    [InlineData("1.2")]
    [InlineData("1000")]
    [InlineData("10.0000")]
    public void OtherNumbersAreNotMistakenForAThousandsSeparator(string text) =>
        Assert.DoesNotContain("without a separator", Photo(text).Message, StringComparison.Ordinal);

    [Fact]
    public void ThePlainNumbersTheRefusalSuggestsAreTakenAsTheyAre()
    {
        Assert.Equal(new Fraction(1000, 1), Length("1000").Value);
        Assert.Equal(new Fraction(3, 2), Length("1.5").Value);
    }

    [Fact]
    public void TheFramesPerSecondSentenceWritesItsExampleInTheCulture()
    {
        Assert.Equal("Type a frame rate from 1 to 120 fps, like 24 or 29,97.", Fps("abc", _de).Message);
        Assert.Equal("Type a frame rate from 1 to 120 fps, like 24 or 29,97.", Fps("abc", _fr).Message);
        Assert.Equal("Type a frame rate from 1 to 120 fps, like 24 or 29.97.", Fps("abc", _en).Message);
    }

    [Fact]
    public void ALongLengthInASentenceReadsAsAClockLengthNotAsSeconds()
    {
        Assert.Equal("A video can be from 0.1 s to 10 h 00 min long, so 11:00:00 is now 10 h 00 min.", Length("11:00:00").Message);
        Assert.Equal("90 s", TimingInput.Describe(TimingField.LengthSeconds, new Fraction(90, 1), _en));
        Assert.Equal("1 h 00 min", TimingInput.Describe(TimingField.LengthSeconds, new Fraction(3600, 1), _en));
        Assert.Equal("3600.5 s", TimingInput.Describe(TimingField.LengthSeconds, new Fraction(7201, 2), _en));
    }

    [Fact]
    public void TheDotWorksInGermanToo()
    {
        Assert.Equal(new Fraction(30000, 1001), Fps("29.97", _de).Value);
        Assert.Equal(new Fraction(30000, 1001), Fps("29.97", _fr).Value);
    }

    [Theory]
    [InlineData("30", 30, 1)]
    [InlineData("24.7", 247, 10)]
    [InlineData("24,7", 247, 10)]
    [InlineData("0.1", 1, 10)]
    [InlineData("90 s", 90, 1)]
    [InlineData("1:30", 90, 1)]
    [InlineData("0:05", 5, 1)]
    [InlineData("1:02:03", 3723, 1)]
    [InlineData("10:00:00", 36000, 1)]
    public void ALengthIsSecondsOrMinutesAndSeconds(string text, int numerator, int denominator)
    {
        var entry = Length(text);

        Assert.True(entry.IsValid);
        Assert.Equal(new Fraction(numerator, denominator), entry.Value);
        Assert.Equal(string.Empty, entry.Message);
    }

    [Fact]
    public void ALengthKeepsOneDecimal()
    {
        var entry = Length("24.76");

        Assert.Equal(new Fraction(248, 10), entry.Value);
        Assert.Equal("Rounded to 24.8 s.", entry.Message);
    }

    [Theory]
    [InlineData("0", 1, 10, "0.1 s")]
    [InlineData("0.04", 1, 10, "0.1 s")]
    [InlineData("0:00", 1, 10, "0.1 s")]
    [InlineData("36001", 36000, 1, "10 h 00 min")]
    [InlineData("11:00:00", 36000, 1, "10 h 00 min")]
    [InlineData("99:00:00", 36000, 1, "10 h 00 min")]
    public void ALengthOutsideTheRangeIsHeldToTheBoundAndSaid(string text, int numerator, int denominator, string shown)
    {
        var entry = Length(text);

        Assert.True(entry.IsValid);
        Assert.Equal(new Fraction(numerator, denominator), entry.Value);
        Assert.Equal($"A video can be from 0.1 s to 10 h 00 min long, so {text} is now {shown}.", entry.Message);
    }

    [Theory]
    [InlineData("1:60")]
    [InlineData("1:2:3:4")]
    [InlineData("1:")]
    [InlineData(":30")]
    [InlineData("1:30.5")]
    [InlineData("abc")]
    public void ALengthThatIsNotATimeIsRefused(string text)
    {
        var entry = Length(text);

        Assert.False(entry.IsValid);
        Assert.Equal("Type a length in seconds, like 30, or as minutes and seconds, like 1:30. It can be from 0.1 s to 10 h 00 min.", entry.Message);
    }

    [Theory]
    [InlineData("0.25", 1, 4)]
    [InlineData("0,5", 1, 2)]
    [InlineData("2", 2, 1)]
    [InlineData("10", 10, 1)]
    [InlineData("0.1", 1, 10)]
    [InlineData("1.5 s", 3, 2)]
    public void SecondsPerPhotoAreReadAsTyped(string text, int numerator, int denominator)
    {
        var entry = Photo(text);

        Assert.True(entry.IsValid);
        Assert.Equal(new Fraction(numerator, denominator), entry.Value);
        Assert.Equal(string.Empty, entry.Message);
    }

    [Fact]
    public void SecondsPerPhotoKeepTwoDecimals()
    {
        var entry = Photo("0.3333");

        Assert.Equal(new Fraction(33, 100), entry.Value);
        Assert.Equal("Rounded to 0.33 s.", entry.Message);
    }

    [Theory]
    [InlineData("0.05", 1, 10, "0.1 s")]
    [InlineData("0.001", 1, 10, "0.1 s")]
    [InlineData("11", 10, 1, "10 s")]
    [InlineData("3600", 10, 1, "10 s")]
    public void SecondsPerPhotoOutsideTheRangeAreHeldToTheBoundAndSaid(string text, int numerator, int denominator, string shown)
    {
        var entry = Photo(text);

        Assert.True(entry.IsValid);
        Assert.Equal(new Fraction(numerator, denominator), entry.Value);
        Assert.Equal($"A photo can stay from 0.1 s to 10 s, so {text} is now {shown}.", entry.Message);
    }

    [Fact]
    public void ANumberIsWrittenWithTheCulturesDecimalSeparator()
    {
        Assert.Equal("29.97", TimingInput.Number(new Fraction(30000, 1001), _en));
        Assert.Equal("29,97", TimingInput.Number(new Fraction(30000, 1001), _de));
        Assert.Equal("29,97", TimingInput.Number(new Fraction(30000, 1001), _fr));
        Assert.Equal("30", TimingInput.Number(new Fraction(30, 1), _de));
        Assert.Equal("0,25", TimingInput.Number(new Fraction(1, 4), _de));
        Assert.Equal("59,94", TimingInput.Number(new Fraction(60000, 1001), _fr));
    }

    [Fact]
    public void WhatTheBoxWritesBackIsWhatItReads()
    {
        // The round trip of every value the panel can store: written in a culture, read in the same one.
        foreach (var culture in new[] { _en, _de, _fr })
        {
            foreach (var rate in new[] { new Fraction(30, 1), new Fraction(30000, 1001), new Fraction(60000, 1001), new Fraction(25, 2), new Fraction(24000, 1001) })
            {
                Assert.Equal(rate, Fps(TimingInput.Number(rate, culture), culture).Value);
            }

            foreach (var seconds in new[] { new Fraction(1, 10), new Fraction(247, 10), new Fraction(30, 1), new Fraction(36000, 1) })
            {
                Assert.Equal(seconds, TimingInput.Parse(TimingField.LengthSeconds, TimingInput.Number(seconds, culture), culture).Value);
            }

            foreach (var seconds in new[] { new Fraction(1, 10), new Fraction(1, 4), new Fraction(333, 100), new Fraction(10, 1) })
            {
                Assert.Equal(seconds, TimingInput.Parse(TimingField.SecondsPerPhoto, TimingInput.Number(seconds, culture), culture).Value);
            }
        }
    }

    [Theory]
    [InlineData("30 fps", 30, 1)]
    [InlineData("30fps", 30, 1)]
    [InlineData("30 FPS", 30, 1)]
    [InlineData("３０", 30, 1)]
    [InlineData("２９．９７ ｆｐｓ", 30000, 1001)]
    [InlineData("24,5 fps", 49, 2)]
    public void TheEnglishUnitAndFullWidthDigitsAreReadInEveryLanguage(string text, long numerator, long denominator)
    {
        foreach (var culture in new[] { _en, _de, _fr })
        {
            var entry = Fps(text, culture);
            Assert.True(entry.IsValid, $"{text} in {culture.Name}: {entry.Message}");
            Assert.Equal(new Fraction(numerator, denominator), entry.Value);
        }

        Assert.Equal(new Fraction(90, 1), Length("1：30").Value);
        Assert.Equal(new Fraction(2, 1), Length("2 s").Value);
        Assert.Equal(new Fraction(1, 2), Photo("０.5s").Value);
    }

    [Fact]
    public void TheUnitShownAfterTheBoxIsTheUnitTheBoxReads()
    {
        // The label after the box and the unit typed come from the same string, so they cannot disagree (#114).
        Assert.Equal(new Fraction(30, 1), Fps("30 " + TimingWords.FramesPerSecond).Value);
        Assert.Equal(new Fraction(45, 1), Length("45 " + TimingWords.Second).Value);
    }
}
