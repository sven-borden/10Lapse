using System.Buffers.Binary;
using System.Text;

namespace TenLapse.Tests.Media;

/// <summary>Small ICC v2 matrix/curve display profiles written by hand, and a way to embed one in a JPEG.</summary>
internal static class IccProfiles
{
    // sRGB's primaries adapted to _d50, as sRGB profiles carry them.
    private static readonly (double X, double Y, double Z) _red = (0.4361, 0.2225, 0.0139);
    private static readonly (double X, double Y, double Z) _green = (0.3851, 0.7169, 0.0971);
    private static readonly (double X, double Y, double Z) _blue = (0.1431, 0.0606, 0.7141);
    private static readonly (double X, double Y, double Z) _d50 = (0.9642, 1.0, 0.8249);

    /// <summary>The Windows copy of the ICC's sRGB profile, when this PC has it.</summary>
    public static string WindowsSrgbPath => Path.Combine(Environment.SystemDirectory, "spool", "drivers", "color", "sRGB Color Space Profile.icm");

    /// <summary>sRGB: its primaries and its exact tone curve as a 1024-entry table, under a name that is not "sRGB".</summary>
    public static byte[] Srgb() => Build("Display (10Lapse test)", _red, _green, _blue, SrgbCurve());

    /// <summary>sRGB's red and blue primaries swapped: pure red in the file is pure blue on screen.</summary>
    public static byte[] SwappedRedAndBlue() => Build("sRGB (10Lapse test, red and blue swapped)", _blue, _green, _red, GammaCurve(2.2));

    /// <summary>sRGB's primaries with a plain 2.2 gamma: close to sRGB, but not in the shadows.</summary>
    public static byte[] Gamma22() => Build("Gamma 2.2 (10Lapse test)", _red, _green, _blue, GammaCurve(2.2));

    /// <summary>The JPEG with <paramref name="profile"/> embedded as an APP2 ICC_PROFILE segment after its JFIF header.</summary>
    public static byte[] Embed(byte[] jpeg, byte[] profile)
    {
        var at = 2;
        if (jpeg[2] == 0xFF && jpeg[3] == 0xE0)
        {
            at = 4 + ((jpeg[4] << 8) | jpeg[5]);
        }

        var length = 2 + 12 + 2 + profile.Length;
        byte[] header = [0xFF, 0xE2, (byte)(length >> 8), (byte)length, .. "ICC_PROFILE\0"u8, 1, 1];
        return [.. jpeg[..at], .. header, .. profile, .. jpeg[at..]];
    }

    private static byte[] Build(string description, (double, double, double) red, (double, double, double) green, (double, double, double) blue, byte[] curve)
    {
        (string Signature, byte[] Data)[] tags =
        [
            ("desc", Description(description)),
            ("cprt", Text("No copyright: test data")),
            ("wtpt", Xyz(_d50)),
            ("rXYZ", Xyz(red)),
            ("gXYZ", Xyz(green)),
            ("bXYZ", Xyz(blue)),
            ("rTRC", curve),
            ("gTRC", curve),
            ("bTRC", curve),
        ];
        using var data = new MemoryStream();
        var offsets = new int[tags.Length];
        var start = 128 + 4 + (tags.Length * 12);
        for (var i = 0; i < tags.Length; i++)
        {
            // Tags that share their data share their offset (the three curves).
            var same = Array.FindIndex(tags, 0, i, t => ReferenceEquals(t.Data, tags[i].Data));
            if (same >= 0)
            {
                offsets[i] = offsets[same];
                continue;
            }

            offsets[i] = start + (int)data.Length;
            data.Write(tags[i].Data);
            while (data.Length % 4 != 0)
            {
                data.WriteByte(0);
            }
        }

        var profile = new byte[start + data.Length];
        var span = profile.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(span, (uint)profile.Length);
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], 0x02100000);
        "mntr"u8.CopyTo(span[12..]);
        "RGB "u8.CopyTo(span[16..]);
        "XYZ "u8.CopyTo(span[20..]);
        BinaryPrimitives.WriteUInt16BigEndian(span[24..], 2026);
        BinaryPrimitives.WriteUInt16BigEndian(span[26..], 1);
        BinaryPrimitives.WriteUInt16BigEndian(span[28..], 1);
        "acsp"u8.CopyTo(span[36..]);
        "MSFT"u8.CopyTo(span[40..]);
        Xyz(_d50).AsSpan(8).CopyTo(span[68..]);
        BinaryPrimitives.WriteUInt32BigEndian(span[128..], (uint)tags.Length);
        for (var i = 0; i < tags.Length; i++)
        {
            var entry = span[(132 + (i * 12))..];
            Encoding.ASCII.GetBytes(tags[i].Signature, entry);
            BinaryPrimitives.WriteUInt32BigEndian(entry[4..], (uint)offsets[i]);
            BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)tags[i].Data.Length);
        }

        data.ToArray().CopyTo(span[start..]);
        return profile;
    }

    private static byte[] Xyz((double X, double Y, double Z) value)
    {
        var tag = new byte[20];
        "XYZ "u8.CopyTo(tag);
        BinaryPrimitives.WriteInt32BigEndian(tag.AsSpan(8), (int)Math.Round(value.X * 65536));
        BinaryPrimitives.WriteInt32BigEndian(tag.AsSpan(12), (int)Math.Round(value.Y * 65536));
        BinaryPrimitives.WriteInt32BigEndian(tag.AsSpan(16), (int)Math.Round(value.Z * 65536));
        return tag;
    }

    private static byte[] GammaCurve(double gamma)
    {
        var tag = new byte[14];
        "curv"u8.CopyTo(tag);
        BinaryPrimitives.WriteUInt32BigEndian(tag.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16BigEndian(tag.AsSpan(12), (ushort)Math.Round(gamma * 256));
        return tag;
    }

    private static byte[] SrgbCurve()
    {
        const int count = 1024;
        var tag = new byte[12 + (count * 2)];
        "curv"u8.CopyTo(tag);
        BinaryPrimitives.WriteUInt32BigEndian(tag.AsSpan(8), count);
        for (var i = 0; i < count; i++)
        {
            var encoded = i / (double)(count - 1);
            var linear = encoded <= 0.04045 ? encoded / 12.92 : Math.Pow((encoded + 0.055) / 1.055, 2.4);
            BinaryPrimitives.WriteUInt16BigEndian(tag.AsSpan(12 + (i * 2)), (ushort)Math.Round(linear * 65535));
        }

        return tag;
    }

    private static byte[] Description(string text)
    {
        // textDescriptionType: ASCII with its length, then empty Unicode and ScriptCode parts.
        var ascii = Encoding.ASCII.GetBytes(text + "\0");
        var tag = new byte[12 + ascii.Length + 4 + 4 + 2 + 1 + 67];
        "desc"u8.CopyTo(tag);
        BinaryPrimitives.WriteUInt32BigEndian(tag.AsSpan(8), (uint)ascii.Length);
        ascii.CopyTo(tag, 12);
        return tag;
    }

    private static byte[] Text(string text)
    {
        var ascii = Encoding.ASCII.GetBytes(text + "\0");
        var tag = new byte[8 + ascii.Length];
        "text"u8.CopyTo(tag);
        ascii.CopyTo(tag, 8);
        return tag;
    }
}
