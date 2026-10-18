using TenLapse.Core.Timing;

namespace TenLapse.Tests.Timing;

public sealed class FractionTests
{
    [Theory]
    [InlineData(60, 2, 30, 1)]
    [InlineData(2000, 4004, 500, 1001)]
    [InlineData(30000, 1001, 30000, 1001)]
    [InlineData(5, 10, 1, 2)]
    public void ConstructorReducesToLowestTerms(long num, long den, int expectedNum, int expectedDen)
    {
        var fraction = new Fraction(num, den);

        Assert.Equal(expectedNum, fraction.Numerator);
        Assert.Equal(expectedDen, fraction.Denominator);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -2)]
    public void ConstructorRejectsNonPositiveParts(long num, long den)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Fraction(num, den));
    }

    [Fact]
    public void ConstructorThrowsInsteadOfWrappingWhenThePartsDoNotFit()
    {
        Assert.Throws<OverflowException>(() => new Fraction(long.MaxValue, 1));
        Assert.Throws<OverflowException>(() => new Fraction(1, long.MaxValue));
    }

    [Fact]
    public void EqualValuesAreEqualWhateverTheyWereBuiltFrom()
    {
        var a = new Fraction(30, 1);
        var b = new Fraction(60, 2);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, new Fraction(30000, 1001));
        Assert.True(a != new Fraction(30000, 1001));
    }

    [Fact]
    public void ComparisonIsExact()
    {
        var ntsc = new Fraction(30000, 1001);

        Assert.True(ntsc < new Fraction(30, 1));
        Assert.True(ntsc > new Fraction(2997, 100));
        Assert.True(new Fraction(1, 2) <= new Fraction(2, 4));
        Assert.True(new Fraction(1, 2) >= new Fraction(2, 4));
        Assert.Equal(0, new Fraction(1, 3).CompareTo(new Fraction(2, 6)));
    }

    [Fact]
    public void ToDoubleAndToString()
    {
        Assert.Equal(29.97003, new Fraction(30000, 1001).ToDouble(), 5);
        Assert.Equal("30", new Fraction(30, 1).ToString());
        Assert.Equal("30000/1001", new Fraction(30000, 1001).ToString());
    }

    [Fact]
    public void ArithmeticIsExactAndReduced()
    {
        var third = new Fraction(1, 3);
        var sixth = new Fraction(1, 6);

        Assert.Equal(new Fraction(1, 18), third * sixth);
        Assert.Equal(new Fraction(2, 1), third / sixth);
        Assert.Equal(new Fraction(1001, 30000), new Fraction(30000, 1001).Reciprocal());
        Assert.Equal(new Fraction(30, 1), new Fraction(30, 1).Reciprocal().Reciprocal());
    }

    [Fact]
    public void ArithmeticThrowsInsteadOfWrapping()
    {
        var big = new Fraction(int.MaxValue, 1);

        Assert.Throws<OverflowException>(() => big * big);
        Assert.Throws<DivideByZeroException>(() => new Fraction(1, 2) / default(Fraction));
        Assert.Throws<DivideByZeroException>(() => default(Fraction).Reciprocal());
    }

    [Theory]
    [InlineData("30", 30, 1)]
    [InlineData(" 30 ", 30, 1)]
    [InlineData("12.5", 25, 2)]
    [InlineData("12,5", 25, 2)]
    [InlineData("0.5", 1, 2)]
    [InlineData("0.1", 1, 10)]
    [InlineData("29.97", 2997, 100)]
    [InlineData("29,97", 2997, 100)]
    [InlineData("23,976", 23976, 1000)]  // a single comma is always a decimal point
    [InlineData("1,000", 1, 1)]
    [InlineData("1.000", 1, 1)]
    [InlineData("37.4829", 374829, 10000)]
    [InlineData("30000/1001", 30000, 1001)]
    [InlineData("60 / 4", 15, 1)]
    public void TryParseReadsWhatAUserTypes(string text, int expectedNum, int expectedDen)
    {
        Assert.True(Fraction.TryParse(text, out var fraction));
        Assert.Equal(new Fraction(expectedNum, expectedDen), fraction);
    }

    [Fact]
    public void TryParseApproximatesLongDecimalsWithASmallDenominator()
    {
        // 33.3333333 rounds to 6 decimals, then the closest fraction with a denominator up to 10,000.
        Assert.True(Fraction.TryParse("33.3333333", out var fraction));

        Assert.Equal(new Fraction(100, 3), fraction);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("0.0000001")]
    [InlineData("-5")]
    [InlineData("1/0")]
    [InlineData("0/5")]
    [InlineData("-1/2")]
    [InlineData("1/2/3")]
    [InlineData("1e3")]
    [InlineData("1.5/2")]
    [InlineData("99999999999")]
    [InlineData("1,000.5")]
    [InlineData("1.000,5")]
    [InlineData("1,2,3")]
    [InlineData("1.2.3")]
    [InlineData("1..2")]
    [InlineData("0.00001")]   // would approximate to zero: used to throw
    [InlineData("0.00004")]
    [InlineData("0.00005")]
    [InlineData("0.00009")]
    public void TryParseRejectsEverythingElse(string text)
    {
        Assert.False(Fraction.TryParse(text, out _));
    }

    [Fact]
    public void TryParseAcceptsTheSmallestRepresentableValue()
    {
        Assert.True(Fraction.TryParse("0.0001", out var fraction));
        Assert.Equal(new Fraction(1, 10_000), fraction);
    }

    [Theory]
    [InlineData(1, 20_000)]
    [InlineData(1, 100_000)]
    [InlineData(1, 100_000_000)]
    public void NearestNeverReturnsZero(long numerator, long denominator)
    {
        Assert.Equal(new Fraction(1, 10_000), Fraction.Nearest(numerator, denominator, 10_000));
    }

    [Fact]
    public void NearestPicksTheSmallerDenominatorOnATie()
    {
        // 5/12 is exactly 1/12 from both 1/3 and 1/2, and neither is closer within denominators up to 3.
        Assert.Equal(new Fraction(1, 2), Fraction.Nearest(5, 12, 3));
        Assert.Equal(new Fraction(1, 2), Fraction.Nearest(10, 24, 3));
    }

    [Fact]
    public void DefaultFractionIsZeroOverOneAndComparesAsSuch()
    {
        var zero = default(Fraction);

        Assert.Equal(0, zero.Numerator);
        Assert.Equal(1, zero.Denominator);
        Assert.True(zero == default(Fraction));
        Assert.True(zero.Equals((object)default(Fraction)));
        Assert.Equal(zero.GetHashCode(), default(Fraction).GetHashCode());
        Assert.NotEqual(new Fraction(1, 1), zero);
        Assert.True(zero < new Fraction(1, int.MaxValue));
        Assert.Equal(0, zero.CompareTo(default));
        Assert.Equal("0", zero.ToString());
        Assert.Equal(0.0, zero.ToDouble());
    }

    [Fact]
    public void NearestClampsInsteadOfOverflowing()
    {
        Assert.Equal(new Fraction(int.MaxValue, 1), Fraction.Nearest(long.MaxValue, 1, 10_000));
    }

    [Fact]
    public void TryParseRejectsNull()
    {
        Assert.False(Fraction.TryParse(null, out _));
    }
}
