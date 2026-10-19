using System.Globalization;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;

namespace TenLapse.Presentation;

/// <summary>The three numbers the Timing panel's box can hold: one per speed mode.</summary>
public enum TimingField
{
    /// <summary>Per second: a frame rate from 1 to 120, two decimals at most (29.97 and 59.94 are the NTSC rates).</summary>
    FramesPerSecond,

    /// <summary>Length: a target length in seconds, one decimal at most.</summary>
    LengthSeconds,

    /// <summary>Per photo: seconds for each photo, two decimals at most.</summary>
    SecondsPerPhoto,
}

/// <summary>What a typed value came to.</summary>
/// <param name="IsValid">False when the text is not a number for this box; nothing changes then.</param>
/// <param name="Value">The value to store, always inside the field's range and kept to its decimals. Meaningless when not valid.</param>
/// <param name="Message">Empty when the text was used as typed; otherwise the sentence that says what became of it (not a number, clamped, rounded).</param>
public readonly record struct TimingEntry(bool IsValid, Fraction Value, string Message);

/// <summary>
/// Reads what the user typed into the Timing panel's box (#17). It never throws and never changes a value without
/// saying so: text that is not a number is refused with a sentence, a number outside the range is held to the
/// nearest bound with a sentence, and a number with more decimals than the box keeps (37.4829 fps, the legacy
/// slider's "fractional garbage") is rounded with a sentence. Parsing is <see cref="FrameRates.TryParse"/> and
/// <see cref="Fraction.TryParse"/> from #10: a dot or a single comma is the decimal point whatever the culture,
/// and an entry within 0.01 of an NTSC rate becomes that exact rational (29.97 is 30000/1001), which is not
/// rounded. Only the words of the sentences and the digits of a value written back depend on the culture.
/// </summary>
public static class TimingInput
{
    /// <summary>The lowest value the field accepts.</summary>
    public static Fraction Min(TimingField field) => field switch
    {
        TimingField.FramesPerSecond => TimingSettings.MinFramesPerSecond,
        TimingField.LengthSeconds => TimingSettings.MinLengthSeconds,
        TimingField.SecondsPerPhoto => TimingSettings.MinSecondsPerPhoto,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown timing field."),
    };

    /// <summary>The highest value the field accepts.</summary>
    public static Fraction Max(TimingField field) => field switch
    {
        TimingField.FramesPerSecond => TimingSettings.MaxFramesPerSecond,
        TimingField.LengthSeconds => TimingSettings.MaxLengthSeconds,
        TimingField.SecondsPerPhoto => TimingSettings.MaxSecondsPerPhoto,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown timing field."),
    };

    /// <summary>The number as the box shows it: up to two decimals, trailing zeros dropped, the culture's decimal separator ("29,97" in German).</summary>
    public static string Number(Fraction value, CultureInfo culture) => TimingFormat.Decimal(value, culture);

    /// <summary>A value with its unit, for the sentences: "29.97 fps", "24.7 s", "0.25 s".</summary>
    public static string Describe(TimingField field, Fraction value, CultureInfo culture)
    {
        var unit = field == TimingField.FramesPerSecond ? TimingWords.FramesPerSecond : TimingWords.Second;
        return $"{Number(value, culture)} {unit}";
    }

    /// <summary>
    /// Reads <paramref name="text"/> for <paramref name="field"/>. A trailing unit the panel itself shows ("fps", "s") is
    /// ignored. Length also takes minutes and seconds ("1:30") and hours ("1:02:03").
    /// </summary>
    /// <param name="field">The box.</param>
    /// <param name="text">What was typed.</param>
    /// <param name="culture">The culture the sentences and the value in them are written in.</param>
    public static TimingEntry Parse(TimingField field, string? text, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var typed = (text ?? string.Empty).Trim();
        var (reading, value) = Read(field, StripUnit(field, typed));
        if (reading == Reading.NotANumber)
        {
            return new TimingEntry(false, default, NotANumber(field, culture));
        }

        var rounded = false;
        if (reading == Reading.Value && !IsNtsc(field, value))
        {
            (reading, value, rounded) = KeepDecimals(field, value);
        }

        var min = Min(field);
        var max = Max(field);
        if (reading == Reading.TooSmall || (reading == Reading.Value && value < min))
        {
            return Clamped(field, typed, min, culture);
        }

        if (reading == Reading.TooLarge || value > max)
        {
            return Clamped(field, typed, max, culture);
        }

        return new TimingEntry(true, value, rounded ? TimingText.Format(culture, TimingText.Rounded, Describe(field, value, culture)) : string.Empty);
    }

    private enum Reading
    {
        NotANumber,
        TooSmall,
        Value,
        TooLarge,
    }

    private static TimingEntry Clamped(TimingField field, string typed, Fraction bound, CultureInfo culture)
    {
        var format = field switch
        {
            TimingField.FramesPerSecond => TimingText.ClampedFramesPerSecond,
            TimingField.LengthSeconds => TimingText.ClampedLength,
            _ => TimingText.ClampedSecondsPerPhoto,
        };

        // Frame rates give the bounds as plain numbers ("from 1 to 120 fps"); the others carry their unit.
        object low = field == TimingField.FramesPerSecond ? Number(Min(field), culture) : Bound(field, Min(field), culture);
        object high = field == TimingField.FramesPerSecond ? Number(Max(field), culture) : Bound(field, Max(field), culture);
        return new TimingEntry(true, bound, TimingText.Format(culture, format, typed, Describe(field, bound, culture), low, high));
    }

    // A bound in a sentence: a long length reads "10 h 00 min", not 36000 s.
    private static string Bound(TimingField field, Fraction value, CultureInfo culture) =>
        field == TimingField.LengthSeconds && value >= new Fraction(60, 1)
            ? TimingFormat.Duration((long)value.Numerator * FrameTiming.TicksPerSecond / value.Denominator)
            : Describe(field, value, culture);

    private static string NotANumber(TimingField field, CultureInfo culture) => field switch
    {
        TimingField.FramesPerSecond => TimingText.Format(culture, TimingText.NotANumberFramesPerSecond, Number(Min(field), culture), Number(Max(field), culture)),
        TimingField.LengthSeconds => TimingText.Format(culture, TimingText.NotANumberLength, Bound(field, Min(field), culture), Bound(field, Max(field), culture)),
        _ => TimingText.Format(culture, TimingText.NotANumberSecondsPerPhoto, Bound(field, Min(field), culture), Bound(field, Max(field), culture)),
    };

    // The NTSC rationals (n * 1000/1001) are exact and are not rounded to hundredths.
    private static bool IsNtsc(TimingField field, Fraction value) => field == TimingField.FramesPerSecond && value.Denominator == 1001;

    private static int Decimals(TimingField field) => field == TimingField.LengthSeconds ? 1 : 2;

    // Rounds half up to the field's decimals. A value that is not already that precise is reported as rounded.
    private static (Reading Reading, Fraction Value, bool Rounded) KeepDecimals(TimingField field, Fraction value)
    {
        var step = Decimals(field) == 1 ? 10 : 100;
        if (step % value.Denominator == 0)
        {
            return (Reading.Value, value, false);
        }

        var scaled = (((Int128)value.Numerator * step * 2) + value.Denominator) / (2 * (Int128)value.Denominator);
        return scaled == 0
            ? (Reading.TooSmall, default, false)
            : (Reading.Value, new Fraction((long)scaled, step), true);
    }

    private static string StripUnit(TimingField field, string text)
    {
        string[] units = field == TimingField.FramesPerSecond ? [TimingWords.FramesPerSecond] : [TimingWords.Second];
        foreach (var unit in units)
        {
            if (text.EndsWith(unit, StringComparison.OrdinalIgnoreCase) && text.Length > unit.Length)
            {
                return text[..^unit.Length].TrimEnd();
            }
        }

        return text;
    }

    private static (Reading Reading, Fraction Value) Read(TimingField field, string body)
    {
        if (body.Length == 0)
        {
            return (Reading.NotANumber, default);
        }

        if (field == TimingField.LengthSeconds && body.Contains(':', StringComparison.Ordinal))
        {
            return ReadClock(body);
        }

        if (body.Contains('/', StringComparison.Ordinal))
        {
            return TryParse(field, body, out var ratio) ? (Reading.Value, ratio) : (Reading.NotANumber, default);
        }

        if (!IsPlainNumber(body))
        {
            return (Reading.NotANumber, default);
        }

        if (TryParse(field, body, out var value))
        {
            return (Reading.Value, value);
        }

        // Digits that Fraction cannot hold: zero or less than 0.0001 on one side, billions on the other.
        return decimal.TryParse(body.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) && number < 1
            ? (Reading.TooSmall, default)
            : (Reading.TooLarge, default);
    }

    private static bool TryParse(TimingField field, string body, out Fraction value) =>
        field == TimingField.FramesPerSecond ? FrameRates.TryParse(body, out value) : Fraction.TryParse(body, out value);

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

    // "1:30" is 90 s, "1:02:03" is 3723 s; the parts after the first are below 60.
    private static (Reading Reading, Fraction Value) ReadClock(string body)
    {
        var parts = body.Split(':');
        if (parts.Length is < 2 or > 3 || parts.Any(part => part.Length is 0 or > 6 || !part.All(char.IsAsciiDigit)))
        {
            return (Reading.NotANumber, default);
        }

        long total = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var number = long.Parse(parts[i], CultureInfo.InvariantCulture);
            if (i > 0 && number >= 60)
            {
                return (Reading.NotANumber, default);
            }

            total = (total * 60) + number;
        }

        return total == 0 ? (Reading.TooSmall, default)
            : total > int.MaxValue ? (Reading.TooLarge, default)
            : (Reading.Value, new Fraction(total, 1));
    }
}
