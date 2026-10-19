using TenLapse.Media.Encoding;
using TenLapse.Presentation;

namespace TenLapse.Services;

/// <summary>
/// The encoder probe (#9), run once per launch in the background. Nothing waits for it: the Output panel shows a
/// quiet pending state and takes <see cref="Probe"/>'s answer when it arrives (<see cref="OutputCapabilitiesDelivery"/>),
/// and Copy diagnostics reads <see cref="Loaded"/> when there is one.
/// </summary>
/// <remarks>
/// It begins once the window's first frame is on screen (<see cref="Start"/>), not with the window (#90): a cold probe
/// creates a D3D11 device and runs hardware test encodes for seconds, and the start-up crash came on fresh builds,
/// whose first launch probes cold, while XAML was creating its own device. A window that has not drawn after
/// <see cref="FirstFrameLimit"/> (started minimized) gets the probe anyway.
/// </remarks>
internal sealed class EncoderStartup
{
    /// <summary>How long the probe waits for the first frame before it starts anyway.</summary>
    public static readonly TimeSpan FirstFrameLimit = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource _cancellation = new();
    private readonly TaskCompletionSource _start = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly StartupTrace _trace;
    private EncoderCapabilities? _loaded;

    /// <summary>
    /// Readies the probe, or the load of its cache file when that is still valid for this PC (about 0.25 s; a cold probe
    /// about 3.5 s), to run at <see cref="Start"/>.
    /// </summary>
    /// <param name="cachePath">The cache file, <see cref="AppEnvironment.EncoderCacheFile"/>.</param>
    /// <param name="trace">The start-up trace (#90): whether the cache answered or the probe ran cold, and when the answer came.</param>
    public EncoderStartup(string cachePath, StartupTrace trace)
    {
        ArgumentException.ThrowIfNullOrEmpty(cachePath);
        ArgumentNullException.ThrowIfNull(trace);
        _trace = trace;
        var token = _cancellation.Token;
        Probe = RunAsync(cachePath, token);
    }

    /// <summary>Lets the probe begin: the window's first frame is on screen. Safe on any thread, and more than once.</summary>
    public void Start() => _start.TrySetResult();

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
        try
        {
            await _start.Task.WaitAsync(FirstFrameLimit, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _trace.Phase("no first frame yet; the encoder probe starts anyway");
        }

        _trace.Phase("encoder probe: reading its cache");
        var cold = false;
        var capabilities = await EncoderProbe.LoadOrProbeAsync(
            cachePath,
            () =>
            {
                cold = true;
                _trace.Phase("encoder probe: no cache for this build and PC, probing cold");
            },
            cancellationToken).ConfigureAwait(false);
        _trace.Phase(cold ? "encoder probe answered after a cold probe" : "encoder probe answered from its cache");
        Volatile.Write(ref _loaded, capabilities);
        return new EncoderOutputCapabilities(capabilities);
    }
}
