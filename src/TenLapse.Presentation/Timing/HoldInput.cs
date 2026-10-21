using System.Globalization;
using System.Text;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;

namespace TenLapse.Presentation;

/// <summary>What a typed hold came to (#49).</summary>
/// <param name="IsValid">False when the text is not a number of seconds; nothing changes then.</param>
/// <param name="Value">The hold to store, in range and whole tenths. Meaningless when not valid.</param>
/// <param name="Message">Empty when the text was used as typed; otherwise the sentence that says what became of it.</param>
public readonly record struct HoldEntry(bool IsValid, TimeSpan Value, string Message);

/// <summary>
/// Reads what the user typed into a hold box of the Timing panel (#49): seconds from 0 to
/// <see cref="SequenceSettings.MaxHold"/>, kept to a tenth. As in <see cref="TimingInput"/>, a dot or a single comma is the
/// decimal point whatever the culture, "1,000" is refused rather than guessed, the unit may follow (the user's
/// language's and the English "s"), and nothing is changed without a sentence: a value past the range is held to it and
/// more decimals are rounded, each said under the boxes. An empty box means no hold.
/// </summary>
public static class HoldInput
{
    // The English unit, accepted whatever the language: not shown text, so it stays in code.
    private const string InvariantSecond = "s";

    /// <summary>The hold as its box shows it: "0", "1.5" ("1,5" in German).</summary>
    public static string Number(TimeSpan hold, CultureInfo culture) =>
        hold <= TimeSpan.Zero ? 0.ToString(culture) : TimingFormat.Decimal(Seconds(hold), culture);

    /// <summary>The hold with its unit, for the sentences: "0 s", "1.5 s".</summary>
    public static string Describe(TimeSpan hold, CultureInfo culture) =>
        hold <= TimeSpan.Zero ? TimingFormat.WholeSeconds(0, culture) : TimingFormat.Seconds(Seconds(hold), culture);

    /// <summary>Reads <paramref name="text"/> as a hold. Never throws.</summary>
    /// <param name="text">What was typed.</param>
    /// <param name="culture">The culture the sentences and the values in them are written in.</param>
    public static HoldEntry Parse(string? text, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var typed = (text ?? string.Empty).Normalize(NormalizationForm.FormKC).Trim();
        var body = StripUnit(typed);
        if (body.Length == 0)
        {
            return new HoldEntry(true, TimeSpan.Zero, string.Empty);
        }

        if (!IsPlainNumber(body))
        {
            return new HoldEntry(false, default, NotANumber(culture));
        }

        if (LooksLikeThousands(body))
        {
            return new HoldEntry(false, default, TimingText.Format(culture, TimingText.ThousandsSeparator, TimingFormat.Decimal(new Fraction(3, 2), culture)));
        }

        // Digits past what a decimal holds are far past the range: held to the longest, as any long hold.
        if (!decimal.TryParse(body.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds)
            || seconds > (decimal)SequenceSettings.MaxHold.TotalSeconds)
        {
            return Clamped(typed, SequenceSettings.MaxHold, culture);
        }

        var tenths = decimal.Round(seconds, 1, MidpointRounding.AwayFromZero);
        var hold = TimeSpan.FromTicks((long)(tenths * FrameTiming.TicksPerSecond));
        return tenths == seconds
            ? new HoldEntry(true, hold, string.Empty)
            : new HoldEntry(true, hold, TimingText.Format(culture, TimingText.Rounded, Describe(hold, culture)));
    }

    private static Fraction Seconds(TimeSpan hold) => new(hold.Ticks, FrameTiming.TicksPerSecond);

    private static HoldEntry Clamped(string typed, TimeSpan bound, CultureInfo culture) =>
        new(true, bound, TimingText.Format(culture, TimingText.ClampedHold, typed, Describe(bound, culture), Describe(TimeSpan.Zero, culture), Describe(SequenceSettings.MaxHold, culture)));

    private static string NotANumber(CultureInfo culture) =>
        TimingText.Format(culture, TimingText.NotANumberHold, Describe(TimeSpan.Zero, culture), Describe(SequenceSettings.MaxHold, culture), TimingFormat.Decimal(new Fraction(3, 2), culture));

    private static string StripUnit(string text)
    {
        foreach (var unit in new[] { TimingWords.Second.Normalize(NormalizationForm.FormKC), InvariantSecond })
        {
            if (unit.Length > 0 && text.EndsWith(unit, StringComparison.OrdinalIgnoreCase) && text.Length > unit.Length)
            {
                return text[..^unit.Length].TrimEnd();
            }
        }

        return text;
    }

    // Digits with at most one dot or comma, and at least one digit.
    private static bool IsPlainNumber(string body)
    {
        var separators = 0;
        var digits = 0;
        foreach (var c in body)
        {
            if (c is '.' or ',')
            {
                separators++;
            }
            else if (c is >= '0' and <= '9')
            {
                digits++;
            }
            else
            {
                return false;
            }
        }

        return separators <= 1 && digits > 0;
    }

    // One separator followed by exactly three digits after a non-zero whole part ("1,000", "1.000") reads as a thousands
    // separator in many countries and as a decimal point in others: refused, as the speed box does.
    private static bool LooksLikeThousands(string body)
    {
        var at = body.IndexOfAny(['.', ',']);
        return at >= 0 && body.Length - at - 1 == 3 && body[..at].Any(c => c is >= '1' and <= '9');
    }
}
