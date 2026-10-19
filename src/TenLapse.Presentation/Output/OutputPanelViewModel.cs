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
/// Stored settings that are no longer offered (the project came from a PC that could do 4K; the frame rate went
/// up; the HEVC extension was removed) are replaced when the panel notices, by the largest size still offered or
/// by H.264, and <see cref="Notice"/> says so: nothing unsupported is ever left selected for #24 to export.
/// </remarks>
public sealed partial class OutputPanelViewModel : ObservableObject, IDisposable
{
    // A correction writes the project, which raises Changed and so refreshes again; this many passes always settles it.
    private const int MaxPasses = 4;

    private static readonly OutputResolution[] _resolutions = [.. Enum.GetValues<OutputResolution>().OrderBy(resolution => (int)resolution)];
    private static readonly VideoFormat[] _formats = [VideoFormat.Mp4H264, VideoFormat.Mp4Hevc];

    private static readonly OutputOption<OutputQuality>[] _qualities =
        [.. Enum.GetValues<OutputQuality>().Select(quality => new OutputOption<OutputQuality>(quality, OutputText.QualityOption(quality)))];

    private readonly ShellViewModel _shell;
    private readonly IErrorPresenter _errors;
    private Project _project;
    private IOutputCapabilities? _capabilities;
    private OutputOption<OutputResolution>[] _resolutionOptions = [];
    private OutputOption<VideoFormat>[] _formatOptions = [];
    private OutputOption<OutputResolution>? _selectedResolution;
    private OutputOption<VideoFormat>? _selectedFormat;
    private OutputOption<OutputQuality>? _selectedQuality;
    private string _estimatedSize = string.Empty;
    private string _note = string.Empty;
    private string _notice = string.Empty;
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

    /// <summary>The project's resolution when it is offered; null while pending or when nothing is offered. Setting it edits the project.</summary>
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

    /// <summary>The project's format when it is offered. Setting it edits the project.</summary>
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

    /// <summary>The project's quality. Setting it edits the project.</summary>
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
    /// What the panel changed on its own, in words ("4K can't be exported on this PC with these settings, so 1440p
    /// is selected."); empty until it does, and again after the user picks a value.
    /// </summary>
    public string Notice
    {
        get => _notice;
        private set
        {
            if (SetProperty(ref _notice, value))
            {
                OnPropertyChanged(nameof(HasNotice));
            }
        }
    }

    /// <summary>True when <see cref="Notice"/> has something to show.</summary>
    public bool HasNotice => _notice.Length > 0;

    /// <summary>
    /// True when the project's current output settings are confirmed exportable here: the probe has answered and the
    /// resolution and format are offered. #24 enables Export only then.
    /// </summary>
    public bool CanExport => _capabilities is not null && _selectedResolution is not null && _selectedFormat is not null;

    /// <summary>
    /// Hands over what the startup probe found (or <see cref="ConservativeOutputCapabilities"/> when it failed). Call
    /// it on the UI thread; a later call replaces the earlier answer. The lists appear in one step, and a stored
    /// setting this PC cannot do is replaced (see <see cref="Notice"/>).
    /// </summary>
    public void SetCapabilities(IOutputCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        _capabilities = capabilities;
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(IsSettled));
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
        if ((e.Changes & (ProjectChanges.Frames | ProjectChanges.Timing | ProjectChanges.FrameSettings | ProjectChanges.Output)) != 0)
        {
            Refresh();
        }
    }

    private void OnProjectReplaced(object? sender, ProjectReplacedEventArgs e)
    {
        _project.Changed -= OnProjectChanged;
        _project = e.Current.Model;
        _project.Changed += OnProjectChanged;
        Notice = string.Empty;
        Refresh();
    }

    // The user picked a value: write it to the project, say nothing more about earlier corrections. A closed project
    // (the window is going away) takes no edits.
    private void Choose(Func<OutputSettings, OutputSettings> change)
    {
        Notice = string.Empty;
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
                        var corrected = Update();
                        if (!corrected && !_again)
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

    // One pass: reads the project, replaces what is not offered (true when it wrote the project), publishes the rest.
    private bool Update()
    {
        var snapshot = _project.Snapshot;
        var output = snapshot.Output;
        if (_capabilities is not { } capabilities)
        {
            Publish(snapshot, [], [], capabilities: null);
            return false;
        }

        var shape = Shape(snapshot);
        var formats = _formats.Where(capabilities.IsAvailable).ToList();
        var corrected = output;
        var notices = new List<string>();
        if (formats.Count > 0 && !formats.Contains(output.Format))
        {
            var replacement = formats.Contains(VideoFormat.Mp4H264) ? VideoFormat.Mp4H264 : formats[0];
            notices.Add(string.Format(CultureInfo.CurrentCulture, OutputText.FormatReplaced, OutputText.CodecName(output.Format), OutputText.CodecName(replacement)));
            corrected = corrected with { Format = replacement };
        }

        var offered = _resolutions.Where(resolution => capabilities.Supports(corrected.Format, resolution, shape.Aspect, shape.Rate, shape.PhotoSize)).ToList();
        if (offered.Count > 0 && !offered.Contains(corrected.Resolution))
        {
            var replacement = offered[^1];
            notices.Add(string.Format(CultureInfo.CurrentCulture, OutputText.ResolutionReplaced, OutputText.ResolutionName(corrected.Resolution), OutputText.ResolutionName(replacement)));
            corrected = corrected with { Resolution = replacement };
        }

        if (corrected != output && !_project.IsClosed)
        {
            Notice = string.Join(' ', notices);
            _project.Output = corrected;
            return true;
        }

        Publish(snapshot, formats, offered, capabilities);
        return false;
    }

    // Match photos takes the shape of the most common photo; with no sizes read yet it is 16:9, as the video is.
    private static (FrameAspect Aspect, PixelSize? PhotoSize, Fraction Rate) Shape(ProjectSnapshot snapshot)
    {
        var aspect = snapshot.FrameSettings.Aspect;
        PixelSize? photo = null;
        if (aspect == FrameAspect.MatchPhotos)
        {
            photo = OutputSize.MostCommonPhotoSize(snapshot.Frames);
            if (photo is null)
            {
                aspect = FrameAspect.Ratio16x9;
            }
        }

        return (aspect, photo, snapshot.EffectiveRate.Rate);
    }

    private void Publish(ProjectSnapshot snapshot, List<VideoFormat> formats, List<OutputResolution> resolutions, IOutputCapabilities? capabilities)
    {
        var output = snapshot.Output;
        var shape = Shape(snapshot);

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

        // The selections are instances from the lists, so a ComboBox finds them.
        Select(ref _selectedResolution, _resolutionOptions.FirstOrDefault(option => option.Value == output.Resolution), nameof(SelectedResolution));
        Select(ref _selectedFormat, _formatOptions.FirstOrDefault(option => option.Value == output.Format), nameof(SelectedFormat));
        Select(ref _selectedQuality, _qualities.First(option => option.Value == output.Quality), nameof(SelectedQuality));
        OnPropertyChanged(nameof(CanExport));

        if (capabilities is null)
        {
            EstimatedSize = string.Empty;
            Note = string.Empty;
            return;
        }

        var offersEverything = Enum.GetValues<OutputResolution>().Length == resolutions.Count;
        var allFast = resolutions.All(resolution => capabilities.UsesHardware(output.Format, resolution, shape.Aspect, shape.Rate, shape.PhotoSize));
        Note = offersEverything && allFast ? string.Empty : capabilities.Summary(output.Format, shape.Aspect, shape.Rate, shape.PhotoSize);

        EstimatedSize = snapshot.Frames.Length > 0 && _selectedResolution is not null && _selectedFormat is not null
            ? OutputText.Estimate(ExportBitrate.EstimateBytes(snapshot.Frames.Length, shape.Rate, snapshot.VideoSize, output.Format, output.Quality))
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
