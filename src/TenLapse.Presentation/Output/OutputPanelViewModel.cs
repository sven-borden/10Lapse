using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation;

/// <summary>
/// The Output section of the inspector (#23): resolution, format and quality, and the estimated file size. It offers
/// only what <see cref="IOutputCapabilities"/> confirms for the project's format, shape and frame rate: a size this
/// PC cannot encode is left out of the list, never shown disabled, and one note says why. Until the startup probe
/// has answered (<see cref="SetCapabilities"/>) it offers nothing and says so quietly. Every change goes through
/// <see cref="Project.Output"/>, so it marks the project as having unexported changes like any other edit. All of
/// it runs on the UI thread, the one thread that may change a <see cref="Project"/>.
/// </summary>
/// <remarks>
/// <see cref="Project.Output"/> is the user's preference and changes only when the user picks something here (or
/// presses <see cref="UseSelected"/>). What the lists show as selected, and what an export must use, is the
/// <see cref="EffectiveOutput"/>: the preference made to fit the current shape, rate and capabilities by
/// <see cref="OutputPlan"/> (a format that is not offered becomes H.264, a resolution that is not offered becomes the
/// largest offered one not above it), with <see cref="Notice"/> naming each replacement and why. Nothing is ever
/// written for a replacement, on a real answer or a provisional one, so when the shape or the capabilities change
/// back, the preference returns by itself and the notice goes.
/// </remarks>
public sealed partial class OutputPanelViewModel : ObservableObject, IDisposable
{
    // Publishing can make a ComboBox push a value back, which refreshes again; this many passes always settles it.
    private const int MaxPasses = 4;

    private static readonly OutputOption<OutputQuality>[] _qualities =
        [.. Enum.GetValues<OutputQuality>().Select(quality => new OutputOption<OutputQuality>(quality, OutputText.QualityOption(quality)))];

    private static readonly OutputOption<GifFrameRate>[] _gifRates =
        [.. Enum.GetValues<GifFrameRate>().Select(rate => new OutputOption<GifFrameRate>(rate, OutputText.GifRateOption(rate)))];

    private readonly ShellViewModel _shell;
    private readonly IErrorPresenter _errors;
    private Project _project;
    private IOutputCapabilities? _capabilities;
    private OutputOption<OutputResolution>[] _resolutionOptions = [];
    private OutputOption<VideoFormat>[] _formatOptions = [];
    private OutputOption<GifSize>[] _gifSizeOptions = [];
    private OutputOption<OutputResolution>? _selectedResolution;
    private OutputOption<VideoFormat>? _selectedFormat;
    private OutputOption<OutputQuality>? _selectedQuality;
    private OutputOption<GifSize>? _selectedGifSize;
    private OutputOption<GifFrameRate>? _selectedGifRate;
    private bool _isGif;
    private string _estimatedSize = string.Empty;
    private string _note = string.Empty;
    private string _notice = string.Empty;
    private OutputPlan? _plan;
    private bool _refreshing;
    private bool _again;
    private bool _disposed;

    /// <summary>Follows the shell's current project, also across <c>New project</c>.</summary>
    /// <param name="shell">The shell whose <see cref="ShellViewModel.CurrentProject"/> the panel edits.</param>
    /// <param name="errors">The error path: a failed edit is shown and logged.</param>
    public OutputPanelViewModel(ShellViewModel shell, IErrorPresenter errors)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(errors);
        _shell = shell;
        _errors = errors;
        _project = shell.CurrentProject.Model;
        _project.Changed += OnProjectChanged;
        _shell.ProjectReplaced += OnProjectReplaced;
        Refresh();
    }

    /// <summary>True until the startup probe has answered: the lists are empty and <see cref="Status"/> says it is checking.</summary>
    public bool IsPending => _capabilities is null;

    /// <summary>True once the lists show what this PC confirmed.</summary>
    public bool IsSettled => _capabilities is not null;

    /// <summary>The resolutions this PC can encode for the project's format, shape and rate, smallest first.</summary>
    public IReadOnlyList<OutputOption<OutputResolution>> Resolutions => _resolutionOptions;

    /// <summary>The formats this PC offers: H.264, and HEVC only when it can be encoded and played back.</summary>
    public IReadOnlyList<OutputOption<VideoFormat>> Formats => _formatOptions;

    /// <summary>Standard, High and Maximum (always the same three).</summary>
    public IReadOnlyList<OutputOption<OutputQuality>> Qualities => _qualities;

    /// <summary>The resolution shown as selected (the effective one); null while pending or when nothing is offered. Setting it stores the pick as the project's resolution.</summary>
    public OutputOption<OutputResolution>? SelectedResolution
    {
        get => _selectedResolution;
        set
        {
            if (value is not null && !Equals(value, _selectedResolution))
            {
                Choose(output => output with { Resolution = value.Value });
            }
        }
    }

    /// <summary>The format shown as selected (the effective one). Setting it stores the pick as the project's format.</summary>
    public OutputOption<VideoFormat>? SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (value is not null && !Equals(value, _selectedFormat))
            {
                Choose(output => output with { Format = value.Value });
            }
        }
    }

    /// <summary>The project's quality. Setting it stores the pick.</summary>
    public OutputOption<OutputQuality>? SelectedQuality
    {
        get => _selectedQuality;
        set
        {
            if (value is not null && !Equals(value, _selectedQuality))
            {
                Choose(output => output with { Quality = value.Value });
            }
        }
    }

    /// <summary>The GIF sizes (#51), Small to Large, each with its pixel size for the project's shape; empty while pending.</summary>
    public IReadOnlyList<OutputOption<GifSize>> GifSizes => _gifSizeOptions;

    /// <summary>The GIF frame rate presets (always the same three).</summary>
    public IReadOnlyList<OutputOption<GifFrameRate>> GifRates => _gifRates;

    /// <summary>The project's GIF size. Setting it stores the pick.</summary>
    public OutputOption<GifSize>? SelectedGifSize
    {
        get => _selectedGifSize;
        set
        {
            if (value is not null && !Equals(value, _selectedGifSize))
            {
                Choose(output => output with { Gif = output.Gif with { Size = value.Value } });
            }
        }
    }

    /// <summary>The project's GIF frame rate preset. Setting it stores the pick.</summary>
    public OutputOption<GifFrameRate>? SelectedGifRate
    {
        get => _selectedGifRate;
        set
        {
            if (value is not null && !Equals(value, _selectedGifRate))
            {
                Choose(output => output with { Gif = output.Gif with { Rate = value.Value } });
            }
        }
    }

    /// <summary>True when the selected format is an animated GIF: its size and rate lists show, resolution and quality are absent.</summary>
    public bool IsGif
    {
        get => _isGif;
        private set
        {
            if (SetProperty(ref _isGif, value))
            {
                OnPropertyChanged(nameof(ShowsVideoOptions));
                OnPropertyChanged(nameof(ShowsGifOptions));
            }
        }
    }

    /// <summary>True when the resolution and quality lists show: settled, with a video format.</summary>
    public bool ShowsVideoOptions => IsSettled && !IsGif;

    /// <summary>True when the GIF size and frame rate lists show: settled, with GIF.</summary>
    public bool ShowsGifOptions => IsSettled && IsGif;

    /// <summary>"Checking what this PC can encode…" while pending, otherwise empty.</summary>
    public string Status => IsPending ? OutputText.Checking : string.Empty;

    /// <summary>"About 180 MB", or empty without photos, while pending, or when nothing is offered.</summary>
    public string EstimatedSize
    {
        get => _estimatedSize;
        private set
        {
            if (SetProperty(ref _estimatedSize, value))
            {
                OnPropertyChanged(nameof(HasEstimate));
            }
        }
    }

    /// <summary>True when <see cref="EstimatedSize"/> has something to show.</summary>
    public bool HasEstimate => _estimatedSize.Length > 0;

    /// <summary>
    /// The one line under the lists when sizes are left out or only the slower software encoder takes them
    /// ("This PC's graphics hardware exports up to 1440p."); empty when everything is offered at full speed.
    /// </summary>
    public string Note
    {
        get => _note;
        private set
        {
            if (SetProperty(ref _note, value))
            {
                OnPropertyChanged(nameof(HasNote));
            }
        }
    }

    /// <summary>True when <see cref="Note"/> has something to show.</summary>
    public bool HasNote => _note.Length > 0;

    /// <summary>
    /// What the panel shows in place of the stored choice, and why ("4K isn't available for this shape on this PC, so
    /// 1440p is selected."); empty when the stored choice is what is selected. It goes by itself when the shape or
    /// the capabilities allow the stored choice again, or when the user stores the shown one.
    /// </summary>
    public string Notice
    {
        get => _notice;
        private set
        {
            if (SetProperty(ref _notice, value))
            {
                OnPropertyChanged(nameof(HasNotice));
                OnPropertyChanged(nameof(HasReplacement));
            }
        }
    }

    /// <summary>True when <see cref="Notice"/> has something to show.</summary>
    public bool HasNotice => _notice.Length > 0;

    /// <summary>True while the selected settings differ from the stored ones, so <see cref="UseSelected"/> has something to store.</summary>
    public bool HasReplacement => _plan?.IsReplacing == true;

    /// <summary>
    /// The settings an export must use: the stored preference made to fit this PC, the frame shape and the rate
    /// (see <see cref="OutputPlan"/>). Null while the probe is pending and when nothing is offered. #24 exports with
    /// this, never with <see cref="Project.Output"/>, and hides Export when it is null (<see cref="CanExport"/>).
    /// </summary>
    public OutputSettings? EffectiveOutput => _plan?.Effective;

    /// <summary>
    /// True when Match photos gives a shape no offered size can encode (a 10:1 panorama) although a fixed shape
    /// would export: the Frame panel says so and suggests a fixed shape.
    /// </summary>
    public bool ShapeCannotBeExported => _plan?.ShapeLimits == true;

    /// <summary>
    /// Stores the selected settings as the project's choice, for a user who wants the replacement to stay. It is the
    /// way to pick the entry that is already displayed, which a ComboBox does not report. Does nothing without a replacement.
    /// </summary>
    public void UseSelected()
    {
        if (_plan is { Effective: { } effective } plan && plan.IsReplacing)
        {
            Choose(_ => effective);
        }
    }

    /// <summary>
    /// True when something can be exported: an answer has arrived (even a provisional one) and a format and a
    /// resolution are offered, so <see cref="EffectiveOutput"/> exists. It says nothing about frames: #24 must also
    /// require at least one.
    /// </summary>
    public bool CanExport => _plan?.Effective is not null;

    /// <summary>
    /// Hands over what the startup probe found (or <see cref="ConservativeOutputCapabilities"/> when it failed). Call
    /// it on the UI thread; a later call replaces the earlier answer. The lists appear in one step, and a stored
    /// setting this PC cannot do is shown replaced (see <see cref="Notice"/>), never written: that holds for the
    /// conservative stand-in (the probe is slow or failed) and the real answer alike, so a later answer brings the
    /// stored setting back.
    /// </summary>
    /// <param name="capabilities">What this PC can encode.</param>
    public void SetCapabilities(IOutputCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        _capabilities = capabilities;
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(IsSettled));
        OnPropertyChanged(nameof(ShowsVideoOptions));
        OnPropertyChanged(nameof(ShowsGifOptions));
        OnPropertyChanged(nameof(Status));
        Refresh();
    }

    /// <summary>Stops following the project.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _project.Changed -= OnProjectChanged;
        _shell.ProjectReplaced -= OnProjectReplaced;
    }

    private void OnProjectChanged(object? sender, ProjectChangedEventArgs e)
    {
        // Only what the lists and the estimate depend on; renaming the project or marking it dirty changes neither.
        if ((e.Changes & (ProjectChanges.Frames | ProjectChanges.Timing | ProjectChanges.Sequence | ProjectChanges.FrameSettings | ProjectChanges.Output)) != 0)
        {
            Refresh();
        }
    }

    private void OnProjectReplaced(object? sender, ProjectReplacedEventArgs e)
    {
        _project.Changed -= OnProjectChanged;
        _project = e.Current.Model;
        _project.Changed += OnProjectChanged;
        Refresh();
    }

    // The user picked a value: write it to the project; change gets the stored preference, so what the user did not
    // touch stays as it was. A closed project (the window is going away) takes no edits.
    private void Choose(Func<OutputSettings, OutputSettings> change)
    {
        _errors.Guard(
            () =>
            {
                if (!_project.IsClosed)
                {
                    _project.Output = change(_project.Output);
                }
            },
            OutputText.ChangeFailed);

        // A list that showed the pick but did not keep it (the write failed) goes back to the project's value.
        OnPropertyChanged(nameof(SelectedResolution));
        OnPropertyChanged(nameof(SelectedFormat));
        OnPropertyChanged(nameof(SelectedQuality));
        OnPropertyChanged(nameof(SelectedGifSize));
        OnPropertyChanged(nameof(SelectedGifRate));
    }

    private void Refresh()
    {
        if (_refreshing)
        {
            _again = true;
            return;
        }

        _refreshing = true;
        try
        {
            _errors.Guard(
                () =>
                {
                    for (var pass = 0; pass < MaxPasses; pass++)
                    {
                        _again = false;
                        Update();
                        if (!_again)
                        {
                            break;
                        }
                    }
                },
                OutputText.RefreshFailed);
        }
        finally
        {
            _refreshing = false;
        }
    }

    // One pass: reads the project and publishes what it and this PC allow. Writes nothing.
    private void Update()
    {
        var snapshot = _project.Snapshot;
        if (_capabilities is not { } capabilities)
        {
            Publish(snapshot, plan: null, capabilities: null);
            return;
        }

        Publish(snapshot, OutputPlan.Resolve(snapshot, capabilities), capabilities);
    }

    private void Publish(ProjectSnapshot snapshot, OutputPlan? plan, IOutputCapabilities? capabilities)
    {
        _plan = plan;
        var output = plan?.Effective ?? snapshot.Output;
        var shape = OutputPlan.ShapeOf(snapshot);
        var formats = plan?.Formats ?? [];
        var resolutions = plan?.Resolutions ?? [];

        // A list is replaced only when its entries differ, so the open dropdown and the selection do not flicker.
        var resolutionOptions = resolutions
            .Select(resolution => new OutputOption<OutputResolution>(resolution, OutputText.ResolutionOption(resolution, OutputSize.For(resolution, shape.Aspect, shape.PhotoSize))))
            .ToArray();
        if (!resolutionOptions.SequenceEqual(_resolutionOptions))
        {
            _resolutionOptions = resolutionOptions;
            OnPropertyChanged(nameof(Resolutions));
        }

        var formatOptions = formats.Select(format => new OutputOption<VideoFormat>(format, OutputText.FormatOption(format))).ToArray();
        if (!formatOptions.SequenceEqual(_formatOptions))
        {
            _formatOptions = formatOptions;
            OnPropertyChanged(nameof(Formats));
        }

        // GIF sizes are offered while the answer is in, whatever the format, so switching to GIF finds them ready.
        var gifSizeOptions = plan is null
            ? []
            : Enum.GetValues<GifSize>().Select(size => new OutputOption<GifSize>(size, OutputText.GifSizeOption(size, GifOutput.Size(size, shape.Aspect, shape.PhotoSize)))).ToArray();
        if (!gifSizeOptions.SequenceEqual(_gifSizeOptions))
        {
            _gifSizeOptions = gifSizeOptions;
            OnPropertyChanged(nameof(GifSizes));
        }

        // The selections are instances from the lists, so a ComboBox finds them.
        Select(ref _selectedResolution, _resolutionOptions.FirstOrDefault(option => option.Value == output.Resolution), nameof(SelectedResolution));
        Select(ref _selectedFormat, _formatOptions.FirstOrDefault(option => option.Value == output.Format), nameof(SelectedFormat));
        Select(ref _selectedQuality, _qualities.First(option => option.Value == output.Quality), nameof(SelectedQuality));
        Select(ref _selectedGifSize, _gifSizeOptions.FirstOrDefault(option => option.Value == output.Gif.Size), nameof(SelectedGifSize));
        Select(ref _selectedGifRate, _gifRates.First(option => option.Value == output.Gif.Rate), nameof(SelectedGifRate));
        IsGif = plan?.IsGif == true;
        Notice = plan?.Notice ?? string.Empty;
        OnPropertyChanged(nameof(HasReplacement));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(EffectiveOutput));
        OnPropertyChanged(nameof(ShapeCannotBeExported));

        if (capabilities is null || plan is null)
        {
            EstimatedSize = string.Empty;
            Note = string.Empty;
            return;
        }

        if (plan.IsGif)
        {
            // The GIF's own estimate (a range: GIF size depends on the photos far more than a video's), and a note only
            // when the video is slower than the preset, so the GIF keeps the video's speed.
            var videoRate = shape.Rate;
            var gifRate = GifOutput.EffectiveRate(videoRate, output.Gif.Rate);
            Note = gifRate < GifOutput.Rate(output.Gif.Rate) ? OutputText.GifKeepsSpeed(videoRate) : string.Empty;
            // The GIF covers the video's frames (#49: holds and the way back included); a hold is stored as one frame.
            EstimatedSize = snapshot.Frames.Length > 0
                ? OutputText.GifEstimate(GifOutput.EstimateBytes(GifOutput.StoredFrames(snapshot.Playback, videoRate, gifRate), GifOutput.Size(output.Gif.Size, shape.Aspect, shape.PhotoSize)))
                : string.Empty;
            return;
        }

        var offersEverything = Enum.GetValues<OutputResolution>().Length == resolutions.Count;
        var allFast = resolutions.All(resolution => capabilities.UsesHardware(output.Format, resolution, shape.Aspect, shape.Rate, shape.PhotoSize));
        Note = offersEverything && allFast ? string.Empty : capabilities.Summary(output.Format, shape.Aspect, shape.Rate, shape.PhotoSize);

        // The estimate is for the video the effective settings make, not the stored ones, as long as it plays (#49).
        EstimatedSize = snapshot.Frames.Length > 0 && plan.Effective is { } effective
            ? OutputText.Estimate(ExportBitrate.EstimateBytes(snapshot.Playback.Length, shape.Rate, OutputSize.For(effective.Resolution, shape.Aspect, shape.PhotoSize), effective.Format, effective.Quality))
            : string.Empty;
    }

    private void Select<T>(ref OutputOption<T>? field, OutputOption<T>? value, string name)
        where T : struct, Enum
    {
        if (!ReferenceEquals(field, value))
        {
            field = value;
            OnPropertyChanged(name);
        }
    }
}
