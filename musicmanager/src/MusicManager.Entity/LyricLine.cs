namespace MusicManager.Entity;

/// <summary>One line of a lyric: its text, its timing, and (when present) per-word timing, translations and romanization.</summary>
public class LyricLine
{
    private readonly string _line;
    private readonly long _startTimeMs; // line start time
    private readonly long _endTimeMs; // line end time
    private WordTimeStamp[] _wordTimeStamps; // empty if line synced
    // translation / transliteration lines that share this line's timing, in file order
    private List<string> _translations = new List<string>();
    // the romanization of this line, with the timing of the words of the line; null when it has none. It has one word per
    // word of the line, except that the reading of several characters that make up one word is a single word lasting as
    // long as all of them (今|日 => "kyou"), so it can have fewer.
    // In the standard file it is the word-synced line written just above the original.
    private WordTimeStamp[] _romanizedWords;

    public LyricLine(string line, long startTimeMs, long endTimeMs)
    {
        _line = line;
        _startTimeMs = startTimeMs;
        _endTimeMs = endTimeMs;
    }

    public string Line => _line;

    public long StartTimeMs => _startTimeMs;

    public long EndTimeMs => _endTimeMs;

    public WordTimeStamp[] WordTimeStamps
    {
        get => _wordTimeStamps;
        set => _wordTimeStamps = value;
    }

    /// <summary>true when this line carries per-word timing</summary>
    public bool IsWordSynced => _wordTimeStamps != null && _wordTimeStamps.Length > 0;

    public WordTimeStamp[] RomanizedWords
    {
        get => _romanizedWords;
        set => _romanizedWords = value;
    }

    public bool HasRomanization() => _romanizedWords != null && _romanizedWords.Length > 0;

    /// <summary>The translation lines of this line; the setter keeps a copy (null gives an empty list).</summary>
    public List<string> Translations
    {
        get => _translations;
        set => _translations = value == null ? new List<string>() : new List<string>(value);
    }

    public void AddTranslation(string translation)
    {
        if (translation != null && translation.Length > 0)
        {
            _translations.Add(translation);
        }
    }
}
