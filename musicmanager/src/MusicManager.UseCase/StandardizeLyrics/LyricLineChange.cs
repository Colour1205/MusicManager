namespace MusicManager.UseCase.StandardizeLyrics;

/// <summary>
/// One lyric line whose romaji a run changes, for the preview of a single song: the Korean/Japanese line, the old romaji
/// above it that is replaced (if there is one) and the Latin-letter romaji that replaces or adds it.
/// </summary>
public sealed class LyricLineChange
{
    public enum Kind
    {
        /// <summary>the line had no romaji: a romaji line is added above it</summary>
        RomajiAdded,

        /// <summary>the line had romaji in Chinese characters (or another non-Latin script): it is replaced with Latin letters</summary>
        RomajiReplaced
    }

    private readonly Kind _kind;
    private readonly long _timeMs;
    private readonly string _original;
    private readonly string _oldRomaji;
    private readonly string _newRomaji;

    /// <param name="kind">added or replaced</param>
    /// <param name="timeMs">the line's start time in the song</param>
    /// <param name="original">the line as it is written in the lyrics (words joined, trimmed)</param>
    /// <param name="oldRomaji">the old non-Latin romaji that is replaced; null for RomajiAdded</param>
    /// <param name="newRomaji">
    /// the Latin romaji (words joined, trimmed); null while it is not known, because no Claude answer (or saved answer)
    /// exists for the line yet
    /// </param>
    public LyricLineChange(Kind kind, long timeMs, string original, string oldRomaji, string newRomaji)
    {
        _kind = kind;
        _timeMs = timeMs;
        _original = original ?? "";
        _oldRomaji = oldRomaji;
        _newRomaji = newRomaji;
    }

    /// <summary>
    /// Java getKind(). A method, not a property named Kind: a C# member cannot share its name with the nested enum
    /// <see cref="Kind"/> (the same reason as <c>Lyric.GetSyncType()</c>).
    /// </summary>
    public Kind GetKind() => _kind;

    public long TimeMs => _timeMs;

    public string Original => _original;

    /// <summary>the old romaji that goes away, or null when the line had none</summary>
    public string OldRomaji => _oldRomaji;

    /// <summary>the Latin romaji the line gets, or null while it is unknown</summary>
    public string NewRomaji => _newRomaji;
}
