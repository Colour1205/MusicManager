using MusicManager.Entity;

namespace MusicManager.UseCase.ConvertLRC;

public class ConvertLRCInputData
{
    public static readonly string[] AvailableFormats = { "TTML", "HMRC", "LRC" };

    // currently only supports "TTML" and "HMRC" and "LRC"
    public ConvertLRCInputData(List<Music> musics, List<string> targetFormats)
    {
        Musics = musics;
        TargetFormats = targetFormats;
    }

    public List<Music> Musics { get; }

    public List<string> TargetFormats { get; }

    /// <summary>
    /// the single source of truth for "no formats saved/selected yet" fallback behavior, so every screen that needs a
    /// default resolves it the same way
    /// </summary>
    /// <returns>formats itself if non-empty, otherwise every available format</returns>
    public static List<string> OrDefaultFormats(List<string> formats)
    {
        if (formats == null || formats.Count == 0)
        {
            return new List<string>(AvailableFormats);
        }
        return formats;
    }
}
