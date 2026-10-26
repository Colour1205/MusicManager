using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.InterfaceAdapter.Library;
using MusicManager.View.Library;

namespace MusicManager.App.View.Library;

/// <summary>
/// The window of one song: its cover, tags and path, a preview of what Process would do to it (the files written and
/// deleted, the lyric lines that get romaji, the tag changes) and a button to process just this song. A ContentDialog
/// with Process this song as its primary button, Options as its secondary one and Close; the library page keeps it up to
/// date while it is open (a finished lyrics check, new Options) and hands it the source of the preview, which is worked
/// out in the background and arrives in two parts: the lyrics first, the tags later.
///
/// It is compact: the changed lines scroll in a box of their own (see <see cref="ChangedLinesView"/>) and the dialog does
/// not grow with the lyrics. Only one ContentDialog can be open at a time, so Options closes this dialog for the moment
/// (the result is <see cref="ContentDialogResult.Secondary"/>); the library page shows the Options and then this very
/// dialog again, with its preview still running.
/// </summary>
public sealed partial class SongDialog : ContentDialog
{
    /// <summary>starts one load of the preview that reports to the listener; the dialog cancels the handle it gets back</summary>
    public delegate SongPreviewLoader.IHandle PreviewSource(SongPreviewLoader.IListener listener);

    private readonly Music _music;

    // what the preview shows now
    private SongPreviewLoader.IHandle _running;
    private int _generation; // a load whose generation is not this one has been replaced: its answers are ignored
    private bool _closed;
    private bool _unavailable = true; // no source of previews was given
    private bool _lyricsLoading;
    private bool _tagsLoading;
    private SongLyricsPreview _lyricsPreview;
    private SongTagsPreview _tagsPreview;

    public SongDialog(Music music, SongStatus status)
    {
        InitializeComponent();
        _music = music;
        AutomationProperties.SetName(this, BlankAs(music.Title, "Song"));

        BuildHeader();
        RenderLyrics();
        RenderTags();
    }

    public Music Music => _music;

    /// <summary>the library page enables the button only while no run is going and no check is still in progress</summary>
    public void SetProcessEnabled(bool enabled)
    {
        IsPrimaryButtonEnabled = enabled;
    }

    /// <summary>
    /// Called by the library page when the lyrics check finishes or the Options change. The one-line status is not shown any
    /// more (the preview says the same in detail), so there is nothing to update here.
    /// </summary>
    public void SetStatus(SongStatus status)
    {
    }

    // ------------------------------------------------------------------ the preview

    /// <summary>
    /// Starts the preview from this source, replacing (and cancelling) the one that is running; both parts show the status
    /// text and redacted blocks until their answer arrives. null = there is no preview.
    /// </summary>
    public void SetPreviewSource(PreviewSource newSource)
    {
        CancelPreview();
        _lyricsPreview = null;
        _tagsPreview = null;
        _unavailable = newSource == null;
        _lyricsLoading = newSource != null;
        _tagsLoading = newSource != null;
        RenderLyrics();
        RenderTags();
        if (newSource != null && !_closed)
        {
            int mine = ++_generation;
            _running = newSource(new PreviewListener(this, mine));
        }
    }

    /// <summary>both parts go back to the status text and redacted blocks (the answers that were shown are dropped)</summary>
    public void SetPreviewLoading()
    {
        _unavailable = false;
        _lyricsPreview = null;
        _tagsPreview = null;
        _lyricsLoading = true;
        _tagsLoading = true;
        RenderLyrics();
        RenderTags();
    }

    /// <summary>shows what Process would do to the lyrics of this song, in place of the status text and redacted blocks</summary>
    public void SetLyricsPreview(SongLyricsPreview preview)
    {
        _unavailable = false;
        _lyricsLoading = false;
        _lyricsPreview = preview;
        RenderLyrics();
    }

    /// <summary>shows what Process would do to the tags of this song, in place of the status text and redacted block</summary>
    public void SetTagsPreview(SongTagsPreview preview)
    {
        _unavailable = false;
        _tagsLoading = false;
        _tagsPreview = preview;
        RenderTags();
    }

    private void CancelPreview()
    {
        _generation++;
        if (_running != null)
        {
            _running.Cancel();
            _running = null;
        }
    }

    /// <summary>ends the dialog for good: the preview that is still being worked out is dropped (Java: dispose)</summary>
    public void Dispose()
    {
        _closed = true;
        CancelPreview();
        _coverToken++;
    }

    /// <summary>what the preview job tells the dialog; an answer of a job that was replaced or of a closed dialog is ignored</summary>
    private sealed class PreviewListener : SongPreviewLoader.IListener
    {
        private readonly SongDialog _dialog;
        private readonly int _mine;

        public PreviewListener(SongDialog dialog, int mine)
        {
            _dialog = dialog;
            _mine = mine;
        }

        private bool Current => _mine == _dialog._generation && !_dialog._closed;

        public void Lyrics(SongLyricsPreview preview)
        {
            if (Current)
            {
                _dialog.SetLyricsPreview(preview);
            }
        }

        public void Tags(SongTagsPreview preview)
        {
            if (Current)
            {
                _dialog.SetTagsPreview(preview);
            }
        }

        // the loader's progress words ("Asking Claude for the romaji... 30 of 74 lines") are not shown: the animated status text
        // only says that something is going on
        public void Progress(string text)
        {
        }

        public void TagsProgress(string text)
        {
        }
    }

    // ------------------------------------------------------------------ rendering of the preview

    private void RenderLyrics()
    {
        ShowLyricsWaiting(false);
        LyricsMessageNeutral.Visibility = Visibility.Collapsed;
        LyricsMessageBad.Visibility = Visibility.Collapsed;
        FileRows.Visibility = Visibility.Collapsed;
        LinesView.Visibility = Visibility.Collapsed;
        LyricsNotes.Visibility = Visibility.Collapsed;

        if (_unavailable)
        {
            ShowLyricsMessage("The preview is not available.", false);
        }
        else if (_lyricsLoading || _lyricsPreview == null)
        {
            ShowLyricsWaiting(true);
        }
        else
        {
            SongLyricsPreview preview = _lyricsPreview;
            var rows = new List<PreviewFileRow>();
            var deleted = new List<SongLyricsPreview.FileLine>();
            foreach (SongLyricsPreview.FileLine file in preview.Files)
            {
                if (file.GetKind() == SongLyricsPreview.FileLine.Kind.Delete)
                {
                    deleted.Add(file);
                }
                else
                {
                    rows.Add(PreviewFileRow.Write(file));
                }
            }
            if (deleted.Count > 0) // one short row: the old files share the song's name, only their endings differ
            {
                rows.Add(PreviewFileRow.Delete(deleted));
            }
            FileRows.ItemsSource = rows;
            FileRows.Visibility = Show(rows.Count > 0);

            if (preview.Lines.Count > 0)
            {
                LinesView.SetLines(preview.Lines);
                LinesView.Visibility = Visibility.Visible;
            }
            if (preview.Notes.Count > 0)
            {
                LyricsNotes.Text = string.Join("\n", preview.Notes);
                LyricsNotes.Visibility = Visibility.Visible;
            }
            if (preview.Files.Count == 0 && preview.Lines.Count == 0 && !JavaString.IsBlank(preview.Message))
            {
                ShowLyricsMessage(preview.Message, preview.Tone == SongStatus.Tone.Bad);
            }
        }
    }

    /// <summary>
    /// The status text and the redacted blocks of the Lyrics card. Each one is shown or collapsed itself (not only a panel
    /// around them), because the status text runs only while it is visible.
    /// </summary>
    private void ShowLyricsWaiting(bool waiting)
    {
        LyricsThinking.Visibility = Show(waiting);
        LyricsSkeleton.Visibility = Show(waiting);
    }

    /// <summary>one sentence of the preview (secondary text, or the critical color for a failure)</summary>
    private void ShowLyricsMessage(string text, bool failure)
    {
        TextBlock block = failure ? LyricsMessageBad : LyricsMessageNeutral;
        block.Text = text;
        block.Visibility = Visibility.Visible;
    }

    private void RenderTags()
    {
        List<SongTagsPreview.TagLine> changes = _tagsPreview == null
                ? new List<SongTagsPreview.TagLine>()
                : _tagsPreview.Changes;

        SongTagsPreview.TagLine artistChange = null;
        var otherChanges = new List<SongTagsPreview.TagLine>();
        foreach (SongTagsPreview.TagLine change in changes)
        {
            if (artistChange == null && string.Equals("Artist", change.Label, StringComparison.OrdinalIgnoreCase))
            {
                artistChange = change;
            }
            else
            {
                otherChanges.Add(change);
            }
        }

        var rows = new List<PreviewTagRow>();
        rows.Add(artistChange != null ? PreviewTagRow.Changed(artistChange) : PreviewTagRow.Plain("Artist", ArtistsOf(_music)));
        foreach (SongTagsPreview.TagLine change in otherChanges)
        {
            rows.Add(PreviewTagRow.Changed(change));
        }
        // (the album is in the header above: no row of its own, the window is short enough as it is)
        TagRows.ItemsSource = rows;

        // while the artist names are worked out: one redacted block under the Artist row (collapsed when it is done)
        TagsSkeleton.Visibility = Show(_tagsLoading);

        // the answer's own sentence, e.g. that nothing changes
        string note = !_tagsLoading && _tagsPreview != null && changes.Count == 0 ? _tagsPreview.Message : "";
        TagsNote.Text = note;
        TagsNote.Visibility = Show(!JavaString.IsBlank(note));
    }

    // ------------------------------------------------------------------ the header

    private int _coverToken;

    private void BuildHeader()
    {
        TitleText.Text = BlankAs(_music.Title, "(no title)");
        ToolTipService.SetToolTip(TitleText, TitleText.Text);
        ArtistsText.Text = ArtistsOf(_music);
        ToolTipService.SetToolTip(ArtistsText, ArtistsText.Text);
        if (_music.Album != null && !JavaString.IsBlank(_music.Album))
        {
            AlbumText.Text = _music.Album;
            ToolTipService.SetToolTip(AlbumText, _music.Album);
        }
        else
        {
            AlbumText.Visibility = Visibility.Collapsed;
        }
        PathText.Text = BlankAs(_music.Path, "");
        ToolTipService.SetToolTip(PathText, _music.Path);
        ApplyCover();
    }

    /// <summary>the cover (or the placeholder note), decoded in the background</summary>
    private void ApplyCover()
    {
        int token = ++_coverToken;
        var image = CoverImageCache.Get(_music.Path, _music.CoverArt, () =>
        {
            if (token == _coverToken)
            {
                ApplyCover();
            }
        });
        CoverBrush.ImageSource = image;
        CoverPlaceholder.Visibility = Show(image == null);
    }

    // ------------------------------------------------------------------ small helpers

    private static string ArtistsOf(Music music)
    {
        return music.Artists == null || music.Artists.Count == 0 ? "(no artist)" : string.Join(", ", music.Artists);
    }

    private static string BlankAs(string text, string fallback)
    {
        return text == null || JavaString.IsBlank(text) ? fallback : text;
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>
/// One row of the files list of the Lyrics card: a file that is written ("+ .lrc  new word-synced file, built from the
/// .ttml") or the one sentence for all the old files that go ("Deletes the old .ttml and .hmrc").
/// </summary>
public sealed class PreviewFileRow
{
    private PreviewFileRow()
    {
    }

    public bool IsDelete { get; private init; }

    /// <summary>the full file name (the tooltip)</summary>
    public string Name { get; private init; } = "";

    /// <summary>the file has the song's name: only its ending (".lrc") is worth showing</summary>
    public string Ending { get; private init; } = "";

    public string Note { get; private init; } = "";

    public string Sentence { get; private init; } = "";

    public Visibility WriteVisibility => IsDelete ? Visibility.Collapsed : Visibility.Visible;

    public Visibility DeleteVisibility => IsDelete ? Visibility.Visible : Visibility.Collapsed;

    public static PreviewFileRow Write(SongLyricsPreview.FileLine file)
    {
        return new PreviewFileRow { Name = file.Name, Ending = EndingOf(file.Name), Note = file.Note ?? "" };
    }

    /// <summary>"Deletes the old .ttml and .hmrc" (or "Moves ... to the Recycle Bin")</summary>
    public static PreviewFileRow Delete(List<SongLyricsPreview.FileLine> files)
    {
        var endings = new List<string>();
        bool recycle = false;
        foreach (SongLyricsPreview.FileLine file in files)
        {
            string ending = EndingOf(file.Name);
            if (!endings.Contains(ending))
            {
                endings.Add(ending);
            }
            recycle |= (file.Note ?? "").ToLowerInvariant().Contains("recycle", StringComparison.Ordinal);
        }
        string list = endings.Count == 1
                ? endings[0]
                : string.Join(", ", endings.GetRange(0, endings.Count - 1)) + " and " + endings[endings.Count - 1];
        string sentence = (recycle ? "Moves the old " : "Deletes the old ") + list + (recycle ? " to the Recycle Bin" : "");
        return new PreviewFileRow { IsDelete = true, Sentence = sentence };
    }

    private static string EndingOf(string name)
    {
        name ??= "";
        int dot = name.LastIndexOf('.');
        return dot >= 0 ? name.Substring(dot) : name;
    }
}

/// <summary>One row of the Tags card: "Artist  aespa" when nothing changes, "Artist  old (struck out)  ->  new" when it does.</summary>
public sealed class PreviewTagRow
{
    private const string Arrow = "  →  ";

    private PreviewTagRow()
    {
    }

    public string Label { get; private init; } = "";

    public string Value { get; private init; } = "";

    public string Before { get; private init; } = "";

    public string After { get; private init; } = "";

    public bool IsChange { get; private init; }

    public Visibility PlainVisibility => IsChange ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ChangedVisibility => IsChange ? Visibility.Visible : Visibility.Collapsed;

    public string ChangeToolTip => Before + Arrow + After;

    /// <summary>the new value, led by an arrow (it is shown under the old one, which is struck out)</summary>
    public string AfterText => "→  " + After;

    public static PreviewTagRow Plain(string label, string value)
    {
        return new PreviewTagRow { Label = label, Value = value ?? "" };
    }

    public static PreviewTagRow Changed(SongTagsPreview.TagLine change)
    {
        return new PreviewTagRow
        {
            Label = change.Label ?? "",
            Before = change.Before ?? "",
            After = change.After ?? "",
            IsChange = true,
        };
    }
}
