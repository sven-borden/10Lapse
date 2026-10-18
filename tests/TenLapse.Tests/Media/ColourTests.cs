using TenLapse.Core.Timing;
using TenLapse.Media.Export;
using TenLapse.Media.Inspection;
using Windows.UI;
using static TenLapse.Tests.Media.MediaSkips;

namespace TenLapse.Tests.Media;

/// <summary>
/// What the export does to colour. The source reader decodes with the matrix the file's tags name, so a colour
/// that comes back as itself means the tag matches the matrix the converter actually used.
/// </summary>
public sealed class ColourTests : IDisposable
{
    private const double Tolerance = 6;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TenLapse.Tests", Guid.NewGuid().ToString("N"));

    public ColourTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A reader still closing the file; the temp folder is cleaned up by Windows eventually.
        }
    }

    [Theory]
    [InlineData(640, 360, 230, 40, 40, true)] // BT.601
    [InlineData(1024, 576, 30, 200, 60, true)] // BT.601: the last height the converter treats as SD
    [InlineData(1024, 578, 30, 200, 60, true)] // BT.709
    [InlineData(1920, 540, 40, 60, 220, true)] // BT.601: the width does not count
    [InlineData(360, 640, 230, 40, 40, true)] // BT.709: portrait
    [InlineData(1280, 720, 40, 60, 220, true)] // BT.709
    [InlineData(640, 360, 230, 40, 40, false)]
    [InlineData(1280, 720, 230, 40, 40, false)]
    public async Task ASaturatedColourDecodesBackAsItself(int width, int height, byte red, byte green, byte blue, bool hardware)
    {
        SkipWithoutH264Encoder();
        var path = Path.Combine(_directory, $"colour-{width}x{height}-{hardware}.mp4");
        var settings = new VideoExportSettings(width, height, new Fraction(30, 1)) { HardwareAcceleration = hardware };

        await ExportOrSkipAsync(new SolidFrames(3, width, height, Color.FromArgb(255, red, green, blue)), path, settings);

        var averages = new List<(double R, double G, double B)>();
        VideoFileReader.ReadFrames(path, frame => averages.Add(CentreAverage(frame)));
        Assert.Equal(3, averages.Count);
        var (r, g, b) = averages[1];
        Assert.True(
            Math.Abs(r - red) <= Tolerance && Math.Abs(g - green) <= Tolerance && Math.Abs(b - blue) <= Tolerance,
            $"({red}, {green}, {blue}) at {width}x{height} decoded as ({r:0.0}, {g:0.0}, {b:0.0}): the tagged matrix is not the one the converter used.");
    }

    // The average of the middle quarter of a BGRA frame, away from any edge effects of 4:2:0 subsampling.
    private static (double R, double G, double B) CentreAverage(DecodedVideoFrame frame)
    {
        double r = 0, g = 0, b = 0;
        var count = 0;
        for (var y = frame.Height / 4; y < frame.Height * 3 / 4; y++)
        {
            for (var x = frame.Width / 4; x < frame.Width * 3 / 4; x++)
            {
                var offset = (y * frame.Stride) + (x * 4);
                b += frame.Pixels[offset];
                g += frame.Pixels[offset + 1];
                r += frame.Pixels[offset + 2];
                count++;
            }
        }

        return (r / count, g / count, b / count);
    }
}
