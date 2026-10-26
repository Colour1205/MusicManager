using MusicManager.Common;

namespace MusicManager.UseCase.StandardizeLyrics;

public class StandardizeLyricsInputData
{
    public const string DefaultModel = "claude-sonnet-5-5";

    private readonly string _path;
    private readonly bool _dryRun;
    private readonly bool _moveToTrash;
    private readonly bool _romanize;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly HashSet<string> _audioPaths;
    private readonly bool _previewDetail;

    /// <summary>standardizes the lyrics of every song under the folder; no romaji</summary>
    public StandardizeLyricsInputData(string path, bool dryRun, bool moveToTrash)
        : this(path, dryRun, moveToTrash, false, null, DefaultModel)
    {
    }

    /// <summary>every song under the folder</summary>
    public StandardizeLyricsInputData(string path, bool dryRun, bool moveToTrash, bool romanize,
        string apiKey, string model)
        : this(path, dryRun, moveToTrash, romanize, apiKey, model, null)
    {
    }

    /// <param name="path">library folder to scan recursively</param>
    /// <param name="dryRun">only work out what would happen; write, delete and ask Claude nothing</param>
    /// <param name="moveToTrash">send deprecated files to the recycle bin instead of deleting them permanently</param>
    /// <param name="romanize">also give every Korean and Japanese line a Latin-letter romaji line, made by Claude</param>
    /// <param name="apiKey">the key to use for romaji; blank means the ANTHROPIC_API_KEY environment variable</param>
    /// <param name="model">the Claude model that romanizes</param>
    /// <param name="audioPaths">only these songs (audio file paths, as the data access lists them); null = every song</param>
    public StandardizeLyricsInputData(string path, bool dryRun, bool moveToTrash, bool romanize,
        string apiKey, string model, IEnumerable<string> audioPaths)
        : this(path, dryRun, moveToTrash, romanize, apiKey, model, audioPaths, false)
    {
    }

    /// <param name="path">library folder to scan recursively</param>
    /// <param name="dryRun">only work out what would happen; write, delete and ask Claude nothing</param>
    /// <param name="moveToTrash">send deprecated files to the recycle bin instead of deleting them permanently</param>
    /// <param name="romanize">also give every Korean and Japanese line a Latin-letter romaji line, made by Claude</param>
    /// <param name="apiKey">the key to use for romaji; blank means the ANTHROPIC_API_KEY environment variable</param>
    /// <param name="model">the Claude model that romanizes</param>
    /// <param name="audioPaths">only these songs (audio file paths, as the data access lists them); null = every song</param>
    /// <param name="previewDetail">
    /// only with dryRun, for the preview of one song's window: nothing is written or deleted, but each result lists the
    /// lines whose romaji changes (<see cref="SongLyricsResult.LineChanges"/>) and, when romanize is on, the romaji for
    /// those lines is made now (the saved answers, and Claude for what is not saved yet) so that the preview can show it.
    /// A missing key or a failed request is no error here: the lines just come without their romaji text.
    /// </param>
    public StandardizeLyricsInputData(string path, bool dryRun, bool moveToTrash, bool romanize,
        string apiKey, string model, IEnumerable<string> audioPaths, bool previewDetail)
    {
        _previewDetail = previewDetail && dryRun;
        _path = path;
        _dryRun = dryRun;
        _moveToTrash = moveToTrash;
        _romanize = romanize;
        _apiKey = apiKey;
        _model = model == null || JavaText.IsBlank(model) ? DefaultModel : JavaString.Trim(model);
        _audioPaths = audioPaths == null ? null : new HashSet<string>(audioPaths);
    }

    public string Path => _path;

    public bool IsDryRun => _dryRun;

    public bool IsMoveToTrash => _moveToTrash;

    public bool IsRomanize => _romanize;

    public string ApiKey => _apiKey;

    public string Model => _model;

    /// <summary>a dry run that also lists the changed lines and makes their romaji; see the constructor</summary>
    public bool IsPreviewDetail => _previewDetail;

    /// <summary>the songs to handle, or null for every song under the folder</summary>
    public HashSet<string> AudioPaths => _audioPaths;
}
