using System.Runtime.CompilerServices;
using TenLapse.Core.Import;
using TenLapse.Core.Projects;
using TenLapse.Media.Import;
using TenLapse.Presentation;
using TenLapse.Presentation.Services;
using TenLapse.Tests.Presentation;

namespace TenLapse.Tests.Import;

/// <summary>
/// An import source that hands out the listing batches it was given, in order, and reads each frame with
/// <see cref="Read"/>. A gate (<see cref="HoldListBatch"/>, <see cref="HoldReadBatch"/>) holds a batch back until the test
/// opens it, so a test can act between two batches.
/// </summary>
internal sealed class ScriptedImportSource(params IReadOnlyList<Frame>[] batches) : IPhotoImportSource
{
    private readonly Dictionary<int, TaskCompletionSource> _listGates = [];
    private readonly Dictionary<int, TaskCompletionSource> _readGates = [];

    /// <summary>What reading a frame gives: by default a 4x3 photo with no capture time.</summary>
    public Func<Frame, FrameReading> Read { get; set; } = frame => FrameReading.Read(frame with { PixelSize = new PixelSize(4, 3) });

    /// <summary>Frames per reading batch.</summary>
    public int ReadBatch { get; set; } = 2;

    /// <summary>Online-only photos the listing hands out in a batch of their own, after the others.</summary>
    public IReadOnlyList<Frame> CloudOnly { get; set; } = [];

    /// <summary>Every frame reading was asked for, in order.</summary>
    public List<Frame> ReadRequested { get; } = [];

    /// <summary>Raised (on the source's thread) just before reading batch n would be handed out and its gate waits.</summary>
    public TaskCompletionSource ReadBatchReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Raised once listing batch n has been handed out and the next one waits at its gate.</summary>
    public TaskCompletionSource ListBatchReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static Frame Photo(string name, long size = 1000) => new($@"C:\Scripted\{name}", size, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    public TaskCompletionSource HoldListBatch(int index) => _listGates[index] = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource HoldReadBatch(int index) => _readGates[index] = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsPhotoFile(string path) => path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase);

    public async IAsyncEnumerable<ImportListBatch> ListAsync(ImportRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        for (var i = 0; i < batches.Length; i++)
        {
            if (_listGates.TryGetValue(i, out var gate))
            {
                ListBatchReached.TrySetResult();
                await gate.Task.WaitAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            yield return new ImportListBatch(batches[i], []);
        }

        if (CloudOnly.Count > 0)
        {
            yield return new ImportListBatch([], []) { CloudOnly = CloudOnly };
        }
    }

    public async IAsyncEnumerable<IReadOnlyList<FrameReading>> ReadAsync(IReadOnlyList<Frame> frames, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        ReadRequested.AddRange(frames);
        var index = 0;
        foreach (var chunk in frames.Chunk(ReadBatch))
        {
            if (_readGates.TryGetValue(index, out var gate))
            {
                ReadBatchReached.TrySetResult();
                await gate.Task.WaitAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            yield return [.. chunk.Select(Read)];
            index++;
        }
    }
}

/// <summary>Answers the pickers with what the test set, and records where they were asked to open.</summary>
internal sealed class FakePhotoPicker : IPhotoPicker
{
    public string? Folder { get; set; }

    public IReadOnlyList<string> Photos { get; set; } = [];

    public List<string?> StartFolders { get; } = [];

    public Task<string?> PickFolderAsync(string? startFolder)
    {
        StartFolders.Add(startFolder);
        return Task.FromResult(Folder);
    }

    public Task<IReadOnlyList<string>> PickPhotosAsync(string? startFolder)
    {
        StartFolders.Add(startFolder);
        return Task.FromResult(Photos);
    }
}

/// <summary>A shell, its import view model and their fakes, all made on one <see cref="UiThread"/>.</summary>
internal sealed class ImportHarness : IDisposable
{
    private ImportHarness(UiThread ui, IPhotoImportSource source)
    {
        Ui = ui;
        Source = source;
        (Shell, Import) = ui.Run(() =>
        {
            var shell = new ShellViewModel(Dialogs, Errors);
            return (shell, new ImportViewModel(shell, source, Picker, Folders, ui, Dialogs, InfoBars, Errors));
        });
    }

    public UiThread Ui { get; }

    public IPhotoImportSource Source { get; }

    public FakeDialogService Dialogs { get; } = new(answer: true);

    public FakeErrorSink Errors { get; } = new();

    public FakeInfoBarService InfoBars { get; } = new();

    public FakePhotoPicker Picker { get; } = new();

    public SessionImportFolders Folders { get; } = new();

    public ShellViewModel Shell { get; }

    public ImportViewModel Import { get; }

    public Project Project => Shell.CurrentProject.Model;

    /// <summary>The real Media importer (the formats this PC reads).</summary>
    public static ImportHarness WithPhotoImporter() => new(new UiThread(), new PhotoImporter());

    public static ImportHarness With(IPhotoImportSource source) => new(new UiThread(), source);

    /// <summary>Imports into the current project, started on the UI thread, and waits for the end.</summary>
    public Task ImportAsync(params string[] paths) => Ui.RunAsync(() => Import.ImportAsync(paths));

    /// <summary>Drops on the window, and waits for the import it starts (if any) to end.</summary>
    public async Task DropAsync(params string[] paths)
    {
        await Ui.RunAsync(() => Import.DropAsync(paths));
        await Import.WhenIdleAsync();
    }

    /// <summary>Failures the error path showed, cancellations left out.</summary>
    public IReadOnlyList<Exception> RealFailures() =>
        [.. Errors.Shown.Select(shown => shown.Exception).Where(exception => !ErrorMessages.IsCancellation(exception))];

    public void Dispose() => Ui.Dispose();
}
