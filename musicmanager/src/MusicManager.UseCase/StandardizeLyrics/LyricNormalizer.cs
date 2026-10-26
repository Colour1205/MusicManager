using MusicManager.Entity;

namespace MusicManager.UseCase.StandardizeLyrics;

/// <summary>
/// brings a lyric read from any source format (.ttml, .hmrc, .lrc) into the one shape the standard
/// file can represent: a word carries the whitespace that follows it, and there are no words that are
/// only whitespace. Apple-style TTML often times the space between two words as a word of its own
/// ("ALL", " ", "IN"); the standard file writes that as "ALL " followed by a gap instead.
/// </summary>
public static class LyricNormalizer
{
    /// <summary>normalizes every word-synced line of the lyric in place and returns it</summary>
    public static Lyric Normalize(Lyric lyric)
    {
        if (lyric == null || lyric.Lines == null)
        {
            return lyric;
        }
        foreach (LyricLine line in lyric.Lines)
        {
            if (line != null && line.WordTimeStamps != null)
            {
                line.WordTimeStamps = Normalize(line.WordTimeStamps);
            }
        }
        return lyric;
    }

    /// <summary>the words without the empty ones, a spacer word's text moved onto the word before it (public: package-private in the Java)</summary>
    public static WordTimeStamp[] Normalize(WordTimeStamp[] words)
    {
        List<WordTimeStamp> result = new List<WordTimeStamp>();
        foreach (WordTimeStamp word in words)
        {
            if (word == null || word.Word == null || word.Word.Length == 0)
            {
                continue;
            }
            if (!JavaText.IsBlank(word.Word))
            {
                result.Add(word);
            }
            else if (result.Count > 0)
            {
                // a spacer: its text moves onto the word before it, whose own timing is kept
                WordTimeStamp previous = result[result.Count - 1];
                if (!JavaText.IsWhitespace(previous.Word[previous.Word.Length - 1]))
                {
                    result[result.Count - 1] = new WordTimeStamp(previous.Word + " ",
                            previous.StartTimeMs, previous.EndTimeMs);
                }
            }
            // whitespace before the first word of a line carries no information and is dropped
        }
        return result.ToArray();
    }
}
