using TenLapse.Core.Projects;
using TenLapse.Presentation;
using TenLapse.Presentation.Services;

namespace TenLapse.Debugging;

/// <summary>
/// Debug-only switches for looking at the shell before import exists (this folder is not built in Release):
/// <c>--sample-frames=N</c> fills the project with N frames whose files do not exist, so the Editing state
/// shows, and <c>--infobar</c> raises a sample InfoBar. <c>--theme=light|dark</c> is the gallery's switch.
/// </summary>
internal static class DebugLaunch
{
    private const int MaxSampleFrames = 10_000;

    private static readonly string[] _args = Environment.GetCommandLineArgs();

    /// <summary>Applies the switches to a freshly built shell, on the UI thread.</summary>
    public static void Apply(ShellViewModel shell, IInfoBarService infoBars)
    {
        if (Value("--sample-frames=") is { } text && int.TryParse(text, out var count) && count > 0)
        {
            AddSampleFrames(shell.CurrentProject.Model, Math.Min(count, MaxSampleFrames));
        }

        if (_args.Contains("--infobar", StringComparer.OrdinalIgnoreCase))
        {
            infoBars.Show(
                InfoBarSeverity.Caution,
                "3 photos couldn't be read and were skipped.",
                "Show list",
                () =>
                {
                    // A second InfoBar, so the exit and the next entrance can be watched too.
                    infoBars.Show(InfoBarSeverity.Neutral, "Export cancelled. The unfinished file was removed.");
                    return Task.CompletedTask;
                });
        }
    }

    private static void AddSampleFrames(Project project, int count)
    {
        project.Name = "Harbor sunset";
        var start = new DateTime(2026, 6, 21, 19, 0, 0, DateTimeKind.Utc);
        project.AddFrames(Enumerable.Range(1, count)
            .Select(i => new Frame($@"C:\10Lapse debug samples\Harbor sunset\IMG_{i:0000}.JPG", 4_200_000, start.AddSeconds(5 * i)))
            .ToArray());
    }

    private static string? Value(string prefix) =>
        _args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];
}
