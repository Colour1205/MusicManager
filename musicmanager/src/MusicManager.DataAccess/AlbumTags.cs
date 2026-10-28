using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeAlbums;

namespace MusicManager.DataAccess;

/// <summary>
/// reads and writes the ALBUM tag of audio files (with the artist tags that say whose albums they are).
/// DataAccess delegates its IStandardizeAlbumsDataAccess methods here.
/// </summary>
public static class AlbumTags
{
    /// <summary>the album-related tags of every audio file under root, in path order; files without a readable tag are skipped</summary>
    public static List<SongAlbumTags> Read(string root)
    {
        return Read(root, null);
    }

    /// <param name="progress">told (files read, files to read) after each file; may be null</param>
    public static List<SongAlbumTags> Read(string root, Action<int, int> progress)
    {
        List<SongAlbumTags> songs = new List<SongAlbumTags>();
        List<string> files = ArtistTags.AudioFiles(root);
        int done = 0;
        foreach (string file in files)
        {
            progress?.Invoke(done++, files.Count);
            try
            {
                using AudioTags tag = AudioTags.Open(file);
                if (!tag.HasTag)
                {
                    continue;
                }
                songs.Add(new SongAlbumTags(file, ArtistTags.Values(tag, AudioTags.Field.Album).FirstOrDefault(),
                        ArtistTags.Values(tag, AudioTags.Field.Artist), ArtistTags.Values(tag, AudioTags.Field.AlbumArtist),
                        ArtistTags.Values(tag, AudioTags.Field.Title).FirstOrDefault()));
            }
            catch (Exception)
            {
                // unreadable file: leave it out of the standardization instead of aborting the whole scan
            }
        }
        return songs;
    }

    /// <summary>the ALBUM tag of a file, or null when it has none or cannot be read</summary>
    public static string ReadAlbum(string file)
    {
        try
        {
            using AudioTags tag = AudioTags.Open(file);
            return tag.HasTag ? ArtistTags.Values(tag, AudioTags.Field.Album).FirstOrDefault() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>writes the album and verifies it by reading the file back; false if anything failed</summary>
    public static bool Write(SongAlbumUpdate update)
    {
        string file = update.AudioPath;
        try
        {
            using (AudioTags tag = AudioTags.Open(file))
            {
                tag.CreateTagIfMissing();
                ArtistTags.Replace(tag, AudioTags.Field.Album, new List<string> { update.Album });
                tag.Commit();
            }
            return ReadAlbum(file) == update.Album;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
