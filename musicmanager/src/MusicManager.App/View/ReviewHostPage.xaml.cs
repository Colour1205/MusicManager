using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MusicManager.App.View;

/// <summary>
/// The Frame page that shows the review page ProcessFlow made. A Frame navigates to page TYPES, but the flow hands the
/// window a ready-made review instance (it feeds that instance the events of the run), so this page just holds it: the
/// instance comes in as the navigation parameter and becomes the content. It also passes the title bar's Back request on
/// to the review when the review wants to answer it (<see cref="IBackHandler"/>).
/// </summary>
public sealed partial class ReviewHostPage : Page, IBackHandler
{
    public ReviewHostPage()
    {
        InitializeComponent();
    }

    /// <summary>the review that is shown (null when the page was left)</summary>
    public object Review { get; private set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        Review = e.Parameter;
        Content = e.Parameter as UIElement;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        // free the review for good: a left review is of no use any more, and a UIElement has only one parent
        Content = null;
        Review = null;
    }

    public bool OnBackRequested()
    {
        return Review is IBackHandler handler && handler.OnBackRequested();
    }
}
