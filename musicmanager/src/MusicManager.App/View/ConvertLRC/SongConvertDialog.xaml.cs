using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MusicManager.App.View.Library;
using MusicManager.Entity;
using MusicManager.UseCase.ConvertLRC;

namespace MusicManager.App.View.ConvertLRC;

/// <summary>
/// Modal dialog for converting a single song: lets the user pick target formats just for this song without touching the
/// app's general Settings, then triggers the conversion.
/// </summary>
/// <remarks>
/// Deprecated: the old stand-alone Convert LRC feature, kept and unused. The app is one window now (View/Library); the
/// convert feature comes back in it by switching on AppFeatures.LegacyConvertLrc.
/// </remarks>
[Obsolete("the old stand-alone Convert LRC feature; the convert feature is behind AppFeatures.LegacyConvertLrc")]
public sealed partial class SongConvertDialog : ContentDialog
{
    private readonly List<KeyValuePair<string, CheckBox>> _formatCheckBoxes = new List<KeyValuePair<string, CheckBox>>();
    private readonly Action<List<string>> _onConvert;

    /// <param name="owner">the window, an element of it, or its XamlRoot</param>
    public SongConvertDialog(object owner, Music music, List<string> defaultFormats, Action<List<string>> onConvert)
    {
        InitializeComponent();
        XamlRoot = ContentDialogPrompts.RootOf(owner);
        Title = "Convert " + music.Title;
        _onConvert = onConvert;

        foreach (string format in ConvertLRCInputData.AvailableFormats)
        {
            var checkBox = new CheckBox { Content = format, IsChecked = defaultFormats.Contains(format) };
            _formatCheckBoxes.Add(new KeyValuePair<string, CheckBox>(format, checkBox));
            FormatsPanel.Children.Add(checkBox);
        }
    }

    private void OnConvertClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var selected = new List<string>();
        foreach (KeyValuePair<string, CheckBox> entry in _formatCheckBoxes)
        {
            if (entry.Value.IsChecked == true)
            {
                selected.Add(entry.Key);
            }
        }
        if (selected.Count == 0)
        {
            // one dialog at a time: the "No formats selected" message is a line in this dialog instead of a second one
            FormatError.IsOpen = true;
            args.Cancel = true;
            return;
        }
        _onConvert(selected);
    }
}
