using TenLapse.Core.Projects;

namespace TenLapse.Presentation.Services;

/// <summary>
/// Explains how to get HEIC and RAW support (#14): after an import, the photos skipped because Windows has no decoder
/// for them become one InfoBar for each missing extension, never one per photo, with a button that opens its Store
/// page. A HEIC photo can need two: the HEIF decoder, then the HEVC codec inside it. One InfoBar then names both, its
/// button opens the first, and pressing it raises the next ("Next, get the HEVC Video Extensions").
/// </summary>
public sealed class CodecExtensionPrompts
{
    private readonly ICodecExtensions _codecs;
    private readonly IInfoBarService _infoBars;
    private readonly IErrorSink _errors;

    /// <param name="codecs">Asks the PC what is installed and opens the Store.</param>
    /// <param name="infoBars">Shows the prompts, and the follow-up and failure ones.</param>
    /// <param name="errors">Logs a failure to open the Store (the user hears it from an InfoBar, not from this).</param>
    public CodecExtensionPrompts(ICodecExtensions codecs, IInfoBarService infoBars, IErrorSink errors)
    {
        ArgumentNullException.ThrowIfNull(codecs);
        ArgumentNullException.ThrowIfNull(infoBars);
        ArgumentNullException.ThrowIfNull(errors);
        _codecs = codecs;
        _infoBars = infoBars;
        _errors = errors;
    }

    /// <summary>True for a skipped file these prompts speak for: it needs an extension and the cause is known.</summary>
    public static bool Covers(SkippedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return file.Reason == SkipReason.NeedsExtension && file.MissingExtensions.Count > 0;
    }

    /// <summary>
    /// The InfoBars for the files <see cref="Covers"/> in <paramref name="skipped"/>, one per distinct set of missing
    /// extensions, in the order the sets first appear. Asks the PC about each later extension once. Empty when no file
    /// is covered. Blocking (it may list Media Foundation decoders): call it off the UI thread.
    /// </summary>
    public IReadOnlyList<InfoBarRequest> Create(IEnumerable<SkippedFile> skipped)
    {
        ArgumentNullException.ThrowIfNull(skipped);
        var installed = new Dictionary<PhotoCodecExtension, bool>();
        var groups = new List<(List<PhotoCodecExtension> Needed, int Count)>();
        foreach (var file in skipped.Where(Covers))
        {
            // The first is known missing; each later one only if the PC does not have it.
            List<PhotoCodecExtension> needed = [file.MissingExtensions[0]];
            foreach (var later in file.MissingExtensions.Skip(1))
            {
                if (!installed.TryGetValue(later, out var has))
                {
                    installed[later] = has = _codecs.IsInstalled(later);
                }

                if (!has)
                {
                    needed.Add(later);
                }
            }

            var index = groups.FindIndex(group => group.Needed.SequenceEqual(needed));
            if (index < 0)
            {
                groups.Add((needed, 1));
            }
            else
            {
                groups[index] = (groups[index].Needed, groups[index].Count + 1);
            }
        }

        return [.. groups.Select(group => Prompt(group.Needed, group.Count))];
    }

    /// <summary>Shows the InfoBars <see cref="Create"/> makes. Off the UI thread, like <see cref="Create"/>.</summary>
    public void Show(IEnumerable<SkippedFile> skipped)
    {
        foreach (var request in Create(skipped))
        {
            _infoBars.Show(request);
        }
    }

    private InfoBarRequest Prompt(List<PhotoCodecExtension> needed, int count) => new(
        InfoBarSeverity.Caution,
        ProjectText.CodecNeeded(count, needed),
        ProjectText.CodecGetFromStore,
        () => OpenAsync(needed, 0));

    // Runs on the UI thread after the InfoBar has gone. A refusal or a failure to launch is told in words that name the
    // extension, so the user can search the Store themselves; the button never silently does nothing.
    private async Task OpenAsync(List<PhotoCodecExtension> needed, int index)
    {
        var extension = needed[index];
        var opened = false;
        try
        {
            opened = await _codecs.OpenStorePageAsync(extension).ConfigureAwait(true);
        }
        catch (Exception exception) when (!ErrorMessages.IsCancellation(exception) && !ErrorMessages.IsFatal(exception))
        {
            _errors.Report(exception, ProjectText.CodecStoreOpenFailed);
        }

        if (!opened)
        {
            _infoBars.Show(InfoBarSeverity.Critical, ProjectText.CodecStoreFallback(extension));
            return;
        }

        if (index + 1 < needed.Count)
        {
            _infoBars.Show(new InfoBarRequest(
                InfoBarSeverity.Caution,
                ProjectText.CodecNext(needed[index + 1]),
                ProjectText.CodecGetFromStore,
                () => OpenAsync(needed, index + 1)));
        }
    }
}
