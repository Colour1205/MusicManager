namespace MusicManager.UseCase.StandardizeLyrics;

/// <summary>thrown when a lyric file cannot be turned into a usable Lyric at all (after every repair attempt)</summary>
public class LyricParseException : Exception
{
    public LyricParseException(string message) : base(message)
    {
    }

    public LyricParseException(string message, Exception cause) : base(message, cause)
    {
    }
}
