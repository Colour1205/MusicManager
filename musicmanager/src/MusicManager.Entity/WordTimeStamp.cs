namespace MusicManager.Entity;

/// <summary>One word of a lyric line with its start and end time in milliseconds.</summary>
public class WordTimeStamp
{
    private readonly string _word;
    private readonly long _startTimeMs;
    private readonly long _endTimeMs;

    public WordTimeStamp(string word, long startTimeMs, long endTimeMs)
    {
        _word = word;
        _startTimeMs = startTimeMs;
        _endTimeMs = endTimeMs;
    }

    public string Word => _word;

    public long StartTimeMs => _startTimeMs;

    public long EndTimeMs => _endTimeMs;
}
