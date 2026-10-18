using System.Globalization;

namespace TenLapse.Core.Timing;

/// <summary>
/// An immutable positive rational number, used for frame rates (30, 30000/1001, 1/2 for two
/// seconds per photo) and for the seconds values the speed modes convert between.
/// Always reduced by the greatest common divisor, so equal values have equal parts.
/// </summary>
/// <remarks>
/// The parts are <see cref="int"/>. Arithmetic runs in 128-bit integers and throws
/// <see cref="OverflowException"/> when a reduced result no longer fits, never wraps.
/// <c>default(Fraction)</c> is 0/1, the only non-positive value; the timing functions reject it.
/// </remarks>
public readonly struct Fraction : IEquatable<Fraction>, IComparable<Fraction>
{
    private readonly int _numerator;
    private readonly int _denominator;

    /// <summary>Creates <c>numerator / denominator</c>, reduced. Both must be positive.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A part is zero or negative.</exception>
    /// <exception cref="OverflowException">The reduced parts do not fit in an <see cref="int"/>.</exception>
    public Fraction(long numerator, long denominator)
        : this(Reduce(numerator, denominator))
    {
    }

    private Fraction((int Numerator, int Denominator) parts)
    {
        _numerator = parts.Numerator;
        _denominator = parts.Denominator;
    }

    /// <summary>The reduced numerator.</summary>
    public int Numerator => _numerator;

    /// <summary>The reduced denominator, at least 1.</summary>
    public int Denominator => _denominator == 0 ? 1 : _denominator;

    /// <summary>The value as a double, for display and sanity checks. Never use it to time a frame.</summary>
    public double ToDouble() => (double)Numerator / Denominator;

    /// <summary>The value <c>Denominator / Numerator</c>: 30 fps becomes 1/30 s per photo and back.</summary>
    /// <exception cref="DivideByZeroException">The value is zero (<c>default</c>).</exception>
    public Fraction Reciprocal()
    {
        if (Numerator == 0)
        {
            throw new DivideByZeroException("The reciprocal of zero is undefined.");
        }

        return new Fraction(Reduce(Denominator, Numerator));
    }

    /// <summary>
    /// The decimal as a fraction: exact when its reduced denominator is at most
    /// <see cref="FrameRates.MaxDenominator"/>, otherwise the closest fraction with such a
    /// denominator. The value is rounded to 6 decimals first. 12.5 is 25/2; 29.97 is 2997/100
    /// (use <see cref="FrameRates.TryParse"/> to get the NTSC rate instead).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive after rounding, or below <c>1 / MaxDenominator</c> (0.0001), which no fraction with such a denominator can represent.</exception>
    /// <exception cref="OverflowException">The value is larger than an <see cref="int"/> can hold.</exception>
    public static Fraction FromDecimal(decimal value)
    {
        var rounded = decimal.Round(value, 6, MidpointRounding.AwayFromZero);
        if (rounded <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A fraction must be positive.");
        }

        if (rounded < 1m / FrameRates.MaxDenominator)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "The value is too small to represent.");
        }

        if (rounded > int.MaxValue)
        {
            throw new OverflowException("The value is too large.");
        }

        var scaled = (long)(rounded * 1_000_000m);
        return Nearest(scaled, 1_000_000, FrameRates.MaxDenominator);
    }

    /// <summary>
    /// Parses what a user types: a decimal ("29.97", "12,5": dot or comma, digits only, no sign
    /// or exponent) through <see cref="FromDecimal"/>, or a ratio of two whole numbers
    /// ("30000/1001"). Never throws: returns false for anything else, for zero, for negative values
    /// and for values that cannot be represented (below 0.0001, or too large for 32-bit parts).
    /// The text is read with the invariant culture.
    /// </summary>
    public static bool TryParse(string? text, out Fraction result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        try
        {
            var slash = text.IndexOf('/');
            if (slash >= 0)
            {
                if (!long.TryParse(text.AsSpan(0, slash).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var numerator)
                    || !long.TryParse(text.AsSpan(slash + 1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var denominator)
                    || numerator <= 0
                    || denominator <= 0)
                {
                    return false;
                }

                result = new Fraction(numerator, denominator);
                return true;
            }

            if (!decimal.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
                || decimal.Round(value, 6, MidpointRounding.AwayFromZero) < 1m / FrameRates.MaxDenominator)
            {
                return false;
            }

            result = FromDecimal(value);
            return true;
        }
        catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>
    /// The fraction <c>numerator / denominator</c> if its reduced denominator is at most
    /// <paramref name="maxDenominator"/>, otherwise the closest fraction that satisfies the limit
    /// (best rational approximation by continued fractions, ties go to the smaller denominator).
    /// Never returns zero and never throws for a positive input: a value below
    /// <c>1 / maxDenominator</c> gives <c>1 / maxDenominator</c>, and a result whose parts do not
    /// fit in an <see cref="int"/> (only for values above about 2 billion) gives
    /// <see cref="int.MaxValue"/>. Both are far outside any usable frame rate.
    /// </summary>
    internal static Fraction Nearest(Int128 numerator, Int128 denominator, int maxDenominator)
    {
        if (numerator <= 0 || denominator <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numerator), "A fraction must be positive.");
        }

        var gcd = Gcd(numerator, denominator);
        var n0 = numerator / gcd;
        var d0 = denominator / gcd;
        if (d0 <= maxDenominator)
        {
            return Clamped(n0, d0, maxDenominator);
        }

        // The last convergent below the limit and the best semiconvergent after it; the closer one wins.
        Int128 p0 = 0, q0 = 1, p1 = 1, q1 = 0;
        var n = n0;
        var d = d0;
        while (true)
        {
            var a = n / d;
            var q2 = q0 + (a * q1);
            if (q2 > maxDenominator)
            {
                break;
            }

            (p0, q0, p1, q1) = (p1, q1, p0 + (a * p1), q2);
            (n, d) = (d, n - (a * d));
        }

        var k = (maxDenominator - q0) / q1;
        var boundP = p0 + (k * p1);
        var boundQ = q0 + (k * q1);
        var errorConvergent = Int128.Abs((p1 * d0) - (n0 * q1)) * boundQ;
        var errorSemi = Int128.Abs((boundP * d0) - (n0 * boundQ)) * q1;
        return errorConvergent <= errorSemi
            ? Clamped(p1, q1, maxDenominator)
            : Clamped(boundP, boundQ, maxDenominator);
    }

    private static Fraction Clamped(Int128 numerator, Int128 denominator, int maxDenominator)
    {
        if (numerator <= 0)
        {
            return new Fraction(Narrow(1, maxDenominator));
        }

        return numerator > int.MaxValue || denominator > int.MaxValue
            ? new Fraction(Narrow(int.MaxValue, 1))
            : new Fraction(Narrow(numerator, denominator));
    }

    /// <summary>Exact product. Throws <see cref="OverflowException"/> when the reduced result does not fit.</summary>
    public static Fraction operator *(Fraction left, Fraction right) =>
        new(Reduce((Int128)left.Numerator * right.Numerator, (Int128)left.Denominator * right.Denominator));

    /// <summary>Exact quotient. Throws <see cref="OverflowException"/> when the reduced result does not fit.</summary>
    public static Fraction operator /(Fraction left, Fraction right)
    {
        if (right.Numerator == 0)
        {
            throw new DivideByZeroException("Division by zero.");
        }

        return new Fraction(Reduce((Int128)left.Numerator * right.Denominator, (Int128)left.Denominator * right.Numerator));
    }

    /// <summary>Equal values.</summary>
    public static bool operator ==(Fraction left, Fraction right) => left.Equals(right);

    /// <summary>Different values.</summary>
    public static bool operator !=(Fraction left, Fraction right) => !left.Equals(right);

    /// <summary>Strictly smaller.</summary>
    public static bool operator <(Fraction left, Fraction right) => left.CompareTo(right) < 0;

    /// <summary>Strictly larger.</summary>
    public static bool operator >(Fraction left, Fraction right) => left.CompareTo(right) > 0;

    /// <summary>Smaller or equal.</summary>
    public static bool operator <=(Fraction left, Fraction right) => left.CompareTo(right) <= 0;

    /// <summary>Larger or equal.</summary>
    public static bool operator >=(Fraction left, Fraction right) => left.CompareTo(right) >= 0;

    /// <inheritdoc />
    public bool Equals(Fraction other) => Numerator == other.Numerator && Denominator == other.Denominator;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Fraction other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);

    /// <summary>Exact comparison by cross-multiplication.</summary>
    public int CompareTo(Fraction other) =>
        ((long)Numerator * other.Denominator).CompareTo((long)other.Numerator * Denominator);

    /// <summary>"30" for a whole number, otherwise "30000/1001". Invariant culture.</summary>
    public override string ToString() => Denominator == 1
        ? Numerator.ToString(CultureInfo.InvariantCulture)
        : string.Create(CultureInfo.InvariantCulture, $"{Numerator}/{Denominator}");

    private static (int Numerator, int Denominator) Reduce(Int128 numerator, Int128 denominator)
    {
        if (numerator <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numerator), numerator, "The numerator must be positive.");
        }

        if (denominator <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator), denominator, "The denominator must be positive.");
        }

        var gcd = Gcd(numerator, denominator);
        return Narrow(numerator / gcd, denominator / gcd);
    }

    private static (int Numerator, int Denominator) Narrow(Int128 numerator, Int128 denominator)
    {
        if (numerator <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numerator), numerator, "The value rounds to zero.");
        }

        if (numerator > int.MaxValue || denominator > int.MaxValue)
        {
            throw new OverflowException("The fraction does not fit in 32-bit parts.");
        }

        return ((int)numerator, (int)denominator);
    }

    private static Int128 Gcd(Int128 a, Int128 b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }
}
