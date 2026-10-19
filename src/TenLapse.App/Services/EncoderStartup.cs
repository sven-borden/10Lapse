using TenLapse.Media.Encoding;
using TenLapse.Presentation;

namespace TenLapse.Services;

/// <summary>
/// The encoder probe (#9) started once at launch, in the background. Nothing waits for it: the Output panel shows a
/// quiet pending state and takes <see cref="Probe"/>'s answer when it arrives (<see cref="OutputCapabilitiesDelivery"/>),
/// and Copy diagnostics reads <see cref="Loaded"/> when there is one.
/// </summary>
internal sealed class EncoderStartup
{
    private readonly CancellationTokenSource _cancellation = new();
    private EncoderCapabilities? _loaded;

    /// <summary>Starts the probe, or loads its cache file when it is still valid for this PC (about 0.25 s; a cold probe about 3.5 s).</summary>
    /// <param name="cachePath">The cache file, <see cref="AppEnvironment.EncoderCacheFile"/>.</param>
    public EncoderStartup(string cachePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(cachePath);
        var token = _cancellation.Token;
        Probe = Task.Run(() => RunAsync(cachePath, token), token);
    }

    /// <summary>The probe's answer for the Output panel. It faults only for a bug in the probe; a missing GPU is an answer. It is cancelled after <see cref="Cancel"/>.</summary>
    public Task<IOutputCapabilities> Probe { get; }

    /// <summary>The token that <see cref="Cancel"/> cancels: hand it to whoever waits for <see cref="Probe"/>.</summary>
    public CancellationToken Token => _cancellation.Token;

    /// <summary>
    /// Stops the probe (the window is closing). The probe checks between its test encodes and removes the file of
    /// the one it was writing, so a cold probe ends within one test encode and leaves nothing in the temp folder.
    /// </summary>
    public void Cancel() => _cancellation.Cancel();

    /// <summary>What this session's probe found, or null while it is still running (or failed).</summary>
    public EncoderCapabilities? Loaded => Volatile.Read(ref _loaded);

    private async Task<IOutputCapabilities> RunAsync(string cachePath, CancellationToken cancellationToken)
    {
        var capabilities = await EncoderProbe.LoadOrProbeAsync(cachePath, cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _loaded, capabilities);
        return new EncoderOutputCapabilities(capabilities);
    }
}
