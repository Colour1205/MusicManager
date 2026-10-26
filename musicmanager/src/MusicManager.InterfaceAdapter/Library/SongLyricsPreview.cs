namespace MusicManager.InterfaceAdapter.Library;

/// <summary>
/// What Process would do to one song's lyrics, ready to show in the song window: the files that are written and
/// deleted, and the lyric lines whose romaji changes (old romaji above, new romaji below). Made by
/// <see cref="SongPreviewMapper"/> from a detailed preview of the lyrics use case.
/// </summary>
public sealed class SongLyricsPreview
{
    /// <summary>one lyric file that is written, deleted or left as it is</summary>
    public sealed class FileLine
    {
        public enum Kind
        {
            /// <summary>a .lrc is written (new, or rewritten)</summary>
            Write,
            /// <summary>a deprecated file is deleted or sent to the Recycle Bin</summary>
            Delete
        }

        private readonly Kind _kind;
        private readonly string _name; // the file name without folders
        private readonly string _note; // short: "new word-synced file, built from the .ttml", "deleted", "goes to the Recycle Bin"

        public FileLine(Kind kind, string name, string note)
        {
            _kind = kind;
            _name = name;
            _note = note;
        }

        /// <summary>
        /// Java getKind(). A method, not a property named Kind: a C# member cannot share its name with the nested enum
        /// <see cref="Kind"/> (the same rule as Lyric.GetSyncType()).
        /// </summary>
        public Kind GetKind() => _kind;

        public string Name => _name;

        public string Note => _note;
    }

    /// <summary>one lyric line whose romaji changes</summary>
    public sealed class LineLine
    {
        private readonly string _time; // "1:23"
        private readonly string _original; // the Korean/Japanese line
        private readonly string _oldRomaji; // old non-Latin romaji that is replaced, or null
        private readonly string _newRomaji; // the Latin romaji, or null while it is not known

        public LineLine(string time, string original, string oldRomaji, string newRomaji)
        {
            _time = time;
            _original = original;
            _oldRomaji = oldRomaji;
            _newRomaji = newRomaji;
        }

        public string Time => _time;

        public string Original => _original;

        public string OldRomaji => _oldRomaji;

        public string NewRomaji => _newRomaji;
    }

    private readonly List<FileLine> _files;
    private readonly string _linesTitle;
    private readonly List<LineLine> _lines;
    private readonly List<string> _notes;
    private readonly string _message;
    private readonly SongStatus.Tone _tone;
    private readonly int _linesWithoutRomaji;

    /// <param name="files">what is written and deleted, in the order to show it (empty when nothing is)</param>
    /// <param name="linesTitle">
    /// the heading over the lines, e.g. "74 lines get romaji" or "2 lines get romaji, 45 old Chinese-character lines
    /// are replaced"; empty when there are no lines
    /// </param>
    /// <param name="lines">the changed lines in song order</param>
    /// <param name="notes">warnings and other remarks (e.g. why romaji text is missing), one sentence each</param>
    /// <param name="message">
    /// the one sentence for a song with nothing to show ("Nothing changes in the lyrics", "There is no lyric file next
    /// to this song", "The lyrics are off in Options", a failure reason); empty when files or lines are shown
    /// </param>
    /// <param name="tone">ACTIVE when something changes, MUTED when nothing does, BAD for a failure</param>
    /// <param name="linesWithoutRomaji">how many of the lines have no new romaji text yet (no key, or Claude failed)</param>
    public SongLyricsPreview(List<FileLine> files, string linesTitle, List<LineLine> lines, List<string> notes,
        string message, SongStatus.Tone? tone, int linesWithoutRomaji)
    {
        _files = files == null ? new List<FileLine>() : files;
        _linesTitle = linesTitle == null ? "" : linesTitle;
        _lines = lines == null ? new List<LineLine>() : lines;
        _notes = notes == null ? new List<string>() : notes;
        _message = message == null ? "" : message;
        _tone = tone ?? SongStatus.Tone.Muted;
        _linesWithoutRomaji = linesWithoutRomaji;
    }

    public List<FileLine> Files => _files;

    public string LinesTitle => _linesTitle;

    public List<LineLine> Lines => _lines;

    public List<string> Notes => _notes;

    public string Message => _message;

    public SongStatus.Tone Tone => _tone;

    public int LinesWithoutRomaji => _linesWithoutRomaji;
}
