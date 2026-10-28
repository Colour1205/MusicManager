using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MusicManager.View.Library;

namespace MusicManager.App.View.Library;

/// <summary>
/// A suggestion of the album name box. It shows the <see cref="AlbumSuggestion"/> that is its DataContext, so the list can
/// recycle it.
/// </summary>
public sealed partial class AlbumSuggestionRow : UserControl
{
    public AlbumSuggestionRow()
    {
        InitializeComponent();
        DataContextChanged += (sender, args) => Show(args.NewValue as AlbumSuggestion);
    }

    private void Show(AlbumSuggestion suggestion)
    {
        if (suggestion == null)
        {
            return;
        }
        NameText.Text = suggestion.Name;
        DetailText.Text = suggestion.Detail;
        DetailText.Visibility = suggestion.Detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible; // a line that has no text takes no room
    }
}
