using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TenLapse.Presentation.Export;
using TenLapse.Presentation.Keyboard;
using TenLapse.Presentation.Services;
using TenLapse.Services;
using TenLapse.Shell;

namespace TenLapse.Debugging;

/// <summary>
/// The focus walk (#38): <c>--focus-walk</c> writes <c>focus.log</c> in the log folder (<c>--log-folder=</c>), or
/// <c>--focus-walk=PATH</c>. Once a screen has settled after a change (Empty, Editing, Settings; the export panel and the
/// finished state; wide or narrow and the panel's tab; an InfoBar or a dialog opening or closing), it records where the
/// keyboard focus landed, then walks the Tab order with <see cref="FocusManager.TryMoveFocus(FocusNavigationDirection, FindNextElementOptions)"/>
/// (what Tab does) until it comes round again, one line per stop with its UI Automation name, control type, accelerator key
/// and size, and puts the focus back where it was. A stop with no name is marked UNNAMED, one that is not on screen
/// HIDDEN. Every UIA notification the app raises (<see cref="Announcer"/>) is logged too. It proves the order and the focus
/// moves without real input; scripts/a11y-check.ps1 runs it. Not built in Release.
/// </summary>
internal static class DebugFocusWalk
{
    private const int MaxStops = 120;
    private static readonly Lock _gate = new();
    private static readonly long _start = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
    private static readonly string? _path = FindPath();

    // Held, or the collector may take the timer.
    private static DispatcherQueueTimer? _timer;
    private static string _walked = string.Empty;
    private static string _seen = string.Empty;
    private static DateTime _seenAt;

    /// <summary>Whether the switch is given.</summary>
    public static bool IsOn => _path is not null;

    /// <summary>Starts watching the shell's screen (UI thread, once the shell is built).</summary>
    public static void Apply(ShellView shell, IErrorSink log)
    {
        ApplyShortcuts(shell, log);
        if (_path is null)
        {
            return;
        }

        File.WriteAllText(_path, string.Empty);
        _timer = shell.DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => log.RunLogged(() => Check(shell), "The Debug focus walk failed.");
        _timer.Start();
    }

    // --shortcut-after=ctrl+n@8,ctrl+o@3: at each of those seconds after launch, runs that shortcut through the same method
    // its keyboard accelerator calls (ShellView.RunShortcut), and notes whether it applied on the screen of the moment. It
    // proves what each shortcut does without real key presses; the key reaching the accelerator is the framework's part.
    private static readonly List<DispatcherQueueTimer> _shortcutTimers = [];

    private static void ApplyShortcuts(ShellView shell, IErrorSink log)
    {
        var given = Environment.GetCommandLineArgs().FirstOrDefault(arg => arg.StartsWith("--shortcut-after=", StringComparison.OrdinalIgnoreCase));
        if (given is null)
        {
            return;
        }

        foreach (var entry in given["--shortcut-after=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split('@');
            ShellShortcut? shortcut = parts[0].ToUpperInvariant() switch
            {
                "CTRL+O" => ShellShortcut.ChooseFolder,
                "CTRL+SHIFT+O" => ShellShortcut.ChoosePhotos,
                "CTRL+N" => ShellShortcut.NewProject,
                "CTRL+E" => ShellShortcut.Export,
                _ => null,
            };
            if (shortcut is not { } chosen || parts.Length != 2
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                continue;
            }

            var timer = shell.DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(seconds);
            timer.IsRepeating = false;
            timer.Tick += (_, _) => log.RunLogged(
                () => Note($"shortcut {parts[0]} ({chosen}): {(shell.RunShortcut(chosen) ? "applied" : "not available here, nothing done")}"),
                "The Debug shortcut switch failed.");
            _shortcutTimers.Add(timer);
            timer.Start();
        }
    }

    /// <summary>Writes one line (any thread). A failure to write is dropped: this is a Debug aid.</summary>
    public static void Note(string text)
    {
        if (_path is null)
        {
            return;
        }

        var elapsed = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - _start).TotalMilliseconds;
        var line = string.Create(CultureInfo.InvariantCulture, $"{elapsed,9:0} {text}{Environment.NewLine}");
        lock (_gate)
        {
            try
            {
                File.AppendAllText(_path, line);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    // A screen is walked once it has looked the same for a second (motion done, focus placed).
    private static void Check(ShellView shell)
    {
        if (shell.XamlRoot is not { } root)
        {
            return;
        }

        var screen = Screen(shell, root);
        var now = DateTime.UtcNow;
        if (!string.Equals(screen, _seen, StringComparison.Ordinal))
        {
            _seen = screen;
            _seenAt = now;
            return;
        }

        if (string.Equals(screen, _walked, StringComparison.Ordinal) || now - _seenAt < TimeSpan.FromSeconds(1))
        {
            return;
        }

        _walked = screen;
        Walk(screen, root);
    }

    private static string Screen(ShellView shell, XamlRoot root)
    {
        var view = shell.ViewModel;
        var name = view.IsSettingsOpen ? "Settings" : view.ShowsEmpty ? "Empty" : "Editing";
        if (shell.Export.State != ExportPanelState.Editing)
        {
            name += " " + shell.Export.State;
        }

        name += shell.Layout.IsNarrow ? string.Create(CultureInfo.InvariantCulture, $" narrow({shell.Layout.Tab})") : " wide";
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            name += popup.Child switch
            {
                ContentDialog => " +dialog",
                ToolTip or null => string.Empty,
                var child => " +popup(" + child.GetType().Name + ")",
            };
        }

        if (shell.FindName("ShellInfoBar") is InfoBar { IsOpen: true } bar)
        {
            name += " +infobar(" + bar.Severity + ")";
        }

        return name;
    }

    private static void Walk(string screen, XamlRoot root)
    {
        var start = FocusManager.GetFocusedElement(root) as DependencyObject;
        var text = new StringBuilder();
        text.Append("screen: ").AppendLine(screen);
        text.Append("  focus landed on: ").AppendLine(Describe(start));
        // From the first stop of the window (or of the open dialog, which keeps Tab inside it) to the last, where Tab
        // leaves the content for the title bar and comes round to the first again.
        var dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Select(popup => popup.Child).OfType<ContentDialog>().FirstOrDefault();
        var scope = (DependencyObject?)dialog ?? root.Content;
        // Searched from the window's content in both cases: a dialog's popup keeps Tab inside it anyway.
        var options = new FindNextElementOptions { SearchRoot = root.Content };
        var stops = new List<DependencyObject>();
        if (FocusManager.FindFirstFocusableElement(scope) is Control first && first.Focus(FocusState.Keyboard)
            && FocusManager.GetFocusedElement(root) is DependencyObject firstFocused)
        {
            stops.Add(firstFocused);
            text.Append("   1. ").AppendLine(Describe(firstFocused));
            for (var i = 1; i < MaxStops; i++)
            {
                if (!FocusManager.TryMoveFocus(FocusNavigationDirection.Next, options)
                    || FocusManager.GetFocusedElement(root) is not DependencyObject focused)
                {
                    text.AppendLine("  (end: Tab goes round to 1)");
                    break;
                }

                if (stops.Any(stop => ReferenceEquals(stop, focused)))
                {
                    text.Append("  (round again at ").Append(Describe(focused)).AppendLine(")");
                    break;
                }

                stops.Add(focused);
                text.Append(CultureInfo.InvariantCulture, $"  {stops.Count,2}. ").AppendLine(Describe(focused));
            }
        }
        else
        {
            text.AppendLine("  (nothing focusable)");
        }

        // Back where it was: a control takes it again; with nothing focused before, the walk leaves the last stop.
        if (start is Control control)
        {
            control.Focus(FocusState.Programmatic);
        }
        else
        {
            text.AppendLine("  (focus not restored: nothing to put it back on)");
        }

        Note(text.ToString().TrimEnd());
    }

    // "Button 'Export video' [ExportButton] key=Control+E 94x32", with UNNAMED or HIDDEN when the stop is wrong.
    internal static string Describe(DependencyObject? node)
    {
        if (node is null)
        {
            return "(nothing)";
        }

        var peer = node is UIElement element ? FrameworkElementAutomationPeer.FromElement(element) ?? FrameworkElementAutomationPeer.CreatePeerForElement(element) : null;
        var name = peer?.GetName() ?? string.Empty;
        var type = peer?.GetLocalizedControlType() ?? node.GetType().Name;
        var id = node is FrameworkElement { Name.Length: > 0 } named ? " [" + named.Name + "]" : string.Empty;
        var key = peer?.GetAcceleratorKey() is { Length: > 0 } accelerator ? " key=" + accelerator : string.Empty;
        var help = node is UIElement { } helped && AutomationProperties.GetHelpText(helped) is { Length: > 0 } helpText ? " help=\"" + helpText + "\"" : string.Empty;
        var size = node is FrameworkElement frame ? string.Create(CultureInfo.InvariantCulture, $" {frame.ActualWidth:0}x{frame.ActualHeight:0}") : string.Empty;
        var flags = string.Empty;
        if (name.Length == 0)
        {
            flags += " UNNAMED";
        }

        if (!OnScreen(node))
        {
            flags += " HIDDEN";
        }

        return $"{type} '{name}'{id}{key}{help}{size}{flags}";
    }

    // Has a size, and neither it nor a parent is collapsed or transparent.
    private static bool OnScreen(DependencyObject node)
    {
        if (node is FrameworkElement { ActualWidth: <= 0 } or FrameworkElement { ActualHeight: <= 0 })
        {
            return false;
        }

        for (var current = node; current is not null; current = VisualTreeHelper.GetParent(current) ?? (current as FrameworkElement)?.Parent)
        {
            if (current is UIElement { Visibility: Visibility.Collapsed } or UIElement { Opacity: <= 0 })
            {
                return false;
            }

            if (current is Popup)
            {
                break;
            }
        }

        return true;
    }

    private static string? FindPath()
    {
        var args = Environment.GetCommandLineArgs();
        if (args.FirstOrDefault(arg => arg.StartsWith("--focus-walk=", StringComparison.OrdinalIgnoreCase)) is { } given)
        {
            return Path.GetFullPath(given["--focus-walk=".Length..]);
        }

        if (!args.Contains("--focus-walk", StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var folder = DebugLaunch.LogFolder ?? AppEnvironment.LogFolder();
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "focus.log");
    }
}
