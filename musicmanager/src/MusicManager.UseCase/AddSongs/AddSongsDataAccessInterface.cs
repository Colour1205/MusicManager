namespace MusicManager.UseCase.AddSongs;

public interface IAddSongsDataAccess
{
    /// <summary>
    /// Stages the songs of the picked files and folders for the library: every audio file is copied into a folder of its own
    /// that is laid out as the library will be (a folder named after the folder it came from, with the lyric files that share
    /// the song's name and the cover next to it). The library is not changed; until the add is committed or discarded the
    /// other reading methods of the data access show the staged songs at the path they will have in the library. Nothing is
    /// overwritten, the originals stay where they are, and a song the library has already is not staged. An add that was
    /// staged before and never decided is thrown away.
    /// </summary>
    /// <param name="libraryPath">the folder the library is kept in</param>
    /// <param name="sources">the files and folders the user picked</param>
    /// <param name="preferredFormat">
    /// when a folder has the same song in several formats only one is added: this format, or when there is none in it the
    /// largest file (see <see cref="PreferredFormat"/>)
    /// </param>
    /// <param name="progress">told (songs done, songs to do) after each song; may be null</param>
    AddSongsOutputData AddSongs(string libraryPath, IReadOnlyList<string> sources, string preferredFormat, Action<int, int> progress);

    /// <summary>
    /// Moves the staged songs into the library, each to the path it was shown at, all or none (a failure undoes what was
    /// moved). Nothing staged is a success with no songs.
    /// </summary>
    AddCommitOutputData CommitAdded();

    /// <summary>throws the staged songs away; the library was never touched</summary>
    void DiscardAdded();
}
