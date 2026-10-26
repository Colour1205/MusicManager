using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MusicManager.Entity;
using MusicManager.UseCase.ConvertLRC;
using MusicManager.View.Library;

namespace MusicManager.App.View.Library;

/// <summary>
/// The Options window (Java: view/Library/OptionsDialog): what Process does (lyrics, romaji, artists, the Claude key and
/// model, and, when AppFeatures.LegacyConvertLrc is on, the Convert formats mode). A ContentDialog: Save applies, Cancel
/// and Esc discard. The choices, which control depends on which and how they turn into StandardizeOptions live in
/// OptionsModel (View project); this class puts them on the screen and reads them back.
/// </summary>
public sealed partial class OptionsDialog : ContentDialog
{
    private readonly bool _legacyConvert;
    private readonly AppSettings _working;
    private readonly Action<AppSettings> _onSaved;
    private readonly List<OptionsModel.SeparatorChoice> _separatorChoices =
            new List<OptionsModel.SeparatorChoice>(OptionsModel.Separators);
    private readonly List<KeyValuePair<string, CheckBox>> _formatChecks = new List<KeyValuePair<string, CheckBox>>();
    private bool _loading;
    private bool _convertMode;
    private string _model = "";
    private bool _modelHooked;

    private OptionsDialog(XamlRoot xamlRoot, AppSettings current, bool apiKeyAvailable, Action<AppSettings> onSaved,
            bool legacyConvert)
    {
        InitializeComponent();
        XamlRoot = xamlRoot;
        _legacyConvert = legacyConvert;
        _working = current.Copy(); // the dialog edits a copy; onSaved gets it only when Save is pressed
        _onSaved = onSaved;

        RomajiAddsText.Text = ReviewText.RomajiAdds;
        RomajiReplacesText.Text = ReviewText.RomajiReplaces;
        RomajiKeepsText.Text = ReviewText.RomajiKeeps;
        KeyHintText.Text = apiKeyAvailable
                ? "Found ANTHROPIC_API_KEY in your environment: leave this empty, or type another key. "
                        + "A typed key is kept in memory only and never saved."
                : "No ANTHROPIC_API_KEY found: type a key here. It is kept in memory only and never saved.";
        ModelBox.ItemsSource = OptionsModel.Models;

        if (legacyConvert)
        {
            BuildConvertPage();
            ModeBar.Visibility = Visibility.Visible;
        }

        Load(_working.StandardizeOptions);
        ShowMode(legacyConvert && _working.StandardizeOptions.GetMode() == StandardizeOptions.Mode.Convert);

        // the options are taller than a small window: the scroll viewer in the dialog is as tall as the window allows
        LimitHeight();
        if (xamlRoot != null)
        {
            xamlRoot.Changed += OnRootChanged;
            Closed += (sender, args) => xamlRoot.Changed -= OnRootChanged;
        }
    }

    /// <summary>The window's height less the title, the buttons and the dialog's own padding.</summary>
    private void LimitHeight()
    {
        double window = XamlRoot == null ? 700 : XamlRoot.Size.Height;
        ContentScroller.MaxHeight = Math.Max(200, window - 250);
    }

    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        LimitHeight();
    }

    /// <summary>
    /// Shows the dialog on a COPY of the settings; onSaved gets the edited copy only if the user presses Save. The task
    /// ends when the dialog is closed.
    /// </summary>
    /// <param name="xamlRoot">the XamlRoot of the window (or of any element in it)</param>
    /// <param name="apiKeyAvailable">whether ANTHROPIC_API_KEY can be found without typing a key</param>
    public static async Task OpenAsync(XamlRoot xamlRoot, AppSettings settings, bool apiKeyAvailable,
            Action<AppSettings> onSaved)
    {
        OptionsDialog dialog = Create(xamlRoot, settings, apiKeyAvailable, onSaved, AppFeatures.LegacyConvertLrc);
        await ContentDialogPrompts.ShowAsync(dialog);
    }

    /// <summary>The dialog, not shown yet; the switch is a parameter so that a test can build either mode.</summary>
    public static OptionsDialog Create(XamlRoot xamlRoot, AppSettings settings, bool apiKeyAvailable,
            Action<AppSettings> onSaved, bool legacyConvert)
    {
        return new OptionsDialog(xamlRoot, settings, apiKeyAvailable, onSaved, legacyConvert);
    }

    // ------------------------------------------------------------------ the controls and the options

    /// <summary>Puts the options into the controls.</summary>
    private void Load(StandardizeOptions options)
    {
        _loading = true;
        try
        {
            OptionsModel.Values values = OptionsModel.Values.From(options);
            LyricsSwitch.IsOn = values.Lyrics;
            RomajiSwitch.IsOn = values.Romaji;
            RecycleSwitch.IsOn = values.RecycleBin;
            ArtistsSwitch.IsOn = values.Artists;
            ClaudeArtistsSwitch.IsOn = values.UseClaudeForArtists;

            OptionsModel.SeparatorChoice choice = OptionsModel.ChoiceFor(values.Separator);
            if (!_separatorChoices.Contains(choice))
            {
                _separatorChoices.Add(choice); // a separator the list does not have, from an edited settings file: keep it
            }
            SeparatorBox.ItemsSource = _separatorChoices;
            SeparatorBox.SelectedItem = choice;

            ApiKeyBox.Password = values.ApiKey ?? "";
            ShowModel(values.Model ?? "");
        }
        finally
        {
            _loading = false;
        }
        UpdateStates();
    }

    /// <summary>
    /// Puts the model into the editable box: a model of the list is selected, any other id is typed in. Once more when the
    /// dialog is on the screen, because the text of an editable box set before that does not always stay.
    /// </summary>
    private void ShowModel(string model)
    {
        _model = model;
        ApplyModel();
        if (!_modelHooked)
        {
            _modelHooked = true;
            Opened += (sender, args) => ApplyModel();
        }
    }

    private void ApplyModel()
    {
        bool wasLoading = _loading;
        _loading = true;
        try
        {
            if (Array.IndexOf(OptionsModel.Models, _model) >= 0)
            {
                ModelBox.SelectedItem = _model;
            }
            else
            {
                ModelBox.Text = _model;
            }
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    /// <summary>The controls as values.</summary>
    private OptionsModel.Values ReadValues()
    {
        var values = new OptionsModel.Values();
        values.Lyrics = LyricsSwitch.IsOn;
        values.Romaji = RomajiSwitch.IsOn;
        values.RecycleBin = RecycleSwitch.IsOn;
        values.Artists = ArtistsSwitch.IsOn;
        values.UseClaudeForArtists = ClaudeArtistsSwitch.IsOn;
        values.Separator = SeparatorBox.SelectedItem is OptionsModel.SeparatorChoice separator
                ? separator.Value() : StandardizeOptions.DefaultSeparator;
        values.Model = ModelText();
        values.ApiKey = ApiKeyBox.Password;
        return values;
    }

    /// <summary>
    /// What is in the editable model box. ComboBox.Text is only brought up to date when the text is submitted or the box
    /// loses focus; Save can be pressed with Enter straight from the text, so the text box itself is asked first.
    /// </summary>
    private string ModelText()
    {
        TextBox editor = FindTextBox(ModelBox);
        string text = editor != null ? editor.Text : ModelBox.Text;
        return text ?? "";
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

    /// <summary>A control that has no effect is greyed out (the row dims with its switch).</summary>
    private void UpdateStates()
    {
        OptionsModel.Enabled enabled = ReadValues().Enabled();
        SetEnabled(RomajiRow, RomajiSwitch, enabled.Romaji);
        SetEnabled(RecycleRow, RecycleSwitch, enabled.RecycleBin);
        SetEnabled(ClaudeArtistsRow, ClaudeArtistsSwitch, enabled.UseClaudeForArtists);
        SetEnabled(SeparatorRow, SeparatorBox, enabled.Separator);
        ApiKeyBox.IsEnabled = enabled.ClaudeSettings;
        ModelBox.IsEnabled = enabled.ClaudeSettings;
        ClaudeExpander.Opacity = enabled.ClaudeSettings ? 1.0 : 0.6;
    }

    private static void SetEnabled(UIElement row, Control control, bool enabled)
    {
        control.IsEnabled = enabled;
        row.Opacity = enabled ? 1.0 : 0.6;
    }

    private void OnSwitchToggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            UpdateStates();
        }
    }

    // ------------------------------------------------------------------ the deprecated Convert mode

    /// <summary>The Convert formats page: which formats to write out next to the songs.</summary>
    private void BuildConvertPage()
    {
        List<string> ticked = OptionsModel.FormatsToTick(_working.DefaultTargetFormats);
        foreach (string format in ConvertLRCInputData.AvailableFormats)
        {
            var box = new CheckBox { Content = OptionsModel.FormatLabel(format), IsChecked = ticked.Contains(format) };
            _formatChecks.Add(new KeyValuePair<string, CheckBox>(format, box));
            FormatsPanel.Children.Add(box);
        }
    }

    private void OnModeChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (!_loading)
        {
            ShowMode(sender.SelectedItem == ConvertModeItem);
        }
    }

    private void ShowMode(bool convert)
    {
        if (!_legacyConvert)
        {
            return;
        }
        _convertMode = convert;
        bool wasLoading = _loading;
        _loading = true;
        ModeBar.SelectedItem = convert ? ConvertModeItem : StandardizeModeItem;
        _loading = wasLoading;
        StandardizePanel.Visibility = convert ? Visibility.Collapsed : Visibility.Visible;
        ConvertPanel.Visibility = convert ? Visibility.Visible : Visibility.Collapsed;
        FormatError.IsOpen = false;
    }

    // ------------------------------------------------------------------ Save

    private void OnSaveClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        StandardizeOptions options = ReadValues().ApplyTo(_working.StandardizeOptions);
        if (_legacyConvert)
        {
            var ticked = new List<string>();
            foreach (KeyValuePair<string, CheckBox> entry in _formatChecks)
            {
                if (entry.Value.IsChecked == true)
                {
                    ticked.Add(entry.Key);
                }
            }
            if (_convertMode && ticked.Count == 0)
            {
                // one dialog at a time: the "No formats selected" message is a line in this dialog instead of a second one
                FormatError.IsOpen = true;
                args.Cancel = true;
                return;
            }
            options.SetMode(_convertMode ? StandardizeOptions.Mode.Convert : StandardizeOptions.Mode.Standardize);
            if (ticked.Count > 0)
            {
                _working.DefaultTargetFormats = OptionsModel.SelectedFormats(ticked);
            }
        }
        _working.StandardizeOptions = options;
        _onSaved(_working);
    }
}
