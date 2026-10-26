namespace MusicManager.UseCase.StandardizeArtists;

public class StandardizeArtistsAnalyzeInputData
{
    private readonly string _path;
    private readonly string _separator;
    private readonly bool _useAi;
    private readonly string _apiKey; // null = use the environment variable
    private readonly string _model;
    private readonly HashSet<string> _audioPaths;

    public StandardizeArtistsAnalyzeInputData(string path, string separator, bool useAi, string apiKey, string model)
        : this(path, separator, useAi, apiKey, model, null)
    {
    }

    /// <param name="audioPaths">
    /// the songs whose tags may be changed (audio file paths as the data access lists them); null =
    /// every song. The names are always worked out from the WHOLE folder, so a song changed on its
    /// own gets the same name for an artist as every other song. The paths are copied.
    /// </param>
    public StandardizeArtistsAnalyzeInputData(string path, string separator, bool useAi, string apiKey, string model,
            IEnumerable<string> audioPaths)
    {
        _path = path;
        _separator = separator;
        _useAi = useAi;
        _apiKey = apiKey;
        _model = model;
        _audioPaths = audioPaths == null ? null : new HashSet<string>(audioPaths);
    }

    public string Path => _path;

    public string Separator => _separator;

    public bool IsUseAi => _useAi;

    public string ApiKey => _apiKey;

    public string Model => _model;

    /// <summary>the songs whose tags may change, or null for every song</summary>
    public HashSet<string> AudioPaths => _audioPaths;
}
