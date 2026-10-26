namespace MusicManager.UseCase.StandardizeLyrics;

/// <summary>what the romaji part of a run did (or, in a preview, would do)</summary>
public sealed class RomajiSummary
{
    /// <summary>the run did not include romaji</summary>
    public static readonly RomajiSummary None = new RomajiSummary(false, 0, 0, 0, 0, 0, 0, null);

    private readonly bool _requested;
    private readonly int _uniqueLines; // different lines that need a romaji line (a repeated chorus counts once)
    private readonly int _cachedLines; // of those, already romanized by an earlier run
    private readonly int _requests; // requests to Claude that were (or would be) needed
    private readonly int _linesAdded; // lyric lines that got (dry run: would get) a romaji line, counting replaced ones
    private readonly int _linesReplaced; // of those, lines whose old non-Latin romaji was replaced
    private readonly int _linesMissing; // lyric lines that needed one but could not get it
    private readonly string _stoppedBecause; // why Claude was not asked about every line, or null

    public RomajiSummary(bool requested, int uniqueLines, int cachedLines, int requests, int linesAdded,
            int linesReplaced, int linesMissing, string stoppedBecause)
    {
        _requested = requested;
        _uniqueLines = uniqueLines;
        _cachedLines = cachedLines;
        _requests = requests;
        _linesAdded = linesAdded;
        _linesReplaced = linesReplaced;
        _linesMissing = linesMissing;
        _stoppedBecause = stoppedBecause;
    }

    public bool IsRequested => _requested;

    public int UniqueLines => _uniqueLines;

    public int CachedLines => _cachedLines;

    public int Requests => _requests;

    public int LinesAdded => _linesAdded;

    /// <summary>lines with old romaji in Chinese characters (or any non-Latin script) that are, or would be, replaced</summary>
    public int LinesReplaced => _linesReplaced;

    public int LinesMissing => _linesMissing;

    public string StoppedBecause => _stoppedBecause;
}
