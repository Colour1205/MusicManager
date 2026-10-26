namespace MusicManager.UseCase.StandardizeArtists;

/// <summary>one tag field of one song changing from before to after (for the preview)</summary>
public class FieldChange
{
    private readonly string _field; // "ARTIST", "ALBUMARTIST" or "ARTISTS"
    private readonly string _before;
    private readonly string _after;

    public FieldChange(string field, string before, string after)
    {
        _field = field;
        _before = before;
        _after = after;
    }

    public string Field => _field;

    public string Before => _before;

    public string After => _after;
}
