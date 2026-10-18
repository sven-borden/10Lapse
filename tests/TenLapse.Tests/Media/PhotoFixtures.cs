using System.Buffers.Binary;
using System.Runtime.InteropServices.WindowsRuntime;
using TenLapse.Media.Decoding;
using Windows.Foundation;
using Windows.Graphics.Imaging;

namespace TenLapse.Tests.Media;

/// <summary>Writes small test photos with WIC's own encoders, and the odd files no encoder writes, into a temp folder.</summary>
internal sealed class PhotoFixtures : IDisposable
{
    public static readonly (byte B, byte G, byte R) Red = (0, 0, 255);
    public static readonly (byte B, byte G, byte R) Blue = (255, 0, 0);

    public PhotoFixtures()
    {
        Directory = Path.Combine(Path.GetTempPath(), "TenLapse.Tests", "photos-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
    }

    public string Directory { get; }

    public string PathOf(string name) => Path.Combine(Directory, name);

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file a test still holds; Windows cleans the temp folder eventually.
        }
    }

    /// <summary>
    /// A width x height image, red in the top-left quadrant and blue elsewhere, written with the encoder
    /// <paramref name="encoderId"/>. <paramref name="frames"/> &gt; 1 writes more frames (GIF, TIFF), each a solid
    /// green after the first. <paramref name="orientation"/> sets EXIF <c>System.Photo.Orientation</c>.
    /// </summary>
    public async Task<string> WriteAsync(
        string name, Guid encoderId, int width = 64, int height = 32, ushort? orientation = null, int frames = 1, bool noise = false)
    {
        var path = PathOf(name);
        await WriteToAsync(path, encoderId, width, height, orientation, frames, noise);
        return path;
    }

    public static async Task WriteToAsync(
        string path, Guid encoderId, int width = 64, int height = 32, ushort? orientation = null, int frames = 1, bool noise = false)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var stream = file.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(encoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, Quadrants(width, height, noise));
        if (orientation is { } value)
        {
            await encoder.BitmapProperties.SetPropertiesAsync(
                [new KeyValuePair<string, BitmapTypedValue>("System.Photo.Orientation", new BitmapTypedValue(value, PropertyType.UInt16))]);
        }

        for (var i = 1; i < frames; i++)
        {
            await encoder.GoToNextFrameAsync();
            var green = new byte[width * height * 4];
            for (var p = 0; p < green.Length; p += 4)
            {
                green[p + 1] = 255;
                green[p + 3] = 255;
            }

            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, green);
        }

        await encoder.FlushAsync();
    }

    /// <summary>A PNG whose every pixel <paramref name="colour"/> gives (x, y to B, G, R), through WIC's encoder.</summary>
    public async Task<string> WritePatternAsync(string name, int width, int height, Func<int, int, (byte B, byte G, byte R)> colour)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (b, g, r) = colour(x, y);
                var offset = ((y * width) + x) * 4;
                (pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]) = (b, g, r, 255);
            }
        }

        var path = PathOf(name);
        using var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var stream = file.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync();
        return path;
    }

    /// <summary>A 16-bit RGBA PNG, red top-left and blue elsewhere, through WIC's encoder.</summary>
    public async Task<string> WritePngAsync(string name, BitmapPixelFormat format, int width = 64, int height = 32)
    {
        var path = PathOf(name);
        using var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var stream = file.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        var pixels = new byte[width * height * 8];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var red = x < width / 2 && y < height / 2;
                var offset = ((y * width) + x) * 8;
                BinaryPrimitives.WriteUInt16LittleEndian(pixels.AsSpan(offset), red ? (ushort)0xFFFF : (ushort)0);
                BinaryPrimitives.WriteUInt16LittleEndian(pixels.AsSpan(offset + 4), red ? (ushort)0 : (ushort)0xFFFF);
                BinaryPrimitives.WriteUInt16LittleEndian(pixels.AsSpan(offset + 6), 0xFFFF);
            }
        }

        encoder.SetPixelData(format, BitmapAlphaMode.Straight, (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync();
        return path;
    }

    /// <summary>An 8-bit grayscale PNG written by hand (WinRT's PNG encoder takes no gray input): 200 top-left, 40 elsewhere.</summary>
    public string WriteGrayPng(string name, int width = 64, int height = 32) =>
        WriteRawPng(name, width, height, colorType: 0, palette: null, (x, y) => x < width / 2 && y < height / 2 ? (byte)200 : (byte)40);

    /// <summary>An 8-bit palette PNG written by hand: index 0 red top-left, index 1 blue elsewhere.</summary>
    public string WritePalettePng(string name, int width = 64, int height = 32) =>
        WriteRawPng(name, width, height, colorType: 3, palette: [255, 0, 0, 0, 0, 255], (x, y) => x < width / 2 && y < height / 2 ? (byte)0 : (byte)1);

    private string WriteRawPng(string name, int width, int height, byte colorType, byte[]? palette, Func<int, int, byte> sample)
    {
        using var memory = new MemoryStream();
        memory.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = 8;
        ihdr[9] = colorType;
        WriteChunk(memory, "IHDR", ihdr);
        if (palette is not null)
        {
            WriteChunk(memory, "PLTE", palette);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        {
            for (var y = 0; y < height; y++)
            {
                zlib.WriteByte(0);
                for (var x = 0; x < width; x++)
                {
                    zlib.WriteByte(sample(x, y));
                }
            }
        }

        WriteChunk(memory, "IDAT", compressed.ToArray());
        WriteChunk(memory, "IEND", []);
        var path = PathOf(name);
        File.WriteAllBytes(path, memory.ToArray());
        return path;
    }

    /// <summary>
    /// An uncompressed CMYK TIFF written by hand (WinRT's encoders take no CMYK input): cyan+magenta (blue) with
    /// the top-left quadrant magenta+yellow (red).
    /// </summary>
    public string WriteCmykTiff(string name, int width = 64, int height = 32)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var red = x < width / 2 && y < height / 2;
                var offset = ((y * width) + x) * 4;
                pixels[offset] = red ? (byte)0 : (byte)255;      // C
                pixels[offset + 1] = 255;                         // M
                pixels[offset + 2] = red ? (byte)255 : (byte)0;   // Y
                pixels[offset + 3] = 0;                           // K
            }
        }

        // Header (8) + IFD (2 + 11 entries x 12 + 4) + BitsPerSample values (8), then the pixels.
        const int entries = 11;
        const int ifdOffset = 8;
        const int bitsOffset = ifdOffset + 2 + (entries * 12) + 4;
        const int dataOffset = bitsOffset + 8;
        var tiff = new byte[dataOffset + pixels.Length];
        var span = tiff.AsSpan();
        span[0] = (byte)'I';
        span[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 42);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], ifdOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(span[ifdOffset..], entries);
        var entry = ifdOffset + 2;
        void Entry(ushort tag, ushort type, uint count, uint value)
        {
            var field = tiff.AsSpan();
            BinaryPrimitives.WriteUInt16LittleEndian(field[entry..], tag);
            BinaryPrimitives.WriteUInt16LittleEndian(field[(entry + 2)..], type);
            BinaryPrimitives.WriteUInt32LittleEndian(field[(entry + 4)..], count);
            if (type == 3 && count == 1)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(field[(entry + 8)..], (ushort)value);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(field[(entry + 8)..], value);
            }

            entry += 12;
        }

        Entry(256, 4, 1, (uint)width);                 // ImageWidth
        Entry(257, 4, 1, (uint)height);                // ImageLength
        Entry(258, 3, 4, bitsOffset);                  // BitsPerSample 8,8,8,8
        Entry(259, 3, 1, 1);                           // Compression: none
        Entry(262, 3, 1, 5);                           // PhotometricInterpretation: separated (CMYK)
        Entry(273, 4, 1, dataOffset);                  // StripOffsets
        Entry(277, 3, 1, 4);                           // SamplesPerPixel
        Entry(278, 4, 1, (uint)height);                // RowsPerStrip
        Entry(279, 4, 1, (uint)pixels.Length);         // StripByteCounts
        Entry(284, 3, 1, 1);                           // PlanarConfiguration: chunky
        Entry(332, 3, 1, 1);                           // InkSet: CMYK
        BinaryPrimitives.WriteUInt32LittleEndian(span[entry..], 0);
        for (var i = 0; i < 4; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(span[(bitsOffset + (i * 2))..], 8);
        }

        pixels.CopyTo(span[dataOffset..]);
        var path = PathOf(name);
        File.WriteAllBytes(path, tiff);
        return path;
    }

    /// <summary>A valid PNG header claiming <paramref name="width"/> x <paramref name="height"/>, with one tiny IDAT: too large to decode whole.</summary>
    public string WriteHugePngHeader(string name, uint width, uint height)
    {
        using var memory = new MemoryStream();
        memory.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 2;  // RGB
        WriteChunk(memory, "IHDR", ihdr);
        WriteChunk(memory, "IDAT", [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01]);
        WriteChunk(memory, "IEND", []);
        var path = PathOf(name);
        File.WriteAllBytes(path, memory.ToArray());
        return path;
    }

    public static byte[] Quadrants(int width, int height, bool noise = false)
    {
        var random = new Random(1);
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (b, g, r) = x < width / 2 && y < height / 2 ? Red : Blue;
                var offset = ((y * width) + x) * 4;
                pixels[offset] = noise ? (byte)random.Next(256) : b;
                pixels[offset + 1] = noise ? (byte)random.Next(256) : g;
                pixels[offset + 2] = noise ? (byte)random.Next(256) : r;
                pixels[offset + 3] = 255;
            }
        }

        return pixels;
    }

    /// <summary>The decoded photo's pixels, BGRA, top-down, tightly packed.</summary>
    public static byte[] Bgra(DecodedPhoto photo)
    {
        var bytes = new byte[photo.Size.Width * photo.Size.Height * 4];
        photo.Pixels.CopyToBuffer(bytes.AsBuffer());
        return bytes;
    }

    /// <summary>The pixel at the centre of a quadrant (0 top-left, 1 top-right, 2 bottom-left, 3 bottom-right).</summary>
    public static (byte B, byte G, byte R) Quadrant(DecodedPhoto photo, int quadrant)
    {
        var (width, height) = (photo.Size.Width, photo.Size.Height);
        var x = (quadrant % 2 == 0 ? width / 4 : width * 3 / 4);
        var y = (quadrant < 2 ? height / 4 : height * 3 / 4);
        var bytes = Bgra(photo);
        var offset = ((y * width) + x) * 4;
        return (bytes[offset], bytes[offset + 1], bytes[offset + 2]);
    }

    /// <summary>True when the colour is clearly red (JPEG keeps it within a few levels).</summary>
    public static bool IsRed((byte B, byte G, byte R) pixel) => pixel.R > 200 && pixel.G < 60 && pixel.B < 60;

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var header = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)data.Length);
        System.Text.Encoding.ASCII.GetBytes(type, header.AsSpan(4));
        stream.Write(header);
        stream.Write(data);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(header.AsSpan(4), data));
        stream.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        void Add(ReadOnlySpan<byte> bytes)
        {
            foreach (var b in bytes)
            {
                crc ^= b;
                for (var k = 0; k < 8; k++)
                {
                    crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
                }
            }
        }

        Add(type);
        Add(data);
        return crc ^ 0xFFFFFFFFu;
    }
}
