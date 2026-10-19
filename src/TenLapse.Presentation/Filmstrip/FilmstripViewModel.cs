using System.Collections.Immutable;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TenLapse.Core.Ordering;
using TenLapse.Core.Projects;
using TenLapse.Presentation.Preview;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation.Filmstrip;

/// <summary>What a key did in the strip, and which thumbnail should have the focus afterwards.</summary>
/// <param name="Handled">The key was the strip's.</param>
/// <param name="Focus">The index of the thumbnail to focus; -1 for none.</param>
public readonly record struct FilmstripKeyResult(bool Handled, int Focus);

/// <summary>
/// The filmstrip (#20): one item per frame of the shell's current project, in playback order, kept up to date from the
/// project's change deltas; the selection; the playhead (the frame the preview shows, from <see cref="PlayerViewModel"/>);
/// removal and undo; reorder by drag and by Alt+arrow; the gap markers of <see cref="FrameGaps"/>; and the thumbnail size.
/// On the UI thread.
/// </summary>
/// <remarks>
/// <para>
/// <b>Selection.</b> A click selects the frame alone and shows it; Ctrl+click adds or takes out one frame, Shift+click
/// selects from the anchor (the last frame clicked without Shift) to the clicked one, Ctrl+Shift+click adds that range.
/// The selection is never empty while there are frames: with nothing else selected it is the frame at the playhead, and
/// a selection of that one frame follows the playhead (playing, stepping, scrubbing), so Delete always removes what the
/// preview shows unless the user picked other frames. A selection of several frames stays where it is.
/// </para>
/// <para>
/// <b>Undo.</b> <see cref="FrameHistory"/> records removals and reorders made here; Ctrl+Z undoes the latest as one
/// project change and selects the frames it brought back or moved back. Nothing else is undoable (see FrameHistory).
/// </para>
/// <para>
/// <b>Reorder.</b> A drop or Alt+Left/Right moves the selected frames as one block (<see cref="FrameMoves.Move"/>) and
/// switches the sort to manual (#15's <c>ReorderManually</c>, through the history). A drop that leaves the order as it
/// was changes nothing, the sort mode included.
/// </para>
/// </remarks>
public sealed partial class FilmstripViewModel : ObservableObject, IDisposable
{
    /// <summary>The smallest thumbnail height (DIPs): about 18 thumbnails across the stage of a 1280 px window, as the canvas shows.</summary>
    public const double MinThumbnailHeight = 32;

    /// <summary>The largest thumbnail height (DIPs).</summary>
    public const double MaxThumbnailHeight = 96;

    /// <summary>The slider's step (DIPs), on the 4 px grid.</summary>
    public const double ThumbnailHeightStep = 8;

    // Above this many frames added, removed or moved at once, the list is replaced with one reset instead of one
    // notification per frame (a sort, an import batch of 500).
    private const int IncrementalLimit = 64;

    private readonly ShellViewModel _shell;
    private readonly PlayerViewModel _player;
    private readonly IErrorPresenter _errors;
    private readonly Dictionary<Guid, FilmstripItem> _byId = [];
    private readonly HashSet<Guid> _selected = [];
    private readonly HashSet<Guid> _flagged = [];
    private readonly List<FilmstripItem> _gapItems = [];
    private Project _project;
    private FrameHistory _history;
    private Guid? _anchor;
    private Guid? _playhead;
    private int _suppressFollow;
    private Dictionary<Guid, int>? _exporting;
    private ImmutableArray<Frame> _exportFrames;
    private int _pendingFrom;
    private List<Guid>? _dragIds;
    private bool _followed;
    private bool _gapsStale;
    private bool _disposed;
    private double _thumbnailHeight = MinThumbnailHeight;

    /// <summary>Follows <paramref name="shell"/>'s current project and <paramref name="player"/>'s playhead. On the UI thread.</summary>
    public FilmstripViewModel(ShellViewModel shell, PlayerViewModel player, IErrorPresenter errors)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));
        _project = shell.CurrentProject.Model;
        _history = new FrameHistory(_project);
        Items = [];
        CaptionText = string.Empty;
        FileText = string.Empty;
        UndoText = ProjectText.FilmstripUndoText(null);
        RemoveCommand = errors.Command(() => Remove(), ProjectText.FilmstripRemoveFailed);
        UndoCommand = errors.Command(() => Undo(), ProjectText.FilmstripUndoFailed, () => CanUndo);
        MoveEarlierCommand = errors.Command(() => MoveBy(-1), ProjectText.FilmstripMoveFailed);
        MoveLaterCommand = errors.Command(() => MoveBy(1), ProjectText.FilmstripMoveFailed);

        _project.Changed += OnProjectChanged;
        shell.ProjectReplaced += OnProjectReplaced;
        player.PropertyChanged += OnPlayerPropertyChanged;
        player.StateChanged += OnPlayerStateChanged;
        Rebuild(_project.Frames);
        InvalidateGaps();
        SyncPlayhead();
    }

    /// <summary>Something for Narrator to hear: a removal, an undo, a move (the view raises it as a UIA notification).</summary>
    public event EventHandler<string>? Announced;

    /// <summary>The playhead moved to another frame; the view keeps it in sight.</summary>
    public event EventHandler? PlayheadMoved;

    /// <summary>The gap markers are out of date; the view calls <see cref="RefreshGaps"/> a moment later, once for a burst of changes.</summary>
    public event EventHandler? GapsInvalidated;

    /// <summary>The frames changed (any change, a new project included); the view tells its thumbnail cache.</summary>
    public event EventHandler<ImmutableArray<Frame>>? FramesChanged;

    /// <summary>One item per frame, in playback order.</summary>
    public FilmstripItems Items { get; }

    /// <summary>Remove from project (the context menu; Delete).</summary>
    public IRelayCommand RemoveCommand { get; }

    /// <summary>Undo (the context menu; Ctrl+Z).</summary>
    public IRelayCommand UndoCommand { get; }

    /// <summary>Move earlier (the context menu; Alt+Left).</summary>
    public IRelayCommand MoveEarlierCommand { get; }

    /// <summary>Move later (the context menu; Alt+Right).</summary>
    public IRelayCommand MoveLaterCommand { get; }

    /// <summary>"Frame 0312 of 0742", with " · 3 selected" for several.</summary>
    [ObservableProperty]
    public partial string CaptionText { get; private set; }

    /// <summary>"IMG_0312.JPG · 19:26:05": the frame the preview shows.</summary>
    [ObservableProperty]
    public partial string FileText { get; private set; }

    /// <summary>True when Ctrl+Z has something to undo.</summary>
    [ObservableProperty]
    public partial bool CanUndo { get; private set; }

    /// <summary>The context menu's Undo, named after the edit it undoes: "Undo removing 3 frames", "Undo move".</summary>
    [ObservableProperty]
    public partial string UndoText { get; private set; }

    partial void OnCanUndoChanged(bool value) => UndoCommand.NotifyCanExecuteChanged();

    /// <summary>The thumbnails' height in DIPs, <see cref="MinThumbnailHeight"/> to <see cref="MaxThumbnailHeight"/>, on the 4 px grid.</summary>
    public double ThumbnailHeight
    {
        get => _thumbnailHeight;
        set
        {
            if (double.IsNaN(value))
            {
                return;
            }

            var height = Math.Clamp(Math.Round(value / 4) * 4, MinThumbnailHeight, MaxThumbnailHeight);
            if (SetProperty(ref _thumbnailHeight, height))
            {
                OnPropertyChanged(nameof(ThumbnailWidth));
            }
        }
    }

    /// <summary>The thumbnails' width in DIPs: 3:2.</summary>
    public double ThumbnailWidth => Math.Round(_thumbnailHeight * 1.5);

    /// <summary>How many frames are selected.</summary>
    public int SelectedCount => _selected.Count;

    /// <summary>The selected frames' ids.</summary>
    public IReadOnlyCollection<Guid> SelectedIds => _selected;

    /// <summary>The index of the frame at the playhead, or -1.</summary>
    public int PlayheadIndex => _playhead is { } id && _byId.TryGetValue(id, out var item) ? item.Number - 1 : -1;

    /// <summary>How many gap markers show.</summary>
    public int GapCount => _gapItems.Count;

    /// <summary>The undo history of the current project.</summary>
    internal FrameHistory History => _history;

    /// <summary>The item at <paramref name="index"/>, or null.</summary>
    public FilmstripItem? ItemAt(int index) => index >= 0 && index < Items.Count ? Items[index] : null;

    /// <summary>
    /// A click on thumbnail <paramref name="index"/> (or Enter, or Narrator's Select): selects as the remarks say and shows
    /// the frame, unless Ctrl took it out of the selection.
    /// </summary>
    public void Click(int index, bool shift, bool control)
    {
        if (ItemAt(index) is not { } item)
        {
            return;
        }

        var show = true;
        if (shift)
        {
            var range = Range(AnchorIndex(index), index);
            if (!control)
            {
                _selected.Clear();
            }

            _selected.UnionWith(range);
        }
        else if (control)
        {
            if (_selected.Remove(item.Id))
            {
                show = false;
                if (_selected.Count == 0)
                {
                    _selected.Add(item.Id);
                }
            }
            else
            {
                _selected.Add(item.Id);
            }

            _anchor = item.Id;
        }
        else
        {
            _selected.Clear();
            _selected.Add(item.Id);
            _anchor = item.Id;
        }

        ApplySelection();
        if (show)
        {
            Show(index);
        }
    }

    /// <summary>Adds thumbnail <paramref name="index"/> to the selection without showing it (Narrator's AddToSelection).</summary>
    public void AddToSelection(int index)
    {
        if (ItemAt(index) is { } item && _selected.Add(item.Id))
        {
            ApplySelection();
        }
    }

    /// <summary>Takes thumbnail <paramref name="index"/> out of the selection (Narrator's RemoveFromSelection); the last one stays.</summary>
    public void RemoveFromSelection(int index)
    {
        if (ItemAt(index) is { } item && _selected.Count > 1 && _selected.Remove(item.Id))
        {
            ApplySelection();
        }
    }

    /// <summary>Does what a filmstrip key asks, with thumbnail <paramref name="focused"/> focused (-1 for none).</summary>
    public FilmstripKeyResult Handle(FilmstripAction action, int focused)
    {
        var count = Items.Count;
        switch (action)
        {
            case FilmstripAction.None:
                return new(false, focused);
            case FilmstripAction.Remove:
                // Paused first: playing, a single selection follows the playhead, so the frame removed is the one on
                // screen when the key went down, and the preview stays on what replaced it.
                _player.Pause();
                TakeFocused(focused);
                return new(Remove() > 0, PlayheadIndex);
            case FilmstripAction.Undo:
                return new(Undo(), PlayheadIndex);
        }

        if (count == 0)
        {
            return new(false, -1);
        }

        focused = focused < 0 || focused >= count ? Math.Max(0, PlayheadIndex) : focused;
        switch (action)
        {
            case FilmstripAction.Previous:
                return Navigate(Math.Max(0, focused - 1));
            case FilmstripAction.Next:
                return Navigate(Math.Min(count - 1, focused + 1));
            case FilmstripAction.First:
                return Navigate(0);
            case FilmstripAction.Last:
                return Navigate(count - 1);
            case FilmstripAction.ExtendPrevious:
                return Extend(focused, Math.Max(0, focused - 1));
            case FilmstripAction.ExtendNext:
                return Extend(focused, Math.Min(count - 1, focused + 1));
            case FilmstripAction.ExtendFirst:
                return Extend(focused, 0);
            case FilmstripAction.ExtendLast:
                return Extend(focused, count - 1);
            case FilmstripAction.FocusPrevious:
                return new(true, Math.Max(0, focused - 1));
            case FilmstripAction.FocusNext:
                return new(true, Math.Min(count - 1, focused + 1));
            case FilmstripAction.Toggle:
                Click(focused, shift: false, control: true);
                return new(true, focused);
            case FilmstripAction.ExtendToFocused:
                Click(focused, shift: true, control: false);
                return new(true, focused);
            case FilmstripAction.Activate:
                _player.Pause();
                Click(focused, shift: false, control: false);
                return new(true, focused);
            case FilmstripAction.SelectAll:
                _selected.UnionWith(Items.Select(item => item.Id));
                ApplySelection();
                return new(true, focused);
            case FilmstripAction.Collapse:
                if (_selected.Count <= 1)
                {
                    return new(false, focused);
                }

                SelectOnly(_playhead);
                return new(true, PlayheadIndex);
            case FilmstripAction.MoveEarlier:
            case FilmstripAction.MoveLater:
                // Paused first, or the selection would walk on with the video and the next Alt+arrow move another frame.
                _player.Pause();
                TakeFocused(focused);
                var id = Items[focused].Id;
                MoveBy(action == FilmstripAction.MoveEarlier ? -1 : 1);
                return new(true, _byId.TryGetValue(id, out var moved) ? moved.Number - 1 : focused);
            default:
                return new(false, focused);
        }
    }

    /// <summary>
    /// A drag starts on thumbnail <paramref name="index"/>: the video pauses, the frame is selected alone unless it is
    /// selected already, and the selected frames are taken as the ones that move. Until <see cref="EndDrag"/> the selection
    /// stays put whatever the playhead does, and the drop moves exactly these frames.
    /// </summary>
    public void BeginDrag(int index)
    {
        _player.Pause();
        if (ItemAt(index) is { IsSelected: false })
        {
            Click(index, shift: false, control: false);
        }

        _dragIds = [.. Items.Where(item => _selected.Contains(item.Id)).Select(item => item.Id)];
    }

    /// <summary>
    /// The drag ends: the frames taken by <see cref="BeginDrag"/> move to the seam <paramref name="insertIndex"/>
    /// (see <see cref="MoveTo"/>), or nothing moves when it is null (Escape, a lost pointer).
    /// </summary>
    /// <returns>True when the order changed.</returns>
    public bool EndDrag(int? insertIndex)
    {
        var ids = _dragIds;
        _dragIds = null;
        return ids is not null && insertIndex is { } seam && MoveTo(seam, ids);
    }

    /// <summary>True between <see cref="BeginDrag"/> and <see cref="EndDrag"/>.</summary>
    public bool IsDragging => _dragIds is not null;

    /// <summary>
    /// The context menu is about to act: the video pauses, so the selection stops following the playhead, and a
    /// right-click on thumbnail <paramref name="index"/> outside the selection selects it alone first, as in File
    /// Explorer. Null when the menu opens from the keyboard.
    /// </summary>
    public void PrepareContextMenu(int? index)
    {
        _player.Pause();
        if (index is { } at && ItemAt(at) is { IsSelected: false })
        {
            Click(at, shift: false, control: false);
        }
    }

    /// <summary>
    /// Removes the selected frames from the project (never from disk), as one undoable change, and says so to Narrator.
    /// The playhead stays on its photo, or moves to the next one when its photo left.
    /// </summary>
    /// <returns>How many frames were removed.</returns>
    public int Remove()
    {
        if (Items.Count == 0)
        {
            return 0;
        }

        var ids = Items.Where(item => _selected.Contains(item.Id)).Select(item => item.Id).ToList();
        if (ids.Count == 0 && _playhead is { } playhead)
        {
            ids.Add(playhead);
        }

        // When the frame on screen goes, the preview shows the first frame after it that stays (or the last one before).
        Guid? next = null;
        var removing = ids.ToHashSet();
        if (_playhead is { } shown && removing.Contains(shown) && _byId.TryGetValue(shown, out var shownItem))
        {
            var after = Items.Skip(shownItem.Number).FirstOrDefault(item => !removing.Contains(item.Id))
                ?? Items.Take(shownItem.Number - 1).LastOrDefault(item => !removing.Contains(item.Id));
            next = after?.Id;
        }

        var count = _history.Remove(ids);
        UpdateUndo();
        if (next is { } stay && _byId.TryGetValue(stay, out var stayItem))
        {
            _player.Seek(stayItem.Number - 1);
        }

        if (count > 0)
        {
            Announced?.Invoke(this, ProjectText.FilmstripRemoved(count));
        }

        return count;
    }

    /// <summary>Undoes the latest removal or move, selects what came back, and says so to Narrator.</summary>
    /// <returns>False when there was nothing to undo.</returns>
    public bool Undo()
    {
        var undone = _history.Undo();
        UpdateUndo();
        if (undone is null)
        {
            Announced?.Invoke(this, ProjectText.FilmstripNothingToUndo);
            return false;
        }

        if (!undone.Frames.IsEmpty)
        {
            _selected.Clear();
            _selected.UnionWith(undone.Frames.Where(_byId.ContainsKey));
            _anchor = undone.Frames[0];
            ApplySelection();
            if (undone.Kind == FrameEditKind.Removal && _byId.TryGetValue(undone.Frames[0], out var first))
            {
                Show(first.Number - 1);
            }
        }

        Announced?.Invoke(this, undone.Kind == FrameEditKind.Removal ? ProjectText.FilmstripRestored(undone.Frames.Length) : ProjectText.FilmstripReorderUndone);
        return true;
    }

    /// <summary>
    /// A drop: moves the selected frames (or the frames <paramref name="ids"/>, those of them still in the project) as one
    /// block to the seam <paramref name="insertIndex"/> (0 before the first frame, <c>Count</c> after the last), switching
    /// the sort to manual. Nothing happens when the order would not change.
    /// </summary>
    /// <returns>True when the order changed.</returns>
    public bool MoveTo(int insertIndex, IReadOnlyCollection<Guid>? ids = null)
    {
        if (Items.Count == 0)
        {
            return false;
        }

        var wanted = ids is null ? _selected : ids.ToHashSet();
        var moving = Items.Where(item => wanted.Contains(item.Id)).ToList();
        if (moving.Count == 0 && ids is null && _playhead is { } playhead && _byId.TryGetValue(playhead, out var shown))
        {
            moving.Add(shown);
        }

        if (moving.Count == 0)
        {
            return false;
        }

        var current = Items.Select(item => item.Id).ToList();
        var order = FrameMoves.Move(current, moving.Select(item => item.Id).ToHashSet(), insertIndex);
        if (order.SequenceEqual(current))
        {
            return false;
        }

        var oldNumber = moving[0].Number;
        _history.Reorder(order, moving.Select(item => item.Id));
        UpdateUndo();
        Announced?.Invoke(this, ProjectText.FilmstripMoved(moving.Count, oldNumber, moving[0].Number));
        return true;
    }

    /// <summary>Moves the selected frames one place earlier (-1) or later (+1), as one block (Alt+Left, Alt+Right).</summary>
    /// <returns>True when the order changed.</returns>
    public bool MoveBy(int direction)
    {
        var indices = Items.Where(item => _selected.Contains(item.Id)).Select(item => item.Number - 1).ToList();
        if (indices.Count == 0)
        {
            if (PlayheadIndex < 0)
            {
                return false;
            }

            indices.Add(PlayheadIndex);
        }

        return MoveTo(direction < 0 ? Math.Max(0, indices[0] - 1) : Math.Min(Items.Count, indices[^1] + 2));
    }

    /// <summary>
    /// #24's export progress: of <paramref name="exporting"/> (the frames the export renders, its snapshot), those from
    /// index <paramref name="firstPending"/> on are not written yet, and their thumbnails draw the picture at 35 %.
    /// Frames are matched by id, so an edit during the export (a removal, a move, an import) keeps the dim on the right
    /// photos, and a frame the export does not render is never dimmed. Cheap enough for every progress step: the ids are
    /// indexed once per export (per snapshot array). <see cref="ClearPending"/> ends it.
    /// </summary>
    public void SetPending(ImmutableArray<Frame> exporting, int firstPending)
    {
        if (exporting.IsDefault)
        {
            ClearPending();
            return;
        }

        if (_exporting is null || exporting != _exportFrames)
        {
            _exportFrames = exporting;
            _exporting = new Dictionary<Guid, int>(exporting.Length);
            for (var i = 0; i < exporting.Length; i++)
            {
                _exporting[exporting[i].Id] = i;
            }
        }

        _pendingFrom = Math.Clamp(firstPending, 0, exporting.Length);
        ApplyPending(0);
    }

    /// <summary>The export ended (done, failed or cancelled): no thumbnail is dimmed.</summary>
    public void ClearPending()
    {
        _exporting = null;
        _exportFrames = default;
        ApplyPending(0);
    }

    /// <summary>
    /// Puts the gap markers where <see cref="FrameGaps.Detect"/> finds them for the current order and sort mode. A hole the
    /// user made is no gap: the frames the user removed in this project (<see cref="FrameHistory.Removed"/>) are put back
    /// in their sorted places for the detection, so only photos that were never in the project count as missing. A gap
    /// found in front of a removed frame is drawn in front of the next frame still there.
    /// </summary>
    public void RefreshGaps()
    {
        _gapsStale = false;
        foreach (var item in _gapItems)
        {
            item.GapBefore = null;
        }

        _gapItems.Clear();
        var mode = _project.SortMode;
        var scan = WithRemoved(_project.Frames, mode);
        foreach (var found in FrameGaps.Detect(scan, mode))
        {
            for (var next = found.AfterIndex + 1; next < scan.Count; next++)
            {
                if (!_byId.TryGetValue(scan[next].Id, out var item))
                {
                    continue;
                }

                var gap = found;
                if (item.GapBefore is { } earlier)
                {
                    // Two gaps with only removed frames between them: one marker that says both.
                    gap = earlier with
                    {
                        Kinds = earlier.Kinds | found.Kinds,
                        MissingFrames = earlier.MissingFrames + found.MissingFrames,
                        Interval = earlier.Interval ?? found.Interval,
                        TypicalInterval = earlier.TypicalInterval ?? found.TypicalInterval,
                    };
                }
                else
                {
                    _gapItems.Add(item);
                }

                item.GapBefore = gap;
                break;
            }
        }

        OnPropertyChanged(nameof(GapCount));
    }

    /// <summary>Stops following the shell, the project and the player.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _project.Changed -= OnProjectChanged;
        _shell.ProjectReplaced -= OnProjectReplaced;
        _player.PropertyChanged -= OnPlayerPropertyChanged;
        _player.StateChanged -= OnPlayerStateChanged;
    }

    // Keys on a focused thumbnail act from the focus ring: a single selection that only followed the playhead (the video
    // played on after the user picked the focused frame) gives way to the focused frame. A selection the user made stays.
    private void TakeFocused(int focused)
    {
        if (_followed && ItemAt(focused) is { } item && !(_selected.Count == 1 && _selected.Contains(item.Id)))
        {
            SelectOnly(item.Id);
        }
    }

    // The frames with those the user removed (and has not imported again) in the places the sort mode gives them; the
    // frames alone when nothing was removed or the mode has no gaps.
    private IReadOnlyList<Frame> WithRemoved(ImmutableArray<Frame> frames, SortMode mode)
    {
        if (_history.Removed.Count == 0 || mode is SortMode.Manual or SortMode.DateModified)
        {
            return frames;
        }

        var paths = frames.Select(frame => frame.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var gone = _history.Removed.Where(frame => !paths.Contains(frame.SourcePath)).ToList();
        return gone.Count == 0 ? frames : FrameSorter.Sort(frames.Concat(gone), mode);
    }

    private void UpdateUndo()
    {
        CanUndo = _history.CanUndo;
        UndoText = ProjectText.FilmstripUndoText(_history.NextUndo);
    }

    private FilmstripKeyResult Navigate(int index)
    {
        _player.Pause();
        Click(index, shift: false, control: false);
        return new(true, index);
    }

    private FilmstripKeyResult Extend(int focused, int index)
    {
        var range = Range(AnchorIndex(focused), index);
        _selected.Clear();
        _selected.UnionWith(range);
        ApplySelection();
        _player.Pause();
        Show(index);
        return new(true, index);
    }

    // The anchor's index; the given index when there is no anchor (or it left).
    private int AnchorIndex(int fallback) =>
        _anchor is { } id && _byId.TryGetValue(id, out var item) ? item.Number - 1 : fallback;

    private IEnumerable<Guid> Range(int from, int to)
    {
        var (low, high) = from <= to ? (from, to) : (to, from);
        for (var i = Math.Max(0, low); i <= high && i < Items.Count; i++)
        {
            yield return Items[i].Id;
        }
    }

    private void SelectOnly(Guid? id)
    {
        _selected.Clear();
        if (id is { } only)
        {
            _selected.Add(only);
        }

        _anchor = id;
        ApplySelection();
    }

    // Shows the frame in the preview; the selection the caller made stays as it is.
    private void Show(int index)
    {
        _suppressFollow++;
        try
        {
            _player.Seek(index);
        }
        finally
        {
            _suppressFollow--;
        }

        SyncPlayhead();
    }

    private void ApplySelection()
    {
        _followed = false;
        foreach (var id in _flagged.Where(id => !_selected.Contains(id)).ToList())
        {
            _flagged.Remove(id);
            if (_byId.TryGetValue(id, out var item))
            {
                item.IsSelected = false;
            }
        }

        foreach (var id in _selected)
        {
            if (_flagged.Add(id) && _byId.TryGetValue(id, out var item))
            {
                item.IsSelected = true;
            }
        }

        OnPropertyChanged(nameof(SelectedCount));
        UpdateCaptions();
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.CurrentFrame) or nameof(PlayerViewModel.FrameCount))
        {
            _errors.Guard(SyncPlayhead, ProjectText.FilmstripProjectFailed);
        }
    }

    private void OnPlayerStateChanged(object? sender, EventArgs e) => _errors.Guard(SyncPlayhead, ProjectText.FilmstripProjectFailed);

    // The playhead's frame, read from the player once it has caught up with the project: the player and this view model
    // both follow the project's Changed event, in either order, and the player sets CurrentFrame a moment before its state.
    private void SyncPlayhead()
    {
        if (_disposed)
        {
            return;
        }

        Guid? id = null;
        var state = _player.State;
        if (_player.FrameCount > 0 || Items.Count > 0)
        {
            var frames = state.Snapshot.Frames;
            var index = _player.CurrentFrame;
            if (state.Snapshot.Version != _project.Version || frames.Length != _project.Frames.Length || index < 0 || index >= frames.Length
                || !_byId.ContainsKey(frames[index].Id))
            {
                return;
            }

            id = frames[index].Id;
        }

        var previous = _playhead;
        if (previous != id)
        {
            if (previous is { } old && _byId.TryGetValue(old, out var oldItem))
            {
                oldItem.IsPlayhead = false;
            }

            _playhead = id;
            if (id is { } now && _byId.TryGetValue(now, out var item))
            {
                item.IsPlayhead = true;
            }
        }

        // A selection of the one frame at the playhead follows it; an empty selection falls back to it. Not during a
        // drag: the frames that move were taken when it started.
        if (_suppressFollow == 0 && _dragIds is null && id is { } playhead
            && (_selected.Count == 0 || (_selected.Count == 1 && previous is { } was && was != playhead && _selected.Contains(was))))
        {
            SelectOnly(playhead);
            _followed = true;
        }

        if (previous != id)
        {
            UpdateCaptions();
            PlayheadMoved?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateCaptions()
    {
        if (_playhead is { } id && _byId.TryGetValue(id, out var item))
        {
            CaptionText = ProjectText.FilmstripCaption(item.Number, Items.Count, _selected.Count);
            FileText = ProjectText.FilmstripFileCaption(item.Frame.FileName, item.Frame.CaptureTime);
        }
        else
        {
            CaptionText = string.Empty;
            FileText = string.Empty;
        }
    }

    private void OnProjectChanged(object? sender, ProjectChangedEventArgs e) =>
        _errors.Guard(() => Follow(e), ProjectText.FilmstripProjectFailed);

    private void OnProjectReplaced(object? sender, ProjectReplacedEventArgs e) =>
        _errors.Guard(
            () =>
            {
                _project.Changed -= OnProjectChanged;
                _project = e.Current.Model;
                _project.Changed += OnProjectChanged;
                _history = new FrameHistory(_project);
                UpdateUndo();
                _selected.Clear();
                _flagged.Clear();
                _gapItems.Clear();
                _anchor = null;
                _playhead = null;
                _dragIds = null;
                _byId.Clear();
                Rebuild(_project.Frames);
                InvalidateGaps();
                FramesChanged?.Invoke(this, _project.Frames);
                SyncPlayhead();
                UpdateCaptions();
            },
            ProjectText.FilmstripProjectFailed);

    private void Follow(ProjectChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if ((e.Changes & ProjectChanges.Frames) != 0)
        {
            var frames = e.Snapshot.Frames;
            var from = e.Frames is { } change ? Apply(change, frames) : Rebuild(frames);
            Renumber(from);
            ApplyPending(from);
            _selected.RemoveWhere(id => !_byId.ContainsKey(id));
            _flagged.RemoveWhere(id => !_byId.ContainsKey(id));
            _gapItems.RemoveAll(item => !_byId.ContainsKey(item.Id));
            if (_anchor is { } anchor && !_byId.ContainsKey(anchor))
            {
                _anchor = null;
            }

            if (_playhead is { } playhead && !_byId.ContainsKey(playhead))
            {
                _playhead = null;
            }

            FramesChanged?.Invoke(this, frames);
            SyncPlayhead();
            OnPropertyChanged(nameof(SelectedCount));
            UpdateCaptions();
        }

        if ((e.Changes & (ProjectChanges.Frames | ProjectChanges.SortMode)) != 0)
        {
            InvalidateGaps();
        }
    }

    // Applies one delta; returns the first index whose number may have changed.
    private int Apply(FrameChange change, ImmutableArray<Frame> frames)
    {
        switch (change.Kind)
        {
            case FrameChangeKind.Added when change.Count <= IncrementalLimit:
                foreach (var index in change.Indices)
                {
                    var item = new FilmstripItem(frames[index], index + 1);
                    _byId[item.Id] = item;
                    Items.Insert(index, item);
                }

                return change.Count == 0 ? Items.Count : change.Indices[0];
            case FrameChangeKind.Removed when change.Count <= IncrementalLimit:
                for (var i = change.Indices.Length - 1; i >= 0; i--)
                {
                    var index = change.Indices[i];
                    _byId.Remove(Items[index].Id);
                    Items.RemoveAt(index);
                }

                return change.Count == 0 ? Items.Count : change.Indices[0];
            case FrameChangeKind.Updated:
                foreach (var index in change.Indices)
                {
                    Items[index].Frame = frames[index];
                }

                return Items.Count;
            case FrameChangeKind.Reset:
                return Reorder(frames);
            default:
                return Rebuild(frames);
        }
    }

    // A new order: a few moves when only a few frames moved (a drag keeps the focused thumbnail's element), else one reset.
    private int Reorder(ImmutableArray<Frame> frames)
    {
        if (frames.Length != Items.Count)
        {
            return Rebuild(frames);
        }

        var work = Items.Select(item => item.Id).ToList();
        var moves = new List<(int From, int To)>();
        for (var i = 0; i < frames.Length; i++)
        {
            if (work[i] == frames[i].Id)
            {
                continue;
            }

            var from = work.IndexOf(frames[i].Id, i + 1);
            if (from < 0 || moves.Count >= IncrementalLimit)
            {
                return Rebuild(frames);
            }

            moves.Add((from, i));
            work.RemoveAt(from);
            work.Insert(i, frames[i].Id);
        }

        foreach (var (from, to) in moves)
        {
            Items.Move(from, to);
        }

        for (var i = 0; i < frames.Length; i++)
        {
            Items[i].Frame = frames[i];
        }

        return moves.Count == 0 ? Items.Count : moves.Min(move => Math.Min(move.From, move.To));
    }

    // Replaces the list whole, keeping the items (and their states) of frames still there.
    private int Rebuild(ImmutableArray<Frame> frames)
    {
        var items = new List<FilmstripItem>(frames.Length);
        var byId = new Dictionary<Guid, FilmstripItem>(frames.Length);
        for (var i = 0; i < frames.Length; i++)
        {
            var frame = frames[i];
            if (_byId.TryGetValue(frame.Id, out var item))
            {
                item.Frame = frame;
            }
            else
            {
                item = new FilmstripItem(frame, i + 1);
            }

            items.Add(item);
            byId[frame.Id] = item;
        }

        _byId.Clear();
        foreach (var pair in byId)
        {
            _byId[pair.Key] = pair.Value;
        }

        Items.ReplaceAll(items);
        return 0;
    }

    private void Renumber(int from)
    {
        for (var i = Math.Max(0, from); i < Items.Count; i++)
        {
            Items[i].Number = i + 1;
        }
    }

    private void ApplyPending(int from)
    {
        for (var i = Math.Max(0, from); i < Items.Count; i++)
        {
            Items[i].IsPending = _exporting is { } exporting && exporting.TryGetValue(Items[i].Id, out var place) && place >= _pendingFrom;
        }
    }

    private void InvalidateGaps()
    {
        if (_gapsStale)
        {
            return;
        }

        _gapsStale = true;
        GapsInvalidated?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>The arithmetic of a reorder (#20): where a block of frames lands when dropped at a seam.</summary>
public static class FrameMoves
{
    /// <summary>
    /// The order after moving the frames in <paramref name="moving"/> (as one block, in their present order) to the seam
    /// <paramref name="insertIndex"/> of <paramref name="order"/>: 0 is before the first frame, <c>order.Count</c> after the
    /// last, and seam <c>i</c> is between frames <c>i - 1</c> and <c>i</c>. The other frames keep their order.
    /// </summary>
    public static List<Guid> Move(IReadOnlyList<Guid> order, IReadOnlySet<Guid> moving, int insertIndex)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(moving);
        insertIndex = Math.Clamp(insertIndex, 0, order.Count);
        var before = 0;
        for (var i = 0; i < insertIndex; i++)
        {
            if (moving.Contains(order[i]))
            {
                before++;
            }
        }

        var rest = order.Where(id => !moving.Contains(id)).ToList();
        var block = order.Where(moving.Contains).ToList();
        rest.InsertRange(insertIndex - before, block);
        return rest;
    }
}
