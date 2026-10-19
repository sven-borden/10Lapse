using TenLapse.Presentation.Services;

namespace TenLapse.Presentation;

/// <summary>
/// Hands the startup probe's answer to the Output panel without ever making it wait for the probe (#9: a cached load
/// takes about 0.25 s, a cold probe a few seconds, a stuck test encode up to its own timeout).
/// </summary>
public static class OutputCapabilitiesDelivery
{
    /// <summary>How long the panel waits for the probe before it settles on the conservative answer: 15 s (#9).</summary>
    public static TimeSpan DefaultPatience { get; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Waits for <paramref name="probe"/> up to <paramref name="patience"/> and gives the panel its answer on the UI
    /// thread. If the probe is slower than that the panel gets <see cref="ConservativeOutputCapabilities"/> as a
    /// stand-in answer (what it replaces is shown, never written to the project), and the real answer replaces it when it
    /// arrives. If the probe fails the failure is shown (the error path) and the panel keeps the stand-in. Never throws except for a cancellation of <paramref name="cancellationToken"/>.
    /// </summary>
    /// <param name="panel">The panel.</param>
    /// <param name="probe">The startup probe, already running.</param>
    /// <param name="ui">Runs the hand-over on the UI thread, where the project may be changed.</param>
    /// <param name="errors">Shows a failed probe.</param>
    /// <param name="patience">How long to wait before the conservative answer.</param>
    /// <param name="cancellationToken">Stops waiting (the window is closing).</param>
    public static async Task DeliverAsync(
        this OutputPanelViewModel panel,
        Task<IOutputCapabilities> probe,
        IUiDispatcher ui,
        IErrorPresenter errors,
        TimeSpan patience,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(errors);

        IOutputCapabilities answer;
        try
        {
            answer = await probe.WaitAsync(patience, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Too slow: offer what every PC does now, and the probe's own answer when it comes.
            await ui.RunAsync(() => panel.SetCapabilities(ConservativeOutputCapabilities.Instance)).ConfigureAwait(false);
            await DeliverLateAsync(panel, probe, ui, errors, cancellationToken).ConfigureAwait(false);
            return;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && exception is not OperationCanceledException)
        {
            errors.Show(exception, OutputText.ProbeFailed);
            await ui.RunAsync(() => panel.SetCapabilities(ConservativeOutputCapabilities.Instance)).ConfigureAwait(false);
            return;
        }

        await ui.RunAsync(() => panel.SetCapabilities(answer)).ConfigureAwait(false);
    }

    private static async Task DeliverLateAsync(
        OutputPanelViewModel panel,
        Task<IOutputCapabilities> probe,
        IUiDispatcher ui,
        IErrorPresenter errors,
        CancellationToken cancellationToken)
    {
        IOutputCapabilities answer;
        try
        {
            answer = await probe.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && exception is not OperationCanceledException)
        {
            errors.Show(exception, OutputText.ProbeFailed);
            return;
        }

        await ui.RunAsync(() => panel.SetCapabilities(answer)).ConfigureAwait(false);
    }
}
