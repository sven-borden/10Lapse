using TenLapse.Core.Settings;
using TenLapse.Presentation;
using TenLapse.Presentation.Export;
using TenLapse.Presentation.Rating;
using TenLapse.Presentation.Services;

namespace TenLapse.Tests.Presentation;

/// <summary>Records each ask, and answers or throws as the test says.</summary>
internal sealed class FakeRatingService : IRatingService
{
    public int Asked { get; private set; }

    public bool Result { get; set; } = true;

    public Exception? Failure { get; set; }

    public Task<bool> RequestAsync()
    {
        Asked++;
        return Failure is { } failure ? Task.FromException<bool>(failure) : Task.FromResult(Result);
    }
}

/// <summary>The rating prompt (#31): the second success only, never after a failure or a cancel, once per version, across restarts.</summary>
public sealed class RatingPromptTests
{
    private const string Version = "3.0.0.0";

    private readonly MemorySettingsStore _store = new();
    private readonly FakeErrorSink _errors = new();
    private readonly FakeLog _log = new();
    private readonly ManualTimeProvider _settingsTime = new();
    private readonly ManualTimeProvider _time = new();
    private readonly FakeRatingService _service = new();
    private bool _canAsk = true;

    private RememberedSettings Remembered(MemorySettingsStore? store = null) => new(store ?? _store, _errors, _log, _settingsTime);

    private RatingPrompt Prompt(RememberedSettings remembered, string version = Version) =>
        new(remembered, _service, () => _canAsk, version, _log, _errors, _time, RatingPrompt.DefaultDelay);

    private async Task Succeed(RatingPrompt prompt, bool waitForTheAsk = true)
    {
        prompt.ExportEnded(ExportOutcome.Succeeded);
        if (waitForTheAsk)
        {
            _time.Advance(RatingPrompt.DefaultDelay);
            await prompt.WhenSettled;
        }
    }

    [Fact]
    public async Task TheFirstSuccessAsksNothing()
    {
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);

        await Succeed(prompt);

        Assert.Equal(0, _service.Asked);
        Assert.Equal(1, remembered.SuccessfulExports);
        Assert.Null(remembered.RatingAskedVersion);
    }

    [Fact]
    public async Task TheSecondSuccessAsksOnceAfterTheDelayAndRemembersTheVersion()
    {
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);
        await Succeed(prompt);

        await Succeed(prompt, waitForTheAsk: false);
        Assert.Equal(0, _service.Asked);
        _time.Advance(RatingPrompt.DefaultDelay - TimeSpan.FromMilliseconds(1));
        Assert.Equal(0, _service.Asked);
        _time.Advance(TimeSpan.FromMilliseconds(1));
        await prompt.WhenSettled;

        Assert.Equal(1, _service.Asked);
        Assert.Equal(2, remembered.SuccessfulExports);
        Assert.Equal(Version, remembered.RatingAskedVersion);
        Assert.Empty(_errors.Shown);
    }

    [Fact]
    public async Task LaterSuccessesInTheSameVersionAskNoMore()
    {
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);
        for (var i = 0; i < 5; i++)
        {
            await Succeed(prompt);
        }

        Assert.Equal(1, _service.Asked);
        Assert.Equal(5, remembered.SuccessfulExports);
    }

    [Fact]
    public async Task ANewVersionAsksAgainOnItsNextSuccess()
    {
        using var remembered = Remembered();
        using (var first = Prompt(remembered))
        {
            await Succeed(first);
            await Succeed(first);
        }

        Assert.Equal(1, _service.Asked);

        using var second = Prompt(remembered, "3.1.0.0");
        await Succeed(second);

        Assert.Equal(2, _service.Asked);
        Assert.Equal("3.1.0.0", remembered.RatingAskedVersion);
    }

    [Theory]
    [InlineData(ExportOutcome.Failed)]
    [InlineData(ExportOutcome.Cancelled)]
    public async Task AFailureOrACancelNeverCountsAndNeverAsks(ExportOutcome outcome)
    {
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);

        for (var i = 0; i < 4; i++)
        {
            prompt.ExportEnded(outcome);
            _time.Advance(RatingPrompt.DefaultDelay);
            await prompt.WhenSettled;
        }

        Assert.Equal(0, _service.Asked);
        Assert.Equal(0, remembered.SuccessfulExports);
    }

    [Fact]
    public async Task FailuresBetweenSuccessesDoNotCountButDoNotResetTheCount()
    {
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);

        await Succeed(prompt);
        prompt.ExportEnded(ExportOutcome.Failed);
        prompt.ExportEnded(ExportOutcome.Cancelled);
        Assert.Equal(0, _service.Asked);
        await Succeed(prompt);

        Assert.Equal(1, _service.Asked);
        Assert.Equal(2, remembered.SuccessfulExports);
    }

    [Fact]
    public async Task TheCountAndTheVersionSurviveARestart()
    {
        using (var remembered = Remembered())
        {
            using var prompt = Prompt(remembered);
            await Succeed(prompt);
            remembered.Flush();
        }

        Assert.Equal(1, _store.Stored.SuccessfulExports);

        // The second launch: one more success is the second in all.
        using (var remembered = Remembered())
        {
            using var prompt = Prompt(remembered);
            await Succeed(prompt);
            remembered.Flush();
            Assert.Equal(1, _service.Asked);
        }

        Assert.Equal(2, _store.Stored.SuccessfulExports);
        Assert.Equal(Version, _store.Stored.RatingAskedVersion);

        // The third launch, same version: no new ask.
        using (var remembered = Remembered())
        {
            using var prompt = Prompt(remembered);
            await Succeed(prompt);
        }

        Assert.Equal(1, _service.Asked);
    }

    [Fact]
    public async Task AWindowInTheBackgroundOrBusyPostponesWithoutRecordingAndTheNextSuccessAsks()
    {
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);
        await Succeed(prompt);
        _canAsk = false;

        await Succeed(prompt);

        Assert.Equal(0, _service.Asked);
        Assert.Null(remembered.RatingAskedVersion);
        Assert.Contains(_log.Entries, entry => entry.Message == RatingText.Postponed);

        _canAsk = true;
        await Succeed(prompt);

        Assert.Equal(1, _service.Asked);
        Assert.Equal(Version, remembered.RatingAskedVersion);
    }

    [Fact]
    public async Task ANewExportStartingDropsTheWaitingAsk()
    {
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);
        await Succeed(prompt);
        await Succeed(prompt, waitForTheAsk: false);

        prompt.ExportStarted();
        _time.Advance(RatingPrompt.DefaultDelay);
        await prompt.WhenSettled;

        Assert.Equal(0, _service.Asked);
        Assert.Null(remembered.RatingAskedVersion);
        Assert.Contains(_log.Entries, entry => entry.Message == RatingText.Superseded);
    }

    [Fact]
    public async Task ClosingDropsTheWaitingAsk()
    {
        using var remembered = Remembered();
        var prompt = Prompt(remembered);
        await Succeed(prompt);
        await Succeed(prompt, waitForTheAsk: false);

        prompt.Dispose();
        _time.Advance(RatingPrompt.DefaultDelay);
        await prompt.WhenSettled;

        Assert.Equal(0, _service.Asked);
    }

    [Fact]
    public async Task AnAskTheStoreCouldNotMakeIsNotRecordedSoTheNextSuccessTriesAgain()
    {
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);
        _service.Result = false;
        await Succeed(prompt);
        await Succeed(prompt);

        Assert.Equal(1, _service.Asked);
        Assert.Null(remembered.RatingAskedVersion);

        _service.Result = true;
        await Succeed(prompt);

        Assert.Equal(2, _service.Asked);
        Assert.Equal(Version, remembered.RatingAskedVersion);
    }

    [Fact]
    public async Task AFailingStoreIsLoggedOnlyAndNotRecorded()
    {
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);
        _service.Failure = new InvalidOperationException("No Store.");
        await Succeed(prompt);

        await Succeed(prompt);

        Assert.Equal(1, _service.Asked);
        Assert.Null(remembered.RatingAskedVersion);
        Assert.Empty(_errors.Shown);
        var (exception, context) = Assert.Single(_errors.Reported);
        Assert.Equal("No Store.", exception.Message);
        Assert.Equal(RatingText.RequestFailed, context);
    }

    [Fact]
    public async Task ResetToDefaultsKeepsTheCountAndTheVersionSoItCannotRearmThePrompt()
    {
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);
        remembered.LastFolder = @"C:\Photos";
        await Succeed(prompt);
        await Succeed(prompt);

        Assert.True(remembered.ResetToDefaults());

        Assert.Null(remembered.LastFolder);
        Assert.Equal(2, _store.Stored.SuccessfulExports);
        Assert.Equal(Version, _store.Stored.RatingAskedVersion);

        await Succeed(prompt);

        Assert.Equal(1, _service.Asked);
    }

    [Fact]
    public async Task ItFollowsTheExportViewModelsRealStartsAndEnds()
    {
        using var app = new ExportHarness(capabilities: new FakeOutputCapabilities());
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);
        app.On(() => prompt.Attach(app.Export));

        // A cancel and a failure first: neither counts.
        app.Exporter.CleanUp.SetResult(true);
        app.StartExport();
        app.On(() => app.Export.CancelCommand.Execute(null));
        app.Settle();
        app.StartExport();
        app.Exporter.Fail(new InvalidOperationException("Encoder broke."));
        app.Settle();
        Assert.Equal(0, remembered.SuccessfulExports);

        // Two successes: the second asks, after the delay.
        app.StartExport();
        app.Exporter.Finish(app.Video());
        app.Settle();
        app.On(() => app.Export.BackToEditingCommand.Execute(null));
        app.StartExport();
        app.Exporter.Finish(app.Video());
        app.Settle();
        Assert.Equal(2, remembered.SuccessfulExports);
        Assert.Equal(0, _service.Asked);
        _time.Advance(RatingPrompt.DefaultDelay);
        await prompt.WhenSettled;

        Assert.Equal(1, _service.Asked);
        Assert.Equal(Version, remembered.RatingAskedVersion);

        // Detached on dispose: a later success counts nowhere.
        prompt.Dispose();
        app.On(() => app.Export.BackToEditingCommand.Execute(null));
        app.StartExport();
        app.Exporter.Finish(app.Video());
        app.Settle();
        Assert.Equal(2, remembered.SuccessfulExports);
    }
}

/// <summary>The Settings page's Rate 10Lapse and Send feedback (#31): the right links, and a sentence when one would not open.</summary>
public sealed class SettingsFeedbackTests
{
    private readonly FakeErrorSink _errors = new();
    private readonly FakeInfoBarService _infoBars = new();
    private readonly FakeLauncher _launcher = new();

    private SettingsViewModel Settings()
    {
        var log = new FakeLog();
        var remembered = new RememberedSettings(new MemorySettingsStore(), _errors, log, new ManualTimeProvider());
        var collector = new TenLapse.Presentation.Services.DiagnosticsCollector([], log);
        return new SettingsViewModel(_errors, new DiagnosticsCopier(collector, new FakeClipboard(), _infoBars), remembered, _infoBars, _launcher);
    }

    [Fact]
    public async Task RateOpensTheStoreReviewPageForTheApp()
    {
        await Settings().RateCommand.ExecuteAsync(null);

        Assert.Equal("ms-windows-store://review/?ProductId=9NBN2XH6S4D8", Assert.Single(_launcher.Launched).OriginalString);
        Assert.Empty(_infoBars.Shown);
        Assert.Empty(_errors.Shown);
    }

    [Fact]
    public async Task SendFeedbackOpensANewIssueInTheBrowser()
    {
        await Settings().SendFeedbackCommand.ExecuteAsync(null);

        Assert.Equal("https://github.com/sven-borden/10Lapse/issues/new", Assert.Single(_launcher.Launched).AbsoluteUri);
        Assert.Empty(_infoBars.Shown);
        Assert.Empty(_errors.Shown);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ALinkThatWouldNotOpenGivesOneCautionSentenceThatSaysWhatToDo(bool rate)
    {
        _launcher.Result = false;
        var settings = Settings();

        await (rate ? settings.RateCommand : settings.SendFeedbackCommand).ExecuteAsync(null);

        var bar = Assert.Single(_infoBars.Shown);
        Assert.Equal(InfoBarSeverity.Caution, bar.Severity);
        Assert.Equal(rate ? RatingText.RateFailed : RatingText.FeedbackFailed, bar.Message);
        Assert.Empty(_errors.Shown);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ALaunchThatThrowsIsLoggedAndGivesTheSameSentence(bool rate)
    {
        _launcher.Failure = new InvalidOperationException("Launch refused.");
        var settings = Settings();

        await (rate ? settings.RateCommand : settings.SendFeedbackCommand).ExecuteAsync(null);

        Assert.Equal("Launch refused.", Assert.Single(_errors.Reported).Exception.Message);
        var bar = Assert.Single(_infoBars.Shown);
        Assert.Equal(InfoBarSeverity.Caution, bar.Severity);
        Assert.Equal(rate ? RatingText.RateFailed : RatingText.FeedbackFailed, bar.Message);
    }
}
