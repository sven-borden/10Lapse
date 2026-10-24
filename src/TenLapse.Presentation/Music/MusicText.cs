using System.Globalization;
using TenLapse.Core.Text;
using TenLapse.Core.Timing;

namespace TenLapse.Presentation.Music;

/// <summary>
/// Every user-visible word of the Music section (#50) and of its failures, as whole format strings read from
/// <c>Strings/MusicText.resx</c>; the log contexts stay here in English.
/// </summary>
public static class MusicText
{
    private static readonly TextResources _strings = new("TenLapse.Presentation.Strings.MusicText", typeof(MusicText).Assembly);

    /// <summary>The section's heading, also its tab in a narrow window.</summary>
    public static string Heading => _strings.Get();

    /// <summary>The line under the heading while there is no music: what the section does.</summary>
    public static string EmptyHint => _strings.Get();

    /// <summary>The button that chooses a song.</summary>
    public static string AddButton => _strings.Get();

    /// <summary>The Add button's screen reader name.</summary>
    public static string AddName => _strings.Get();

    /// <summary>The button that chooses another song in place of this one.</summary>
    public static string ReplaceButton => _strings.Get();

    /// <summary>The Replace button's screen reader name.</summary>
    public static string ReplaceName => _strings.Get();

    /// <summary>The button that takes the music out of the project (never off the disk).</summary>
    public static string RemoveButton => _strings.Get();

    /// <summary>The Remove button's screen reader name.</summary>
    public static string RemoveName => _strings.Get();

    /// <summary>The song's screen reader name: {0} the file's name, {1} its length.</summary>
    public static string SongName => _strings.Get();

    /// <summary>The label above the start slider.</summary>
    public static string StartLabel => _strings.Get();

    /// <summary>The start slider's screen reader name.</summary>
    public static string StartName => _strings.Get();

    /// <summary>The label above the fade in slider.</summary>
    public static string FadeInLabel => _strings.Get();

    /// <summary>The label above the fade out slider.</summary>
    public static string FadeOutLabel => _strings.Get();

    /// <summary>The fade in slider's screen reader name.</summary>
    public static string FadeInName => _strings.Get();

    /// <summary>The fade out slider's screen reader name.</summary>
    public static string FadeOutName => _strings.Get();

    /// <summary>How the song fits a video it outlasts.</summary>
    public static string FitsVideo => _strings.Get();

    /// <summary>How the song fits a video it ends before: {0} how much earlier.</summary>
    public static string EndsEarly => _strings.Get();

    /// <summary>A start at or past the song's end.</summary>
    public static string StartsAfterEnd => _strings.Get();

    /// <summary>Under the controls while the output is a GIF.</summary>
    public static string GifSilent => _strings.Get();

    /// <summary>The song's file is not where the project says: {0} its name.</summary>
    public static string Missing => _strings.Get();

    /// <summary>The InfoBar for a chosen file that does not play: {0} its name.</summary>
    public static string Unreadable => _strings.Get();

    /// <summary>The InfoBar for an export whose music could not be read.</summary>
    public static string ExportFailed => _strings.Get();

    /// <summary>Log context: an edit of the music failed.</summary>
    public static string ChangeFailed => "Changing the music failed.";

    /// <summary>Log context: the section could not follow the project.</summary>
    public static string RefreshFailed => "Showing the music failed.";

    /// <summary>Log context: choosing a song failed.</summary>
    public static string ChooseFailed => "Choosing the music failed.";

    /// <summary>Log context: the preview's music could not follow the player.</summary>
    public static string PreviewFailed => "Playing the music in the preview failed.";

    /// <summary>Log: a chosen file did not play.</summary>
    public static string UnreadableLog(string path, string reason) => $"The music file {path} cannot be played: {reason}";

    /// <summary>"Song.mp3, 3 min 42 s" for the screen reader.</summary>
    public static string Song(string fileName, TimeSpan length) =>
        string.Format(CultureInfo.CurrentCulture, SongName, fileName, Length(length));

    /// <summary>A length or a point in the song: "12.5 s", "3 min 42 s".</summary>
    public static string Length(TimeSpan length) => TimingFormat.Duration(Math.Max(0, length.Ticks));

    /// <summary>A fade: "0 s", "1.5 s" (in tenths).</summary>
    public static string Fade(TimeSpan fade)
    {
        var tenths = (long)Math.Round(fade.TotalSeconds * 10, MidpointRounding.AwayFromZero);
        return tenths <= 0 ? TimingFormat.WholeSeconds(0) : TimingFormat.Seconds(new Fraction(tenths, 10));
    }

    /// <summary>"The song ends 6.2 s before the video does…".</summary>
    public static string EndsEarlyBy(TimeSpan early) => string.Format(CultureInfo.CurrentCulture, EndsEarly, Length(early));

    /// <summary>"Song.mp3 can't be found…".</summary>
    public static string MissingFile(string fileName) => string.Format(CultureInfo.CurrentCulture, Missing, fileName);

    /// <summary>"Song.mp3 can't be played…".</summary>
    public static string UnreadableFile(string fileName) => string.Format(CultureInfo.CurrentCulture, Unreadable, fileName);
}
