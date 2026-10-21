using TenLapse.Core.Projects;

namespace TenLapse.Core.Look;

/// <summary>One photo's measurement, or why it could not be measured.</summary>
public sealed record ExposureMeasurement
{
    private ExposureMeasurement(PhotoExposure? exposure, string? failure)
    {
        Exposure = exposure;
        Failure = failure;
    }

    /// <summary>The measurement; null when <see cref="Failure"/> says why there is none.</summary>
    public PhotoExposure? Exposure { get; }

    /// <summary>What went wrong, for the log (the photo cannot be read, it is gone); null when it was measured.</summary>
    public string? Failure { get; }

    /// <summary>A photo that was measured.</summary>
    public static ExposureMeasurement Measured(PhotoExposure exposure) => new(exposure ?? throw new ArgumentNullException(nameof(exposure)), null);

    /// <summary>A photo that could not be measured.</summary>
    public static ExposureMeasurement Failed(string failure) => new(null, failure ?? throw new ArgumentNullException(nameof(failure)));
}

/// <summary>
/// Measures a photo for deflicker (#45): its brightness (<see cref="LuminanceMeter"/> on a small decode) and its Exif
/// exposure settings. In Core so Media can implement it with the Windows decoders and Presentation can drive it.
/// Safe on any thread; several photos may be measured at once.
/// </summary>
public interface IExposureMeter
{
    /// <summary>Measures <paramref name="frame"/>'s photo. Never throws for the photo: a failure comes back as one. Only a cancellation ends it early.</summary>
    Task<ExposureMeasurement> MeasureAsync(Frame frame, CancellationToken cancellationToken);
}
