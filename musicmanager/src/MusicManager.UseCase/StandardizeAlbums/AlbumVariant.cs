namespace MusicManager.UseCase.StandardizeAlbums;

/// <summary>one spelling of an album found in the library, and how many songs use it</summary>
public class AlbumVariant
{
    private readonly string _text;
    private readonly int _count;

    public AlbumVariant(string text, int count)
    {
        _text = text;
        _count = count;
    }

    public string Text => _text;

    public int Count => _count;
}
