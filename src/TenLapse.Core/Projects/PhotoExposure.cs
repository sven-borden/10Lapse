namespace TenLapse.Core.Projects;

/// <summary>
/// What the exposure pass (#45) learned about one photo: how bright the picture is, and the camera's exposure settings
/// when its Exif block has them. It belongs to the photo, like <see cref="Frame.PixelSize"/>: it travels with the frame
/// through reorders, removals and undo, and the project file keeps it so a reopened project is not measured again.
/// </summary>
/// <remarks>
/// <see cref="Luminance"/> is in stops (EV): the mean of log2 of the linear luminance over the middle 80 % of the pixels
/// of a small decode (<see cref="Look.LuminanceMeter"/>). A gain of n stops applied in linear light raises it by n, which is
/// what lets the curve aim at a brightness and hit it.
/// </remarks>
public sealed record PhotoExposure
{
    /// <summary>The darkest luminance a photo can measure, in stops (a black picture).</summary>
    public const double MinLuminance = -16;

    /// <summary>Creates a measurement.</summary>
    /// <param name="luminance">The picture's brightness in stops, <see cref="MinLuminance"/> to 0 (white).</param>
    public PhotoExposure(double luminance)
    {
        Luminance = luminance;
    }

    /// <summary>The picture's brightness in stops, from <see cref="MinLuminance"/> (black) to 0 (white).</summary>
    public double Luminance
    {
        get;
        init => field = double.IsFinite(value) && value >= MinLuminance && value <= 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "A luminance is from -16 to 0 stops.");
    }

    /// <summary>The shutter time in seconds (Exif <c>ExposureTime</c>), when the photo has it.</summary>
    public double? ExposureTime
    {
        get;
        init => field = Positive(value);
    }

    /// <summary>The aperture's f-number (Exif <c>FNumber</c>), when the photo has it.</summary>
    public double? FNumber
    {
        get;
        init => field = Positive(value);
    }

    /// <summary>The ISO sensitivity (Exif <c>PhotographicSensitivity</c>, or <c>ISOSpeed</c> past 65,535), when the photo has it.</summary>
    public double? Iso
    {
        get;
        init => field = Positive(value);
    }

    private static double? Positive(double? value) =>
        value is null || (double.IsFinite(value.Value) && value.Value > 0)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "An exposure setting is a positive number.");
}
