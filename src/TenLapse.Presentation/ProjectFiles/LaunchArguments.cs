using TenLapse.Core.Projects;

namespace TenLapse.Presentation.ProjectFiles;

/// <summary>Reads the project file 10Lapse was started with from its command line (#52).</summary>
public static class LaunchArguments
{
    /// <summary>
    /// The first argument after the program that names a <c>.10lapse</c> file, made full; null when there is none.
    /// Switches (<c>--…</c>, and the notification activator's <c>----AppNotificationActivated:</c>) are not files.
    /// </summary>
    public static string? ProjectFile(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        foreach (var arg in args.Skip(1))
        {
            var path = arg.Trim().Trim('"');
            if (path.StartsWith('-') || !path.EndsWith(ProjectFileJson.Extension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Not a path: look on.
            }
        }

        return null;
    }
}
