using System.Globalization;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;

namespace TenLapse.Presentation;

/// <summary>
/// What the Output panel (#23) offers for one project state, and the settings an export must use: the stored
/// <see cref="Project.Output"/> is the user's preference, never overwritten by a correction, and the
/// <see cref="Effective"/> output is that preference made to fit the current frame shape, frame rate and
/// <see cref="IOutputCapabilities"/>: the format if it is offered, otherwise H.264, and the largest offered
/// resolution not above the preference (the smallest offered one when all are above). It is a pure function of a
/// snapshot and an answer, so when the shape or the capabilities change back, the preference returns by itself.
/// The export (#24) calls <see cref="Resolve"/> with the project's snapshot, or reads
/// <see cref="OutputPanelViewModel.EffectiveOutput"/>, and exports with <see cref="Effective"/>, never with the
/// raw stored settings.
/// </summary>
/// <param name="Preference">The stored settings.</param>
/// <param name="Effective">What to export and to show as selected; null when no format or no resolution is offered at all.</param>
/// <param name="Formats">The formats offered, H.264 first.</param>
/// <param name="Resolutions">The resolutions offered for the effective format, smallest first.</param>
/// <param name="Notice">Words naming each replacement and why (this PC, or this shape); empty when nothing was replaced.</param>
/// <param name="Aspect">The frame shape the answer was computed for (Match photos with no sizes read yet is 16:9).</param>
/// <param name="PhotoSize">Match photos: the most common photo size.</param>
/// <param name="Rate">The rate the clip plays at.</param>
/// <param name="ShapeLimits">True when nothing is offered for this shape although the same PC would export 16:9 (Match photos with an extreme ratio).</param>
public sealed record OutputPlan(
    OutputSettings Preference,
    OutputSettings? Effective,
    IReadOnlyList<VideoFormat> Formats,
    IReadOnlyList<OutputResolution> Resolutions,
    string Notice,
    FrameAspect Aspect,
    PixelSize? PhotoSize,
    Fraction Rate,
    bool ShapeLimits)
{
    private static readonly OutputResolution[] _allResolutions = [.. Enum.GetValues<OutputResolution>().OrderBy(resolution => (int)resolution)];
    private static readonly VideoFormat[] _allFormats = [VideoFormat.Mp4H264, VideoFormat.Mp4Hevc];

    /// <summary>True when the effective output differs from the stored preference.</summary>
    public bool IsReplacing => Effective is { } effective && effective != Preference;

    /// <summary>Works out the plan for a snapshot and an answer about this PC. Does not write anything.</summary>
    /// <param name="snapshot">The project's current state; its <see cref="ProjectSnapshot.Output"/> is the preference.</param>
    /// <param name="capabilities">What this PC can encode (the real answer, or a provisional one).</param>
    public static OutputPlan Resolve(ProjectSnapshot snapshot, IOutputCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var (aspect, photo, rate) = ShapeOf(snapshot);
        var preference = snapshot.Output;
        var formats = _allFormats.Where(capabilities.IsAvailable).ToList();
        var effective = preference;
        var notices = new List<string>();
        if (formats.Count > 0 && !formats.Contains(preference.Format))
        {
            var replacement = formats.Contains(VideoFormat.Mp4H264) ? VideoFormat.Mp4H264 : formats[0];
            notices.Add(string.Format(CultureInfo.CurrentCulture, OutputText.FormatReplaced, OutputText.CodecName(preference.Format), OutputText.CodecName(replacement)));
            effective = effective with { Format = replacement };
        }

        var offered = _allResolutions.Where(resolution => capabilities.Supports(effective.Format, resolution, aspect, rate, photo)).ToList();
        if (offered.Count > 0 && !offered.Contains(effective.Resolution))
        {
            var below = offered.Where(resolution => resolution < effective.Resolution).ToList();
            var replacement = below.Count > 0 ? below[^1] : offered[0];

            // The shape is the reason when the same size would be fine at 16:9 (the shape the sizes are quoted for).
            var byShape = aspect != FrameAspect.Ratio16x9
                && capabilities.Supports(effective.Format, effective.Resolution, FrameAspect.Ratio16x9, rate, null);
            notices.Add(string.Format(
                CultureInfo.CurrentCulture,
                byShape ? OutputText.ResolutionReplacedByShape : OutputText.ResolutionReplaced,
                OutputText.ResolutionName(effective.Resolution),
                OutputText.ResolutionName(replacement)));
            effective = effective with { Resolution = replacement };
        }

        var canExport = formats.Count > 0 && offered.Count > 0;

        // Nothing fits this shape (a 10:1 panorama under Match photos) although a plain 16:9 would: the shape is why.
        var shapeLimits = !canExport && formats.Count > 0 && aspect != FrameAspect.Ratio16x9
            && _allResolutions.Any(resolution => capabilities.Supports(effective.Format, resolution, FrameAspect.Ratio16x9, rate, null));
        return new OutputPlan(preference, canExport ? effective : null, formats, offered, string.Join(' ', notices), aspect, photo, rate, shapeLimits);
    }

    // Match photos takes the shape of the most common photo; with no sizes read yet it is 16:9, as the video is.
    internal static (FrameAspect Aspect, PixelSize? PhotoSize, Fraction Rate) ShapeOf(ProjectSnapshot snapshot)
    {
        var aspect = snapshot.FrameSettings.Aspect;
        PixelSize? photo = null;
        if (aspect == FrameAspect.MatchPhotos)
        {
            photo = OutputSize.MostCommonPhotoSize(snapshot.Frames);
            if (photo is null)
            {
                aspect = FrameAspect.Ratio16x9;
            }
        }

        return (aspect, photo, snapshot.EffectiveRate.Rate);
    }
}
