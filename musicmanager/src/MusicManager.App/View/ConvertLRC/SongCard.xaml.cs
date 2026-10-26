using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using MusicManager.Entity;
using Windows.Storage.Streams;

namespace MusicManager.App.View.ConvertLRC;

/// <summary>
/// A single card representing one song in the song grid: cover art (or a placeholder), title, artist, and a conversion
/// status. Pressing it invokes onClick so the caller can offer per-song conversion options.
/// </summary>
/// <remarks>
/// Deprecated: the old stand-alone Convert LRC feature, kept and unused. The app is one window now (View/Library); the
/// convert feature comes back in it by switching on AppFeatures.LegacyConvertLrc.
/// </remarks>
[Obsolete("the old stand-alone Convert LRC feature; the convert feature is behind AppFeatures.LegacyConvertLrc")]
public sealed partial class SongCard : UserControl
{
    private const int ThumbnailSize = 48;

    private readonly Music _music;
    private readonly Action<Music> _onClick;

    public SongCard(Music music, Action<Music> onClick)
    {
        InitializeComponent();
        _music = music;
        _onClick = onClick;

        TitleText.Text = music.Title ?? "";
        ToolTipService.SetToolTip(CardButton, music.Title);
        string artistText = music.Artists != null && music.Artists.Count != 0 ? string.Join(", ", music.Artists) : null;
        ArtistText.Text = artistText ?? "";
        ArtistText.Visibility = artistText == null ? Visibility.Collapsed : Visibility.Visible;
        if (music.CoverArt != null && music.CoverArt.Length != 0)
        {
            _ = ShowCoverAsync(music.CoverArt);
        }
    }

    public Music Music => _music;

    /// <summary>shows a neutral "in progress" status while a conversion is running</summary>
    public void ShowConverting()
    {
        StatusText.Text = "Converting…";
        VisualStateManager.GoToState(this, "Idle", false);
    }

    /// <summary>shows the result of the most recent conversion attempt (accent = converted, critical = failed)</summary>
    public void ShowResult(bool success)
    {
        StatusText.Text = success ? "Converted" : "Failed";
        VisualStateManager.GoToState(this, success ? "Converted" : "Failed", false);
    }

    private void OnCardClick(object sender, RoutedEventArgs e)
    {
        _onClick(_music);
    }

    /// <summary>the cover art, decoded at twice the size so it stays sharp on a scaled display; the note stays if it cannot be read</summary>
    private async Task ShowCoverAsync(byte[] coverArt)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(coverArt);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            var image = new BitmapImage { DecodePixelWidth = ThumbnailSize * 2 };
            await image.SetSourceAsync(stream);
            CoverImage.Source = image;
            NoteIcon.Visibility = Visibility.Collapsed;
        }
        catch (Exception)
        {
            // not an image the app can read: the placeholder stays
        }
    }
}
