using MusicManager.Entity;

namespace MusicManager.UseCase.StandardizeAlbums;

public interface IStandardizeAlbumsDataAccess
{
    /// <summary>reads the album-related tags of every audio file under path (recursively)</summary>
    List<SongAlbumTags> ReadAlbumTags(string path);

    /// <summary>
    /// asks the AI which album each raw value is and what that album is officially called, so that every spelling of one
    /// album gets one name (answers it gave earlier are remembered and not asked for again). Every key of raws must appear
    /// in the result.
    /// </summary>
    /// <param name="raws">raw value -> how many songs use it and where they are</param>
    /// <param name="apiKey">the Anthropic API key; null when none is known (then only remembered answers can be used)</param>
    /// <param name="model">the model id to use</param>
    /// <param name="notes">receives a sentence for anything the user should know</param>
    /// <param name="status">receives a short sentence before each request, to show progress; may be null</param>
    /// <exception cref="StandardizeArtists.ArtistAiException">if the service cannot be used</exception>
    IDictionary<string, ParsedAlbum> ParseAlbumsWithAi(IDictionary<string, AlbumRaw> raws, string apiKey, string model,
            List<string> notes, Action<string> status);

    /// <summary>the key from the ANTHROPIC_API_KEY environment variable (including one set after this program started), or null if it is not set</summary>
    string ReadApiKeyFromEnvironment();

    /// <summary>writes the ALBUM tag to the audio file and verifies it by reading it back; returns true on success</summary>
    bool WriteAlbumTag(SongAlbumUpdate update);

    /// <summary>
    /// Moves the songs into the folder named after their album: renames their folder when they are all of it, otherwise
    /// moves each song with the lyric files that go with it. Nothing is overwritten and a song is moved whole or not at
    /// all.
    /// </summary>
    AlbumFolderMoveResult MoveToAlbumFolder(AlbumFolderMove move);
}
