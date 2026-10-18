using System.Buffers.Binary;
using System.Diagnostics;
using TenLapse.Core.Projects;
using TenLapse.Media.Decoding;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace TenLapse.Spike;

/// <summary>
/// Spike-only (#13): times one photo's decode through WinRT's <see cref="BitmapDecoder"/> with each choice
/// <c>PhotoDecoder</c> makes switched on or off (the stream, colour management, the scaling filter), then
/// <see cref="PhotoDecoder.DecodeAsync"/> itself on the photo as it is, with an sRGB profile embedded, and with a
/// profile that is not sRGB.
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
            ("winrt file, Fant, sRGB", false, ColorManagementMode.ColorManageToSRgb, BitmapInterpolationMode.Fant),
        };
        foreach (var (name, managed, colour, filter) in variants)
        {
            await TimeAsync(name, runs, () => DecodeAsync(path, managed, colour, filter, coverWidth, coverHeight));
        }

        // The same photo with an embedded profile: Windows' sRGB one, and that one with its red and blue primaries
        // swapped (as costly to convert as Display P3 or Adobe RGB, and plainly visible if it were skipped).
        var srgbProfile = Path.Combine(Environment.SystemDirectory, "spool", "drivers", "color", "sRGB Color Space Profile.icm");
        var directory = Directory.CreateTempSubdirectory("10lapse-decode-bench-");
        try
        {
            var photos = new List<(string Name, string Path)> { ("no profile", path) };
            if (File.Exists(srgbProfile) && IsJpeg(path))
            {
                var jpeg = await File.ReadAllBytesAsync(path);
                var profile = await File.ReadAllBytesAsync(srgbProfile);
                var withSrgb = Path.Combine(directory.FullName, "srgb.jpg");
                var withOther = Path.Combine(directory.FullName, "swapped.jpg");
                await File.WriteAllBytesAsync(withSrgb, EmbedProfile(jpeg, profile));
                await File.WriteAllBytesAsync(withOther, EmbedProfile(jpeg, SwapRedAndBlue(profile)));
                photos.Add(("sRGB profile", withSrgb));
                photos.Add(("swapped-primaries profile", withOther));
            }
            else
            {
                Console.WriteLine("(no profile variants: the photo is not a JPEG, or this PC has no sRGB Color Space Profile.icm)");
            }

            var cover = PhotoDecodeOptions.Cover(new PixelSize(coverWidth, coverHeight));
            foreach (var (name, photo) in photos)
            {
                await TimeAsync($"winrt file, Fant, sRGB at the source, {name}", runs, () => DecodeAsync(photo, false, ColorManagementMode.ColorManageToSRgb, BitmapInterpolationMode.Fant, coverWidth, coverHeight));
                await TimeAsync($"PhotoDecoder cover, {name}", runs, () => PhotoDecoderAsync(photo, cover));
                await TimeAsync($"PhotoDecoder.ReadInfoAsync, {name}", runs, () => ReadInfoAsync(photo));
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }

        return 0;
    }

    private static async Task TimeAsync(string name, int runs, Func<Task<string>> decode)
    {
        await decode();
        var clock = Stopwatch.StartNew();
        var size = "";
        for (var i = 0; i < runs; i++)
        {
            size = await decode();
        }

        Console.WriteLine($"| {name} | {size} | {clock.Elapsed.TotalMilliseconds / runs:0.0} ms |");
    }

    private static async Task<string> PhotoDecoderAsync(string path, PhotoDecodeOptions options)
    {
        var result = await PhotoDecoder.DecodeAsync(path, options);
        if (!result.Succeeded)
        {
            return $"failed: {result.Failure.Reason}";
        }

        using var photo = result.Value;
        return $"{photo.Size.Width}x{photo.Size.Height}";
    }

    private static async Task<string> ReadInfoAsync(string path)
    {
        var result = await PhotoDecoder.ReadInfoAsync(path);
        return result.Succeeded ? $"{result.Value.Size.Width}x{result.Value.Size.Height}" : $"failed: {result.Failure.Reason}";
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

    private static bool IsJpeg(string path)
    {
        Span<byte> start = stackalloc byte[3];
        using var file = File.OpenRead(path);
        return file.Read(start) == 3 && start is [0xFF, 0xD8, 0xFF];
    }

    // An APP2 ICC_PROFILE segment right after the start-of-image marker (profiles under 64 KB).
    private static byte[] EmbedProfile(byte[] jpeg, byte[] profile)
    {
        var length = 2 + 12 + 2 + profile.Length;
        byte[] header = [0xFF, 0xE2, (byte)(length >> 8), (byte)length, .. "ICC_PROFILE\0"u8, 1, 1];
        return [.. jpeg[..2], .. header, .. profile, .. jpeg[2..]];
    }

    // The profile with the offsets of its rXYZ and bXYZ tags exchanged: red and blue primaries swapped.
    private static byte[] SwapRedAndBlue(byte[] profile)
    {
        var copy = (byte[])profile.Clone();
        var count = BinaryPrimitives.ReadInt32BigEndian(copy.AsSpan(128));
        int red = -1, blue = -1;
        for (var i = 0; i < count; i++)
        {
            var entry = 132 + (i * 12);
            if (copy.AsSpan(entry, 4).SequenceEqual("rXYZ"u8))
            {
                red = entry;
            }
            else if (copy.AsSpan(entry, 4).SequenceEqual("bXYZ"u8))
            {
                blue = entry;
            }
        }

        if (red < 0 || blue < 0)
        {
            throw new InvalidOperationException("The sRGB profile has no rXYZ or bXYZ tag.");
        }

        var redData = copy.AsSpan(red + 4, 8).ToArray();
        copy.AsSpan(blue + 4, 8).CopyTo(copy.AsSpan(red + 4));
        redData.CopyTo(copy.AsSpan(blue + 4));
        return copy;
    }
}
