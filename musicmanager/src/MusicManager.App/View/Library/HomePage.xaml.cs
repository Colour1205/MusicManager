using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MusicManager.Common;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace MusicManager.App.View.Library;

/// <summary>
/// The first page (Java: HomePage): pick the music folder and press Open. This is the only action button of the start
/// page; what to do with the songs is decided on the next page, where the list stays in view. A folder that is missing
/// or was not readable is reported in an InfoBar on the page.
/// </summary>
public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();

        string lastPath = MusicManagerFrame.Current.Context.Settings.Load().LastPath;
        if (lastPath != null)
        {
            PathBox.Text = lastPath;
        }

        // the first time the page is shown it is not laid out yet when the window asks for the focus
        Loaded += (_, _) => FocusPath();
    }

    /// <summary>the folder in the field, trimmed</summary>
    public string Path() => JavaString.Trim(PathBox.Text);

    public void SetPath(string path)
    {
        PathBox.Text = path;
    }

    /// <summary>puts the keyboard cursor in the field (when the page is shown)</summary>
    public void FocusPath()
    {
        PathBox.Focus(FocusState.Programmatic);
        PathBox.SelectAll();
    }

    /// <summary>shows a message above the Open button (for example why a folder could not be opened)</summary>
    public void ShowMessage(string title, string message, InfoBarSeverity severity)
    {
        MessageBar.Title = title;
        MessageBar.Message = message;
        MessageBar.Severity = severity;
        MessageBar.IsOpen = true;
    }

    private void PathBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) // Enter in the field
        {
            e.Handled = true;
            Open();
        }
    }

    private void PathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        MessageBar.IsOpen = false; // the message was about the previous text
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        Browse();
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        Open();
    }

    private async void Browse()
    {
        FolderPicker picker = new FolderPicker();
        picker.SuggestedStartLocation = Directory.Exists(System.IO.Path.Combine(UserHome.Path, "Downloads"))
                ? PickerLocationId.Downloads
                : PickerLocationId.Desktop;
        picker.CommitButtonText = "Select Music Folder";
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, MusicManagerFrame.Current.Handle);

        Windows.Storage.StorageFolder folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            PathBox.Text = folder.Path;
        }
    }

    private void Open()
    {
        string path = Path();
        if (path.Length == 0 || !Directory.Exists(path))
        {
            ShowMessage("Invalid path", "Please choose a valid music folder.", InfoBarSeverity.Warning);
            PathBox.Focus(FocusState.Programmatic);
            return;
        }

        // load fresh instead of keeping a copy from startup: the Options window saves its choices in the same file
        MusicManager.Entity.AppSettings settings = MusicManagerFrame.Current.Context.Settings.Load();
        settings.LastPath = path;
        MusicManagerFrame.Current.Context.Settings.Save(settings);

        MusicManagerFrame.Current.OpenFolder(path);
    }
}
