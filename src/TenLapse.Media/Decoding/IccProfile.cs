using System.Buffers.Binary;

namespace TenLapse.Media.Decoding;

/// <summary>
/// Reads just enough of an ICC colour profile to tell whether converting to sRGB would change anything: its
/// colour space, and for an RGB matrix/curve profile its primaries and tone curves.
/// </summary>
/// <remarks>
/// By what the profile does, not by its name: cameras and phones embed sRGB under many names ("sRGB
/// IEC61966-2.1", "sRGB built-in", "sRGB2014"), and a name is no promise. A profile built from lookup tables
/// rather than a matrix and curves reads as not sRGB, which only costs a conversion that changes nothing.
/// </remarks>
internal static class IccProfile
{
    // How far a primary may be from sRGB's: the ICC's and the vendors' sRGB profiles differ in the third decimal.
    private const double PrimaryTolerance = 0.005;

    // How far a tone curve may be from sRGB's, in linear light: rejects a plain 2.2 gamma (0.0026 off at 5 %),
    // which would shift deep shadows by several levels.
    private const double CurveTolerance = 0.002;

    // sRGB's primaries adapted to the D50 white of the profile connection space, as sRGB profiles carry them.
    private static readonly (string Tag, double X, double Y, double Z)[] _srgbPrimaries =
    [
        ("rXYZ", 0.4361, 0.2225, 0.0139),
        ("gXYZ", 0.3851, 0.7169, 0.0971),
        ("bXYZ", 0.1431, 0.0606, 0.7141),
    ];

    private static readonly double[] _curveSamples = [0.02, 0.05, 0.1, 0.2, 0.35, 0.5, 0.65, 0.8, 0.95];

    /// <summary>True for a profile of RGB data (not CMYK, gray or anything else).</summary>
    public static bool IsRgb(ReadOnlySpan<byte> profile) => profile.Length >= 132 && profile[16..20].SequenceEqual("RGB "u8);

    /// <summary>True when the profile is sRGB in effect: sRGB's primaries and its tone curve on each channel.</summary>
    public static bool IsSrgb(ReadOnlySpan<byte> profile)
    {
        if (!IsRgb(profile) || !profile[20..24].SequenceEqual("XYZ "u8))
        {
            return false;
        }

        foreach (var (tag, x, y, z) in _srgbPrimaries)
        {
            var xyz = Tag(profile, tag);
            if (xyz.Length < 20 || !xyz[..4].SequenceEqual("XYZ "u8)
                || Math.Abs(Fixed(xyz[8..]) - x) > PrimaryTolerance
                || Math.Abs(Fixed(xyz[12..]) - y) > PrimaryTolerance
                || Math.Abs(Fixed(xyz[16..]) - z) > PrimaryTolerance)
            {
                return false;
            }
        }

        foreach (var tag in (ReadOnlySpan<string>)["rTRC", "gTRC", "bTRC"])
        {
            var curve = Tag(profile, tag);
            foreach (var sample in _curveSamples)
            {
                if (Evaluate(curve, sample) is not { } value || Math.Abs(value - Srgb(sample)) > CurveTolerance)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>sRGB's tone curve: encoded value to linear light.</summary>
    public static double Srgb(double encoded) =>
        encoded <= 0.04045 ? encoded / 12.92 : Math.Pow((encoded + 0.055) / 1.055, 2.4);

    // The tag's data, or empty when the profile has no such tag or it does not fit.
    private static ReadOnlySpan<byte> Tag(ReadOnlySpan<byte> profile, string signature)
    {
        var count = BinaryPrimitives.ReadUInt32BigEndian(profile[128..]);
        if (count > (uint)((profile.Length - 132) / 12))
        {
            return default;
        }

        Span<byte> wanted = stackalloc byte[4];
        for (var i = 0; i < 4; i++)
        {
            wanted[i] = (byte)signature[i];
        }

        for (var i = 0; i < (int)count; i++)
        {
            var entry = profile.Slice(132 + (i * 12), 12);
            if (!entry[..4].SequenceEqual(wanted))
            {
                continue;
            }

            var offset = BinaryPrimitives.ReadUInt32BigEndian(entry[4..]);
            var size = BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
            return offset <= profile.Length && size <= profile.Length - offset ? profile.Slice((int)offset, (int)size) : default;
        }

        return default;
    }

    // A 'curv' (identity, a gamma, or a table) or 'para' (an ICC parametric function) curve at x; null if neither.
    private static double? Evaluate(ReadOnlySpan<byte> curve, double x)
    {
        if (curve.Length >= 12 && curve[..4].SequenceEqual("curv"u8))
        {
            var count = BinaryPrimitives.ReadUInt32BigEndian(curve[8..]);
            if (count > (uint)((curve.Length - 12) / 2))
            {
                return null;
            }

            switch (count)
            {
                case 0:
                    return x;
                case 1:
                    return Math.Pow(x, BinaryPrimitives.ReadUInt16BigEndian(curve[12..]) / 256.0);
            }

            var position = x * (count - 1);
            var index = Math.Min((int)position, (int)count - 2);
            var low = BinaryPrimitives.ReadUInt16BigEndian(curve[(12 + (index * 2))..]) / 65535.0;
            var high = BinaryPrimitives.ReadUInt16BigEndian(curve[(14 + (index * 2))..]) / 65535.0;
            return low + ((high - low) * (position - index));
        }

        if (curve.Length >= 16 && curve[..4].SequenceEqual("para"u8))
        {
            var type = BinaryPrimitives.ReadUInt16BigEndian(curve[8..]);
            int[] counts = [1, 3, 4, 5, 7];
            if (type >= counts.Length || curve.Length < 12 + (counts[type] * 4))
            {
                return null;
            }

            var p = new double[7];
            for (var i = 0; i < counts[type]; i++)
            {
                p[i] = Fixed(curve[(12 + (i * 4))..]);
            }

            double Power(double value) => value <= 0 ? 0 : Math.Pow(value, p[0]);
            return type switch
            {
                0 => Power(x),
                1 => x >= -p[2] / p[1] ? Power((p[1] * x) + p[2]) : 0,
                2 => x >= -p[2] / p[1] ? Power((p[1] * x) + p[2]) + p[3] : p[3],
                3 => x >= p[4] ? Power((p[1] * x) + p[2]) : p[3] * x,
                _ => x >= p[4] ? Power((p[1] * x) + p[2]) + p[5] : (p[3] * x) + p[6],
            };
        }

        return null;
    }

    private static double Fixed(ReadOnlySpan<byte> value) => BinaryPrimitives.ReadInt32BigEndian(value) / 65536.0;
}
