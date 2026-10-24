using TenLapse.Presentation;

namespace TenLapse.Debugging;

/// <summary>
/// Debug-only switches for looking at the Timing panel's states (this folder is not built in Release):
/// <c>--timing=fps|length|photo</c> picks the mode, and <c>--timing-value=TEXT</c> then types TEXT into the box and
/// takes it, as if the user had pressed Enter ("29,97", "9999", "fast", "37.4829", "1:30"). The text goes through
/// the panel's view model like a typed entry, so its sentences show.
/// <c>--sequence=boomerang,plays3,start1.5,end2</c> (#49) sets the direction, the plays and the holds the same way (each
/// part optional; a hold is typed into its box, so "start1,5" and "end12" show their sentences).
/// </summary>
internal static class DebugTiming
{
    /// <summary>Applies the switches to the panel's view model, on the UI thread, once the project has its sample frames.</summary>
    public static void Apply(TimingPanelViewModel timing)
    {
        var args = Environment.GetCommandLineArgs();
        string? Value(string prefix) =>
            args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

        switch (Value("--timing=")?.ToLowerInvariant())
        {
            case "fps":
                timing.SelectedModeIndex = 0;
                break;
            case "length":
                timing.SelectedModeIndex = 1;
                break;
            case "photo":
                timing.SelectedModeIndex = 2;
                break;
        }

        if (Value("--timing-value=") is { } text)
        {
            timing.CommitValue(text);
        }

        foreach (var part in (Value("--sequence=") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Equals("boomerang", StringComparison.OrdinalIgnoreCase))
            {
                timing.SelectedDirectionIndex = 1;
            }
            else if (part.StartsWith("plays", StringComparison.OrdinalIgnoreCase) && int.TryParse(part[5..], out var plays))
            {
                timing.SelectedPlaysIndex = plays - 1;
            }
            else if (part.StartsWith("start", StringComparison.OrdinalIgnoreCase))
            {
                timing.CommitHoldStart(part[5..]);
            }
            else if (part.StartsWith("end", StringComparison.OrdinalIgnoreCase))
            {
                timing.CommitHoldEnd(part[3..]);
            }
        }
    }
}
