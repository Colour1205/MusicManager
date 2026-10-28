namespace MusicManager.UseCase.AddSongs;

/// <summary>
/// what became of the files and folders the user asked to add to the library: the songs to add are staged, not in the
/// library, and the paths here are the ones they will have in it
/// </summary>
public class AddSongsOutputData
{
    /// <summary>a song that was not added, and why</summary>
    public sealed record Problem(string Path, string Detail);

    private readonly List<string> _libraryPaths;
    private readonly int _copiedCount;
    private readonly int _alreadyThereCount;
    private readonly int _foundCount;
    private readonly int _otherFormatsCount;
    private readonly List<Problem> _alreadyInLibrary;
    private readonly List<string> _lyricsAddedTo;
    private readonly List<string> _newPaths;
    private readonly List<Problem> _problems;

    /// <param name="libraryPaths">the audio files that are in the library, or will be when the add is confirmed, to be standardized: the staged copies (at their future paths), and the picked songs that were in the library already</param>
    /// <param name="copiedCount">how many songs were staged to be copied into the library</param>
    /// <param name="alreadyThereCount">how many were in the library already (not copied again)</param>
    /// <param name="foundCount">how many songs were found in what the user picked (the ones the library has already included)</param>
    public AddSongsOutputData(List<string> libraryPaths, int copiedCount, int alreadyThereCount, int foundCount,
            List<Problem> problems) : this(libraryPaths, copiedCount, alreadyThereCount, foundCount, 0, problems)
    {
    }

    /// <param name="otherFormatsCount">how many files were left out because the same song was picked in a format that was chosen instead</param>
    public AddSongsOutputData(List<string> libraryPaths, int copiedCount, int alreadyThereCount, int foundCount,
            int otherFormatsCount, List<Problem> problems)
        : this(libraryPaths, copiedCount, alreadyThereCount, foundCount, otherFormatsCount, null, problems)
    {
    }

    /// <param name="alreadyInLibrary">the songs that were not copied because the library has them (same title, artist and album): the picked file, and where the library has the song</param>
    public AddSongsOutputData(List<string> libraryPaths, int copiedCount, int alreadyThereCount, int foundCount,
            int otherFormatsCount, List<Problem> alreadyInLibrary, List<Problem> problems)
        : this(libraryPaths, copiedCount, alreadyThereCount, foundCount, otherFormatsCount, alreadyInLibrary, null, problems)
    {
    }

    /// <param name="lyricsAddedTo">the library songs (audio paths) that had no lyrics and are given the lyric files of the copy that was picked</param>
    public AddSongsOutputData(List<string> libraryPaths, int copiedCount, int alreadyThereCount, int foundCount,
            int otherFormatsCount, List<Problem> alreadyInLibrary, List<string> lyricsAddedTo, List<Problem> problems)
        : this(libraryPaths, copiedCount, alreadyThereCount, foundCount, otherFormatsCount, alreadyInLibrary, lyricsAddedTo,
                null, problems)
    {
    }

    /// <param name="newPaths">the staged songs: the paths they will have in the library (not there yet, until the add is confirmed)</param>
    public AddSongsOutputData(List<string> libraryPaths, int copiedCount, int alreadyThereCount, int foundCount,
            int otherFormatsCount, List<Problem> alreadyInLibrary, List<string> lyricsAddedTo, List<string> newPaths,
            List<Problem> problems)
    {
        _newPaths = newPaths ?? new List<string>();
        _lyricsAddedTo = lyricsAddedTo ?? new List<string>();
        _libraryPaths = libraryPaths;
        _copiedCount = copiedCount;
        _alreadyThereCount = alreadyThereCount;
        _foundCount = foundCount;
        _otherFormatsCount = otherFormatsCount;
        _alreadyInLibrary = alreadyInLibrary ?? new List<Problem>();
        _problems = problems;
    }

    public List<string> LibraryPaths => _libraryPaths;

    /// <summary>how many songs were staged: they are copied into the library when the add is confirmed</summary>
    public int CopiedCount => _copiedCount;

    /// <summary>the staged songs, by the path they will have in the library (a part of <see cref="LibraryPaths"/>)</summary>
    public List<string> NewPaths => _newPaths;

    public int AlreadyThereCount => _alreadyThereCount;

    /// <summary>how many songs there were in what the user picked (0 = no audio file at all)</summary>
    public int FoundCount => _foundCount;

    /// <summary>how many files were not added because the song is there in a format that was chosen instead (not counted in <see cref="FoundCount"/>)</summary>
    public int OtherFormatsCount => _otherFormatsCount;

    /// <summary>songs left out because the library has them already; Path is the picked file, Detail says where the library has it</summary>
    public List<Problem> AlreadyInLibrary => _alreadyInLibrary;

    /// <summary>songs the library has without lyrics that are given those of the picked copy when the add is confirmed (they are in <see cref="LibraryPaths"/> too, to be standardized)</summary>
    public List<string> LyricsAddedTo => _lyricsAddedTo;

    public List<Problem> Problems => _problems;
}
