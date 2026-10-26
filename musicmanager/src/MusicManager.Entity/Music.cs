namespace MusicManager.Entity;

/// <summary>
/// Music class represents a music entity in the application. It can be used to store information about a music track,
/// such as its title, artist, album, and path. It is expected the lyric file are also under the same path but with a
/// lyric extension. (i.e. .lrc, .txt, etc.)
/// </summary>
public class Music
{
    private string _title;
    private List<string> _artists; // Java LinkedList<String>; a List<string> here so callers can index and pass it on
    private string _album;
    private string _path;
    private byte[] _coverArt; // raw embedded artwork image bytes from the file's tag, or null if none

    public Music()
    {
        _title = "";
        _artists = new List<string>();
        _album = "";
        _path = "";
        _coverArt = null;
    }

    public Music(string title, List<string> artists, string album, string path, byte[] coverArt)
    {
        _title = title;
        _artists = artists;
        _album = album;
        _path = path;
        _coverArt = coverArt;
    }

    public string Title
    {
        get => _title;
        set => _title = value;
    }

    public List<string> Artists
    {
        get => _artists;
        set => _artists = value;
    }

    public string Album
    {
        get => _album;
        set => _album = value;
    }

    public string Path
    {
        get => _path;
        set => _path = value;
    }

    public byte[] CoverArt
    {
        get => _coverArt;
        set => _coverArt = value;
    }

    public class Builder
    {
        private string _title;
        private List<string> _artists;
        private string _album;
        private string _path;
        private byte[] _coverArt;

        public Builder()
        {
            _title = "";
            _artists = new List<string>();
            _album = "";
            _path = "";
            _coverArt = null;
        }

        public Builder SetTitle(string title)
        {
            _title = title;
            return this;
        }

        public Builder SetArtists(List<string> artists)
        {
            _artists = artists;
            return this;
        }

        public Builder SetAlbum(string album)
        {
            _album = album;
            return this;
        }

        public Builder SetPath(string path)
        {
            _path = path;
            return this;
        }

        public Builder SetCoverArt(byte[] coverArt)
        {
            _coverArt = coverArt;
            return this;
        }

        public Music Build()
        {
            return new Music(_title, _artists, _album, _path, _coverArt);
        }
    }
}
