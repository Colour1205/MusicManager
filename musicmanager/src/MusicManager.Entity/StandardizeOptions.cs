using System.Text.Json.Serialization;
using MusicManager.Common;

namespace MusicManager.Entity;

/// <summary>
/// What Process does to the songs of the library: the choices of the Options window. They are saved with the other
/// settings, except the API key, which is only ever kept in memory.
/// </summary>
public class StandardizeOptions
{
    /// <summary>Standardize is the normal mode; Convert is the deprecated Convert LRC feature (see AppFeatures)</summary>
    public enum Mode
    {
        Standardize, Convert
    }

    public const string DefaultSeparator = "; ";
    public const string DefaultModel = "claude-sonnet-5-5";

    /// <summary>the format the Add button prefers when a folder has a song in several: the extension, without the dot</summary>
    public const string DefaultPreferredFormat = "flac";

    private Mode _mode = Mode.Standardize;
    private bool _lyrics = true; // one word-synced .lrc per song, deprecated .ttml/.hmrc deleted
    private bool _romaji = true; // Latin-letter romaji for Korean and Japanese lines; replaces old non-Latin romaji
    private bool _recycleBin = false; // send deprecated files to the Recycle Bin instead of deleting them
    private bool _artists = true; // consistent artist names
    private bool _useClaudeForArtists = true;
    private bool _albums = true; // one name for each album: the ALBUM tags, and the songs in a folder named after the album
    private bool _useClaudeForAlbums = true;
    private string _preferredFormat = DefaultPreferredFormat; // for Add: which copy of a song in several formats is added
    private string _separator = DefaultSeparator;
    private string _model = DefaultModel;
    private string _apiKey = ""; // typed into the window; never saved. Blank = the environment variable

    /// <summary>
    /// Java getMode(). A method (with <see cref="SetMode"/>), not a property named Mode: a C# member cannot share its
    /// name with the nested enum <see cref="Mode"/>.
    /// </summary>
    public Mode GetMode() => _mode;

    /// <summary>Java setMode(): null (an unknown mode name read from a file) gives Standardize.</summary>
    public void SetMode(Mode? mode)
    {
        _mode = mode ?? Mode.Standardize;
    }

    public bool IsLyrics
    {
        get => _lyrics;
        set => _lyrics = value;
    }

    public bool IsRomaji
    {
        get => _romaji;
        set => _romaji = value;
    }

    public bool IsRecycleBin
    {
        get => _recycleBin;
        set => _recycleBin = value;
    }

    public bool IsArtists
    {
        get => _artists;
        set => _artists = value;
    }

    public bool IsUseClaudeForArtists
    {
        get => _useClaudeForArtists;
        set => _useClaudeForArtists = value;
    }

    public bool IsAlbums
    {
        get => _albums;
        set => _albums = value;
    }

    public bool IsUseClaudeForAlbums
    {
        get => _useClaudeForAlbums;
        set => _useClaudeForAlbums = value;
    }

    /// <summary>
    /// The format Add prefers when a folder has the same song in several (an extension such as "flac", no dot, lower case);
    /// when it has none in that format the largest file is added. An unset or blank one reads as <see cref="DefaultPreferredFormat"/>.
    /// </summary>
    public string PreferredFormat
    {
        get => _preferredFormat == null || JavaString.IsBlank(_preferredFormat) ? DefaultPreferredFormat
                : JavaString.Trim(_preferredFormat).TrimStart('.').ToLowerInvariant();
        set => _preferredFormat = value;
    }

    /// <summary>The separator; an unset or empty one reads as <see cref="DefaultSeparator"/>.</summary>
    public string Separator
    {
        get => _separator == null || _separator.Length == 0 ? DefaultSeparator : _separator;
        set => _separator = value;
    }

    /// <summary>The Claude model id; an unset or blank one reads as <see cref="DefaultModel"/>, otherwise it is trimmed.</summary>
    public string Model
    {
        get => _model == null || JavaString.IsBlank(_model) ? DefaultModel : JavaString.Trim(_model);
        set => _model = value;
    }

    /// <summary>The API key typed into the window (Java: transient, so it is never saved); null reads as "".</summary>
    [JsonIgnore]
    public string ApiKey
    {
        get => _apiKey ?? "";
        set => _apiKey = value;
    }

    /// <summary>true if the options use Claude for anything (romaji, or artists or albums with their Claude option)</summary>
    public bool UsesClaude()
    {
        return _mode == Mode.Standardize
                && ((_lyrics && _romaji) || (_artists && _useClaudeForArtists) || (_albums && _useClaudeForAlbums));
    }

    public StandardizeOptions Copy()
    {
        StandardizeOptions copy = new StandardizeOptions();
        copy._mode = _mode;
        copy._lyrics = _lyrics;
        copy._romaji = _romaji;
        copy._recycleBin = _recycleBin;
        copy._artists = _artists;
        copy._useClaudeForArtists = _useClaudeForArtists;
        copy._albums = _albums;
        copy._useClaudeForAlbums = _useClaudeForAlbums;
        copy._preferredFormat = _preferredFormat;
        copy._separator = _separator;
        copy._model = _model;
        copy._apiKey = _apiKey;
        return copy;
    }
}
