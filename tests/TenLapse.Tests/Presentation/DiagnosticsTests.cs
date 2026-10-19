using TenLapse.Presentation;
using TenLapse.Presentation.Services;

namespace TenLapse.Tests.Presentation;

/// <summary>Copy diagnostics: the sections, the last 200 log lines, the clipboard and the InfoBar.</summary>
public sealed class DiagnosticsTests
{
    [Fact]
    public void TheTextHasEverySectionAndEndsWithTheLastLogLines()
    {
        var log = new FakeLog();
        for (var i = 1; i <= 250; i++)
        {
            log.Write(TenLapse.Core.Diagnostics.LogLevel.Info, $"line {i}");
        }

        var text = new DiagnosticsCollector([new Section("App", "Version", "3.0.0.0"), new Section("Encoders", "H.264", "NVIDIA, hardware")], log, redaction: new DiagnosticsRedaction(@"C:\Users\Sven")).Collect();

        Assert.StartsWith("10Lapse diagnostics", text, StringComparison.Ordinal);
        Assert.Contains("## App\r\nVersion: 3.0.0.0", text.ReplaceLineEndings("\r\n"), StringComparison.Ordinal);
        Assert.Contains("## Encoders\r\nH.264: NVIDIA, hardware", text.ReplaceLineEndings("\r\n"), StringComparison.Ordinal);
        Assert.Contains("## Last 200 log lines", text, StringComparison.Ordinal);
        Assert.Contains("Info line 51" + Environment.NewLine, text, StringComparison.Ordinal);
        Assert.DoesNotContain("Info line 50" + Environment.NewLine, text, StringComparison.Ordinal);
        Assert.EndsWith("Info line 250" + Environment.NewLine, text, StringComparison.Ordinal);
    }

    [Fact]
    public void AContributorThatFailsIsNotedAndTheRestStillCome()
    {
        var text = new DiagnosticsCollector([new Failing(), new Section("Graphics", "Adapter 1", "GPU")], new FakeLog(), redaction: new DiagnosticsRedaction(@"C:\Users\Sven")).Collect();

        Assert.Contains("## Broken", text, StringComparison.Ordinal);
        Assert.Contains("Not available: InvalidOperationException: DXGI is gone", text, StringComparison.Ordinal);
        Assert.Contains("Adapter 1: GPU", text, StringComparison.Ordinal);
        Assert.Contains("(empty)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProfileFolderIsReplacedSoThePasteDoesNotNameTheAccount()
    {
        var log = new FakeLog();
        log.Write(TenLapse.Core.Diagnostics.LogLevel.Error, @"Could not read c:\users\sven\Pictures\Harbor\IMG_4402.JPG");

        var text = new DiagnosticsCollector([], log, redaction: new DiagnosticsRedaction(@"C:\Users\Sven\")).Collect();

        Assert.Contains(@"%USERPROFILE%\Pictures\Harbor\IMG_4402.JPG", text, StringComparison.Ordinal);
        Assert.DoesNotContain("sven", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"Could not read C:\Users\Sven\Pictures\a.jpg", @"Could not read %USERPROFILE%\Pictures\a.jpg")]
    [InlineData(@"Could not read c:/users/sven/Pictures/b.jpg", @"Could not read %USERPROFILE%/Pictures/b.jpg")]
    [InlineData(@"Uri file:///C:/Users/Sven/Pictures/c.jpg failed", @"Uri file:///%USERPROFILE%/Pictures/c.jpg failed")]
    [InlineData(@"Temp C:\Users\SVEN~1\AppData\Local\Temp\x.tmp is locked", @"Temp %USERPROFILE%\AppData\Local\Temp\x.tmp is locked")]
    [InlineData(@"{""path"":""C:\\Users\\Sven\\Pictures\\g.jpg""}", @"{""path"":""%USERPROFILE%\\Pictures\\g.jpg""}")]
    [InlineData(@"Copying D:\Backup\Sven\Harbor\d.jpg", @"Copying D:\Backup\%USERNAME%\Harbor\d.jpg")]
    [InlineData(@"Share \\nas\homes\sven is offline", @"Share \\nas\homes\%USERNAME% is offline")]
    [InlineData(@"Reading C:\Users\Sven\OneDrive - Contoso Ltd\Pictures\e.jpg", @"Reading %USERPROFILE%\OneDrive - <organization>\Pictures\e.jpg")]
    [InlineData(@"Reading E:\OneDrive - Fabrikam\f.jpg", @"Reading E:\OneDrive - <organization>\f.jpg")]
    [InlineData(@"Reading D:\Photos\Svenja\h.jpg", @"Reading D:\Photos\Svenja\h.jpg")]
    public void TheAccountIsTakenOutOfEveryFormOfAPath(string line, string expected)
    {
        var redaction = new DiagnosticsRedaction(@"C:\Users\Sven", "Sven", @"C:\Users\SVEN~1");

        Assert.Equal(expected, redaction.Apply(line));
    }

    [Fact]
    public void AProfileWithASpaceIsRedactedInItsEscapedForm()
    {
        var redaction = new DiagnosticsRedaction(@"C:\Users\Jane Doe", "Jane Doe");

        Assert.Equal("file:///%USERPROFILE%/Pictures/a.jpg", redaction.Apply("file:///C:/Users/Jane%20Doe/Pictures/a.jpg"));
        Assert.Equal(@"E:\Backup\%USERNAME%\a.jpg", redaction.Apply(@"E:\Backup\Jane Doe\a.jpg"));
    }

    [Fact]
    public void TheCollectorRedactsTheContributorsAndTheLog()
    {
        var log = new FakeLog();
        log.Write(TenLapse.Core.Diagnostics.LogLevel.Error, @"Could not open C:\Users\SVEN~1\OneDrive - Contoso\Pictures\Harbor");

        var text = new DiagnosticsCollector([new Section("App", "Data", @"C:/Users/Sven/AppData")], log, redaction: new DiagnosticsRedaction(@"C:\Users\Sven", "Sven", @"C:\Users\SVEN~1")).Collect();

        Assert.DoesNotContain("sven", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Contoso", text, StringComparison.Ordinal);
        Assert.Contains("Data: %USERPROFILE%/AppData", text, StringComparison.Ordinal);
        Assert.Contains(@"%USERPROFILE%\OneDrive - <organization>\Pictures\Harbor", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CopyDiagnosticsPutsTheTextOnTheClipboardAndSaysSo()
    {
        var clipboard = new FakeClipboard();
        var infoBars = new FakeInfoBarService();
        var settings = Settings(clipboard, infoBars, new FakeErrorSink());

        await settings.CopyDiagnosticsCommand.ExecuteAsync(null);

        Assert.StartsWith("10Lapse diagnostics", clipboard.Text, StringComparison.Ordinal);
        var bar = Assert.Single(infoBars.Shown);
        Assert.Equal(InfoBarSeverity.Success, bar.Severity);
        Assert.Equal("Diagnostics copied. Paste them into a GitHub issue or an email.", bar.Message);
    }

    [Fact]
    public async Task ABusyClipboardGoesToTheErrorPathAndTheCommandKeepsWorking()
    {
        var clipboard = new FakeClipboard { Failure = new System.Runtime.InteropServices.COMException("Clipboard busy", unchecked((int)0x800401D0)) };
        var infoBars = new FakeInfoBarService();
        var errors = new FakeErrorSink();
        var settings = Settings(clipboard, infoBars, errors);

        await settings.CopyDiagnosticsCommand.ExecuteAsync(null);

        Assert.Empty(infoBars.Shown);
        Assert.Equal(ProjectText.CopyDiagnosticsFailed, Assert.Single(errors.Shown).Context);

        clipboard.Failure = null;
        await settings.CopyDiagnosticsCommand.ExecuteAsync(null);
        Assert.NotNull(clipboard.Text);
    }

    private static SettingsViewModel Settings(FakeClipboard clipboard, FakeInfoBarService infoBars, IErrorPresenter errors) =>
        new(
            errors,
            new DiagnosticsCopier(new DiagnosticsCollector([new Section("App", "Version", "3.0.0.0")], new FakeLog()), clipboard, infoBars),
            new RememberedSettings(new TenLapse.Core.Settings.MemorySettingsStore(), errors, new FakeLog()),
            infoBars,
            new FakeLauncher());

    private sealed class Section(string title, string name, string value) : IDiagnosticsContributor
    {
        public string Title => title;

        public void Contribute(DiagnosticsReport report) => report.Add(name, value);
    }

    private sealed class Failing : IDiagnosticsContributor
    {
        public string Title => "Broken";

        public void Contribute(DiagnosticsReport report) => throw new InvalidOperationException("DXGI is gone");
    }
}
