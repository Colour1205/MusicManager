using MusicManager.Entity;

namespace MusicManager.UseCase.ConvertLRC;

public interface IConvertLRCDataAccess
{
    /// <summary>
    /// scans path recursively for music files
    /// </summary>
    Queue<Music> GetMusics(string path);

    /// <summary>
    /// finds and parses the lyric file belonging to each Music, detecting whether it is word synced, line synced, or
    /// has no sync at all. musics with no lyric file detected are skipped. the returned map preserves which Music each
    /// Lyric came from, so callers can report progress back to the right song.
    /// </summary>
    IDictionary<Music, Lyric> GetLRCs(Queue<Music> musics);

    void SaveTTML(Lyric lyric, string content);

    void SaveHMRC(Lyric lyric, string content);

    /// <summary>none word synced LRC, WILL REPLACE FILE</summary>
    void SaveLRC(Lyric lyric, string content);
}
