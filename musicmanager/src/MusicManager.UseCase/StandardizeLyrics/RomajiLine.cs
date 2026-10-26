using MusicManager.Entity;

namespace MusicManager.UseCase.StandardizeLyrics;

/// <summary>one lyric line to romanize: its words in sung order. The key identifies the line, so a chorus is asked about once.</summary>
public sealed class RomajiLine
{
    private const char Separator = (char)0x241F; // SYMBOL FOR UNIT SEPARATOR, never occurs in lyrics (a number, so it cannot get lost from the source)

    private readonly string _key;
    private readonly IReadOnlyList<string> _words;

    public RomajiLine(IEnumerable<string> words)
    {
        List<string> copy = new List<string>(words);
        if (copy.Contains(null))
        {
            throw new ArgumentNullException(nameof(words), "a word is null"); // List.copyOf does not take null elements
        }
        _words = copy.AsReadOnly();
        _key = string.Join(Separator.ToString(), copy);
    }

    /// <summary>the line a lyric line's words make up</summary>
    public static RomajiLine Of(WordTimeStamp[] stamps)
    {
        List<string> words = new List<string>(stamps.Length);
        foreach (WordTimeStamp stamp in stamps)
        {
            words.Add(stamp.Word);
        }
        return new RomajiLine(words);
    }

    public string Key => _key;

    public IReadOnlyList<string> Words => _words;
}
