namespace MusicManager.UseCase.StandardizeLyrics;

/// <summary>what standardizing did (or, in a dry run, would do) for one song</summary>
public enum LyricsAction
{
    /// <summary>built the word-synced .lrc from the song's .ttml</summary>
    ConvertedFromTtml,

    /// <summary>built the word-synced .lrc from the song's .hmrc (no usable .ttml)</summary>
    ConvertedFromHmrc,

    /// <summary>the .lrc was word-synced but in a non-standard syntax; rewritten in the standard one</summary>
    NormalizedLrc,

    /// <summary>the .lrc is already the standard word-synced format; left untouched</summary>
    AlreadyStandard,

    /// <summary>the .lrc is already standard but some Korean/Japanese lines had no romaji, or Chinese-character romaji</summary>
    RomajiAdded,

    /// <summary>no word-synced source exists anywhere; the existing line-synced .lrc is kept</summary>
    KeptLineSynced,

    /// <summary>no .lrc, and only line-synced deprecated files; built a line-synced .lrc from them</summary>
    LineSyncedFromDeprecated,

    /// <summary>the song has no lyric files at all</summary>
    NoLyrics,

    /// <summary>lyric files exist but none could be used; nothing was changed or deleted</summary>
    Failed
}

/// <summary>
/// The description each <see cref="LyricsAction"/> carries in Java (<c>getDescription()</c>); a C# enum cannot hold a
/// field, so it is an extension method. <see cref="GetDescription"/> and <see cref="Description"/> are the same text.
/// </summary>
public static class LyricsActionExtensions
{
    /// <summary>the sentence that tells what the action did, e.g. "Built word-synced .lrc from .ttml"</summary>
    public static string GetDescription(this LyricsAction action)
    {
        return action switch
        {
            LyricsAction.ConvertedFromTtml => "Built word-synced .lrc from .ttml",
            LyricsAction.ConvertedFromHmrc => "Built word-synced .lrc from .hmrc",
            LyricsAction.NormalizedLrc => "Rewrote word-synced .lrc in the standard syntax",
            LyricsAction.AlreadyStandard => "Already standard",
            LyricsAction.RomajiAdded => "Added romaji to the existing .lrc",
            LyricsAction.KeptLineSynced => "No word-synced source, kept line-synced .lrc",
            LyricsAction.LineSyncedFromDeprecated => "Built line-synced .lrc from deprecated file",
            LyricsAction.NoLyrics => "No lyrics",
            LyricsAction.Failed => "Failed",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
    }

    /// <summary>same as <see cref="GetDescription"/></summary>
    public static string Description(this LyricsAction action) => GetDescription(action);

    /// <summary>
    /// the sentence for a preview, where nothing has happened yet: the same facts as <see cref="GetDescription"/>, in the
    /// future ("Will build a word-synced .lrc from the .ttml")
    /// </summary>
    public static string GetPreviewDescription(this LyricsAction action)
    {
        return action switch
        {
            LyricsAction.ConvertedFromTtml => "Will build a word-synced .lrc from the .ttml",
            LyricsAction.ConvertedFromHmrc => "Will build a word-synced .lrc from the .hmrc",
            LyricsAction.NormalizedLrc => "Will rewrite the .lrc in the standard format",
            LyricsAction.AlreadyStandard => "Already standard",
            LyricsAction.RomajiAdded => "Will update the romaji in the existing .lrc",
            LyricsAction.KeptLineSynced => "No word-synced source, will keep the line-synced .lrc",
            LyricsAction.LineSyncedFromDeprecated => "Will build a line-synced .lrc from the old file",
            LyricsAction.NoLyrics => "No lyrics",
            LyricsAction.Failed => "Failed",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
    }
}
