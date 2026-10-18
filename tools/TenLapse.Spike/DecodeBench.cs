using System.Diagnostics;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace TenLapse.Spike;

/// <summary>
/// Spike-only (#13): times one photo's decode through WinRT's <see cref="BitmapDecoder"/> with each choice
/// <c>PhotoDecoder</c> makes switched on or off: the stream, colour management, and the scaling filter.
/// </summary>
internal static class DecodeBench
{
    public static async Task<int> RunAsync(string path, int runs, int coverWidth, int coverHeight)
    {
        Console.WriteLine($"decode-bench: {path}, {runs} runs each, cover {coverWidth}x{coverHeight}");
        var variants = new (string Name, bool ManagedStream, ColorManagementMode Colour, BitmapInterpolationMode? Filter)[]
        {
            ("winrt file, full size, no colour", false, ColorManagementMode.DoNotColorManage, null),
            ("managed file, full size, no colour", true, ColorManagementMode.DoNotColorManage, null),
            ("managed file, full size, sRGB", true, ColorManagementMode.ColorManageToSRgb, null),
            ("managed file, Fant, no colour", true, ColorManagementMode.DoNotColorManage, BitmapInterpolationMode.Fant),
            ("managed file, Fant, sRGB", true, ColorManagementMode.ColorManageToSRgb, BitmapInterpolationMode.Fant),
            ("managed file, Linear, sRGB", true, ColorManagementMode.ColorManageToSRgb, BitmapInterpolationMode.Linear),
            ("managed file, Cubic, sRGB", true, ColorManagementMode.ColorManageToSRgb, BitmapInterpolationMode.Cubic),
            ("winrt file, Fant, no colour", false, ColorManagementMode.DoNotColorManage, BitmapInterpolationMode.Fant),
        };
        foreach (var (name, managed, colour, filter) in variants)
        {
            await DecodeAsync(path, managed, colour, filter, coverWidth, coverHeight);
            var clock = Stopwatch.StartNew();
            var size = "";
            for (var i = 0; i < runs; i++)
            {
                size = await DecodeAsync(path, managed, colour, filter, coverWidth, coverHeight);
            }

            Console.WriteLine($"| {name} | {size} | {clock.Elapsed.TotalMilliseconds / runs:0} ms |");
        }

        return 0;
    }

    private static async Task<string> DecodeAsync(
        string path, bool managedStream, ColorManagementMode colour, BitmapInterpolationMode? filter, int coverWidth, int coverHeight)
    {
        IRandomAccessStream stream;
        FileStream? file = null;
        if (managedStream)
        {
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.RandomAccess);
            stream = file.AsRandomAccessStream();
        }
        else
        {
            stream = await FileRandomAccessStream.OpenAsync(path, FileAccessMode.Read);
        }

        using (file)
        using (stream)
        {
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var transform = new BitmapTransform();
            if (filter is { } mode)
            {
                var scale = Math.Max((double)coverWidth / decoder.OrientedPixelWidth, (double)coverHeight / decoder.OrientedPixelHeight);
                transform.InterpolationMode = mode;
                transform.ScaledWidth = (uint)Math.Ceiling(decoder.PixelWidth * scale);
                transform.ScaledHeight = (uint)Math.Ceiling(decoder.PixelHeight * scale);
            }

            using var pixels = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.RespectExifOrientation, colour);
            return $"{pixels.PixelWidth}x{pixels.PixelHeight}";
        }
    }
}
