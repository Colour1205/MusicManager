namespace MusicManager.UseCase.StandardizeAlbums;

/// <summary>
/// songs that are kept in a folder with another name than their album's: the folder they are in and the name of the folder
/// they belong in (a sibling of the first). When every song of the folder goes to the same place the folder is simply
/// renamed (with its covers and everything else in it), otherwise the songs are moved one by one, into the folder if it is
/// there already.
/// </summary>
public class AlbumFolderMove
{
    private readonly string _sourceDirectory;
    private readonly string _targetName;
    private readonly List<string> _audioPaths;
    private readonly bool _wholeFolder;

    public AlbumFolderMove(string sourceDirectory, string targetName, List<string> audioPaths, bool wholeFolder)
    {
        _sourceDirectory = sourceDirectory;
        _targetName = targetName;
        _audioPaths = audioPaths;
        _wholeFolder = wholeFolder;
    }

    /// <summary>the folder the songs are in now</summary>
    public string SourceDirectory => _sourceDirectory;

    /// <summary>the name of the folder they belong in (see <see cref="AlbumFolderName"/>)</summary>
    public string TargetName => _targetName;

    /// <summary>the songs that move (audio paths as they are now)</summary>
    public List<string> AudioPaths => _audioPaths;

    /// <summary>true if the songs are all the songs of their folder, so the folder itself can be renamed</summary>
    public bool IsWholeFolder => _wholeFolder;
}
