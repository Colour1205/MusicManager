namespace MusicManager.UseCase.StandardizeLyrics;

public class StandardizeLyricsOutputData
{
    private readonly List<SongLyricsResult> _results;
    private readonly bool _dryRun;
    private readonly RomajiSummary _romaji;

    public StandardizeLyricsOutputData(List<SongLyricsResult> results, bool dryRun)
        : this(results, dryRun, RomajiSummary.None)
    {
    }

    public StandardizeLyricsOutputData(List<SongLyricsResult> results, bool dryRun, RomajiSummary romaji)
    {
        _results = results;
        _dryRun = dryRun;
        _romaji = romaji ?? RomajiSummary.None;
    }

    public List<SongLyricsResult> Results => _results;

    public bool IsDryRun => _dryRun;

    public RomajiSummary Romaji => _romaji;

    public int Count(LyricsAction action)
    {
        int n = 0;
        foreach (SongLyricsResult r in _results)
        {
            if (r.Action == action)
            {
                n++;
            }
        }
        return n;
    }

    public int DeletedFileCount
    {
        get
        {
            int n = 0;
            foreach (SongLyricsResult r in _results)
            {
                n += r.DeletedFiles.Count;
            }
            return n;
        }
    }
}
