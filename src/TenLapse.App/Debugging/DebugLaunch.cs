using TenLapse.Core.Projects;
using TenLapse.Presentation;
using TenLapse.Presentation.Services;

namespace TenLapse.Debugging;

/// <summary>
/// Debug-only switches for looking at the shell before import exists (this folder is not built in Release):
/// <c>--sample-frames=N</c> fills the project with N generated photos (DebugPreview.cs, which also has the
/// preview's switches), so the Editing state shows and plays, and <c>--infobar</c> raises #13's skipped-photos InfoBar with its detail list, and a neutral one behind
/// it. <c>--theme=light|dark</c> is the gallery's switch. <c>--crash=command|task|ui|unhandled</c> is in DebugCrash.cs.
/// </summary>
internal static partial class DebugLaunch
{
    private const int MaxSampleFrames = 10_000;

    private static readonly string[] _args = Environment.GetCommandLineArgs();

    /// <summary>Applies the switches to a freshly built shell, on the UI thread.</summary>
    public static void Apply(ShellViewModel shell, IInfoBarService infoBars)
    {
        if (Value("--sample-frames=") is { } text && int.TryParse(text, out var count) && count > 0)
        {
            AddSampleFrames(shell.CurrentProject.Model, Math.Min(count, MaxSampleFrames), infoBars);
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

    private static string? Value(string prefix) =>
        _args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];
}
