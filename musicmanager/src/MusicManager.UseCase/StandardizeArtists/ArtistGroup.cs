namespace MusicManager.UseCase.StandardizeArtists;

/// <summary>every spelling of one artist, and the single standardized name chosen for them</summary>
public class ArtistGroup
{
    private readonly string _key; // stable identity key; what ArtistPlan.GroupKeysByRaw refers to
    private string _canonicalName; // editable by the user before applying
    private readonly bool _suggestedByAi; // true when no English-only spelling existed in the library, so the AI's name was used
    private readonly bool _lowConfidence;
    private readonly List<ArtistVariant> _variants; // sorted by count, most common first
    private readonly int _songCount; // number of songs whose tags mention this artist

    public ArtistGroup(string key, string canonicalName, bool suggestedByAi, bool lowConfidence,
            List<ArtistVariant> variants, int songCount)
    {
        _key = key;
        _canonicalName = canonicalName;
        _suggestedByAi = suggestedByAi;
        _lowConfidence = lowConfidence;
        _variants = variants;
        _songCount = songCount;
    }

    public string Key => _key;

    public string CanonicalName
    {
        get => _canonicalName;
        set => _canonicalName = value;
    }

    public bool IsSuggestedByAi => _suggestedByAi;

    public bool IsLowConfidence => _lowConfidence;

    public List<ArtistVariant> Variants => _variants;

    public int SongCount => _songCount;
}
