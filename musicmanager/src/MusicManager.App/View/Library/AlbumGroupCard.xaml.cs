using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MusicManager.View.Library;

namespace MusicManager.App.View.Library;

/// <summary>
/// The card of one album group (like <see cref="ArtistGroupCard"/>). It shows the <see cref="AlbumGroupItem"/> that is its
/// DataContext, so a virtualized list can recycle it. The name box suggests names while the user types (and every name when
/// it gets the focus); the name is committed when a suggestion is picked, when Enter is pressed and when the box loses focus.
/// </summary>
public sealed partial class AlbumGroupCard : UserControl
{
    private AlbumGroupItem _item;

    public AlbumGroupCard()
    {
        InitializeComponent();
        DataContextChanged += (sender, args) => Show(args.NewValue as AlbumGroupItem);
    }

    private void Show(AlbumGroupItem item)
    {
        if (_item != null)
        {
            _item.PropertyChanged -= OnItemChanged;
        }
        _item = item;
        if (item == null)
        {
            return;
        }
        item.PropertyChanged += OnItemChanged;

        NameBox.ItemsSource = null; // the suggestions are made when the box is used
        NameBox.Text = item.Group.CanonicalName ?? ""; // a name the code writes does not make suggestions (see OnNameTextChanged)
        NameBox.IsEnabled = item.CanEdit;
        ShowFolderNote();
        SongCountButton.Content = item.SongCountText;
        WarningText.Text = item.Warning ?? "";
        WarningRow.Visibility = item.Warning == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnItemChanged(object sender, PropertyChangedEventArgs e)
    {
        if (_item == null)
        {
            return;
        }
        if (e.PropertyName == nameof(AlbumGroupItem.CanEdit))
        {
            NameBox.IsEnabled = _item.CanEdit;
        }
        else if (e.PropertyName == nameof(AlbumGroupItem.FolderNote))
        {
            ShowFolderNote();
        }
    }

    /// <summary>the note about the folder the songs go into, when it is not the folder of the name; a card without one takes no room</summary>
    private void ShowFolderNote()
    {
        string note = _item?.FolderNote;
        FolderText.Text = note ?? "";
        FolderRow.Visibility = string.IsNullOrEmpty(note) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>one row of the list: the song as the cards name it, and the album tag it carries under it</summary>
    private static UIElement SongRow(ReviewText.AlbumSong song)
    {
        var row = new StackPanel { Spacing = 2, Padding = new Thickness(0, 6, 0, 6) };
        row.Children.Add(new TextBlock { Text = song.Name, TextWrapping = TextWrapping.Wrap });
        row.Children.Add(new TextBlock
        {
            Text = song.AlbumTag,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        return row;
    }

    /// <summary>"N songs" was clicked: a window with the list of the songs of this album</summary>
    private async void OnSongCountClick(object sender, RoutedEventArgs e)
    {
        if (_item == null)
        {
            return;
        }
        try
        {
            var songs = new ListView
            {
                ItemsSource = _item.Songs().Select(SongRow).ToList(),
                SelectionMode = ListViewSelectionMode.None,
                MaxHeight = 380,
            };
            var content = new StackPanel { Spacing = 8, MinWidth = 420 };
            content.Children.Add(new TextBlock
            {
                Text = _item.SongCountText,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
            content.Children.Add(songs);
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = _item.Group.CanonicalName,
                Content = content,
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close,
            };
            await ContentDialogPrompts.ShowAsync(dialog);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("The list of songs could not be shown: " + ex);
        }
    }

    /// <summary>
    /// The user typed: suggest the names that fit. Only what the user types does it; a name that the code writes into the
    /// box (the card shown, a name committed, a suggestion highlighted with the arrow keys) does not.
    /// </summary>
    private void OnNameTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_item != null && args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            sender.ItemsSource = _item.Suggest(sender.Text);
        }
    }

    /// <summary>Enter was pressed in the box, or a suggestion was clicked (it is the chosen suggestion): the name is committed.</summary>
    private void OnNameQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_item != null && NameBox.IsEnabled)
        {
            if (args.ChosenSuggestion is AlbumSuggestion chosen)
            {
                Commit(chosen.Name, chosen.Folder); // the row says the folder too
            }
            else
            {
                Commit(args.QueryText, null);
            }
        }
    }

    /// <summary>
    /// The text box got the focus (not a suggestion of the list: focus events of the whole box arrive here): the names that
    /// fit what is in the box are offered, which opens the list, as if the name had just been typed. (Every name would bury
    /// the few that matter: the box is not empty, it has the name in it.)
    /// </summary>
    private void OnNameGotFocus(object sender, RoutedEventArgs e)
    {
        if (_item != null && NameBox.IsEnabled && e.OriginalSource is TextBox)
        {
            NameBox.ItemsSource = _item.Suggest(NameBox.Text);
        }
    }

    /// <summary>
    /// The text box lost the focus: what was typed is the name, as it was when the box was a combo box. Clicking a
    /// suggestion may take the focus on its way, and what it chooses is committed by QuerySubmitted, so this waits until the
    /// click is done, and does nothing while the focus is still in the box.
    /// </summary>
    private void OnNameLostFocus(object sender, RoutedEventArgs e)
    {
        if (_item == null || e.OriginalSource is not TextBox)
        {
            return;
        }
        AlbumGroupItem item = _item;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_item == item && NameBox.IsEnabled && !FocusIsInNameBox())
            {
                Commit(NameBox.Text, null);
            }
        });
    }

    private bool FocusIsInNameBox()
    {
        DependencyObject focused = XamlRoot == null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        for (DependencyObject element = focused; element != null; element = VisualTreeHelper.GetParent(element))
        {
            if (ReferenceEquals(element, NameBox))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>applies the name (and the folder of the picked suggestion, null for a typed name) to the group (an empty box gets the old name back) and shows the name the group has now</summary>
    private void Commit(string typed, string folder)
    {
        string shown = _item.Commit(typed, folder);
        if (NameBox.Text != shown)
        {
            NameBox.Text = shown;
        }
    }
}
