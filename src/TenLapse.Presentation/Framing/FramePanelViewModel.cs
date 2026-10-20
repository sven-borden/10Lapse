using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using TenLapse.Core.Projects;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation;

/// <summary>
/// The Frame section of the inspector (#18): the output shape (Match photos, 16:9, 9:16, 1:1, 4:5, 4:3), Fit or
/// Fill, and the suggestion of 9:16 for a mostly portrait set shown in a landscape shape. Every change is one write
/// to <see cref="Project.FrameSettings"/>, the one place the shape lives, so the preview and the export take it from
/// the next snapshot and nothing else is rebuilt; the Output panel follows the same event. All of it runs on the UI
/// thread, the one thread that may change a <see cref="Project"/>.
/// </summary>
/// <remarks>
/// The suggestion never changes the shape by itself. It shows when more than half of the photos with a known size are
/// portrait and the shape is 16:9 or 4:3, and it goes when the user applies it, dismisses it, or picks a shape that
/// is not landscape (they have decided; going back to 16:9 later does not bring it back). The choice is kept for the
/// project in this panel: <c>New project</c> starts afresh, and #21 can persist it with the project.
/// </remarks>
public sealed class FramePanelViewModel : ObservableObject, IDisposable
{
    private static readonly FrameAspect[] _aspects =
        [FrameAspect.MatchPhotos, FrameAspect.Ratio16x9, FrameAspect.Ratio9x16, FrameAspect.Ratio1x1, FrameAspect.Ratio4x5, FrameAspect.Ratio4x3];

    private static readonly FitMode[] _fits = [FitMode.Fit, FitMode.Fill];

    private static readonly string[] _shapeLabels = [.. _aspects.Select(FrameText.ShapeLabel)];

    private static readonly string[] _shapeNames = [.. _aspects.Select(FrameText.ShapeFullName)];

    private static readonly string[] _fitLabels = [FrameText.FitOption, FrameText.FillOption];

    private readonly ShellViewModel _shell;
    private readonly IErrorPresenter _errors;
    private readonly OutputPanelViewModel? _output;
    private Project _project;
    private int _shapeIndex;
    private int _fitIndex;
    private string _description = string.Empty;
    private string _matchLine = string.Empty;
    private bool _portraitHint;
    private bool _hintClosed;
    private bool _editing;
    private bool _disposed;
    private bool _noSize;

    /// <summary>Follows the shell's current project, also across <c>New project</c>.</summary>
    /// <param name="shell">The shell whose <see cref="ShellViewModel.CurrentProject"/> the panel edits.</param>
    /// <param name="errors">The error path: a failed edit is shown and logged.</param>
    /// <param name="output">
    /// The Output panel, when there is one: Match photos can give a shape that no size can encode (a 10:1 panorama),
    /// and the Frame panel is where the person can fix that, so it says so. The caller owns and disposes it.
    /// </param>
    public FramePanelViewModel(ShellViewModel shell, IErrorPresenter errors, OutputPanelViewModel? output = null)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(errors);
        _shell = shell;
        _errors = errors;
        _output = output;
        if (_output is not null)
        {
            _output.PropertyChanged += OnOutputPropertyChanged;
        }

        _project = shell.CurrentProject.Model;
        _project.Changed += OnProjectChanged;
        _shell.ProjectReplaced += OnProjectReplaced;
        Refresh();
    }

    /// <summary>Raised on the UI thread after the user changed the frame, or when the portrait suggestion appears with the photos: the sentence a screen reader should say.</summary>
    public event EventHandler<string>? Announced;

    /// <summary>The shapes as the segments show them: Match, 16:9, 9:16, 1:1, 4:5, 4:3.</summary>
    public IReadOnlyList<string> ShapeLabels => _shapeLabels;

    /// <summary>The shapes as a screen reader names them ("Match photos" for the first).</summary>
    public IReadOnlyList<string> ShapeNames => _shapeNames;

    /// <summary>The project's shape. Setting it edits the project.</summary>
    public int SelectedShapeIndex
    {
        get => _shapeIndex;
        set
        {
            if (value >= 0 && value < _aspects.Length && value != _shapeIndex)
            {
                var before = _project.FrameSettings;
                Edit(settings => settings with { Aspect = _aspects[value] }, closesHint: next => !IsLandscape(next.Aspect));
                AnnounceIfChanged(before);
            }
        }
    }

    /// <summary>Fit, Fill.</summary>
    public IReadOnlyList<string> FitLabels => _fitLabels;

    /// <summary>The project's placement. Setting it edits the project.</summary>
    public int SelectedFitIndex
    {
        get => _fitIndex;
        set
        {
            if (value >= 0 && value < _fits.Length && value != _fitIndex)
            {
                var before = _project.FrameSettings;
                Edit(settings => settings with { Fit = _fits[value] }, closesHint: next => !IsLandscape(next.Aspect));
                AnnounceIfChanged(before);
            }
        }
    }

    /// <summary>The line under the controls: what Fit or Fill does.</summary>
    public string Description => _description;

    /// <summary>With Match photos chosen, the size it takes ("Matching your photos: 4032 × 3024."); empty for every other shape.</summary>
    public string MatchLine => _matchLine;

    /// <summary>True when <see cref="MatchLine"/> has something to show.</summary>
    public bool HasMatchLine => _matchLine.Length > 0;

    /// <summary>True when Match photos gives a shape no size can be exported in on this PC (an extreme panorama or tall strip): <see cref="NoSizeLine"/> says what to do.</summary>
    public bool HasNoSizeLine => _noSize;

    /// <summary>"Photos this wide or tall are too large to export on this PC. Pick a fixed shape." Empty unless <see cref="HasNoSizeLine"/>.</summary>
    public string NoSizeLine => _noSize ? FrameText.NoSize : string.Empty;

    /// <summary>True while 9:16 is suggested for a mostly portrait set in a landscape shape.</summary>
    public bool HasPortraitHint => _portraitHint;

    /// <summary>Applies the suggestion: the shape becomes 9:16 (Fit or Fill stays) and the suggestion is closed.</summary>
    public void ApplyPortraitHint()
    {
        var before = _project.FrameSettings;
        Edit(settings => settings with { Aspect = FrameAspect.Ratio9x16 }, closesHint: _ => true);
        AnnounceIfChanged(before);
    }

    /// <summary>Closes the suggestion for this project without changing anything else.</summary>
    public void DismissPortraitHint()
    {
        _hintClosed = true;
        _errors.Guard(UpdateHint, FrameText.RefreshFailed);
    }

    /// <summary>Stops following the project.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_output is not null)
        {
            _output.PropertyChanged -= OnOutputPropertyChanged;
        }

        _project.Changed -= OnProjectChanged;
        _shell.ProjectReplaced -= OnProjectReplaced;
    }

    private void OnOutputPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OutputPanelViewModel.ShapeCannotBeExported))
        {
            UpdateNoSize();
        }
    }

    private void UpdateNoSize()
    {
        var wanted = _output is { ShapeCannotBeExported: true } && _project.FrameSettings.Aspect == FrameAspect.MatchPhotos;
        if (SetProperty(ref _noSize, wanted, nameof(HasNoSizeLine)))
        {
            OnPropertyChanged(nameof(NoSizeLine));
        }
    }

    private void OnProjectChanged(object? sender, ProjectChangedEventArgs e)
    {
        // The shape and the photos (their sizes decide Match photos and the suggestion); nothing else.
        if ((e.Changes & (ProjectChanges.Frames | ProjectChanges.FrameSettings)) != 0)
        {
            Refresh();
        }
    }

    private void OnProjectReplaced(object? sender, ProjectReplacedEventArgs e)
    {
        _project.Changed -= OnProjectChanged;
        _project = e.Current.Model;
        _project.Changed += OnProjectChanged;
        _hintClosed = false;
        Refresh();
    }

    // Writes the settings to the project. A closed project (the window is going away) takes no edits. closesHint says
    // whether the new settings end the suggestion for good (applied, or a shape the user picked that is not landscape);
    // it ends before the write so the write's own refresh already hides it, and comes back if the write fails.
    private void Edit(Func<FrameSettings, FrameSettings> change, Func<FrameSettings, bool> closesHint)
    {
        // Refresh runs inside the write; a suggestion it finds is announced with the edit, not on its own.
        _editing = true;
        try
        {
            _errors.Guard(
                () =>
                {
                    if (_project.IsClosed)
                    {
                        return;
                    }

                    var next = change(_project.FrameSettings);
                    var wasClosed = _hintClosed;
                    _hintClosed |= closesHint(next);
                    try
                    {
                        _project.FrameSettings = next;
                    }
                    catch
                    {
                        _hintClosed = wasClosed;
                        throw;
                    }
                },
                FrameText.ChangeFailed);
        }
        finally
        {
            _editing = false;
        }

        // A control that showed the pick but did not keep it (the write failed) goes back to the project's value.
        OnPropertyChanged(nameof(SelectedShapeIndex));
        OnPropertyChanged(nameof(SelectedFitIndex));
    }

    // A pick that did not change the frame (the write failed, or the same value again) says nothing.
    private void AnnounceIfChanged(FrameSettings before)
    {
        var settings = _project.FrameSettings;
        if (settings != before)
        {
            Announced?.Invoke(this, FrameText.Announce(settings.Aspect, settings.Fit));
        }
    }

    private void Refresh() =>
        _errors.Guard(
            () =>
            {
                var settings = _project.Snapshot.FrameSettings;

                SetProperty(ref _shapeIndex, Array.IndexOf(_aspects, settings.Aspect), nameof(SelectedShapeIndex));
                SetProperty(ref _fitIndex, Array.IndexOf(_fits, settings.Fit), nameof(SelectedFitIndex));
                SetProperty(ref _description, FrameText.Hint(settings.Fit), nameof(Description));

                var matchLine = settings.Aspect == FrameAspect.MatchPhotos ? MatchLineFor(_project.Snapshot) : string.Empty;
                if (SetProperty(ref _matchLine, matchLine, nameof(MatchLine)))
                {
                    OnPropertyChanged(nameof(HasMatchLine));
                }

                UpdateHint();
                UpdateNoSize();
            },
            FrameText.RefreshFailed);

    private static string MatchLineFor(ProjectSnapshot snapshot) =>
        OutputSize.MostCommonPhotoSize(snapshot.Frames) is { } photo
            ? FrameText.Format(CultureInfo.CurrentCulture, FrameText.MatchingPhotos, photo.Width, photo.Height)
            : FrameText.MatchingNoPhotos;

    // The suggestion: a landscape shape, and more than half of the photos with a known size are portrait.
    private void UpdateHint()
    {
        var snapshot = _project.Snapshot;
        var wanted = !_hintClosed && IsLandscape(snapshot.FrameSettings.Aspect) && MostlyPortrait(snapshot);
        if (wanted == _portraitHint)
        {
            return;
        }

        _portraitHint = wanted;
        OnPropertyChanged(nameof(HasPortraitHint));
        if (wanted && !_editing)
        {
            // It appears with the photos, where a screen reader would otherwise miss it.
            Announced?.Invoke(this, FrameText.PortraitHint);
        }
    }

    private static bool IsLandscape(FrameAspect aspect) => aspect is FrameAspect.Ratio16x9 or FrameAspect.Ratio4x3;

    private static bool MostlyPortrait(ProjectSnapshot snapshot)
    {
        var known = 0;
        var portrait = 0;
        foreach (var frame in snapshot.Frames)
        {
            if (frame.PixelSize is { } size)
            {
                known++;
                if (size.IsPortrait)
                {
                    portrait++;
                }
            }
        }

        return portrait * 2 > known;
    }
}
