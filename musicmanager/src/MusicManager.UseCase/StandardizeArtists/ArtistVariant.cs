namespace MusicManager.UseCase.StandardizeArtists;

/// <summary>one spelling of an artist found in the library, and how many tag values use it</summary>
public class ArtistVariant
{
    private readonly string _text;
    private readonly int _count;

    public ArtistVariant(string text, int count)
    {
        _text = text;
        _count = count;
    }

    public string Text => _text;

    public int Count => _count;
}
