using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MusicManager.Entity;
using MusicManager.InterfaceAdapter.Library;
using MusicManager.View.Library;
using Windows.System;
using Windows.UI.Core;

namespace MusicManager.App.View.Library;

/// <summary>
/// The scrolling grid of song cards, fast with thousands of songs: an ItemsView virtualizes it, so only the cards that
/// are on screen exist, and a card decodes its cover when it appears (CoverImageCache keeps the most recent ones). A card
/// shows a check box, the cover thumbnail, title, artists and the song's status line. Clicking the check box toggles the
/// selection (shift extends it over a range); activating the rest of the card opens the song. Keyboard: arrows move
/// between the cards, Enter opens, Tab reaches the check boxes (Space toggles), Ctrl+A selects everything shown.
///
/// What the grid shows (the songs, the filter, the selection, the statuses) lives in the SongGridModel of the View
/// project; this control only renders it. All methods and listeners run on the UI thread.
/// </summary>
public sealed partial class SongGrid : UserControl
{
    private readonly SongGridModel _model = new SongGridModel();
    private readonly ItemList _items = new ItemList();
    private Dictionary<SongGridModel.Row, SongCardItem> _itemsByRow = new Dictionary<SongGridModel.Row, SongCardItem>();
    private readonly List<Action> _selectionListeners = new List<Action>();
    private readonly List<Action<Music>> _openListeners = new List<Action<Music>>();
    private string _anchorPath; // the card a shift-click extends from
    private bool _pressedOnCheckBox; // the last pointer press was on a check box

    public SongGrid()
    {
        InitializeComponent();
        Cards.ItemsSource = _items;
        // the check box handles its own click, but the ItemsView still reports the card as invoked: remember where the
        // press started (also when the check box marked it handled) and let the invoke pass if it was on a check box
        Cards.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
                _pressedOnCheckBox = IsInsideCheckBox(e.OriginalSource as DependencyObject)), true);
        Cards.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, _) => _pressedOnCheckBox = false), true);
    }

    /// <summary>true when a pointer press started on a card's check box (so the card must not open)</summary>
    private static bool IsInsideCheckBox(DependencyObject source)
    {
        for (DependencyObject node = source; node != null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is CheckBox)
            {
                return true;
            }
            if (node is ItemContainer)
            {
                return false; // reached the card itself first
            }
        }
        return false;
    }

    // ---------------------------------------------------------------- songs, statuses, filter

    /// <summary>replaces all songs; the selection keeps the songs that are still there</summary>
    public void SetSongs(IEnumerable<Music> songs)
    {
        bool selectionChanged = _model.SetSongs(songs);
        if (_anchorPath != null && _model.ShownIndexOf(_anchorPath) < 0)
        {
            _anchorPath = null;
        }
        ModelChanged();
        ScrollToTop();
        if (selectionChanged)
        {
            FireSelectionChanged();
        }
    }

    /// <summary>replaces the entries of the songs with the same path as the given ones (a refresh), keeping order and selection</summary>
    public void MergeSongs(IEnumerable<Music> refreshed)
    {
        _model.MergeSongs(refreshed);
        ModelChanged();
    }

    /// <summary>the status line of every song, by audio path; songs without an entry show SongStatus.Unknown</summary>
    public void SetStatuses(IReadOnlyDictionary<string, SongStatus> statusByPath)
    {
        _model.SetStatuses(statusByPath);
        // what the filter looks at (the statuses, in the library page) just changed
        if (_model.Refilter())
        {
            ModelChanged();
        }
        else
        {
            NotifyStatusAll();
        }
    }

    /// <summary>
    /// the status of one song (for instance "Converting..."). The filter is not run again for one song: call
    /// <see cref="SetFilter"/> when the shown songs should follow.
    /// </summary>
    public void SetStatus(string audioPath, SongStatus status)
    {
        _model.SetStatus(audioPath, status);
        int index = _model.ShownIndexOf(audioPath);
        if (index >= 0 && index < _items.Count)
        {
            _items[index].NotifyStatus();
        }
    }

    /// <summary>the status a card shows for this audio path</summary>
    public SongStatus StatusOf(string audioPath)
    {
        return _model.StatusOf(audioPath);
    }

    /// <summary>which songs are shown (search and filter chip); null = all. The selection is not changed by it.</summary>
    public void SetFilter(Func<Music, bool> filter)
    {
        if (_model.SetFilter(filter))
        {
            ModelChanged();
            ScrollToTop();
        }
    }

    /// <summary>the songs shown now, in order (a snapshot: it does not change when the grid does)</summary>
    public IReadOnlyList<Music> VisibleSongs()
    {
        return _model.ShownSongs();
    }

    public int VisibleCount()
    {
        return _model.ShownCount();
    }

    /// <summary>all songs, shown or not</summary>
    public int SongCount()
    {
        return _model.SongCount();
    }

    // ---------------------------------------------------------------- selection

    internal bool IsSelected(string path)
    {
        return _model.IsSelected(path);
    }

    /// <summary>audio paths of the selected songs (including songs hidden by the filter); a copy</summary>
    public HashSet<string> SelectedPaths()
    {
        return _model.SelectedPaths();
    }

    public int SelectedCount()
    {
        return _model.SelectedCount();
    }

    /// <summary>selects exactly these songs; paths that are not in the library are ignored</summary>
    public void SetSelectedPaths(IEnumerable<string> audioPaths)
    {
        if (_model.SetSelectedPaths(audioPaths))
        {
            SelectionChanged();
        }
    }

    /// <summary>selects every song that is shown now</summary>
    public void SelectAllVisible()
    {
        if (_model.SelectAllVisible())
        {
            SelectionChanged();
        }
    }

    public void ClearSelection()
    {
        if (_model.ClearSelection())
        {
            _anchorPath = null;
            SelectionChanged();
        }
    }

    /// <summary>scrolls back to the first card</summary>
    public void ScrollToTop()
    {
        Cards.ScrollView?.ScrollTo(0, 0, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
    }

    /// <summary>called (on the UI thread) whenever the selection changes</summary>
    public void AddSelectionListener(Action listener)
    {
        if (listener != null)
        {
            _selectionListeners.Add(listener);
        }
    }

    /// <summary>called (on the UI thread) when the user activates a card's body or presses Enter on it</summary>
    public void AddOpenListener(Action<Music> listener)
    {
        if (listener != null)
        {
            _openListeners.Add(listener);
        }
    }

    // ---------------------------------------------------------------- what the user does

    /// <summary>
    /// The check box of a card changed (a click, Space, or a screen reader through UI Automation: the card's IsSelected is
    /// bound two ways and calls this); with shift held it extends the selection over a range.
    /// </summary>
    internal void ToggleFromUser(SongCardItem item)
    {
        bool extendRange = IsShiftDown();
        int shownIndex = item.Index;
        if (shownIndex < 0 || shownIndex >= _model.ShownCount())
        {
            return;
        }
        string path = _model.ShownRow(shownIndex).Path;
        int anchor = extendRange ? _model.ShownIndexOf(_anchorPath) : -1;
        bool changed;
        if (anchor >= 0)
        {
            // like the other lists: the range takes the state the anchor card has now
            changed = _model.SetRange(anchor, shownIndex, _model.IsSelected(_anchorPath));
            NotifySelectionAll();
        }
        else
        {
            changed = _model.Toggle(shownIndex);
            item.NotifySelection(); // also puts the check box right if the click and the model disagree
        }
        _anchorPath = path;
        if (changed)
        {
            FireSelectionChanged();
        }
    }

    /// <summary>a card was activated (clicked, or Enter pressed on it)</summary>
    private void Cards_ItemInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (_pressedOnCheckBox)
        {
            _pressedOnCheckBox = false; // a click on the check box only selects, it does not open the song
            return;
        }
        if (args.InvokedItem is SongCardItem item)
        {
            OpenFromUser(item.Index);
        }
    }

    private void OpenFromUser(int shownIndex)
    {
        if (shownIndex < 0 || shownIndex >= _model.ShownCount())
        {
            return;
        }
        Music music = _model.ShownRow(shownIndex).Music;
        foreach (Action<Music> listener in _openListeners.ToArray())
        {
            listener(music);
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.A && IsDown(VirtualKey.Control))
        {
            SelectAllVisible();
            e.Handled = true;
        }
    }

    private static bool IsShiftDown() => IsDown(VirtualKey.Shift);

    private static bool IsDown(VirtualKey key)
    {
        return InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
    }

    // ---------------------------------------------------------------- the cards

    /// <summary>
    /// The shown songs changed: bring the list the ItemsView shows in line with the model. A card item is kept for a song
    /// whose row did not change; when only some songs were replaced (a refresh) they are replaced in place, so the grid
    /// keeps its scroll position.
    /// </summary>
    private void ModelChanged()
    {
        int count = _model.ShownCount();
        var next = new List<SongCardItem>(count);
        var byRow = new Dictionary<SongGridModel.Row, SongCardItem>(count);
        for (int i = 0; i < count; i++)
        {
            SongGridModel.Row row = _model.ShownRow(i);
            if (!_itemsByRow.TryGetValue(row, out SongCardItem item))
            {
                item = new SongCardItem(this, row);
            }
            item.Index = i;
            byRow[row] = item;
            next.Add(item);
        }
        _itemsByRow = byRow;

        int different = 0;
        if (next.Count == _items.Count)
        {
            for (int i = 0; i < count; i++)
            {
                if (!ReferenceEquals(_items[i], next[i]))
                {
                    different++;
                }
            }
        }
        if (next.Count != _items.Count || different > 64)
        {
            _items.ReplaceAll(next);
        }
        else if (different > 0)
        {
            for (int i = 0; i < count; i++)
            {
                if (!ReferenceEquals(_items[i], next[i]))
                {
                    _items[i] = next[i];
                }
            }
        }
        NotifyStatusAll(); // the statuses may have changed together with the songs
    }

    private void NotifyStatusAll()
    {
        foreach (SongCardItem item in _items)
        {
            item.NotifyStatus();
        }
    }

    private void NotifySelectionAll()
    {
        foreach (SongCardItem item in _items)
        {
            item.NotifySelection();
        }
    }

    // ---------------------------------------------------------------- listeners

    private void SelectionChanged()
    {
        NotifySelectionAll();
        FireSelectionChanged();
    }

    private void FireSelectionChanged()
    {
        foreach (Action listener in _selectionListeners.ToArray())
        {
            listener();
        }
    }

    /// <summary>the list behind the ItemsView: it can swap everything with one notification</summary>
    private sealed class ItemList : ObservableCollection<SongCardItem>
    {
        public void ReplaceAll(List<SongCardItem> next)
        {
            Items.Clear();
            foreach (SongCardItem item in next)
            {
                Items.Add(item);
            }
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
