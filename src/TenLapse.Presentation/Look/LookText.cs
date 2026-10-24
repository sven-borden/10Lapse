using System.Globalization;
using TenLapse.Core.Text;

namespace TenLapse.Presentation.Look;

/// <summary>
/// Every user-visible word of the Look panel (#45 deflicker, #47 frame blending) and the preview's Before and After switch,
/// as whole format strings read from <c>Strings/LookText.resx</c>; the log contexts stay here in English.
/// </summary>
public static class LookText
{
    private static readonly TextResources _strings = new("TenLapse.Presentation.Strings.LookText", typeof(LookText).Assembly);

    /// <summary>The panel's heading, also its tab in a narrow window.</summary>
    public static string Heading => _strings.Get();

    /// <summary>The switch's label and screen reader name.</summary>
    public static string DeflickerLabel => _strings.Get();

    /// <summary>The line under the switch: what deflicker does.</summary>
    public static string DeflickerHint => _strings.Get();

    /// <summary>The label above the strength slider.</summary>
    public static string StrengthLabel => _strings.Get();

    /// <summary>The strength slider's screen reader name.</summary>
    public static string StrengthName => _strings.Get();

    /// <summary>The measuring progress, counted by all the photos: {0} measured, {1} all.</summary>
    public static PluralText Measuring => _strings.Plural();

    /// <summary>The screen reader name of Cancel beside the measuring progress.</summary>
    public static string MeasureCancelName => _strings.Get();

    /// <summary>The InfoBar for photos that could not be measured: {0} their count.</summary>
    public static PluralText MeasureFailed => _strings.Plural();

    /// <summary>"3 photos not measured" beside Measure: {0} their count.</summary>
    public static PluralText NotMeasured => _strings.Plural();

    /// <summary>The button that measures the photos not measured yet.</summary>
    public static string MeasureButton => _strings.Get();

    /// <summary>The Measure button's screen reader name.</summary>
    public static string MeasureName => _strings.Get();

    /// <summary>The screen reader name of the Before and After switch.</summary>
    public static string CompareName => _strings.Get();

    /// <summary>The switch's first part: the photos without deflicker.</summary>
    public static string CompareBefore => _strings.Get();

    /// <summary>The switch's second part: the photos with deflicker.</summary>
    public static string CompareAfter => _strings.Get();

    /// <summary>Frame blending's switch label and screen reader name (#47).</summary>
    public static string BlendLabel => _strings.Get();

    /// <summary>The line under Frame blending: what it does.</summary>
    public static string BlendHint => _strings.Get();

    /// <summary>The label above the slider of how many photos a frame blends.</summary>
    public static string BlendFramesLabel => _strings.Get();

    /// <summary>That slider's screen reader name.</summary>
    public static string BlendFramesName => _strings.Get();

    /// <summary>The screen reader name of frame blending's strength slider (its label is <see cref="StrengthLabel"/>).</summary>
    public static string BlendStrengthName => _strings.Get();

    /// <summary>Log context: an edit of the look (the switch, the strength) failed.</summary>
    public static string ChangeFailed => "Changing the look failed.";

    /// <summary>Log context: the panel could not follow the project.</summary>
    public static string RefreshFailed => "Showing the look failed.";

    /// <summary>Log context: measuring the photos for deflicker failed as a whole (not one photo).</summary>
    public static string MeasureFailedContext => "Measuring the photos for deflicker failed.";

    /// <summary>"Measuring 1,240 of 5,000 photos…" in the user's culture.</summary>
    public static string MeasuringProgress(int measured, int total)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(measured);
        ArgumentOutOfRangeException.ThrowIfLessThan(total, measured);
        return Measuring.Format(CultureInfo.CurrentCulture, total, Count(measured), Count(total));
    }

    /// <summary>"3 photos couldn't be measured, so deflicker takes their correction from the photos beside them. Check …"</summary>
    public static string MeasureFailedMessage(int count) =>
        MeasureFailed.Format(CultureInfo.CurrentCulture, count, Count(count));

    /// <summary>"3 photos not measured" in the user's culture.</summary>
    public static string NotMeasuredMessage(int count) =>
        NotMeasured.Format(CultureInfo.CurrentCulture, count, Count(count));

    /// <summary>The strength as the slider's value reads: "80%" in English, "80 %" in French, in the user's culture.</summary>
    public static string Percent(int strength) => (strength / 100.0).ToString("P0", CultureInfo.CurrentCulture);

    /// <summary>A count as a slider's value reads it ("16"), in the user's culture.</summary>
    public static string Count(int count) => count.ToString("N0", CultureInfo.CurrentCulture);
}
