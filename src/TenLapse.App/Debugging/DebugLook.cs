using System.Globalization;
using TenLapse.Presentation.Look;

namespace TenLapse.Debugging;

/// <summary>
/// Debug-only switch for the Look panel (#45; this folder is not built in Release): <c>--look=deflicker[,N][,before]</c>
/// turns deflicker on through the panel's view model, as if the switch were clicked, sets the strength to N percent, and
/// with <c>before</c> sets the preview's switch to Before. With <c>--sample-frames=N</c> and <c>--sample-flicker=STOPS</c>
/// (DebugPreview.cs) this shows a flickering sequence being measured and corrected. <c>--look=blend[,photos=N][,blend=S]</c>
/// turns frame blending on (#47) with N photos a frame and S percent strength; both switches can be given together
/// (<c>--look=deflicker,blend,photos=8</c>).
/// </summary>
internal static class DebugLook
{
    /// <summary>Applies the switch to the panel's view model, on the UI thread, while the shell is built.</summary>
    public static void Apply(LookPanelViewModel look)
    {
        var value = Environment.GetCommandLineArgs()
            .FirstOrDefault(arg => arg.StartsWith("--look=", StringComparison.OrdinalIgnoreCase))?["--look=".Length..];
        if (value is null)
        {
            return;
        }

        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Equals("deflicker", StringComparison.OrdinalIgnoreCase))
            {
                look.IsDeflickerOn = true;
            }
            else if (part.Equals("blend", StringComparison.OrdinalIgnoreCase))
            {
                look.IsBlendOn = true;
            }
            else if (part.StartsWith("photos=", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(part["photos=".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var photos))
            {
                look.BlendFrames = photos;
            }
            else if (part.StartsWith("blend=", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(part["blend=".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var blendStrength))
            {
                look.BlendStrength = blendStrength;
            }
            else if (part.Equals("before", StringComparison.OrdinalIgnoreCase))
            {
                look.CompareIndex = 0;
            }
            else if (int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var strength))
            {
                look.Strength = strength;
            }
        }
    }
}
