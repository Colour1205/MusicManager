namespace MusicManager.UseCase.StandardizeLyrics;

/// <summary>
/// what a romanization run produced. Lines that could not be romanized are simply absent from the map, so a failure
/// halfway through still hands back everything that was done (it is also saved, so a rerun continues from there).
/// </summary>
public sealed class RomajiResult
{
    private readonly IReadOnlyDictionary<string, List<string>> _romajiByKey; // line key -> one romanized word per input word
    private readonly int _cachedLines;
    private readonly int _requests;
    private readonly string _stoppedBecause; // null when every line was handled

    public RomajiResult(IReadOnlyDictionary<string, List<string>> romajiByKey, int cachedLines, int requests, string stoppedBecause)
    {
        _romajiByKey = romajiByKey;
        _cachedLines = cachedLines;
        _requests = requests;
        _stoppedBecause = stoppedBecause;
    }

    /// <summary>the romanized words of the line with this key, or null if it was not romanized</summary>
    public List<string> Get(string key)
    {
        if (key == null)
        {
            return null;
        }
        return _romajiByKey.TryGetValue(key, out List<string> words) ? words : null;
    }

    /// <summary>lines that did not need a request because an earlier run had already romanized them</summary>
    public int CachedLines => _cachedLines;

    public int Requests => _requests;

    /// <summary>why the run stopped early (rate limit, rejected key, ...), or null</summary>
    public string StoppedBecause => _stoppedBecause;
}
