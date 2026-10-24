using TenLapse.Core.Settings;
using TenLapse.Presentation;
using TenLapse.Presentation.Export;
using TenLapse.Presentation.Rating;
using TenLapse.Presentation.Services;

namespace TenLapse.Tests.Presentation;

/// <summary>Records each request for the Store's dialog, and answers or throws as the test says.</summary>
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

/// <summary>
/// The rating question (#139): the card in the second success's finished state, never after a failure or a cancel, once
/// per version (recorded as it arrives, so unanswered is an answer, but a card that never arrived is not), across restarts
/// and across two windows sharing the settings; Yes shows the Store's dialog, else its review page, else one InfoBar; No
/// closes it.
/// </summary>
public sealed class RatingPromptTests
{
    private const string Version = "3.0.0.0";

    private readonly MemorySettingsStore _store = new();
    private readonly FakeErrorSink _errors = new();
    private readonly FakeLog _log = new();
    private readonly ManualTimeProvider _settingsTime = new();
    private readonly FakeRatingService _service = new();
    private readonly FakeLauncher _launcher = new();
    private readonly FakeInfoBarService _infoBars = new();

    private RememberedSettings Remembered(MemorySettingsStore? store = null) => new(store ?? _store, _errors, _log, _settingsTime);

    private RatingPrompt Prompt(RememberedSettings remembered, string version = Version) =>
        new(remembered, _service, _launcher, _infoBars, version, _log, _errors);

    // Two successes, then the view says the card can be seen: it shows.
    private static void ShowCard(RatingPrompt prompt)
    {
        prompt.ExportEnded(ExportOutcome.Succeeded);
        prompt.ExportEnded(ExportOutcome.Succeeded);
        Assert.True(prompt.Arrive());
        Assert.True(prompt.IsShown);
    }

    [Fact]
    public void TheFirstSuccessShowsNothing()
    {
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);

        prompt.ExportEnded(ExportOutcome.Succeeded);

        Assert.False(prompt.IsDue);
        Assert.False(prompt.Arrive());
        Assert.False(prompt.IsShown);
        Assert.Equal(1, remembered.SuccessfulExports);
        Assert.Null(remembered.RatingAskedVersion);
    }

    [Fact]
    public void TheSecondSuccessMakesTheCardDueAndRecordsNothingUntilItArrives()
    {
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);
        prompt.ExportEnded(ExportOutcome.Succeeded);

        // The window is minimized or behind: the card waits, unseen, and nothing is recorded.
        prompt.ExportEnded(ExportOutcome.Succeeded);
        remembered.Flush();

        Assert.True(prompt.IsDue);
        Assert.False(prompt.IsShown);
        Assert.Equal(2, _store.Stored.SuccessfulExports);
        Assert.Null(_store.Stored.RatingAskedVersion);
        Assert.Null(remembered.RatingAskedVersion);
        Assert.Contains(_log.Entries, entry => entry.Message == RatingText.Due);
        Assert.DoesNotContain(_log.Entries, entry => entry.Message == RatingText.Shown(Version));
    }

    [Fact]
    public void TheCardsArrivalShowsItAndRecordsTheVersionInTheStoreWithoutCallingTheStoresDialog()
    {
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);
        prompt.ExportEnded(ExportOutcome.Succeeded);
        prompt.ExportEnded(ExportOutcome.Succeeded);
        var changes = new List<string?>();
        prompt.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        Assert.True(prompt.Arrive());

        Assert.True(prompt.IsShown);
        Assert.False(prompt.IsDue);
        Assert.Contains(nameof(RatingPrompt.IsShown), changes);
        Assert.Equal(Version, remembered.RatingAskedVersion);

        // Written at once, not after the pause: another window reads it before it shows its own card.
        Assert.Equal(Version, _store.Stored.RatingAskedVersion);
        Assert.Equal(2, _store.Stored.SuccessfulExports);
        Assert.Equal(0, _service.Asked);
        Assert.Empty(_launcher.Launched);
        Assert.Contains(_log.Entries, entry => entry.Message == RatingText.Shown(Version));
        Assert.Empty(_errors.Reported);

        // A second arrival (the window activated again) does nothing.
        Assert.False(prompt.Arrive());
    }

    [Fact]
    public void TwoWindowsSharingTheSettingsShowTheCardOnceInAVersion()
    {
        // C started before either recorded anything, and writes later with its old copy.
        using var c = Remembered();
        using var a = Remembered();
        using var b = Remembered();
        using var promptA = Prompt(a);
        using var promptB = Prompt(b);

        // A exports, B exports: two in all.
        promptA.ExportEnded(ExportOutcome.Succeeded);
        a.Flush();
        promptB.ExportEnded(ExportOutcome.Succeeded);
        b.Flush();
        Assert.Equal(2, _store.Stored.SuccessfulExports);

        // Both export again: each window's own copy says the question is due.
        promptA.ExportEnded(ExportOutcome.Succeeded);
        promptB.ExportEnded(ExportOutcome.Succeeded);
        Assert.True(promptA.IsDue);
        Assert.True(promptB.IsDue);

        // A's card arrives first and records the version; B reads it from the store and shows nothing.
        Assert.True(promptA.Arrive());
        Assert.False(promptB.Arrive());
        Assert.False(promptB.IsShown);
        Assert.Equal(Version, b.RatingAskedVersion);
        Assert.Contains(_log.Entries, entry => entry.Message == RatingText.AlreadyAsked(Version));

        // B's next success shows nothing either, and C's older copy does not undo the record.
        promptB.ExportEnded(ExportOutcome.Succeeded);
        Assert.False(promptB.IsDue);
        c.ExportFolder = @"C:\Videos";
        c.CountSuccessfulExport();
        c.Flush();
        Assert.Equal(Version, _store.Stored.RatingAskedVersion);
        Assert.Equal(@"C:\Videos", _store.Stored.ExportFolder);
    }

    [Fact]
    public void AnArrivalWhoseWriteFailsStillShowsOnceFromThisWindowsCopy()
    {
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);
        _store.FailWith = new IOException("The disk is full.");

        ShowCard(prompt);

        Assert.Equal(Version, remembered.RatingAskedVersion);
        Assert.Null(_store.Stored.RatingAskedVersion);
        Assert.Single(_errors.Shown);

        // Once the disk takes writes again, the version goes with the next one; a later success shows nothing.
        _store.FailWith = null;
        remembered.Flush();
        Assert.Equal(Version, _store.Stored.RatingAskedVersion);
        prompt.ExportEnded(ExportOutcome.Succeeded);
        Assert.False(prompt.IsDue);
    }

    [Theory]
    [InlineData(ExportOutcome.Failed)]
    [InlineData(ExportOutcome.Cancelled)]
    public void AFailureOrACancelNeverCountsAndNeverShows(ExportOutcome outcome)
    {
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);

        for (var i = 0; i < 4; i++)
        {
            prompt.ExportEnded(outcome);
        }

        Assert.False(prompt.IsShown);
        Assert.Equal(0, remembered.SuccessfulExports);
        Assert.Null(remembered.RatingAskedVersion);
    }

    [Fact]
    public void FailuresBetweenSuccessesDoNotCountButDoNotResetTheCount()
    {
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);

        prompt.ExportEnded(ExportOutcome.Succeeded);
        prompt.ExportEnded(ExportOutcome.Failed);
        prompt.ExportEnded(ExportOutcome.Cancelled);
        Assert.False(prompt.IsDue);
        prompt.ExportEnded(ExportOutcome.Succeeded);

        Assert.True(prompt.IsDue);
        Assert.Equal(2, remembered.SuccessfulExports);
    }

    [Fact]
    public async Task LaterSuccessesInTheSameVersionShowItNoMore()
    {
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);
        ShowCard(prompt);
        await prompt.YesCommand.ExecuteAsync(null);

        for (var i = 0; i < 3; i++)
        {
            prompt.ExportEnded(ExportOutcome.Succeeded);
            Assert.False(prompt.IsDue);
            Assert.False(prompt.IsShown);
        }

        Assert.Equal(5, remembered.SuccessfulExports);
        Assert.Contains(_log.Entries, entry => entry.Message == RatingText.AlreadyAsked(Version));
    }

    [Fact]
    public void ANewVersionAsksOnceMoreOnItsNextSuccess()
    {
        using var remembered = Remembered();
        using (var first = Prompt(remembered))
        {
            ShowCard(first);
        }

        using var second = Prompt(remembered, "3.1.0.0");
        second.ExportEnded(ExportOutcome.Succeeded);

        Assert.True(second.Arrive());
        Assert.True(second.IsShown);
        Assert.Equal("3.1.0.0", remembered.RatingAskedVersion);
    }

    [Fact]
    public void TheCountAndTheVersionSurviveARestartAndAnUnansweredCardDoesNotComeBack()
    {
        using (var remembered = Remembered())
        {
            using var prompt = Prompt(remembered);
            prompt.ExportEnded(ExportOutcome.Succeeded);
            remembered.Flush();
        }

        Assert.Equal(1, _store.Stored.SuccessfulExports);

        // The second launch: one more success is the second in all. The window closes with the card unanswered.
        using (var remembered = Remembered())
        {
            using var prompt = Prompt(remembered);
            prompt.ExportEnded(ExportOutcome.Succeeded);
            Assert.True(prompt.Arrive());
            remembered.Flush();
        }

        Assert.Equal(2, _store.Stored.SuccessfulExports);
        Assert.Equal(Version, _store.Stored.RatingAskedVersion);

        // The third launch, same version: no card.
        using (var remembered = Remembered())
        {
            using var prompt = Prompt(remembered);
            prompt.ExportEnded(ExportOutcome.Succeeded);
            Assert.False(prompt.IsDue);
            Assert.False(prompt.IsShown);
        }

        Assert.Equal(0, _service.Asked);
    }

    [Fact]
    public async Task YesClosesTheCardAndShowsTheStoresDialog()
    {
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);
        ShowCard(prompt);

        await prompt.YesCommand.ExecuteAsync(null);

        Assert.False(prompt.IsShown);
        Assert.Equal(1, _service.Asked);
        Assert.Empty(_launcher.Launched);
        Assert.Empty(_infoBars.Shown);
        Assert.Empty(_errors.Reported);
        Assert.Contains(_log.Entries, entry => entry.Message == RatingText.AnsweredYes);
        Assert.Contains(_log.Entries, entry => entry.Message == RatingText.Asked(Version));
    }

    [Fact]
    public async Task YesOpensTheStoresReviewPageWhenItsDialogCannotShow()
    {
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);
        _service.Result = false;
        ShowCard(prompt);

        await prompt.YesCommand.ExecuteAsync(null);

        Assert.False(prompt.IsShown);
        Assert.Equal(1, _service.Asked);
        Assert.Equal(SettingsViewModel.StoreReviewUri, Assert.Single(_launcher.Launched));
        Assert.Equal("ms-windows-store://review/?ProductId=9NBN2XH6S4D8", _launcher.Launched[0].OriginalString);
        Assert.Empty(_infoBars.Shown);
        Assert.Contains(_log.Entries, entry => entry.Message == RatingText.Unavailable);
    }

    [Fact]
    public async Task YesOpensTheReviewPageWhenTheStoresDialogThrowsAndLogsWhy()
    {
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);
        _service.Failure = new InvalidOperationException("No Store.");
        ShowCard(prompt);

        await prompt.YesCommand.ExecuteAsync(null);

        var (exception, context) = Assert.Single(_errors.Reported);
        Assert.Equal("No Store.", exception.Message);
        Assert.Equal(RatingText.RequestFailed, context);
        Assert.Single(_launcher.Launched);
        Assert.Empty(_infoBars.Shown);
        Assert.Empty(_errors.Shown);
        Assert.Equal(Version, remembered.RatingAskedVersion);
    }

    [Fact]
    public async Task YesOpensTheReviewPageWhenTheStoresCallIsCancelled()
    {
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);
        _service.Failure = new TaskCanceledException("The Store's call was cancelled.");
        ShowCard(prompt);

        await prompt.YesCommand.ExecuteAsync(null);

        Assert.False(prompt.IsShown);
        Assert.Equal(SettingsViewModel.StoreReviewUri, Assert.Single(_launcher.Launched));
        var (exception, context) = Assert.Single(_errors.Reported);
        Assert.IsType<TaskCanceledException>(exception);
        Assert.Equal(RatingText.RequestFailed, context);
        Assert.Empty(_infoBars.Shown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task YesEndsInOneCautionSentenceWhenTheReviewPageWouldNotOpenEither(bool throws)
    {
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);
        _service.Result = false;
        if (throws)
        {
            _launcher.Failure = new InvalidOperationException("Launch refused.");
        }
        else
        {
            _launcher.Result = false;
        }

        ShowCard(prompt);

        await prompt.YesCommand.ExecuteAsync(null);

        Assert.False(prompt.IsShown);
        var bar = Assert.Single(_infoBars.Shown);
        Assert.Equal(InfoBarSeverity.Caution, bar.Severity);
        Assert.Equal(RatingText.RateFailed, bar.Message);
        Assert.Empty(_errors.Shown);
        Assert.Equal(throws ? 1 : 0, _errors.Reported.Count);
    }

    [Fact]
    public async Task NoClosesTheCardAndOpensNothing()
    {
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);
        ShowCard(prompt);

        prompt.NoCommand.Execute(null);

        Assert.False(prompt.IsShown);
        Assert.Equal(0, _service.Asked);
        Assert.Empty(_launcher.Launched);
        Assert.Empty(_infoBars.Shown);
        Assert.Equal(Version, remembered.RatingAskedVersion);
        Assert.Contains(_log.Entries, entry => entry.Message == RatingText.AnsweredNo);

        // A late Yes (a second click) does nothing once the card is gone.
        await prompt.YesCommand.ExecuteAsync(null);
        Assert.Equal(0, _service.Asked);
        Assert.Empty(_launcher.Launched);
    }

    [Fact]
    public void ClosingTheWindowClosesTheCard()
    {
        using var remembered = Remembered();
        var prompt = Prompt(remembered);
        ShowCard(prompt);

        prompt.Dispose();

        Assert.False(prompt.IsShown);
        Assert.Equal(Version, remembered.RatingAskedVersion);
    }

    [Fact]
    public void ResetToDefaultsKeepsTheCountAndTheVersionSoItCannotRearmTheQuestion()
    {
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);
        remembered.LastFolder = @"C:\Photos";
        ShowCard(prompt);
        prompt.NoCommand.Execute(null);

        Assert.True(remembered.ResetToDefaults());

        Assert.Null(remembered.LastFolder);
        Assert.Equal(2, _store.Stored.SuccessfulExports);
        Assert.Equal(Version, _store.Stored.RatingAskedVersion);
        prompt.ExportEnded(ExportOutcome.Succeeded);
        Assert.False(prompt.IsDue);
        Assert.False(prompt.IsShown);
    }

    [Fact]
    public void LeavingTheFinishedStateBeforeTheCardArrivesRecordsNothingAndTheNextSuccessTriesAgain()
    {
        using var app = new ExportHarness(capabilities: new FakeOutputCapabilities());
        using var remembered = Remembered();
        using var prompt = Prompt(remembered);
        app.On(() => prompt.Attach(app.Export));
        app.StartExport();
        app.Exporter.Finish(app.Video());
        app.Settle();
        app.On(() => app.Export.BackToEditingCommand.Execute(null));

        // The second success finishes while the window is minimized: the card is due, unseen.
        app.StartExport();
        app.Exporter.Finish(app.Video());
        app.Settle();
        Assert.True(prompt.IsDue);

        // Back to editing before the window comes back: nothing shown, nothing recorded, a late arrival does nothing.
        app.On(() => app.Export.BackToEditingCommand.Execute(null));
        Assert.False(prompt.IsDue);
        Assert.False(app.On(prompt.Arrive));
        Assert.False(prompt.IsShown);
        remembered.Flush();
        Assert.Null(_store.Stored.RatingAskedVersion);
        Assert.Contains(_log.Entries, entry => entry.Message == RatingText.LeftBeforeArrival);
        Assert.DoesNotContain(_log.Entries, entry => entry.Message == RatingText.LeftUnanswered);

        // The next successful export tries again.
        app.StartExport();
        app.Exporter.Finish(app.Video());
        app.Settle();
        Assert.True(prompt.IsDue);
        Assert.True(app.On(prompt.Arrive));
        Assert.True(prompt.IsShown);
        Assert.Equal(Version, _store.Stored.RatingAskedVersion);
        Assert.Equal(3, _store.Stored.SuccessfulExports);
    }

    [Fact]
    public void ItFollowsTheExportViewModelsRealEndsAndGoesWithTheFinishedState()
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
        Assert.False(prompt.IsShown);

        // Two successes: the second's finished state shows the card.
        app.StartExport();
        app.Exporter.Finish(app.Video());
        app.Settle();
        Assert.False(prompt.IsShown);
        app.On(() => app.Export.BackToEditingCommand.Execute(null));
        app.StartExport();
        app.Exporter.Finish(app.Video());
        app.Settle();
        Assert.True(app.On(() => app.Export.IsFinished));
        Assert.True(prompt.IsDue);
        Assert.False(prompt.IsShown);
        Assert.Null(remembered.RatingAskedVersion);

        // The view: the card arrives (the window in front, its delay passed).
        Assert.True(app.On(prompt.Arrive));
        Assert.True(prompt.IsShown);
        Assert.Equal(Version, remembered.RatingAskedVersion);

        // Back to editing leaves it unanswered: it goes, and does not come back in this version.
        app.On(() => app.Export.BackToEditingCommand.Execute(null));
        Assert.False(prompt.IsShown);
        Assert.Contains(_log.Entries, entry => entry.Message == RatingText.LeftUnanswered);
        app.StartExport();
        app.Exporter.Finish(app.Video());
        app.Settle();
        Assert.False(prompt.IsShown);
        Assert.Equal(3, remembered.SuccessfulExports);

        // Detached on dispose: a later success counts nowhere.
        prompt.Dispose();
        app.On(() => app.Export.BackToEditingCommand.Execute(null));
        app.StartExport();
        app.Exporter.Finish(app.Video());
        app.Settle();
        Assert.Equal(3, remembered.SuccessfulExports);
        Assert.Equal(0, _service.Asked);
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
