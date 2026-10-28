using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using MusicManager.View.Library;

namespace MusicManager.App.View.Library;

/// <summary>
/// A row of the folder tree of the review. It shows the <see cref="FolderNode"/> that is its DataContext, so the tree can
/// recycle it. A tree filled through its RootNodes hands each row the TreeViewNode, with the FolderNode as its Content (only
/// a tree filled through ItemsSource hands the content itself), so both are taken.
/// </summary>
public sealed partial class FolderTreeRow : UserControl
{
    public FolderTreeRow()
    {
        InitializeComponent();
        DataContextChanged += (sender, args) => Show(args.NewValue as FolderNode ?? (args.NewValue as TreeViewNode)?.Content as FolderNode);
    }

    private void Show(FolderNode node)
    {
        if (node == null)
        {
            return;
        }
        AutomationProperties.SetName(this, node.ToString()); // what a screen reader says for the row
        bool isFolder = node.Kind == FolderNodeKind.Library || node.Kind == FolderNodeKind.Folder;
        NameText.Text = node.Name;
        NameText.FontWeight = isFolder ? FontWeights.SemiBold : FontWeights.Normal;
        DetailText.Text = node.Detail;
        DetailText.Visibility = node.Detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible; // a line that has no text takes no room
        FolderIcon.Visibility = isFolder ? Visibility.Visible : Visibility.Collapsed;
        SongIcon.Visibility = node.Kind == FolderNodeKind.Song ? Visibility.Visible : Visibility.Collapsed;
        VisualStateManager.GoToState(this, node.Kind == FolderNodeKind.Note ? "Note"
                : node.Tone == FolderTone.Problem ? "Problem"
                : node.Tone == FolderTone.Gone ? "Gone"
                : node.Tone == FolderTone.Changed ? "Changed" : "Plain", false);
    }
}
