namespace MusicManager.UseCase.StandardizeArtists;

/// <summary>
/// one artist found inside a raw tag string: the exact text it was written as, and the standard name
/// of the artist it refers to. The standard name is the artist's official one: Latin-script for Korean,
/// Japanese and Western artists, and the Chinese credit for Chinese-language artists (G.E.M. 邓紫棋),
/// which stays Chinese ("native").
/// </summary>
public class ParsedArtist
{
    private readonly string _text;
    private readonly string _english;
    private readonly bool _lowConfidence;
    private readonly bool _keepNative;
    private readonly bool _researched;

    /// <param name="text">the substring of the raw tag value naming this artist, as written</param>
    /// <param name="english">the artist's standard name; identical for every spelling of one artist</param>
    /// <param name="lowConfidence">true if the source was unsure (guessed romanization, possible typo merge, ...)</param>
    public ParsedArtist(string text, string english, bool lowConfidence) : this(text, english, lowConfidence, false)
    {
    }

    /// <param name="keepNative">true if the artist's official name is in Chinese, so the Latin-script rule does not apply</param>
    public ParsedArtist(string text, string english, bool lowConfidence, bool keepNative)
        : this(text, english, lowConfidence, keepNative, false)
    {
    }

    /// <param name="researched">
    /// true if the name was searched for on the web after the first check left it unsure: a name that is still unsure
    /// stays marked "please check" and is not searched for again (and paid for again) every time
    /// </param>
    public ParsedArtist(string text, string english, bool lowConfidence, bool keepNative, bool researched)
    {
        _text = text;
        _english = english;
        _lowConfidence = lowConfidence;
        _keepNative = keepNative;
        _researched = researched;
    }

    public string Text => _text;

    /// <summary>the artist's standard name (Latin-script, unless <see cref="IsKeepNative"/>)</summary>
    public string English => _english;

    public bool IsLowConfidence => _lowConfidence;

    /// <summary>true if the artist's official name is Chinese and must stay Chinese</summary>
    public bool IsKeepNative => _keepNative;

    /// <summary>true if the name has been searched for on the web (whatever it found)</summary>
    public bool IsResearched => _researched;
}
