using System.Text.RegularExpressions;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeLyrics;
using RealDataAccessImpl = MusicManager.DataAccess.DataAccess;

namespace MusicManager.Tests.UseCase.StandardizeLyrics;

/// <summary>
/// The one real DataAccess, with every member of the use case's data access overridable. The Java tests subclass
/// DataAccess ("class ScriptedData extends DataAccess") and script Claude; the C# DataAccess is not made for
/// inheritance, so the tests wrap an instance of it and forward everything they do not override.
/// </summary>
public class RealDataAccess : IStandardizeLyricsDataAccess
{
    private readonly IStandardizeLyricsDataAccess _real = new RealDataAccessImpl();

    public virtual List<SongLyricFiles> FindSongs(string path) => _real.FindSongs(path);

    public virtual Lyric ReadLyric(string lyricFilePath) => _real.ReadLyric(lyricFilePath);

    public virtual Lyric ParseLrcText(string lrcText) => _real.ParseLrcText(lrcText);

    public virtual Music ReadMusicTags(string audioPath) => _real.ReadMusicTags(audioPath);

    public virtual void WriteLyricFile(string path, string content) => _real.WriteLyricFile(path, content);

    public virtual bool IsStandardWordSyncedFile(string lrcPath) => _real.IsStandardWordSyncedFile(lrcPath);

    public virtual bool DeleteFile(string path, bool moveToTrash) => _real.DeleteFile(path, moveToTrash);

    public virtual string ReadApiKeyFromEnvironment() => _real.ReadApiKeyFromEnvironment();

    public virtual RomajiEstimate EstimateRomaji(List<RomajiLine> lines) => _real.EstimateRomaji(lines);

    public virtual RomajiResult Romanize(List<RomajiLine> lines, string apiKey, string model,
        RomajiProgressListener progress) => _real.Romanize(lines, apiKey, model, progress);
}

/// <summary>what the scripted Claude of the tests does to a word: Java's replaceAll of the Hangul, kana and Han characters by "x"</summary>
internal static class ScriptedRomaji
{
    // \p{IsHangul}, \p{IsHiragana}, \p{IsKatakana} and \p{IsHan} (Character.UnicodeScript) for the characters of the basic plane
    private static readonly Regex Scripts = new Regex(
        "[ᄀ-ᇿ〮〯ㄱ-ㆎ㈀-㈞㉠-㉾가-힣ힰ-ퟆퟋ-ퟻ"
        + "ﾠ-ﾾￂ-ￇￊ-ￏￒ-ￗￚ-ￜ"
        + "ぁ-ゖゝ-ゟ"
        + "ァ-ヺヽ-ヿㇰ-ㇿ㋐-㋾㌀-㍗ｦ-ｯｱ-ﾝ"
        + "⺀-⺙⺛-⻳⼀-⿕々〇〡-〩〸-〻㐀-䶿一-鿿"
        + "豈-舘並-龎]");

    public static string ReplaceWithX(string text) => Scripts.Replace(text, "x");
}
