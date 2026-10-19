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

    /// <summary>
    /// True for a skipped file these prompts may speak for: it needs an extension and the cause is known. Not the
    /// whole decision: a file whose missing extension is a codec the PC has is left out by <see cref="Plan"/>.
    /// </summary>
    public static bool Covers(SkippedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return file.Reason == SkipReason.NeedsExtension && file.MissingExtensions.Count > 0;
    }

    /// <summary>
    /// Decides once which skipped files the prompts speak for and builds their InfoBars, one per distinct set of
    /// missing extensions, in the order the sets first appear. Asks the PC about each extension once. A file whose
    /// first extension is a codec (HEVC, AV1) is covered only when the PC does not have it: the classifier's
    /// "component missing" codes are broad, so a damaged or 10-bit HEIC on a PC with HEVC must not send the user to
    /// buy it, and stays in <see cref="CodecPromptPlan.NotCovered"/> with the other unreadable files. Blocking (it may
    /// list Media Foundation decoders): call it off the UI thread.
    /// </summary>
    public CodecPromptPlan Plan(IEnumerable<SkippedFile> skipped)
    {
        ArgumentNullException.ThrowIfNull(skipped);
        var installed = new Dictionary<PhotoCodecExtension, bool>();
        var groups = new List<(List<PhotoCodecExtension> Needed, int Count)>();
        var notCovered = new List<SkippedFile>();
        foreach (var file in skipped)
        {
            if (!Covers(file))
            {
                notCovered.Add(file);
                continue;
            }

            var first = file.MissingExtensions[0];
            if (first.IsCodec && Has(first))
            {
                // The PC has this codec, so it is not what stopped the file: the file is unreadable for another reason.
                notCovered.Add(file);
                continue;
            }

            // The first is known missing; each later one only if the PC does not have it.
            List<PhotoCodecExtension> needed = [first];
            needed.AddRange(file.MissingExtensions.Skip(1).Where(later => !Has(later)));

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

        return new CodecPromptPlan([.. groups.Select(group => Prompt(group.Needed, group.Count))], notCovered);

        bool Has(PhotoCodecExtension extension)
        {
            if (!installed.TryGetValue(extension, out var has))
            {
                installed[extension] = has = _codecs.IsInstalled(extension);
            }

            return has;
        }
    }

    /// <summary>The InfoBars of <see cref="Plan"/>. Blocking, like it.</summary>
    public IReadOnlyList<InfoBarRequest> Create(IEnumerable<SkippedFile> skipped) => Plan(skipped).Requests;

    /// <summary>Shows the InfoBars of <paramref name="plan"/>.</summary>
    public void Show(CodecPromptPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        foreach (var request in plan.Requests)
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
            _infoBars.Show(InfoBarSeverity.Caution, ProjectText.CodecStoreFallback(extension));
            return;
        }

        if (index + 1 < needed.Count)
        {
            _infoBars.Show(new InfoBarRequest(
                InfoBarSeverity.Caution,
                ProjectText.CodecNext(needed[index + 1]),
                ProjectText.CodecGetFromStore,
                () => OpenAsync(needed, index + 1),
                showsNext: true));
        }
    }
}

/// <summary>What <see cref="CodecExtensionPrompts.Plan"/> decided about the skipped files of one import.</summary>
/// <param name="Requests">The extension InfoBars, in order.</param>
/// <param name="NotCovered">The skipped files the InfoBars do not speak for: they belong in the generic "couldn't be read" list.</param>
public sealed record CodecPromptPlan(IReadOnlyList<InfoBarRequest> Requests, IReadOnlyList<SkippedFile> NotCovered);
