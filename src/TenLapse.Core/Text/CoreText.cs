namespace TenLapse.Core.Text;

/// <summary>
/// Core's own user-visible words (#39), in <c>Strings/CoreText.resx</c>: the timing units of
/// <see cref="Timing.TimingWords"/> and <see cref="Projects.Project.DefaultName"/>. Core's exception messages and the
/// settings file's problems are for the log and stay in code, in English.
/// </summary>
internal static class CoreText
{
    /// <summary>The resource file.</summary>
    public static TextResources Strings { get; } = new("TenLapse.Core.Strings.CoreText", typeof(CoreText).Assembly);
}
