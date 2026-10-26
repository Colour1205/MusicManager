using MusicManager.Entity;

namespace MusicManager.UseCase.StandardizeLyrics;

public interface IStandardizeLyricsDataAccess
{
    /// <summary>
    /// recursively finds every song under path (one entry per audio file) together
    /// with the .lrc/.ttml/.hmrc sidecars that share its base name
    /// </summary>
    List<SongLyricFiles> FindSongs(string path);

    /// <summary>
    /// parses a lyric file, picking the parser from its extension (.lrc, .ttml,
    /// .hmrc). Tolerates and repairs malformed input; any repair is recorded in
    /// the returned Lyric's warnings.
    /// </summary>
    /// <exception cref="LyricParseException">if nothing usable could be recovered</exception>
    Lyric ReadLyric(string lyricFilePath);

    /// <summary>
    /// parses .lrc text held in memory (same parser as ReadLyric); used to verify a rendered file
    /// BEFORE anything on disk is replaced
    /// </summary>
    Lyric ParseLrcText(string lrcText);

    /// <summary>reads title/artist/album from the audio file's tag; never null (falls back to the file name)</summary>
    Music ReadMusicTags(string audioPath);

    /// <summary>writes content to path as UTF-8 (no BOM) atomically, replacing any existing file</summary>
    void WriteLyricFile(string path, string content);

    /// <summary>true if the file's raw text uses the standard word-synced syntax already ([mm:ss.SSS]&lt;mm:ss.SSS&gt;word...)</summary>
    bool IsStandardWordSyncedFile(string lrcPath);

    /// <summary>deletes a file, optionally by moving it to the recycle bin</summary>
    /// <returns>true if the file is gone afterwards</returns>
    bool DeleteFile(string path, bool moveToTrash);

    /// <summary>the key from the ANTHROPIC_API_KEY environment variable (including one set after this program started), or null</summary>
    string ReadApiKeyFromEnvironment();

    /// <summary>what romanizing these lines would take, counting lines an earlier run already did; makes no request</summary>
    RomajiEstimate EstimateRomaji(List<RomajiLine> lines);

    /// <summary>
    /// romanizes lines into Latin letters, one romanized word per input word. Many lines go into each request and every
    /// answer is saved as it arrives, so a stop (rate limit, no network) loses nothing and a rerun continues. Never
    /// throws: a failure ends the run early and is reported in the result.
    /// </summary>
    /// <param name="progress">told after every request; may be null</param>
    RomajiResult Romanize(List<RomajiLine> lines, string apiKey, string model, RomajiProgressListener progress);
}
