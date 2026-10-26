using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MusicManager.View.Library;

namespace MusicManager.App.View.Library;

/// <summary>
/// The card of one artist group (Java: ArtistCardList.ArtistCard). It shows the <see cref="ArtistGroupItem"/> that is its
/// DataContext, so a virtualized list can recycle it. The name is committed when a spelling is picked, when Enter is
/// pressed and when the box loses focus.
/// </summary>
public sealed partial class ArtistGroupCard : UserControl
{
    private ArtistGroupItem _item;
    private bool _binding;

    public ArtistGroupCard()
    {
        InitializeComponent();
        DataContextChanged += (sender, args) => Show(args.NewValue as ArtistGroupItem);
    }

    private void Show(ArtistGroupItem item)
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

        _binding = true;
        try
        {
            NameBox.ItemsSource = item.Candidates;
            NameBox.SelectedItem = item.Group.CanonicalName; // one of the candidates
            NameBox.Text = item.Group.CanonicalName ?? "";
            NameBox.IsEnabled = item.CanEdit;
        }
        finally
        {
            _binding = false;
        }
        SongCountButton.Content = item.SongCountText;
        SpellingsText.Text = item.SpellingsText;
        SpellingsText.Visibility = item.SpellingsText.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        ToolTipService.SetToolTip(SpellingsText, item.AllSpellings.Length == 0 ? null : item.AllSpellings);
        WarningText.Text = item.Warning ?? "";
        WarningRow.Visibility = item.Warning == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnItemChanged(object sender, PropertyChangedEventArgs e)
    {
        if (_item != null && e.PropertyName == nameof(ArtistGroupItem.CanEdit))
        {
            NameBox.IsEnabled = _item.CanEdit;
        }
    }

    /// <summary>one row of the list: the song as the cards name it, and the artist tag it carries under it</summary>
    private static UIElement SongRow(ReviewText.ArtistSong song)
    {
        var row = new StackPanel { Spacing = 2, Padding = new Thickness(0, 6, 0, 6) };
        row.Children.Add(new TextBlock { Text = song.Name, TextWrapping = TextWrapping.Wrap });
        row.Children.Add(new TextBlock
        {
            Text = song.ArtistTag,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        return row;
    }

    /// <summary>"N songs" was clicked: a window with the list of the songs of this artist</summary>
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

    private void OnNameSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_binding && _item != null && NameBox.SelectedItem is string picked)
        {
            Commit(picked);
        }
    }

    private void OnNameSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        if (!_binding && _item != null)
        {
            Commit(args.Text);
            args.Handled = true;
        }
    }

    private void OnNameLostFocus(object sender, RoutedEventArgs e)
    {
        if (!_binding && _item != null && NameBox.IsEnabled)
        {
            TextBox editor = FindTextBox(NameBox);
            Commit(editor != null ? editor.Text : NameBox.Text);
        }
    }

    private void Commit(string typed)
    {
        string shown = _item.Commit(typed);
        _binding = true;
        try
        {
            if (!Equals(NameBox.SelectedItem, shown))
            {
                NameBox.SelectedItem = shown;
            }
            if (NameBox.Text != shown)
            {
                NameBox.Text = shown;
            }
        }
        finally
        {
            _binding = false;
        }
    }

    private static TextBox FindTextBox(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBox box)
            {
                return box;
            }
            TextBox found = FindTextBox(child);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }
}
