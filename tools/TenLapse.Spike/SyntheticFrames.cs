using System.Globalization;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Text;
using TenLapse.Media.Export;
using TenLapse.Media.Inspection;
using Windows.Foundation;
using Windows.UI;

namespace TenLapse.Spike;

/// <summary>Spike-only: the synthetic frame of #8, a gradient with the frame number in text and as a <see cref="FrameCode"/>.</summary>
internal static class SyntheticImage
{
    public static void Draw(CanvasDrawingSession session, int index, float width, float height)
    {
        var hue = index % 360;
        using var brush = new CanvasLinearGradientBrush(session, FromHue(hue), FromHue((hue + 120) % 360))
        {
            StartPoint = new System.Numerics.Vector2(0, 0),
            EndPoint = new System.Numerics.Vector2(width, height),
        };
        session.FillRectangle(new Rect(0, 0, width, height), brush);
        using var format = new CanvasTextFormat
        {
            FontFamily = "Segoe UI",
            FontSize = height * 0.12f,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Center,
        };
        session.DrawText(
            index.ToString("00000", CultureInfo.InvariantCulture),
            new Rect(0, height * 0.12, width, height * 0.2),
            Color.FromArgb(255, 255, 255, 255),
            format);
        FrameCode.Draw(session, index, width, height);
    }

    private static Color FromHue(int hue)
    {
        var x = (byte)(255 * (1 - Math.Abs((hue / 60.0 % 2) - 1)));
        return (hue / 60) switch
        {
            0 => Color.FromArgb(255, 255, x, 0),
            1 => Color.FromArgb(255, x, 255, 0),
            2 => Color.FromArgb(255, 0, 255, x),
            3 => Color.FromArgb(255, 0, x, 255),
            4 => Color.FromArgb(255, x, 0, 255),
            _ => Color.FromArgb(255, 255, 0, x),
        };
    }
}

/// <summary>Spike-only: synthetic source frames drawn on the GPU at the source size, the "in memory" case.</summary>
internal sealed class SyntheticFrameSource(int count, int width, int height) : IFrameSource
{
    public int FrameCount => count;

    public Task<CanvasBitmap> LoadAsync(int index, CanvasDevice device, CancellationToken cancellationToken) =>
        Task.Run<CanvasBitmap>(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = new CanvasRenderTarget(device, width, height, 96f);
                using (var session = target.CreateDrawingSession())
                {
                    SyntheticImage.Draw(session, index, width, height);
                }

                return target;
            },
            cancellationToken);
}

/// <summary>Spike-only: writes the synthetic frames as JPEG files, the "on disk" case.</summary>
internal static class SyntheticJpegs
{
    public static string PathOf(string directory, int index) =>
        Path.Combine(directory, $"frame_{index.ToString("00000", CultureInfo.InvariantCulture)}.jpg");

    public static async Task WriteAsync(string directory, int count, int width, int height, int noise = 0)
    {
        Directory.CreateDirectory(directory);
        var device = TenLapse.Media.GraphicsDevice.Shared;
        using var gate = new SemaphoreSlim(Environment.ProcessorCount);
        var tasks = Enumerable.Range(0, count).Select(async index =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var path = PathOf(directory, index);
                if (File.Exists(path))
                {
                    return;
                }

                using var target = new CanvasRenderTarget(device, width, height, 96f);
                using (var session = target.CreateDrawingSession())
                {
                    SyntheticImage.Draw(session, index, width, height);
                }

                if (noise > 0)
                {
                    AddNoise(target, index, noise);
                }

                await target.SaveAsync(path, CanvasBitmapFileFormat.Jpeg, 0.9f).AsTask().ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    // Grain of +-amplitude levels per channel, so a JPEG compresses like a real 24 MP photo (8 to 12 MB at
    // quality 0.9) instead of a smooth gradient (about 1 MB): #13 measures decode memory on these.
    private static void AddNoise(CanvasRenderTarget target, int index, int amplitude)
    {
        var pixels = target.GetPixelBytes();
        var random = new Random(index);
        var span = (amplitude * 2) + 1;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)Math.Clamp(pixels[i] + random.Next(span) - amplitude, 0, 255);
            pixels[i + 1] = (byte)Math.Clamp(pixels[i + 1] + random.Next(span) - amplitude, 0, 255);
            pixels[i + 2] = (byte)Math.Clamp(pixels[i + 2] + random.Next(span) - amplitude, 0, 255);
        }

        target.SetPixelBytes(pixels);
    }
}
