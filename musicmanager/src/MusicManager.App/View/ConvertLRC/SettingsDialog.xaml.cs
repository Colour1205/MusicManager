using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MusicManager.App.View.Library;
using MusicManager.UseCase.ConvertLRC;

namespace MusicManager.App.View.ConvertLRC;

/// <summary>
/// Modal dialog letting the user pick which of the available target formats to convert lyrics into.
/// </summary>
/// <remarks>
/// Deprecated: the old stand-alone Convert LRC feature, kept and unused. The app is one window now (View/Library); the
/// convert feature comes back in it by switching on AppFeatures.LegacyConvertLrc.
/// </remarks>
[Obsolete("the old stand-alone Convert LRC feature; the convert feature is behind AppFeatures.LegacyConvertLrc")]
public sealed partial class SettingsDialog : ContentDialog
{
    private readonly List<KeyValuePair<string, CheckBox>> _formatCheckBoxes = new List<KeyValuePair<string, CheckBox>>();
    private readonly Action<List<string>> _onSave;

    /// <param name="owner">the window, an element of it, or its XamlRoot</param>
    public SettingsDialog(object owner, List<string> selectedFormats, Action<List<string>> onSave)
    {
        InitializeComponent();
        XamlRoot = ContentDialogPrompts.RootOf(owner);
        _onSave = onSave;

        foreach (string format in ConvertLRCInputData.AvailableFormats)
        {
            var checkBox = new CheckBox { Content = format, IsChecked = selectedFormats.Contains(format) };
            _formatCheckBoxes.Add(new KeyValuePair<string, CheckBox>(format, checkBox));
            FormatsPanel.Children.Add(checkBox);
        }
    }

    private void OnSaveClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var selected = new List<string>();
        foreach (KeyValuePair<string, CheckBox> entry in _formatCheckBoxes)
        {
            if (entry.Value.IsChecked == true)
            {
                selected.Add(entry.Key);
            }
        }
        _onSave(selected);
    }
}
