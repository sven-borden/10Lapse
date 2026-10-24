using TenLapse.Core.Look;

namespace TenLapse.Tests.Look;

/// <summary>How bright a picture is (#45): the trimmed mean of the log luminance, which a gain in linear light moves by exactly its stops.</summary>
public sealed class LuminanceMeterTests
{
    // A width x height BGRA picture whose pixels are grays with linear values from the function.
    internal static byte[] Grays(int width, int height, Func<int, int, double> linear)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var code = (byte)Math.Round(Math.Clamp(LuminanceMeter.LinearToSrgb(Math.Clamp(linear(x, y), 0, 1)), 0, 1) * 255);
                var at = ((y * width) + x) * 4;
                pixels[at] = pixels[at + 1] = pixels[at + 2] = code;
                pixels[at + 3] = 255;
            }
        }

        return pixels;
    }

    [Fact]
    public void AUniformGrayMeasuresItsOwnLog()
    {
        // sRGB 118 is linear 0.1812, log2 -2.464; the histogram's 1/64-stop bins put it within 1/128.
        var pixels = new byte[16 * 16 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = pixels[i + 1] = pixels[i + 2] = 118;
            pixels[i + 3] = 255;
        }

        Assert.Equal(Math.Log2(LuminanceMeter.SrgbToLinear(118 / 255.0)), LuminanceMeter.Measure(pixels, 16, 16, 64), 1.0 / 128);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(-0.5)]
    [InlineData(0.333)]
    [InlineData(1.0)]
    public void AGainInLinearLightMovesTheMeasureByItsStops(double stops)
    {
        // A gradient from 1/64 to 1/4 of white: no pixel clips at +1 stop, none drops into the floor at -1.
        Func<int, int, double> scene = (x, y) => Math.Pow(2, -6 + (4.0 * ((x * 64) + y) / (64 * 64)));
        var before = LuminanceMeter.Measure(Grays(64, 64, scene), 64, 64, 256);
        var after = LuminanceMeter.Measure(Grays(64, 64, (x, y) => scene(x, y) * Math.Pow(2, stops)), 64, 64, 256);

        // 8-bit rounding of the darkest grays costs a few hundredths of a stop at most.
        Assert.Equal(stops, after - before, 0.03);
    }

    [Fact]
    public void TheClippedSunAndTheNoiseFloorDoNotMoveIt()
    {
        // 5 % white and 5 % black on a mid-gray picture: both fall in the trimmed tenths.
        var gray = LuminanceMeter.Measure(Grays(20, 20, (_, _) => 0.18), 20, 20, 80);
        var withExtremes = LuminanceMeter.Measure(Grays(20, 20, (x, y) => y == 0 ? 1 : y == 19 ? 0 : 0.18), 20, 20, 80);

        Assert.Equal(gray, withExtremes, 0.02);
    }

    [Fact]
    public void TransparentPixelsAreLeftOutAndAStrideIsHonoured()
    {
        // Two pixels a row in a 12-byte stride: a mid gray and a transparent one, then padding.
        byte[] pixels = [100, 100, 100, 255, 0, 0, 0, 0, 9, 9, 9, 9, 100, 100, 100, 255, 0, 0, 0, 0, 9, 9, 9, 9];

        Assert.Equal(Math.Log2(LuminanceMeter.SrgbToLinear(100 / 255.0)), LuminanceMeter.Measure(pixels, 2, 2, 12), 1.0 / 128);
        Assert.Equal(-16, LuminanceMeter.Measure(new byte[16], 2, 2, 8));
    }
}
