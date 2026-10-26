namespace MusicManager.UseCase.StandardizeLyrics;

public class SongLyricsResult
{
    private readonly string _audioPath;
    private readonly LyricsAction _action;
    private readonly string _detail; // warnings or the failure reason; empty when there is nothing to add
    private readonly List<string> _deletedFiles; // deprecated files removed (or that would be removed in a dry run)
    private readonly int _romajiAdded; // Korean/Japanese lines given a romaji line (dry run: that would be)
    private readonly int _romajiReplaced; // of the lines above, those that had old non-Latin romaji replaced
    private readonly int _romajiPresent; // lines that already had Latin romaji and keep it
    private readonly int _romajiMissing; // lines that needed romaji but could not get it (real runs only)
    private readonly string _writtenLrcPath; // the .lrc this run writes (a dry run: would write), or null when it writes none
    private readonly List<LyricLineChange> _lineChanges; // the lines whose romaji changes; only filled by a detailed preview

    public SongLyricsResult(string audioPath, LyricsAction action, string detail, List<string> deletedFiles)
        : this(audioPath, action, detail, deletedFiles, 0, 0, 0, 0)
    {
    }

    public SongLyricsResult(string audioPath, LyricsAction action, string detail, List<string> deletedFiles,
        int romajiAdded, int romajiReplaced, int romajiPresent, int romajiMissing)
        : this(audioPath, action, detail, deletedFiles, romajiAdded, romajiReplaced, romajiPresent, romajiMissing, null,
            null)
    {
    }

    public SongLyricsResult(string audioPath, LyricsAction action, string detail, List<string> deletedFiles,
        int romajiAdded, int romajiReplaced, int romajiPresent, int romajiMissing, string writtenLrcPath,
        List<LyricLineChange> lineChanges)
    {
        _writtenLrcPath = writtenLrcPath;
        _lineChanges = lineChanges ?? new List<LyricLineChange>();
        _audioPath = audioPath;
        _action = action;
        _detail = detail ?? "";
        _deletedFiles = deletedFiles ?? new List<string>();
        _romajiAdded = romajiAdded;
        _romajiReplaced = romajiReplaced;
        _romajiPresent = romajiPresent;
        _romajiMissing = romajiMissing;
    }

    public string AudioPath => _audioPath;

    public LyricsAction Action => _action;

    public string Detail => _detail;

    public List<string> DeletedFiles => _deletedFiles;

    /// <summary>lines that got (or in a dry run would get) a romaji line, counting the replaced ones</summary>
    public int RomajiAdded => _romajiAdded;

    /// <summary>of <see cref="RomajiAdded"/>, the lines whose old romaji in Chinese characters (or other non-Latin) was replaced</summary>
    public int RomajiReplaced => _romajiReplaced;

    /// <summary>lines that already had romaji in Latin letters and were left as they are</summary>
    public int RomajiPresent => _romajiPresent;

    public int RomajiMissing => _romajiMissing;

    /// <summary>the .lrc file this run writes (in a dry run: would write), or null when the song's lyric files stay as they are</summary>
    public string WrittenLrcPath => _writtenLrcPath;

    /// <summary>
    /// the Korean/Japanese lines whose romaji is added or replaced, in song order; empty unless the run asked for a
    /// detailed preview (<see cref="StandardizeLyricsInputData.IsPreviewDetail"/>)
    /// </summary>
    public List<LyricLineChange> LineChanges => _lineChanges;
}
