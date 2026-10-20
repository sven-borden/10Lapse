// Accessibility scan for #38: the Axe.Windows rules, the engine behind Accessibility Insights for Windows, run on the
// window of a running process. Usage:
//   TenLapse.A11yScan <pid> <output-folder> <screen-name>
// Writes <screen-name>.txt (one line per failure: rule, what it means, the element and its parents) and keeps the
// .a11ytest file Axe writes for a scan with failures (it opens in Accessibility Insights, with a screenshot). Prints
// "<screen-name>: N failures" and exits 0 when the scan ran (whatever it found), 1 with one line when it could not run
// (any exception). Read-only:
// it reads the UI Automation tree and sends no input. See scripts/a11y-check.ps1.
using System.Globalization;
using System.Text;
using Axe.Windows.Automation;
using Axe.Windows.Automation.Data;

if (args.Length != 3 || !int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out var processId))
{
    Console.Error.WriteLine("Usage: TenLapse.A11yScan <pid> <output-folder> <screen-name>");
    return 2;
}

var folder = Path.GetFullPath(args[1]);
var screen = args[2];
Directory.CreateDirectory(folder);

try
{
    var config = Config.Builder.ForProcessId(processId)
        .WithOutputDirectory(folder)
        .WithOutputFileFormat(OutputFileFormat.A11yTest)
        .Build();
    var output = ScannerFactory.CreateScanner(config).Scan(new ScanOptions(screen));

    var report = new StringBuilder();
    var total = 0;
    foreach (var window in output.WindowScanOutputs)
    {
        total += window.ErrorCount;
        foreach (var error in window.Errors)
        {
            report.Append(CultureInfo.InvariantCulture, $"{error.Rule.ID}: {error.Rule.Description}").AppendLine();
            report.Append("    element: ").AppendLine(Describe(error.Element));
            for (var parent = error.Element.Parent; parent is not null; parent = parent.Parent)
            {
                report.Append("    in: ").AppendLine(Describe(parent));
            }

            report.Append("    fix: ").AppendLine(error.Rule.HowToFix);
        }

        if (window.OutputFile.A11yTest is { } file)
        {
            report.Append("a11ytest: ").AppendLine(file);
        }
    }

    report.Insert(0, string.Create(CultureInfo.InvariantCulture, $"{screen}: {total} failures{Environment.NewLine}"));
    File.WriteAllText(Path.Combine(folder, screen + ".txt"), report.ToString());
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{screen}: {total} failures"));
    return 0;
}
catch (Exception exception)
{
    // Whatever stopped it (Axe, a UI Automation element that went away mid-scan, the disk), one line and exit 1: the
    // script records the screen as not scanned instead of a stack trace.
    var message = exception.Message.ReplaceLineEndings(" ");
    Console.Error.WriteLine($"{screen}: the scan could not run: {exception.GetType().Name}: {message}");
    return 1;
}

// "Button 'Export video' [ExportButton]", from the properties Axe recorded.
static string Describe(ElementInfo element)
{
    string Property(string name) => element.Properties.TryGetValue(name, out var value) ? value : string.Empty;
    return $"{Property("ControlType")} '{Property("Name")}' [{Property("AutomationId")}] {Property("BoundingRectangle")}".TrimEnd();
}
