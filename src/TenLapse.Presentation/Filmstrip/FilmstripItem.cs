using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TenLapse.Core.Ordering;
using TenLapse.Core.Projects;

namespace TenLapse.Presentation.Filmstrip;

/// <summary>
/// One thumbnail of the filmstrip (#20): the frame, its number, and the states the thumbnail draws (selected, at the
/// playhead, not yet exported, a gap marker in front of it). The view realizes only the items on screen; the rest are
/// these small objects.
/// </summary>
public sealed partial class FilmstripItem : ObservableObject
{
    private Frame _frame;

    internal FilmstripItem(Frame frame, int number)
    {
        _frame = frame;
        Number = number;
    }

    /// <summary>The frame.</summary>
    public Frame Frame
    {
        get => _frame;
        internal set
        {
            if (!ReferenceEquals(_frame, value))
            {
                _frame = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Name));
            }
        }
    }

    /// <summary>The frame's id.</summary>
    public Guid Id => Frame.Id;

    /// <summary>The frame's place in the clip, from 1.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name))]
    public partial int Number { get; internal set; }

    /// <summary>Selected (for removal or a move).</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; internal set; }

    /// <summary>The frame the preview shows: the amber ring.</summary>
    [ObservableProperty]
    public partial bool IsPlayhead { get; internal set; }

    /// <summary>Not yet exported (#24 dims its picture to 35 %). See <see cref="FilmstripViewModel.SetPending"/>.</summary>
    [ObservableProperty]
    public partial bool IsPending { get; internal set; }

    /// <summary>The gap marker in front of this frame (photos probably missing between it and the one before), or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GapText))]
    public partial FrameGap? GapBefore { get; internal set; }

    /// <summary>"Frame 312, IMG_0312.JPG": the thumbnail's name for Narrator.</summary>
    public string Name => ProjectText.FilmstripItemName(Number, Frame.FileName);

    /// <summary>What the gap marker says, or null without one.</summary>
    public string? GapText => GapBefore is { } gap ? ProjectText.FilmstripGap(gap) : null;
}

/// <summary>
/// The filmstrip's items: an observable list that can also be replaced whole with one <see cref="NotifyCollectionChangedAction.Reset"/>,
/// so a sort or a large batch costs one notification instead of thousands.
/// </summary>
public sealed class FilmstripItems : ObservableCollection<FilmstripItem>
{
    /// <summary>Replaces every item and raises one reset.</summary>
    internal void ReplaceAll(IEnumerable<FilmstripItem> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
