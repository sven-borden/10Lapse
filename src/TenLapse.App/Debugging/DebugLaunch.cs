using TenLapse.Core.Projects;
using TenLapse.Presentation;
using TenLapse.Presentation.Services;

namespace TenLapse.Debugging;

/// <summary>
/// Debug-only switches for looking at the shell before import exists (this folder is not built in Release):
/// <c>--sample-frames=N</c> fills the project with N generated photos (DebugPreview.cs, which also has the
/// preview's switches), so the Editing state shows and plays, and <c>--infobar</c> raises #13's skipped-photos InfoBar
/// with its detail list, and a neutral one behind it. <c>--output=720|1080|1440|2160[,hevc][,standard|maximum]</c> stores
/// those Output settings in the project (to see the Output panel replace what this PC cannot do, with
/// <c>--max-resolution</c> and the other switches in DebugOutputCapabilities.cs). <c>--theme=light|dark</c> is the
/// gallery's switch (it sets the main window's theme too). <c>--frame=</c> is in DebugFrame.cs. <c>--crash=command|task|ui|unhandled</c>
/// is in DebugCrash.cs.
/// </summary>
internal static partial class DebugLaunch
{
    private const int MaxSampleFrames = 10_000;

    private static readonly string[] _args = Environment.GetCommandLineArgs();

    /// <summary>The first-launch drop zone starts in its drag-over look (<c>--drag-over</c>).</summary>
    public static bool DragOver => _args.Contains("--drag-over", StringComparer.OrdinalIgnoreCase);

    /// <summary>The first-launch drop zone acts as with Windows animations off (<c>--reduce-motion</c>).</summary>
    public static bool ReduceMotion => _args.Contains("--reduce-motion", StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The unpackaged Debug app registers for the export's Windows notifications (<c>--notifications</c>). Without it, it
    /// does not: registering unpackaged writes per-exe entries under the user's registry and shows real toasts (#24 review,
    /// M1). With it, closing the app removes those entries again (<c>UnregisterAll</c>).
    /// </summary>
    public static bool Notifications => _args.Contains("--notifications", StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Where the remembered settings live (<c>--settings-folder=PATH</c>: <c>settings.json</c> in that folder), or null for the
    /// app's own folder. A test run points it under its branch folder so it leaves the user's settings alone.
    /// </summary>
    public static string? SettingsFolder => Value("--settings-folder=");

    /// <summary>
    /// The encoder probe's cache file (<c>--encoder-cache=FILE</c>), or null for the app's own. A file that does not exist
    /// makes the probe run cold, as on the first launch of a new build (#90).
    /// </summary>
    public static string? EncoderCacheFile => Value("--encoder-cache=");

    /// <summary>Applies the switches to a freshly built shell, on the UI thread.</summary>
    public static void Apply(ShellViewModel shell, IInfoBarService infoBars)
    {
        if (Value("--sample-frames=") is { } text && int.TryParse(text, out var count) && count > 0)
        {
            AddSampleFrames(shell.CurrentProject.Model, Math.Min(count, MaxSampleFrames), infoBars);
        }

        if (Value("--output=") is { } output)
        {
            ApplyOutput(shell.CurrentProject.Model, output);
        }

        if (_args.Contains("--infobar", StringComparer.OrdinalIgnoreCase))
        {
            // #13's InfoBar: View list / Hide list opens the list inside the bar and keeps it open.
            infoBars.ShowWithDetails(
                InfoBarSeverity.Caution,
                "3 photos couldn't be read and were skipped.",
                [
                    new InfoBarDetail("IMG_4402.HEIC", "Damaged file: it ends early"),
                    new InfoBarDetail("IMG_4417.HEIC", "Empty file (0 bytes)"),
                    new InfoBarDetail("IMG_4630.HEIC", "HEIF image sequence, not a photo"),
                ]);

            // A second one waits behind it, so the exit and the next entrance can be watched too.
            infoBars.Show(InfoBarSeverity.Neutral, "Export cancelled. The unfinished file was removed.");
        }
    }

    private static void ApplyOutput(Project project, string text)
    {
        var settings = project.Output;
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            settings = part.ToLowerInvariant() switch
            {
                "720" => settings with { Resolution = OutputResolution.Hd720 },
                "1080" => settings with { Resolution = OutputResolution.Hd1080 },
                "1440" => settings with { Resolution = OutputResolution.Qhd1440 },
                "2160" => settings with { Resolution = OutputResolution.Uhd2160 },
                "hevc" => settings with { Format = VideoFormat.Mp4Hevc },
                "standard" => settings with { Quality = OutputQuality.Standard },
                "maximum" => settings with { Quality = OutputQuality.Maximum },
                _ => settings,
            };
        }

        project.Output = settings;
    }

    private static string? Value(string prefix) =>
        _args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];
}
