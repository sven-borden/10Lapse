using System.Runtime.InteropServices;
using TenLapse.Core.Diagnostics;
using TenLapse.Media.Export;
using TenLapse.Presentation;
using TenLapse.Presentation.Services;
using TenLapse.Services;

namespace TenLapse.Tests.Presentation;

/// <summary>The one error path (#26): every command failure is logged and becomes an InfoBar, and the app goes on.</summary>
public sealed class ErrorPathTests
{
    private readonly FakeLog _log = new();
    private readonly FakeInfoBarService _infoBars = new();
    private int _diagnosticsCopied;

    [Theory]
    [InlineData("file named")]
    [InlineData("file")]
    [InlineData("folder")]
    [InlineData("drive")]
    [InlineData("access")]
    [InlineData("access hresult")]
    [InlineData("disk full")]
    [InlineData("handle disk full")]
    [InlineData("in use")]
    [InlineData("memory")]
    [InlineData("memory hresult")]
    [InlineData("path too long")]
    [InlineData("export")]
    [InlineData("aggregate")]
    [InlineData("inner")]
    [InlineData("unexpected")]
    public async Task ACommandThatThrowsShowsWhatHappenedAndKeepsWorking(string kind)
    {
        var (exception, expected, offersDiagnostics) = Case(kind);
        var errors = Presenter();
        var runs = 0;
        var command = errors.AsyncCommand(
            () =>
            {
                runs++;
                return runs == 1 ? throw exception : Task.CompletedTask;
            },
            "Testing the error path.");

        var failure = await Record.ExceptionAsync(() => command.ExecuteAsync(null));

        Assert.Null(failure);
        var bar = Assert.Single(_infoBars.Shown);
        Assert.Equal(InfoBarSeverity.Critical, bar.Severity);
        Assert.Equal(expected, bar.Message);
        Assert.Equal(offersDiagnostics ? ProjectText.CopyDiagnostics : null, bar.ActionLabel);
        var entry = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("Testing the error path.", entry.Message);
        Assert.Same(exception, entry.Exception);

        // The command is not stuck: it can run again, and this time it works.
        Assert.True(command.CanExecute(null));
        await command.ExecuteAsync(null);
        Assert.Equal(2, runs);
        Assert.Single(_infoBars.Shown);
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("task")]
    [InlineData("aggregate")]
    public async Task ACancellationIsNotAnErrorAndShowsNothing(string kind)
    {
        Exception exception = kind switch
        {
            "operation" => new OperationCanceledException(),
            "task" => new TaskCanceledException(),
            _ => new AggregateException(new OperationCanceledException(), new TaskCanceledException()),
        };
        var command = Presenter().AsyncCommand(() => throw exception, "Exporting.");

        await command.ExecuteAsync(null);

        Assert.Empty(_infoBars.Shown);
        var entry = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Info, entry.Level);
        Assert.Contains("Exporting.", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellingACancellableCommandShowsNothing()
    {
        var started = new TaskCompletionSource();
        var command = Presenter().AsyncCommand(
            async token =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            },
            "Exporting.");

        var running = command.ExecuteAsync(null);
        await started.Task;
        command.Cancel();
        await running;

        Assert.Empty(_infoBars.Shown);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task TheGenericInfoBarsActionCopiesTheDiagnostics()
    {
        await Presenter().AsyncCommand(() => throw new InvalidOperationException("bug"), "Testing.").ExecuteAsync(null);

        var bar = Assert.Single(_infoBars.Shown);
        await bar.Action!();
        Assert.Equal(1, _diagnosticsCopied);
    }

    [Fact]
    public void WithoutACopyActionTheGenericInfoBarHasNoButton()
    {
        new ErrorPresenter(_log, _infoBars).Show(new InvalidOperationException("bug"), "Testing.");

        var bar = Assert.Single(_infoBars.Shown);
        Assert.Equal(ProjectText.ErrorUnexpected, bar.Message);
        Assert.Null(bar.ActionLabel);
    }

    [Fact]
    public void AReportIsLoggedButNotShown()
    {
        Presenter().Report(new InvalidOperationException("dispose"), ProjectText.ClosingProjectFailed);

        Assert.Empty(_infoBars.Shown);
        var entry = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal(ProjectText.ClosingProjectFailed, entry.Message);
    }

    [Fact]
    public void AnUnhandledFailureSaysTheAppKeptRunningButKeepsTheSentencesThatSayWhatToDo()
    {
        var errors = Presenter();

        errors.ShowUnhandled(new InvalidOperationException("bug"), ProjectText.UnhandledUiFailure);
        errors.ShowUnhandled(new IOException("full", unchecked((int)0x80070070)), ProjectText.UnobservedTaskFailure);

        Assert.Equal([ProjectText.ErrorUnhandled, ProjectText.ErrorDiskFull], _infoBars.Shown.Select(bar => bar.Message));
        Assert.Equal(ProjectText.CopyDiagnostics, _infoBars.Shown[0].ActionLabel);
        Assert.All(_log.Entries, entry => Assert.Equal(LogLevel.Critical, entry.Level));
    }

    [Fact]
    public void AMapperThatThrowsIsSkipped()
    {
        var errors = new ErrorPresenter(_log, _infoBars, [new ThrowingMapper(), new VideoExportErrorMapper()]);

        errors.Show(new VideoExportException("full", VideoExportFailure.DiskFull), "Exporting.");

        Assert.Equal(ProjectText.ExportFailedDiskFull, Assert.Single(_infoBars.Shown).Message);
    }

    [Fact]
    public void EveryExportFailureHasItsOwnSentence()
    {
        var failures = Enum.GetValues<VideoExportFailure>();
        var messages = failures.Select(VideoExportErrorMapper.For).ToList();

        Assert.All(messages, message =>
        {
            Assert.Equal(InfoBarSeverity.Critical, message.Severity);
            Assert.NotEqual(ProjectText.ErrorUnexpected, message.Text);
            Assert.EndsWith(".", message.Text, StringComparison.Ordinal);
        });
        Assert.Equal(failures.Length, messages.Select(message => message.Text).Distinct().Count());
        Assert.Null(new VideoExportErrorMapper().Map(new InvalidOperationException()));
    }

    [Fact]
    public async Task ASynchronousCommandThatThrowsShowsAnInfoBar()
    {
        var command = Presenter().Command(() => throw new UnauthorizedAccessException(), "Testing.");

        command.Execute(null);

        Assert.Equal(ProjectText.ErrorAccessDenied, Assert.Single(_infoBars.Shown).Message);
        Assert.True(command.CanExecute(null));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task GuardAsyncNeverThrows()
    {
        var errors = Presenter();

        var failure = await Record.ExceptionAsync(() => errors.GuardAsync(() => throw new InvalidOperationException("bug"), "Testing."));
        var synchronous = Record.Exception(() => errors.Guard(() => throw new InvalidOperationException("bug"), "Testing."));

        Assert.Null(failure);
        Assert.Null(synchronous);
        Assert.Equal(2, _infoBars.Shown.Count);
    }

    [Fact]
    public void OnlyCorruptedStateIsFatal()
    {
        Assert.True(ErrorMessages.IsFatal(new AccessViolationException()));
        Assert.True(ErrorMessages.IsFatal(new SEHException()));
        Assert.True(ErrorMessages.IsFatal(new InvalidProgramException()));
        Assert.False(ErrorMessages.IsFatal(new OutOfMemoryException()));
        Assert.False(ErrorMessages.IsFatal(new InvalidOperationException()));
        Assert.False(ErrorMessages.IsFatal(new NullReferenceException()));
    }

    [Fact]
    public async Task NewProjectThatFailsShowsAnInfoBarAndLeavesTheProjectInPlace()
    {
        var dialogs = new ThrowingDialogService();
        var shell = new ShellViewModel(dialogs, Presenter());
        shell.CurrentProject.Model.Name = "Harbor sunset";
        var before = shell.CurrentProject;

        await shell.NewProjectCommand.ExecuteAsync(null);

        Assert.Same(before, shell.CurrentProject);
        Assert.False(before.IsDisposed);
        Assert.Equal(ProjectText.ErrorUnexpected, Assert.Single(_infoBars.Shown).Message);
        Assert.Equal(ProjectText.NewProjectFailed, Assert.Single(_log.Entries).Message);

        // It keeps working once the dialog does.
        dialogs.Throws = false;
        await shell.NewProjectCommand.ExecuteAsync(null);
        Assert.NotSame(before, shell.CurrentProject);
    }

    [Fact]
    public void SettingsOpensOverTheBodyAndBackClosesIt()
    {
        var shell = new ShellViewModel(new FakeDialogService(), Presenter());
        var changed = new List<string?>();
        shell.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        shell.OpenSettingsCommand.Execute(null);

        Assert.True(shell.IsSettingsOpen);
        Assert.False(shell.ShowsEmpty);
        Assert.False(shell.ShowsEditing);
        Assert.Contains(nameof(ShellViewModel.ShowsEmpty), changed);

        shell.CloseSettingsCommand.Execute(null);

        Assert.False(shell.IsSettingsOpen);
        Assert.True(shell.ShowsEmpty);
        Assert.Empty(_infoBars.Shown);
    }

    private ErrorPresenter Presenter() =>
        new(_log, _infoBars, [new VideoExportErrorMapper()], () =>
        {
            _diagnosticsCopied++;
            return Task.CompletedTask;
        });

    private static (Exception Exception, string Expected, bool OffersDiagnostics) Case(string kind) => kind switch
    {
        "file named" => (new FileNotFoundException("gone", @"C:\Photos\Harbor\IMG_4402.JPG"), "IMG_4402.JPG couldn't be found. It may have been moved, renamed or deleted; check that it's still there, then try again.", false),
        "file" => (new FileNotFoundException("gone"), ProjectText.ErrorFileNotFound, false),
        "folder" => (new DirectoryNotFoundException("gone"), ProjectText.ErrorFolderNotFound, false),
        "drive" => (new DriveNotFoundException("gone"), ProjectText.ErrorFolderNotFound, false),
        "access" => (new UnauthorizedAccessException("denied"), ProjectText.ErrorAccessDenied, false),
        "access hresult" => (new COMException("denied", unchecked((int)0x80070005)), ProjectText.ErrorAccessDenied, false),
        "disk full" => (new IOException("full", unchecked((int)0x80070070)), ProjectText.ErrorDiskFull, false),
        "handle disk full" => (new IOException("full", unchecked((int)0x80070027)), ProjectText.ErrorDiskFull, false),
        "in use" => (new IOException("in use", unchecked((int)0x80070020)), ProjectText.ErrorFileInUse, false),
        "memory" => (new OutOfMemoryException(), ProjectText.ErrorOutOfMemory, false),
        "memory hresult" => (new COMException("memory", unchecked((int)0x8007000E)), ProjectText.ErrorOutOfMemory, false),
        "path too long" => (new PathTooLongException(), ProjectText.ErrorPathTooLong, false),
        "export" => (new VideoExportException("lost", VideoExportFailure.GraphicsDeviceLost), ProjectText.ExportFailedGraphicsDeviceLost, true),
        "aggregate" => (new AggregateException(new FileNotFoundException("gone")), ProjectText.ErrorFileNotFound, false),
        "inner" => (new InvalidOperationException("wrapped", new UnauthorizedAccessException()), ProjectText.ErrorAccessDenied, false),
        _ => (new InvalidOperationException("a bug"), ProjectText.ErrorUnexpected, true),
    };

    private sealed class ThrowingMapper : IErrorMessageMapper
    {
        public ErrorMessage? Map(Exception exception) => throw new InvalidOperationException("mapper bug");
    }

    private sealed class ThrowingDialogService : IDialogService
    {
        public bool Throws { get; set; } = true;

        public Task<bool> ConfirmAsync(Confirmation confirmation) =>
            Throws ? throw new InvalidOperationException("The window has no XAML root yet.") : Task.FromResult(true);
    }
}
