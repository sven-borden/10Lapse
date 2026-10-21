using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TenLapse.Core.Projects;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation.ProjectFiles;

/// <summary>One project on the first-launch screen's recent list (#52).</summary>
public sealed partial class RecentProjectItem : ObservableObject
{
    internal RecentProjectItem(string path, IAsyncRelayCommand open)
    {
        Path = path;
        Name = System.IO.Path.GetFileNameWithoutExtension(path) is { Length: > 0 } name ? name : path;
        Folder = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
        OpenCommand = open;
    }

    /// <summary>The project file.</summary>
    public string Path { get; }

    /// <summary>The project's name: its file's name without the extension.</summary>
    public string Name { get; }

    /// <summary>The folder it is in, shown under the name.</summary>
    public string Folder { get; }

    /// <summary>Opens it (or, when it is gone, says so and takes it off the list).</summary>
    public IAsyncRelayCommand OpenCommand { get; }

    /// <summary>True once the file was looked for and is not there: the note says "Not found" in place of the folder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail), nameof(AutomationName))]
    public partial bool IsMissing { get; internal set; }

    /// <summary>The line under the name: the folder, or the "Not found" note.</summary>
    public string Detail => IsMissing ? ProjectFileText.RecentNotFound : Folder;

    /// <summary>What a screen reader calls it.</summary>
    public string AutomationName => ProjectFileText.RecentItemName(Name, Folder, IsMissing);
}

/// <summary>
/// The recent projects on the first-launch screen (#52): up to five, the most recent first, each a button that opens it.
/// Whether each file is still there is checked off the UI thread; one that is gone shows "Not found", and a click on it
/// says so and takes it off the list. <see cref="ClearCommand"/> empties the list (the files stay). On the UI thread.
/// </summary>
public sealed partial class RecentProjectsViewModel : ObservableObject, IDisposable
{
    // How long the check for the files may take before the list shows them all as there (an unplugged network drive).
    private static readonly TimeSpan _checkLimit = TimeSpan.FromSeconds(1);

    private readonly IRecentProjects _recent;
    private readonly ProjectFilesViewModel _files;
    private readonly IProjectFileSystem _disk;
    private readonly IInfoBarService _infoBars;
    private readonly IErrorPresenter _errors;
    private int _generation;

    /// <param name="recent">The list, kept with the app's settings.</param>
    /// <param name="files">Opens a project.</param>
    /// <param name="disk">Says whether each file is still there.</param>
    /// <param name="infoBars">Says when a file is gone.</param>
    /// <param name="errors">The commands go through it.</param>
    public RecentProjectsViewModel(IRecentProjects recent, ProjectFilesViewModel files, IProjectFileSystem disk, IInfoBarService infoBars, IErrorPresenter errors)
    {
        ArgumentNullException.ThrowIfNull(recent);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(disk);
        ArgumentNullException.ThrowIfNull(infoBars);
        ArgumentNullException.ThrowIfNull(errors);
        _recent = recent;
        _files = files;
        _disk = disk;
        _infoBars = infoBars;
        _errors = errors;
        ClearCommand = errors.Command(Clear, ProjectFileText.RecentClearFailedContext);
        _recent.RecentProjectsChanged += OnRecentProjectsChanged;
        Rebuild();
    }

    /// <summary>The projects, the most recent first.</summary>
    public ObservableCollection<RecentProjectItem> Items { get; } = [];

    /// <summary>True when there is at least one: the heading and the list show only then.</summary>
    [ObservableProperty]
    public partial bool HasItems { get; private set; }

    /// <summary>Clear list: empties the list; the files stay where they are.</summary>
    public IRelayCommand ClearCommand { get; }

    /// <summary>
    /// Raised when the control that had the focus left with the list (Clear list, or a missing project taken off): the
    /// index of the item to focus next, or null for the screen's Choose folder (DESIGN.md: focus moves with the work).
    /// </summary>
    public event EventHandler<int?>? FocusRequested;

    /// <summary>Where the focus goes after the item at <paramref name="removedIndex"/> left a list that now has <paramref name="remaining"/> items: the item now in its place, else the last one, else Choose folder (null).</summary>
    public static int? FocusAfterRemoval(int removedIndex, int remaining) =>
        remaining == 0 ? null : Math.Min(Math.Max(removedIndex, 0), remaining - 1);

    private void Clear()
    {
        _recent.ClearRecentProjects();
        FocusRequested?.Invoke(this, null);
    }

    /// <summary>
    /// Looks again whether each file is there (the first-launch screen calls it when it shows). The answer arrives on the
    /// UI thread; a check that takes longer than a second leaves the items as they are.
    /// </summary>
    public async Task RefreshAsync()
    {
        // Another 10Lapse window may have opened or saved a project since: a change rebuilds the list and checks again.
        var before = _generation;
        await _recent.RefreshRecentProjectsAsync();
        if (before != _generation)
        {
            return;
        }

        var generation = _generation;
        var items = Items.ToList();
        bool[] missing;
        try
        {
            missing = await Task.Run(() => items.Select(item => _disk.Probe(item.Path) is null).ToArray()).WaitAsync(_checkLimit);
        }
        catch (TimeoutException)
        {
            return;
        }

        if (generation != _generation)
        {
            return;
        }

        for (var i = 0; i < items.Count; i++)
        {
            items[i].IsMissing = missing[i];
        }
    }

    /// <summary>Stops following the list.</summary>
    public void Dispose() => _recent.RecentProjectsChanged -= OnRecentProjectsChanged;

    private void OnRecentProjectsChanged(object? sender, EventArgs e)
    {
        Rebuild();
        _ = _errors.GuardAsync(RefreshAsync, ProjectFileText.RecentRefreshFailedContext);
    }

    private void Rebuild()
    {
        _generation++;
        Items.Clear();
        foreach (var path in _recent.RecentProjects)
        {
            RecentProjectItem? item = null;
            item = new RecentProjectItem(path, _errors.AsyncCommand(() => OpenAsync(item!), ProjectFileText.RecentOpenFailedContext));
            Items.Add(item);
        }

        HasItems = Items.Count > 0;
    }

    private async Task OpenAsync(RecentProjectItem item)
    {
        if (await Task.Run(() => _disk.Probe(item.Path)) is null)
        {
            // Nobody else would find it again: say so and take it off the list, and the focus to its neighbour.
            var index = Items.IndexOf(item);
            _recent.RemoveRecentProject(item.Path);
            _infoBars.Show(InfoBarSeverity.Caution, ProjectFileText.RecentGone(item.Name));
            FocusRequested?.Invoke(this, FocusAfterRemoval(index, Items.Count));
            return;
        }

        await _files.OpenPathAsync(item.Path);
    }
}
