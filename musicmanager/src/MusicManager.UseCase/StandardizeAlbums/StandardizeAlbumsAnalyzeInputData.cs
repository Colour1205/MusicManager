namespace MusicManager.UseCase.StandardizeAlbums;

public class StandardizeAlbumsAnalyzeInputData
{
    private readonly string _path;
    private readonly bool _useAi;
    private readonly string _apiKey; // null or blank = use the environment variable
    private readonly string _model;
    private readonly HashSet<string> _audioPaths;

    /// <param name="audioPaths">
    /// the songs whose tags and folders may change (audio file paths as the data access lists them); null = every song.
    /// The names are always worked out from the WHOLE folder. The paths are copied.
    /// </param>
    public StandardizeAlbumsAnalyzeInputData(string path, bool useAi, string apiKey, string model, IEnumerable<string> audioPaths)
    {
        _path = path;
        _useAi = useAi;
        _apiKey = apiKey;
        _model = model;
        _audioPaths = audioPaths == null ? null : new HashSet<string>(audioPaths);
    }

    public string Path => _path;

    public bool IsUseAi => _useAi;

    public string ApiKey => _apiKey;

    public string Model => _model;

    /// <summary>the songs that may change, or null for every song</summary>
    public HashSet<string> AudioPaths => _audioPaths;
}
