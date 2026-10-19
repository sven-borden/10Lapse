using TenLapse.Presentation;

namespace TenLapse.Debugging;

/// <summary>
/// Debug-only switch for looking at the Frame panel's states (this folder is not built in Release):
/// <c>--frame=SHAPE[,fit|fill]</c> picks the shape (<c>match</c>, <c>16:9</c>, <c>9:16</c>, <c>1:1</c>, <c>4:5</c>,
/// <c>4:3</c>) and Fit or Fill through the panel's view model, as if the user had clicked the segments. With
/// <c>--sample-frames=N</c> and <c>--sample-size=WxH</c> (in DebugLaunch.cs) this shows a portrait set in a
/// landscape shape, which raises the 9:16 suggestion.
/// </summary>
internal static class DebugFrame
{
    private static readonly string[] _shapes = ["match", "16:9", "9:16", "1:1", "4:5", "4:3"];

    /// <summary>Applies the switch to the panel's view model, on the UI thread, once the project has its sample frames.</summary>
    public static void Apply(FramePanelViewModel frame)
    {
        var value = Environment.GetCommandLineArgs()
            .FirstOrDefault(arg => arg.StartsWith("--frame=", StringComparison.OrdinalIgnoreCase))?["--frame=".Length..];
        if (value is null)
        {
            return;
        }

        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var shape = Array.IndexOf(_shapes, part.ToLowerInvariant());
            if (shape >= 0)
            {
                frame.SelectedShapeIndex = shape;
            }
            else if (part.Equals("fit", StringComparison.OrdinalIgnoreCase))
            {
                frame.SelectedFitIndex = 0;
            }
            else if (part.Equals("fill", StringComparison.OrdinalIgnoreCase))
            {
                frame.SelectedFitIndex = 1;
            }
        }
    }
}
