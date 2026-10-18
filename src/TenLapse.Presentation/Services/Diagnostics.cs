using System.Globalization;
using System.Text;
using TenLapse.Core;
using TenLapse.Core.Diagnostics;

namespace TenLapse.Presentation.Services;

/// <summary>
/// Adds a section to the text Copy diagnostics puts on the clipboard. The App registers one for the app and
/// Windows, one for the graphics adapters; #9 and #23 add one for the encoder capabilities (which encoders load,
/// hardware or not, HEVC or not) the same way, in <c>App.CreateMainWindow</c>.
/// </summary>
public interface IDiagnosticsContributor
{
    /// <summary>The section's heading ("App", "Graphics").</summary>
    string Title { get; }

    /// <summary>
    /// Adds the section's lines with <see cref="DiagnosticsReport.Add(string, string)"/>. Runs on a thread pool
    /// thread; a failure is caught and noted in the report. Nothing personal beyond what the log holds: no user
    /// name, no machine name, no photo content.
    /// </summary>
    void Contribute(DiagnosticsReport report);
}

/// <summary>The text Copy diagnostics builds: sections of "Name: value" lines.</summary>
public sealed class DiagnosticsReport
{
    private readonly StringBuilder _text = new();

    /// <summary>Starts a section.</summary>
    public void AddSection(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (_text.Length > 0)
        {
            _text.AppendLine();
        }

        _text.Append("## ").AppendLine(title);
    }

    /// <summary>Adds "Name: value".</summary>
    public void Add(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _text.Append(name).Append(": ").AppendLine(value);
    }

    /// <summary>Adds a line as it is.</summary>
    public void AddLine(string line) => _text.AppendLine(line);

    /// <inheritdoc />
    public override string ToString() => _text.ToString();
}

/// <summary>Builds the diagnostics text: every contributor's section, then the last lines of the log.</summary>
public sealed class DiagnosticsCollector
{
    /// <summary>How many log lines the diagnostics end with.</summary>
    public const int LogLineCount = 200;

    /// <summary>What the user's profile folder reads as in the copied text.</summary>
    public const string ProfilePlaceholder = "%USERPROFILE%";

    private readonly IReadOnlyList<IDiagnosticsContributor> _contributors;
    private readonly ILog _log;
    private readonly TimeProvider _clock;
    private readonly string? _profileFolder;

    /// <param name="contributors">The sections, in order.</param>
    /// <param name="log">Whose last <see cref="LogLineCount"/> lines end the text.</param>
    /// <param name="clock">For the time stamp at the top; the system clock by default.</param>
    /// <param name="profileFolder">
    /// The user's profile folder (C:\Users\name), replaced by <see cref="ProfilePlaceholder"/> everywhere in the text
    /// so a pasted report does not carry the account name; the current user's by default.
    /// </param>
    public DiagnosticsCollector(IEnumerable<IDiagnosticsContributor> contributors, ILog log, TimeProvider? clock = null, string? profileFolder = null)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        ArgumentNullException.ThrowIfNull(log);
        _contributors = contributors.ToArray();
        _log = log;
        _clock = clock ?? TimeProvider.System;
        _profileFolder = profileFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>
    /// The diagnostics text, with the profile folder replaced. Never throws for a contributor: its failure becomes a
    /// line in its section.
    /// </summary>
    public string Collect()
    {
        var text = Build();
        return string.IsNullOrEmpty(_profileFolder) || _profileFolder.Length < 4
            ? text
            : text.Replace(_profileFolder.TrimEnd('\\', '/'), ProfilePlaceholder, StringComparison.OrdinalIgnoreCase);
    }

    private string Build()
    {
        var report = new DiagnosticsReport();
        report.AddLine(ProjectText.Format(ProjectText.DiagnosticsHeading, Product.Name));
        report.Add("Collected", _clock.GetLocalNow().ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture));
        foreach (var contributor in _contributors)
        {
            report.AddSection(contributor.Title);
            try
            {
                contributor.Contribute(report);
            }
            catch (Exception exception)
            {
                report.Add("Not available", $"{exception.GetType().Name}: {exception.Message}");
            }
        }

        report.AddSection(ProjectText.Format(ProjectText.DiagnosticsLogSection, LogLineCount));
        var lines = _log.RecentLines(LogLineCount);
        if (lines.Count == 0)
        {
            report.AddLine("(empty)");
        }

        foreach (var line in lines)
        {
            report.AddLine(line);
        }

        return report.ToString();
    }
}

/// <summary>Puts text on the clipboard (the App's runs on the UI thread).</summary>
public interface IClipboardService
{
    /// <summary>Replaces the clipboard's content with <paramref name="text"/>. Throws when the clipboard is busy.</summary>
    void SetText(string text);
}

/// <summary>Copy diagnostics: builds the text off the UI thread, puts it on the clipboard and says so.</summary>
public sealed class DiagnosticsCopier
{
    private readonly DiagnosticsCollector _collector;
    private readonly IClipboardService _clipboard;
    private readonly IInfoBarService _infoBars;

    /// <param name="collector">Builds the text.</param>
    /// <param name="clipboard">Takes it.</param>
    /// <param name="infoBars">Shows <see cref="ProjectText.DiagnosticsCopied"/>.</param>
    public DiagnosticsCopier(DiagnosticsCollector collector, IClipboardService clipboard, IInfoBarService infoBars)
    {
        ArgumentNullException.ThrowIfNull(collector);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(infoBars);
        _collector = collector;
        _clipboard = clipboard;
        _infoBars = infoBars;
    }

    /// <summary>
    /// Copies the diagnostics and shows a Success InfoBar. Call it on the UI thread: the clipboard is set after the
    /// await, back on the caller's context. Nothing leaves the PC; the user pastes it where they choose. Throws
    /// when the clipboard cannot be set (run it through the error path).
    /// </summary>
    public async Task CopyAsync()
    {
        var text = await Task.Run(_collector.Collect);
        _clipboard.SetText(text);
        _infoBars.Show(InfoBarSeverity.Success, ProjectText.DiagnosticsCopied);
    }
}
