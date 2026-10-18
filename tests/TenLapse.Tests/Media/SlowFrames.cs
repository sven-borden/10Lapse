using System.Collections.Concurrent;
using Microsoft.Graphics.Canvas;
using TenLapse.Core.Projects;
using TenLapse.Media.Export;

namespace TenLapse.Tests.Media;

/// <summary>
/// Frames that take a while to load and, like a WIC decode, ignore cancellation once started. Counts loads
/// started and finished, and keeps any exception a load hit (a disposed device, for one).
/// </summary>
internal sealed class SlowFrames(int count, TimeSpan loadTime) : IFrameSource
{
    private int _started;
    private int _finished;

    /// <summary>Runs as load n starts, on the producing thread.</summary>
    public Action<int>? OnStart { get; init; }

    public int Started => Volatile.Read(ref _started);

    public int Finished => Volatile.Read(ref _finished);

    public ConcurrentQueue<Exception> Errors { get; } = new();

    public int FrameCount => count;

    public Task<CanvasBitmap> LoadAsync(int index, CanvasDevice device, PixelSize outputSize, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _started);
        OnStart?.Invoke(index);
        return Task.Run<CanvasBitmap>(
            async () =>
            {
                try
                {
                    await Task.Delay(loadTime, CancellationToken.None);
                    return new CanvasRenderTarget(device, 320, 180, 96f);
                }
                catch (Exception exception)
                {
                    Errors.Enqueue(exception);
                    throw;
                }
                finally
                {
                    Interlocked.Increment(ref _finished);
                }
            },
            CancellationToken.None);
    }
}
