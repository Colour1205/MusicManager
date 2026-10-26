namespace MusicManager.UseCase.StandardizeLyrics;

/// <summary>
/// one song on disk: its audio file plus whichever lyric sidecar files sit next
/// to it with the same base name. A missing sidecar is null.
/// </summary>
public class SongLyricFiles
{
    private readonly string _audioPath;
    private readonly string _lrcPath;
    private readonly string _ttmlPath;
    private readonly string _hmrcPath;

    public SongLyricFiles(string audioPath, string lrcPath, string ttmlPath, string hmrcPath)
    {
        _audioPath = audioPath;
        _lrcPath = lrcPath;
        _ttmlPath = ttmlPath;
        _hmrcPath = hmrcPath;
    }

    public string AudioPath => _audioPath;

    /// <summary>the existing standalone .lrc, or null</summary>
    public string LrcPath => _lrcPath;

    /// <summary>the existing deprecated .ttml, or null</summary>
    public string TtmlPath => _ttmlPath;

    /// <summary>the existing deprecated .hmrc, or null</summary>
    public string HmrcPath => _hmrcPath;

    /// <summary>the path the standard .lrc lives at: the existing one, or audio base name + ".lrc"</summary>
    public string TargetLrcPath
    {
        get
        {
            if (_lrcPath != null)
            {
                return _lrcPath;
            }
            int dot = _audioPath.LastIndexOf('.');
            int slash = Math.Max(_audioPath.LastIndexOf('/'), _audioPath.LastIndexOf('\\'));
            string baseName = dot > slash ? _audioPath.Substring(0, dot) : _audioPath;
            return baseName + ".lrc";
        }
    }
}
