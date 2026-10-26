using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MusicManager.App.View.Library;

/// <summary>
/// A read-only card of the review (Java: the renderer of ResultCardList). It shows the <see cref="ResultCardItem"/> that is
/// its DataContext, so a virtualized list can recycle it.
/// </summary>
public sealed partial class ResultCard : UserControl
{
    public ResultCard()
    {
        InitializeComponent();
        DataContextChanged += (sender, args) => Show(args.NewValue as ResultCardItem);
    }

    private void Show(ResultCardItem item)
    {
        if (item == null)
        {
            return;
        }
        TitleText.Text = item.Title;
        SetText(HeadlineText, item.Headline);
        SetText(DetailsText, item.Details);
        SetText(StatusText, item.Status);
        VisualStateManager.GoToState(this, item.Tone == ResultTone.Critical ? "Critical"
                : item.Tone == ResultTone.Accent ? "Accent" : "Neutral", false);
    }

    /// <summary>A line that has no text takes no room.</summary>
    private static void SetText(TextBlock block, string text)
    {
        block.Text = text;
        block.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
