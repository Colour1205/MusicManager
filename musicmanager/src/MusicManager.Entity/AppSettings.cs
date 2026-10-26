namespace MusicManager.Entity;

/// <summary>
/// Persisted user preferences: the last music folder opened, the choices of the Options window, and (for the
/// deprecated Convert LRC mode) the default target formats.
/// </summary>
public class AppSettings
{
    private string _lastPath;
    private List<string> _defaultTargetFormats;
    private StandardizeOptions _standardizeOptions;

    public AppSettings(string lastPath, List<string> defaultTargetFormats)
        : this(lastPath, defaultTargetFormats, new StandardizeOptions())
    {
    }

    public AppSettings(string lastPath, List<string> defaultTargetFormats, StandardizeOptions standardizeOptions)
    {
        _lastPath = lastPath;
        _defaultTargetFormats = defaultTargetFormats;
        _standardizeOptions = standardizeOptions == null ? new StandardizeOptions() : standardizeOptions;
    }

    public string LastPath
    {
        get => _lastPath;
        set => _lastPath = value;
    }

    public List<string> DefaultTargetFormats
    {
        get => _defaultTargetFormats;
        set => _defaultTargetFormats = value;
    }

    /// <summary>The options of the Options window; the setter turns null into the default options.</summary>
    public StandardizeOptions StandardizeOptions
    {
        get => _standardizeOptions;
        set => _standardizeOptions = value == null ? new StandardizeOptions() : value;
    }

    /// <summary>a copy that can be edited without touching this one</summary>
    public AppSettings Copy()
    {
        return new AppSettings(_lastPath, _defaultTargetFormats == null ? null : new List<string>(_defaultTargetFormats),
                _standardizeOptions.Copy());
    }
}
