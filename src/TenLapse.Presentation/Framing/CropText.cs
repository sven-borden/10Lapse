using System.Globalization;
using TenLapse.Core.Text;

namespace TenLapse.Presentation;

/// <summary>
/// Every user-visible word of crop and pan (#46): its rows in the Frame section and the framing drawn over the preview,
/// as whole format strings read from <c>Strings/CropText.resx</c>; the log contexts stay here in English.
/// </summary>
public static class CropText
{
    private static readonly TextResources _strings = new("TenLapse.Presentation.Strings.CropText", typeof(CropText).Assembly);

    /// <summary>The switch's label and screen reader name.</summary>
    public static string CropLabel => _strings.Get();

    /// <summary>The line under the switch: what crop and pan does.</summary>
    public static string CropHint => _strings.Get();

    /// <summary>The screen reader name of the Video, Start and End switch.</summary>
    public static string ShowName => _strings.Get();

    /// <summary>The switch's first part: the preview shows the video as it will be exported.</summary>
    public static string ShowVideo => _strings.Get();

    /// <summary>The second part: the whole frame, with the start framing over it to adjust.</summary>
    public static string ShowStart => _strings.Get();

    /// <summary>The third part: the whole frame, with the end framing over it to adjust.</summary>
    public static string ShowEnd => _strings.Get();

    /// <summary>The line under the switch while a framing is adjusted: how to adjust it on the preview.</summary>
    public static string EditHint => _strings.Get();

    /// <summary>The label above the zoom slider.</summary>
    public static string ZoomLabel => _strings.Get();

    /// <summary>The zoom slider's screen reader name while the start framing is adjusted.</summary>
    public static string ZoomStartName => _strings.Get();

    /// <summary>The zoom slider's screen reader name while the end framing is adjusted.</summary>
    public static string ZoomEndName => _strings.Get();

    /// <summary>The zoom as a number of times, {0} the number ("1.5×").</summary>
    public static string ZoomValue => _strings.Get();

    /// <summary>The button that gives the framing adjusted the whole frame again.</summary>
    public static string ResetButton => _strings.Get();

    /// <summary>The Reset button's screen reader name.</summary>
    public static string ResetName => _strings.Get();

    /// <summary>The screen reader name of the Smooth and Linear switch.</summary>
    public static string EasingName => _strings.Get();

    /// <summary>The easing that starts and ends gently.</summary>
    public static string EasingSmooth => _strings.Get();

    /// <summary>The easing at one speed.</summary>
    public static string EasingLinear => _strings.Get();

    /// <summary>The screen reader name of the start framing drawn over the preview.</summary>
    public static string FramingStartName => _strings.Get();

    /// <summary>The screen reader name of the end framing drawn over the preview.</summary>
    public static string FramingEndName => _strings.Get();

    /// <summary>The framing's screen reader help: the keys that move and zoom it.</summary>
    public static string FramingHelp => _strings.Get();

    /// <summary>Log context: an edit of crop and pan failed.</summary>
    public static string ChangeFailed => "Changing crop and pan failed.";

    /// <summary>Log context: the crop and pan rows could not follow the project.</summary>
    public static string RefreshFailed => "Showing crop and pan failed.";

    /// <summary>A zoom in the user's culture: "1.5×", "1,5×".</summary>
    public static string Zoom(double zoom) =>
        string.Format(CultureInfo.CurrentCulture, ZoomValue, Math.Round(zoom, 1).ToString("0.0", CultureInfo.CurrentCulture));
}
