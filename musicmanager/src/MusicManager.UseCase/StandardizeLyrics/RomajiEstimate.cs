namespace MusicManager.UseCase.StandardizeLyrics;

/// <summary>what romanizing a set of lines would cost, worked out without calling the service</summary>
public sealed class RomajiEstimate
{
    private readonly int _uniqueLines;
    private readonly int _cachedLines;
    private readonly int _requests;

    public RomajiEstimate(int uniqueLines, int cachedLines, int requests)
    {
        _uniqueLines = uniqueLines;
        _cachedLines = cachedLines;
        _requests = requests;
    }

    /// <summary>different lines among those asked about (a repeated chorus counts once)</summary>
    public int UniqueLines => _uniqueLines;

    /// <summary>of those, lines an earlier run already romanized</summary>
    public int CachedLines => _cachedLines;

    /// <summary>lines that still need the service</summary>
    public int LinesToAsk => _uniqueLines - _cachedLines;

    /// <summary>requests those lines will take</summary>
    public int Requests => _requests;
}
