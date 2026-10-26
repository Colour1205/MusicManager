namespace MusicManager.App;

/// <summary>
/// Switches for features that are built and kept but not shown. Flip one to true and rebuild to bring the feature back.
/// </summary>
public static class AppFeatures
{
    /// <summary>
    /// The old "Convert LRC" feature: write a song's lyrics out as .ttml, .hmrc or a line-synced .lrc.
    /// It is deprecated, because Standardize makes exactly one word-synced .lrc per song and removes .ttml/.hmrc.
    /// When true, the Options window of the library offers a second mode, "Convert formats", that converts the
    /// selected songs (or all of them) with the unchanged ConvertLRC use case. The old stand-alone windows
    /// (view/ConvertLRC) are kept as they were, marked deprecated, and are not used by the app.
    /// </summary>
    public const bool LegacyConvertLrc = false;
}
