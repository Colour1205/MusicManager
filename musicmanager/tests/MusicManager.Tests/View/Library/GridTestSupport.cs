using MusicManager.Entity;

namespace MusicManager.Tests.View.Library;

/// <summary>
/// Fake songs for the song grid tests. The Java class also made PNG covers and ran a body on the Swing event thread;
/// those parts belong to the Swing grid tests, which are not ported.
/// </summary>
public static class GridTestSupport
{
    public static string PathOf(int i)
    {
        return "C:\\music\\folder\\song" + i + ".flac";
    }

    public static Music Song(int i)
    {
        return Song(i, null);
    }

    public static Music Song(int i, byte[] cover)
    {
        var artists = new List<string>();
        artists.Add("Artist " + (i % 3));
        if (i % 2 == 0)
        {
            artists.Add("Guest");
        }
        return new Music("Song " + i, artists, "Album " + (i / 4), PathOf(i), cover);
    }

    public static List<Music> Songs(int n)
    {
        var songs = new List<Music>();
        for (int i = 0; i < n; i++)
        {
            songs.Add(Song(i));
        }
        return songs;
    }
}
