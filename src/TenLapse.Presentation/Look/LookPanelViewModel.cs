using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TenLapse.Core.Projects;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation.Look;

/// <summary>
/// The Look section of the inspector (#45, the canvas's Look section): the Deflicker switch, its strength, the progress
/// of the exposure pass with Cancel, and the Before and After switch under the preview. Every change is one write to
/// <see cref="Project.Look"/>; the preview and the export take the correction from the next snapshot. UI thread.
/// </summary>
/// <remarks>
/// Cancel stops the measuring. When the pass is the one turning deflicker on started, it also turns deflicker off: the
/// user is taking back the switch they just turned, and turning it on again goes on where the pass stopped (the
/// measurements made are kept). A pass that started by itself (a project opened or photos imported with deflicker on)
/// stops without touching the project, so the user's setting and the saved state stay as they were: the photos left
/// take their correction from their measured neighbours, and "3 photos not measured" with Measure shows until they are
/// measured. Before and After is the preview's alone, never saved and never exported: it shows the photos without the
/// correction while it is on Before, and goes back to After whenever deflicker is turned off.
/// </remarks>
public sealed class LookPanelViewModel : ObservableObject, IDisposable
{
    private static readonly string[] _compareLabels = [LookText.CompareBefore, LookText.CompareAfter];

    private readonly ShellViewModel _shell;
    private readonly IErrorPresenter _errors;
    private Project _project;
    private bool _deflicker;
    private int _strength = LookSettings.MaxStrength;
    private int _compareIndex = 1;
    private bool _disposed;

    /// <param name="shell">The shell whose current project the panel edits.</param>
    /// <param name="analysis">The exposure pass, whose progress the panel shows; the caller owns it.</param>
    /// <param name="errors">The error path: a failed edit is shown and logged.</param>
    public LookPanelViewModel(ShellViewModel shell, ExposureAnalysis analysis, IErrorPresenter errors)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(errors);
        _shell = shell;
        Analysis = analysis;
        _errors = errors;
        CancelMeasuringCommand = errors.Command(CancelMeasuring, LookText.ChangeFailed);
        MeasureCommand = errors.Command(analysis.MeasureRemaining, LookText.MeasureFailedContext);
        _project = shell.CurrentProject.Model;
        _project.Changed += OnProjectChanged;
        _shell.ProjectReplaced += OnProjectReplaced;
        Analysis.PropertyChanged += OnAnalysisPropertyChanged;
        Refresh();
    }

    /// <summary>The exposure pass the panel shows.</summary>
    public ExposureAnalysis Analysis { get; }

    /// <summary>Raised when <see cref="ShowsOriginal"/> changes: the preview draws again.</summary>
    public event EventHandler? ShowsOriginalChanged;

    /// <summary>Deflicker on or off. Setting it edits the project.</summary>
    public bool IsDeflickerOn
    {
        get => _deflicker;
        set
        {
            if (value != _deflicker)
            {
                Edit(look => look with { Deflicker = value });
            }
        }
    }

    /// <summary>The strength in percent, 0 to 100, as the slider's value; rounded to a whole percent. Setting it edits the project.</summary>
    public double Strength
    {
        get => _strength;
        set
        {
            var strength = (int)Math.Round(Math.Clamp(double.IsFinite(value) ? value : _strength, LookSettings.MinStrength, LookSettings.MaxStrength));
            if (strength != _strength)
            {
                Edit(look => look with { Strength = strength });
            }
        }
    }

    /// <summary>The strength as text, "80%".</summary>
    public string StrengthText => LookText.Percent(_strength);

    /// <summary>True while photos are being measured: the progress and Cancel show.</summary>
    public bool IsMeasuring => Analysis.IsMeasuring;

    /// <summary>"Measuring 1,240 of 5,000 photos…".</summary>
    public string MeasuringText => Analysis.ProgressText;

    /// <summary>The share measured, 0 to 100.</summary>
    public double MeasuringPercent => Analysis.ProgressPercent;

    /// <summary>Stops the measuring (and turns deflicker off when the pass is the one turning it on started).</summary>
    public IRelayCommand CancelMeasuringCommand { get; }

    /// <summary>True when "3 photos not measured" and Measure show: deflicker is on, nothing is measured, and some photos have no measurement.</summary>
    public bool HasUnmeasured => _deflicker && !Analysis.IsMeasuring && Analysis.Unmeasured > 0;

    /// <summary>"3 photos not measured".</summary>
    public string UnmeasuredText => Analysis.Unmeasured > 0 ? LookText.NotMeasuredMessage(Analysis.Unmeasured) : string.Empty;

    /// <summary>Measures the photos not measured yet.</summary>
    public IRelayCommand MeasureCommand { get; }

    /// <summary>Before, After: the parts of the switch under the preview.</summary>
    public IReadOnlyList<string> CompareLabels => _compareLabels;

    /// <summary>0 for Before (the photos without deflicker), 1 for After. Two-way with the switch.</summary>
    public int CompareIndex
    {
        get => _compareIndex;
        set
        {
            if (value is 0 or 1 && value != _compareIndex)
            {
                var before = ShowsOriginal;
                _compareIndex = value;
                OnPropertyChanged();
                RaiseIfOriginalChanged(before);
            }
        }
    }

    /// <summary>True while the preview should draw the photos without deflicker: on, and Before chosen.</summary>
    public bool ShowsOriginal => _deflicker && _compareIndex == 0;

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
        Analysis.PropertyChanged -= OnAnalysisPropertyChanged;
    }

    private void CancelMeasuring()
    {
        if (Analysis.Cancel() && _deflicker)
        {
            Edit(look => look with { Deflicker = false });
        }
    }

    private void OnAnalysisPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ExposureAnalysis.IsMeasuring):
                OnPropertyChanged(nameof(IsMeasuring));
                OnPropertyChanged(nameof(HasUnmeasured));
                break;
            case nameof(ExposureAnalysis.Unmeasured):
                OnPropertyChanged(nameof(UnmeasuredText));
                OnPropertyChanged(nameof(HasUnmeasured));
                break;
            case nameof(ExposureAnalysis.ProgressText):
                OnPropertyChanged(nameof(MeasuringText));
                break;
            case nameof(ExposureAnalysis.ProgressPercent):
                OnPropertyChanged(nameof(MeasuringPercent));
                break;
        }
    }

    private void OnProjectChanged(object? sender, ProjectChangedEventArgs e)
    {
        if ((e.Changes & ProjectChanges.Look) != 0)
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

    // A closed project (the window is going away) takes no edits. A control that showed a value the project did not
    // take (the write failed) goes back to the project's.
    private void Edit(Func<LookSettings, LookSettings> change)
    {
        _errors.Guard(
            () =>
            {
                if (!_project.IsClosed)
                {
                    _project.Look = change(_project.Look);
                }
            },
            LookText.ChangeFailed);
        Refresh();
        OnPropertyChanged(nameof(IsDeflickerOn));
        OnPropertyChanged(nameof(Strength));
    }

    private void Refresh() =>
        _errors.Guard(
            () =>
            {
                var look = _project.Snapshot.Look;
                var before = ShowsOriginal;
                if (SetProperty(ref _deflicker, look.Deflicker, nameof(IsDeflickerOn)))
                {
                    OnPropertyChanged(nameof(HasUnmeasured));
                    if (!look.Deflicker)
                    {
                        // Off: the next time it is on, the preview starts on After.
                        _compareIndex = 1;
                        OnPropertyChanged(nameof(CompareIndex));
                    }
                }

                if (SetProperty(ref _strength, look.Strength, nameof(Strength)))
                {
                    OnPropertyChanged(nameof(StrengthText));
                }

                RaiseIfOriginalChanged(before);
            },
            LookText.RefreshFailed);

    private void RaiseIfOriginalChanged(bool before)
    {
        if (ShowsOriginal != before)
        {
            OnPropertyChanged(nameof(ShowsOriginal));
            ShowsOriginalChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
