using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeArtists;

namespace MusicManager.DataAccess;

/// <summary>
/// reads and writes the artist-related tag fields (ARTIST, ALBUMARTIST, ARTISTS) of audio files.
/// DataAccess delegates its IStandardizeArtistsDataAccess methods here.
/// </summary>
public static class ArtistTags
{
    /// <summary>the artist-related tags of every audio file under root, in path order; files without a readable tag are skipped</summary>
    public static List<SongArtistTags> Read(string root)
    {
        // java.io.File spells a path with the platform separator and without a trailing one
        root = Path.TrimEndingDirectorySeparator(root.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
        List<string> audioFiles = new List<string>();
        foreach (string file in Walk(root))
        {
            if (DataAccess.IsAudioFileName(Path.GetFileName(file)))
            {
                audioFiles.Add(file);
            }
        }
        // a stable sort, as Java's: files that are equal ignoring case stay in the order they were found
        audioFiles = audioFiles.OrderBy(f => f.ToLowerInvariant(), StringComparer.Ordinal).ToList();

        List<SongArtistTags> songs = new List<SongArtistTags>();
        foreach (string file in audioFiles)
        {
            try
            {
                using AudioTags tag = AudioTags.Open(file);
                if (!tag.HasTag)
                {
                    continue;
                }
                songs.Add(new SongArtistTags(file, Values(tag, AudioTags.Field.Artist),
                        Values(tag, AudioTags.Field.AlbumArtist), Values(tag, AudioTags.Field.Artists),
                        Values(tag, AudioTags.Field.Title).FirstOrDefault()));
            }
            catch (Exception)
            {
                // unreadable file: leave it out of the standardization instead of aborting the whole scan
            }
        }
        return songs;
    }

    /// <summary>
    /// the artists and the title a file is named after: the ARTIST values (the ALBUMARTIST ones when there are none) and the
    /// TITLE; the artists are empty and the title null when the file has no such tag
    /// </summary>
    public static (List<string> Artists, string Title) ReadNameParts(string file)
    {
        using AudioTags tag = AudioTags.Open(file);
        if (!tag.HasTag)
        {
            return (new List<string>(), null);
        }
        List<string> artists = Values(tag, AudioTags.Field.Artist);
        if (artists.Count == 0)
        {
            artists = Values(tag, AudioTags.Field.AlbumArtist);
        }
        return (artists, Values(tag, AudioTags.Field.Title).FirstOrDefault());
    }

    /// <summary>every regular file under root (Files.walk): just root itself if that is a file</summary>
    private static IEnumerable<string> Walk(string root)
    {
        if (File.Exists(root))
        {
            return new[] { root };
        }
        return Directory.EnumerateFiles(root, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = 0, // hidden and system files too
            IgnoreInaccessible = false
        });
    }

    /// <summary>all non-blank values of a field, or an empty list if the tag format does not support it</summary>
    private static List<string> Values(AudioTags tag, AudioTags.Field key)
    {
        List<string> result = new List<string>();
        try
        {
            foreach (string value in tag.GetAll(key))
            {
                if (value != null && JavaString.Trim(value).Length != 0)
                {
                    result.Add(value);
                }
            }
        }
        catch (Exception)
        {
            // field not supported by this tag format
        }
        return result;
    }

    /// <summary>writes the update and verifies it by reading the file back; false if anything failed (the file may be partly updated only if the OS failed mid-write)</summary>
    public static bool Write(SongArtistUpdate update)
    {
        string file = update.AudioPath;
        try
        {
            using (AudioTags tag = AudioTags.Open(file))
            {
                tag.CreateTagIfMissing();

                if (update.ArtistJoined != null)
                {
                    Replace(tag, AudioTags.Field.Artist, new List<string> { update.ArtistJoined });
                }
                if (update.AlbumArtistJoined != null)
                {
                    Replace(tag, AudioTags.Field.AlbumArtist, new List<string> { update.AlbumArtistJoined });
                }
                if (update.ArtistsList != null)
                {
                    Replace(tag, AudioTags.Field.Artists, update.ArtistsList);
                }
                tag.Commit();
            }

            return Verify(file, update);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>removes every existing value of the field, then adds the given ones</summary>
    private static void Replace(AudioTags tag, AudioTags.Field key, List<string> newValues)
    {
        try
        {
            tag.DeleteField(key);
        }
        catch (Exception)
        {
            // nothing to delete / field unsupported: adding below decides whether it works
        }
        foreach (string value in newValues)
        {
            tag.AddField(key, value);
        }
    }

    private static bool Verify(string file, SongArtistUpdate update)
    {
        using AudioTags tag = AudioTags.Open(file);
        if (!tag.HasTag)
        {
            return false;
        }
        if (update.ArtistJoined != null
                && !Values(tag, AudioTags.Field.Artist).SequenceEqual(new[] { update.ArtistJoined }))
        {
            return false;
        }
        if (update.AlbumArtistJoined != null
                && !Values(tag, AudioTags.Field.AlbumArtist).SequenceEqual(new[] { update.AlbumArtistJoined }))
        {
            return false;
        }
        return update.ArtistsList == null || Values(tag, AudioTags.Field.Artists).SequenceEqual(update.ArtistsList);
    }
}
