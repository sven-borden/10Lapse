using Windows.Win32;

namespace TenLapse.Media.Inspection;

/// <summary>
/// One <c>MFStartup</c>/<c>MFShutdown</c> pair. Media Foundation counts them, so
/// nesting sessions, or running one next to a <c>MediaTranscoder</c>, is safe.
/// </summary>
internal sealed class MediaFoundationSession : IDisposable
{
    // MF_VERSION from mfapi.h; MFSTARTUP_FULL is 0.
    private const uint Version = (PInvoke.MF_SDK_VERSION << 16) | PInvoke.MF_API_VERSION;

    private bool _disposed;

    public MediaFoundationSession() => PInvoke.MFStartup(Version, 0).ThrowOnFailure();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        PInvoke.MFShutdown();
    }
}
