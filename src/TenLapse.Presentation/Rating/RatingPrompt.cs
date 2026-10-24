using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TenLapse.Core.Diagnostics;
using TenLapse.Presentation.Export;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation.Rating;

/// <summary>Shows the Microsoft Store's rating dialog. The App implements it; tests use a fake.</summary>
public interface IRatingService
{
    /// <summary>
    /// Shows the Store's dialog. Returns true when the Store showed it (the user may have rated or closed it), false when
    /// it cannot be shown here (no package identity, or the Store answered with an error), so the question opens the
    /// Store's review page instead. Throws when the request fails; the question then opens the review page too.
    /// </summary>
    Task<bool> RequestAsync();
}

/// <summary>Where the rating question keeps its count and the version it last asked in (#31, #139). <see cref="RememberedSettings"/> implements it.</summary>
public interface IRatingMemory
{
    /// <summary>The app version the question was last shown in, or null, as this process knows it.</summary>
    string? RatingAskedVersion { get; }

    /// <summary>
    /// Records <paramref name="version"/> as shown unless it is recorded already, reading what is stored (another 10Lapse
    /// window may have shown it) under the lock every window takes. True when this call recorded it.
    /// </summary>
    bool TryRecordRatingAsked(string version);

    /// <summary>Counts one successful export, remembers the count, and returns it.</summary>
    int CountSuccessfulExport();
}

/// <summary>
/// The rating question (#139, replacing #31's unasked Store dialog): a card in the finished state of the second successful
/// export, "Do you like 10Lapse?" with Yes and No, once per app version. It follows <see cref="ExportViewModel.Ended"/> and
/// <see cref="ExportViewModel.State"/> and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Only <see cref="ExportOutcome.Succeeded"/> counts: a failed or cancelled export never counts and never shows the card.
/// When it is due the card waits (<see cref="IsDue"/>) until the view says it can arrive (<see cref="Arrive"/>: the
/// finished state is on screen, the window is in front, the entrance delay has passed). Only then is the version recorded,
/// reading what is stored so another 10Lapse window that showed it in this version wins, and the card shows. Leaving the
/// finished state before that records nothing, and the next successful export tries again. Once shown, leaving it
/// unanswered (Back to editing, a new export, a new project, closing the window) is an answer too, and it never comes back
/// in that version. A new version may ask once more.
/// </para>
/// <para>
/// <b>Yes</b> closes the card and shows the Store's rating dialog (<see cref="IRatingService"/>); when the Store cannot show
/// it, the Store's review page opens instead (<see cref="SettingsViewModel.StoreReviewUri"/>, as Settings' Rate 10Lapse),
/// and when that does not open either, one InfoBar says what to do: the user asked, so it is never silent. <b>No</b> closes
/// the card and nothing else happens. Everything runs on the UI thread.
/// </para>
/// </remarks>
public sealed partial class RatingPrompt : ObservableObject, IDisposable
{
    /// <summary>Successful exports before the question first shows.</summary>
    public const int AskAfterExports = 2;

    private readonly IRatingMemory _memory;
    private readonly IRatingService _service;
    private readonly ILauncher _launcher;
    private readonly IInfoBarService _infoBars;
    private readonly string _version;
    private readonly ILog _log;
    private readonly IErrorPresenter _errors;
    private ExportViewModel? _export;
    private bool _disposed;

    /// <param name="memory">The count and the version asked in.</param>
    /// <param name="service">Shows the Store's rating dialog.</param>
    /// <param name="launcher">Opens the Store's review page when its dialog cannot show.</param>
    /// <param name="infoBars">Says so when neither opens.</param>
    /// <param name="appVersion">The running app version.</param>
    /// <param name="log">Takes what the question decided.</param>
    /// <param name="errors">The error path: failures are logged; the commands' own failures become an InfoBar.</param>
    public RatingPrompt(IRatingMemory memory, IRatingService service, ILauncher launcher, IInfoBarService infoBars, string appVersion, ILog log, IErrorPresenter errors)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(infoBars);
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(errors);
        _memory = memory;
        _service = service;
        _launcher = launcher;
        _infoBars = infoBars;
        _version = appVersion;
        _log = log;
        _errors = errors;
        YesCommand = errors.AsyncCommand(YesAsync, RatingText.YesFailed);
        NoCommand = errors.Command(No, RatingText.NoFailed);
    }

    /// <summary>True while the card shows in the finished state.</summary>
    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    /// <summary>
    /// True while the card is due in this finished state but has not arrived: nothing is recorded yet. The view calls
    /// <see cref="Arrive"/> once the card can be seen.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDue { get; private set; }

    /// <summary>Yes: the card closes, then the Store's rating dialog, or its review page, or an InfoBar.</summary>
    public IAsyncRelayCommand YesCommand { get; }

    /// <summary>No: the card closes. Nothing else.</summary>
    public IRelayCommand NoCommand { get; }

    /// <summary>Follows <paramref name="export"/>'s ends (to count and show) and its state (the card goes with the finished state).</summary>
    public void Attach(ExportViewModel export)
    {
        ArgumentNullException.ThrowIfNull(export);
        _export = export;
        export.Ended += OnEnded;
        export.PropertyChanged += OnExportPropertyChanged;
    }

    /// <summary>An export ended: counts a success, and when the question is due, makes the card wait to arrive (<see cref="IsDue"/>). Never throws.</summary>
    public void ExportEnded(ExportOutcome outcome)
    {
        if (outcome != ExportOutcome.Succeeded || _disposed)
        {
            return;
        }

        try
        {
            var count = _memory.CountSuccessfulExport();
            if (count < AskAfterExports)
            {
                _log.Info(RatingText.NotYet(count));
                return;
            }

            if (string.Equals(_memory.RatingAskedVersion, _version, StringComparison.Ordinal))
            {
                _log.Info(RatingText.AlreadyAsked(_version));
                return;
            }

            // The finished state is up as an export succeeds (ExportViewModel.Finish sets it first); the card belongs to it.
            if (_export is { IsFinished: false })
            {
                _log.Info(RatingText.NotFinished);
                return;
            }

            // Not recorded yet: the window may be minimized or behind, and a card nobody saw is not an answer.
            IsDue = true;
            _log.Info(RatingText.Due);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _errors.Report(exception, RatingText.PromptFailed);
        }
    }

    /// <summary>
    /// The view: the card can be seen now (the finished state is on screen with the window in front, and its entrance
    /// delay has passed). When it is due, records the version, unless this or another 10Lapse window recorded it already,
    /// and shows the card. Returns whether it shows. Never throws.
    /// </summary>
    public bool Arrive()
    {
        if (!IsDue || _disposed)
        {
            return false;
        }

        IsDue = false;
        try
        {
            // What is stored decides, not this process's copy: another window may have shown it since this one read it.
            if (!_memory.TryRecordRatingAsked(_version))
            {
                _log.Info(RatingText.AlreadyAsked(_version));
                return false;
            }

            // Recorded as it shows: leaving it unanswered is an answer, and a crash or a close must not bring it back.
            IsShown = true;
            _log.Info(RatingText.Shown(_version));
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _errors.Report(exception, RatingText.PromptFailed);
            return false;
        }
    }

    /// <summary>Stops following the export and closes the card.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_export is { } export)
        {
            export.Ended -= OnEnded;
            export.PropertyChanged -= OnExportPropertyChanged;
        }

        IsDue = false;
        IsShown = false;
    }

    private void OnEnded(object? sender, ExportEndedEventArgs e) => ExportEnded(e.Outcome);

    // The finished state left (Back to editing, a new project): the card goes with it, unanswered; a card that had not
    // arrived yet was never seen, so nothing is recorded and the next successful export tries again.
    private void OnExportPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ExportViewModel.State) || _export is not { IsFinished: false })
        {
            return;
        }

        if (IsDue)
        {
            IsDue = false;
            _log.Info(RatingText.LeftBeforeArrival);
        }

        if (IsShown)
        {
            IsShown = false;
            _log.Info(RatingText.LeftUnanswered);
        }
    }

    private async Task YesAsync()
    {
        if (!IsShown)
        {
            return;
        }

        IsShown = false;
        _log.Info(RatingText.AnsweredYes);
        if (await ShowStoreDialogAsync())
        {
            return;
        }

        // As Settings' Rate 10Lapse: the review page, else one InfoBar that says what to do.
        await SettingsViewModel.OpenAsync(_launcher, _errors, _infoBars, SettingsViewModel.StoreReviewUri, RatingText.RateFailed, RatingText.RateCommandFailed);
    }

    // True when the Store showed its dialog; false (logged) when it could not, so the review page opens instead.
    private async Task<bool> ShowStoreDialogAsync()
    {
        try
        {
            if (await _service.RequestAsync())
            {
                _log.Info(RatingText.Asked(_version));
                return true;
            }

            _log.Info(RatingText.Unavailable);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A cancel from the Store's call (a TaskCanceledException) is a failure too: the user pressed Yes and nothing
            // cancels that here, so it must not escape to the error path, which takes a cancel quietly.
            _errors.Report(exception, RatingText.RequestFailed);
        }

        return false;
    }

    private void No()
    {
        if (IsShown)
        {
            IsShown = false;
            _log.Info(RatingText.AnsweredNo);
        }
    }
}
