namespace MusicManager.UseCase.StandardizeArtists;

/// <summary>what became of renaming one song's files to "Artist - Title"</summary>
public class SongRenameResult
{
    public enum Kind
    {
        /// <summary>the audio file and its lyric files have the new name now</summary>
        Renamed,

        /// <summary>the name already was "Artist - Title" for the tags the file has</summary>
        AlreadyNamed,

        /// <summary>the file has no artist or no title to make a name of</summary>
        NoTags,

        /// <summary>a file with the new name is there already: nothing was renamed</summary>
        Exists,

        /// <summary>the new path would be longer than Windows allows: nothing was renamed</summary>
        TooLong,

        /// <summary>something went wrong (the file is in use, the folder is read-only ...): whatever was renamed has been put back</summary>
        Failed
    }

    private readonly Kind _status;
    private readonly string _audioPath; // the audio file before
    private readonly string _newAudioPath; // after (the same when nothing was renamed)
    private readonly string _detail;

    public SongRenameResult(Kind status, string audioPath, string newAudioPath, string detail)
    {
        _status = status;
        _audioPath = audioPath;
        _newAudioPath = newAudioPath;
        _detail = detail;
    }

    public Kind Status => _status;

    public string AudioPath => _audioPath;

    public string NewAudioPath => _newAudioPath;

    /// <summary>why, in a few words, for a song that was not renamed; empty otherwise</summary>
    public string Detail => _detail ?? "";
}
