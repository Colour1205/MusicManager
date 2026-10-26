using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MusicManager.InterfaceAdapter.Library;
using MusicManager.View.Library;

namespace MusicManager.App.View.Library;

/// <summary>
/// The lyric lines of the song window whose romaji changes, one row per line: the time in a left column and, stacked,
/// the old romaji (critical color, struck out), the new Latin romaji (accent color, bold) or a muted italic note while it
/// is not made yet, and the original line. The data (which texts a line has, in which order) is the
/// <see cref="ChangedLinesModel"/> of the View project; the rows are an ItemsRepeater in a scroll box of a fixed
/// maximum height, so the list scrolls inside and the window around it does not grow with the lyrics.
/// </summary>
public sealed partial class ChangedLinesView : UserControl
{
    public ChangedLinesView()
    {
        InitializeComponent();
    }

    /// <summary>the tallest the box gets before it scrolls (240 by default)</summary>
    public double MaxBoxHeight
    {
        get => Scroller.MaxHeight;
        set => Scroller.MaxHeight = value;
    }

    /// <summary>shows these lines (null = none)</summary>
    public void SetLines(IEnumerable<SongLyricsPreview.LineLine> lines)
    {
        var items = new List<ChangedLineItem>();
        foreach (ChangedLinesModel.Block block in ChangedLinesModel.Blocks(lines))
        {
            items.Add(new ChangedLineItem(block));
        }
        Lines.ItemsSource = items;
    }
}

/// <summary>one changed line as its row binds to it, made from the paragraphs of a <see cref="ChangedLinesModel.Block"/></summary>
public sealed class ChangedLineItem
{
    public ChangedLineItem(ChangedLinesModel.Block block)
    {
        Time = block.Time;
        foreach (ChangedLinesModel.Paragraph paragraph in block.Paragraphs)
        {
            switch (paragraph.Kind)
            {
                case ChangedLinesModel.Kind.OldRomaji:
                    OldRomaji = paragraph.Text;
                    break;
                case ChangedLinesModel.Kind.NewRomaji:
                    NewRomaji = paragraph.Text;
                    break;
                case ChangedLinesModel.Kind.NotMade:
                    NotMade = true;
                    break;
                case ChangedLinesModel.Kind.Original:
                    Original = paragraph.Text;
                    break;
            }
        }
    }

    public string Time { get; }

    public string OldRomaji { get; } = "";

    public string NewRomaji { get; } = "";

    public bool NotMade { get; }

    public string NotMadeText => ChangedLinesModel.NotMadeYet;

    public string Original { get; } = "";

    public Visibility OldVisibility => OldRomaji.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

    public Visibility NewVisibility => NewRomaji.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

    public Visibility NotMadeVisibility => NotMade ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OriginalVisibility => Original.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
}
