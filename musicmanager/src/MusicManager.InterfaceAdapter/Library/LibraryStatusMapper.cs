using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeLyrics;
using Category = MusicManager.InterfaceAdapter.Library.SongStatus.Category;
using Tone = MusicManager.InterfaceAdapter.Library.SongStatus.Tone;

namespace MusicManager.InterfaceAdapter.Library;

/// <summary>
/// turns the preview (dry run) of one song's lyrics into what the library shows: a headline that fits one card line,
/// a category for the filter chips and the lines of the song window. Plain ASCII only (a hyphen between the parts,
/// never a middle dot) so every font draws it the same.
/// <para>
/// The romaji numbers here keep the three kinds of line apart: lines with no romaji yet (add), lines whose old romaji
/// is written in Chinese characters (replace; what LDDC's romaji option makes) and lines whose romaji is already in
/// Latin letters (kept). A result counts the replaced lines inside its "added" number too, so "add" below is
/// <c>romajiAdded - romajiReplaced</c>; that way "add 18, replace 12" never reads as 30 + 12.
/// </para>
/// </summary>
public static class LibraryStatusMapper
{
    /// <summary>the longest headline: about what one card line shows before it is cut with an ellipsis (Java: package-private)</summary>
    public const int MaxHeadline = 60;

    /// <param name="preview">the song's result from a dry run of the lyrics use case with romaji on; null = not checked yet</param>
    /// <param name="options">
    /// what Process is set to do: when romaji is off, romaji work does not make a song "needs work"; when lyrics are
    /// off nothing about the lyrics does
    /// </param>
    public static SongStatus Map(SongLyricsResult preview, StandardizeOptions options)
    {
        if (preview == null)
        {
            return SongStatus.Unknown;
        }
        switch (preview.Action)
        {
            case LyricsAction.Failed:
                return Problem(preview);
            case LyricsAction.NoLyrics:
                return NoLyrics();
            default:
                return new Mapping(preview, options != null && options.IsLyrics, options != null && options.IsRomaji)
                    .Status();
        }
    }

    // ---- results that do not depend on the options ----

    private static SongStatus Problem(SongLyricsResult result)
    {
        string sentence = FirstSentence(result.Detail);
        List<string> details = new List<string>();
        details.Add(JavaString.IsBlank(result.Detail) ? "The lyric files could not be used" : JavaString.Trim(result.Detail));
        details.Add("Nothing will be changed or deleted for this song's lyrics");
        string reason = sentence.Length == 0 ? "the lyric files could not be used" : sentence;
        return new SongStatus(Category.Problem, Tone.Bad, Limit("Problem: " + reason), details, 0, 0, 0);
    }

    private static SongStatus NoLyrics()
    {
        List<string> details = new List<string>();
        details.Add("There is no lyric file (.lrc, .ttml or .hmrc) next to this song");
        return new SongStatus(Category.NoLyrics, Tone.Muted, "No lyrics", details, 0, 0, 0);
    }

    // ---- everything else ----

    /// <summary>the working state of mapping one result: what the song has, what Process would do, and how to say it</summary>
    private sealed class Mapping
    {
        private readonly SongLyricsResult _result;
        private readonly bool _lyricsOn;
        private readonly bool _romajiOn;
        private readonly int _deleted; // deprecated files that are (when lyrics are on) deleted
        private readonly int _romajiAdd; // lines with no romaji yet
        private readonly int _romajiReplace; // lines with old romaji in Chinese characters
        private readonly int _romajiPresent; // lines whose romaji is already in Latin letters
        private readonly bool _romajiWork; // Process would add or replace romaji for this song

        public Mapping(SongLyricsResult result, bool lyricsOn, bool romajiOn)
        {
            _result = result;
            _lyricsOn = lyricsOn;
            _romajiOn = romajiOn;
            _deleted = result.DeletedFiles.Count;
            int added = Math.Max(0, result.RomajiAdded);
            _romajiReplace = Math.Min(Math.Max(0, result.RomajiReplaced), added);
            _romajiAdd = added - _romajiReplace;
            _romajiPresent = Math.Max(0, result.RomajiPresent);
            // romaji is made as part of the lyrics step, so it only happens when both are on
            _romajiWork = lyricsOn && romajiOn && added > 0;
        }

        public SongStatus Status()
        {
            switch (_result.Action)
            {
                case LyricsAction.ConvertedFromTtml:
                    return _lyricsOn ? Building(".ttml")
                        : Facts(Category.Standard, "Word-synced .ttml, no standard .lrc",
                            "Has a word-synced .ttml but no .lrc in the standard format");
                case LyricsAction.ConvertedFromHmrc:
                    return _lyricsOn ? Building(".hmrc")
                        : Facts(Category.Standard, "Word-synced .hmrc, no standard .lrc",
                            "Has a word-synced .hmrc but no .lrc in the standard format");
                case LyricsAction.NormalizedLrc:
                    return _lyricsOn ? Rewriting()
                        : Facts(Category.Standard, "Word-synced .lrc, not in the standard format",
                            "Has a word-synced .lrc in a non-standard format");
                case LyricsAction.LineSyncedFromDeprecated:
                    return _lyricsOn ? BuildingLineSynced()
                        : Facts(Category.LineSynced, "Line-synced old file only, no .lrc",
                            "Has no .lrc, only a line-synced old file (.ttml or .hmrc)");
                case LyricsAction.KeptLineSynced:
                    return StandardLrc(true, "Only a line-synced .lrc exists, there is no word-synced source, so it is kept");
                case LyricsAction.RomajiAdded:
                {
                    // the result does not say whether the .lrc is word-synced: take the use case's note at its word
                    bool lineSynced = LooksLineSynced(_result);
                    return _romajiWork && !lineSynced ? RomajiOnly()
                        : StandardLrc(lineSynced, "The .lrc is already in the standard format");
                }
                case LyricsAction.AlreadyStandard:
                default:
                    return _romajiWork ? RomajiOnly()
                        : StandardLrc(false, "Has one word-synced .lrc in the standard format");
            }
        }

        // -- Process would build or change the lyrics --

        private SongStatus Building(string sourceExtension)
        {
            return Changing("Will build the .lrc from " + sourceExtension,
                "Will build one word-synced .lrc from the " + sourceExtension);
        }

        private SongStatus Rewriting()
        {
            return Changing("Will rewrite the .lrc in the standard format",
                "Will rewrite the word-synced .lrc in the standard format");
        }

        private SongStatus BuildingLineSynced()
        {
            return Changing("Will build a line-synced .lrc",
                "Will build a line-synced .lrc from the old file, there is no word-synced source");
        }

        /// <summary>a song whose .lrc is (re)built: the headline is the action, then the romaji and the deletions if they fit</summary>
        private SongStatus Changing(string headline, string situation)
        {
            headline = WithVariants(headline, ", ", RomajiVariants("romaji: "));
            if (_deleted > 0 && headline.Length + 2 + DeleteText().Length <= MaxHeadline)
            {
                headline = headline + ", " + DeleteText();
            }
            return NeedsWork(headline, situation);
        }

        /// <summary>a standard .lrc that only gets romaji (and perhaps loses old files)</summary>
        private SongStatus RomajiOnly()
        {
            string headline;
            if (_romajiReplace == 0)
            {
                headline = "Will add romaji to " + Plural(_romajiAdd, "line", "lines");
            }
            else if (_romajiAdd == 0)
            {
                headline = "Romaji: replace " + OldLines(_romajiReplace);
            }
            else
            {
                headline = "Romaji: add " + _romajiAdd + ", replace " + OldLines(_romajiReplace);
            }
            if (headline.Length > MaxHeadline)
            {
                headline = "Romaji: add " + _romajiAdd + ", replace " + _romajiReplace + " old lines";
            }
            if (_deleted > 0 && headline.Length + 2 + DeleteText().Length <= MaxHeadline)
            {
                headline = headline + ", " + DeleteText();
            }
            return NeedsWork(headline, "The .lrc is already in the standard format");
        }

        private SongStatus NeedsWork(string headline, string situation)
        {
            List<string> details = new List<string>();
            details.Add(situation);
            AddDeletedFiles(details, true);
            AddRomajiLines(details);
            AddNote(details);
            return new SongStatus(Category.NeedsWork, Tone.Active, Limit(headline), details,
                _romajiWork ? _romajiAdd : 0, _romajiWork ? _romajiReplace : 0, _romajiPresent, CardText());
        }

        /// <summary>
        /// the one thing a card says about the lyrics: what the song's lyrics are (never what Process would do with them).
        /// Lyrics built from a .ttml or .hmrc, rewritten or already standard are word-synced; only the line-synced
        /// results are not.
        /// </summary>
        private string CardText()
        {
            switch (_result.Action)
            {
                case LyricsAction.LineSyncedFromDeprecated:
                case LyricsAction.KeptLineSynced:
                    return "Line-synced";
                case LyricsAction.RomajiAdded:
                    return LooksLineSynced(_result) ? "Line-synced" : "Word-synced";
                default:
                    return "Word-synced";
            }
        }

        // -- a .lrc that is there to stay --

        /// <summary>
        /// a standard word-synced .lrc, or the line-synced .lrc that is kept for lack of anything better. It has nothing
        /// to build: it needs work only for romaji or for old files that are deleted next to it.
        /// </summary>
        private SongStatus StandardLrc(bool lineSynced, string situation)
        {
            string label = lineSynced ? "Line-synced only" : "Word-synced";
            if (_romajiWork && lineSynced)
            {
                string headline = WithVariants("Line-synced", " - ", LineSyncedRomajiVariants());
                if (_deleted > 0 && headline.Length + 2 + DeleteText().Length <= MaxHeadline)
                {
                    headline = headline + ", " + DeleteText();
                }
                return NeedsWork(headline, situation);
            }
            if (_lyricsOn && _deleted > 0)
            {
                return NeedsWork((lineSynced ? "Line-synced only" : "Already standard") + " - will " + DeleteText(),
                    situation);
            }

            List<string> details = new List<string>();
            details.Add(situation);
            AddDeletedFiles(details, false);
            AddRomajiLines(details);
            AddNote(details);
            string standardHeadline = label + (_romajiPresent > 0 ? " - romaji ok" : "");
            if (!_lyricsOn && _romajiOn && _romajiAdd + _romajiReplace > 0)
            {
                standardHeadline = label + " - " + Plural(_romajiAdd + _romajiReplace, "line", "lines") + " without Latin romaji";
            }
            return new SongStatus(lineSynced ? Category.LineSynced : Category.Standard,
                lineSynced ? Tone.Muted : Tone.Good, Limit(standardHeadline), details, 0, 0, _romajiPresent, CardText());
        }

        /// <summary>what the lyrics are while lyrics are switched off: facts, nothing is going to be done</summary>
        private SongStatus Facts(Category category, string headline, string situation)
        {
            List<string> details = new List<string>();
            details.Add(situation);
            AddDeletedFiles(details, false);
            AddRomajiLines(details);
            AddNote(details);
            return new SongStatus(category, Tone.Muted, Limit(headline), details, 0, 0, _romajiPresent, CardText());
        }

        // -- pieces of text --

        private string DeleteText()
        {
            return "delete " + Plural(_deleted, "old file", "old files");
        }

        /// <summary>the romaji part of a headline, longest wording first; empty when Process does no romaji for this song</summary>
        private List<string> RomajiVariants(string lead)
        {
            List<string> variants = new List<string>();
            if (!_romajiWork)
            {
                return variants;
            }
            if (_romajiReplace == 0)
            {
                variants.Add(lead + "add " + _romajiAdd);
            }
            else if (_romajiAdd == 0)
            {
                variants.Add(lead + "replace " + OldLines(_romajiReplace));
                variants.Add(lead + "replace " + _romajiReplace + " old");
            }
            else
            {
                variants.Add(lead + "add " + _romajiAdd + ", replace " + OldLines(_romajiReplace));
                variants.Add(lead + "add " + _romajiAdd + ", replace " + _romajiReplace);
            }
            return variants;
        }

        private List<string> LineSyncedRomajiVariants()
        {
            List<string> variants = new List<string>();
            if (_romajiReplace == 0)
            {
                variants.Add("romaji for " + Plural(_romajiAdd, "line", "lines"));
            }
            else if (_romajiAdd == 0)
            {
                variants.Add("replace " + OldLines(_romajiReplace));
                variants.Add("replace " + _romajiReplace + " old romaji lines");
            }
            else
            {
                variants.Add("romaji: add " + _romajiAdd + ", replace " + OldLines(_romajiReplace));
                variants.Add("romaji: add " + _romajiAdd + ", replace " + _romajiReplace);
            }
            return variants;
        }

        /// <summary>the old deprecated files, by name; "will delete" when lyrics are on, otherwise just that they are there</summary>
        private void AddDeletedFiles(List<string> details, bool willDelete)
        {
            if (_deleted == 0)
            {
                return;
            }
            List<string> names = new List<string>();
            foreach (string path in _result.DeletedFiles)
            {
                names.Add(Path.GetFileName(path));
            }
            string list = ": " + string.Join(", ", names);
            details.Add(willDelete ? "Will delete " + Plural(_deleted, "old file", "old files") + list
                : Plural(_deleted, "old file", "old files") + " next to it, not deleted while lyrics are off" + list);
        }

        private void AddRomajiLines(List<string> details)
        {
            if (_romajiWork)
            {
                if (_romajiReplace == 0)
                {
                    details.Add("Romaji: add " + Plural(_romajiAdd, "line", "lines")
                        + ", in Latin letters above the original line");
                }
                else if (_romajiAdd == 0)
                {
                    details.Add("Romaji: replace " + OldLines(_romajiReplace) + " with Latin letters");
                }
                else
                {
                    details.Add("Romaji: add " + Plural(_romajiAdd, "line", "lines") + ", replace "
                        + OldLines(_romajiReplace) + " with Latin letters");
                }
                if (_romajiReplace > 0)
                {
                    details.Add("Old romaji written in Chinese characters (what LDDC's romaji option makes) becomes "
                        + "Latin letters");
                }
            }
            else if (_romajiOn && !_lyricsOn && _romajiAdd + _romajiReplace > 0)
            {
                // lyrics are off, so this is only a fact: nothing is going to be added
                int needing = _romajiAdd + _romajiReplace;
                details.Add(Plural(needing, "line needs", "lines need") + " romaji in Latin letters"
                    + (_romajiReplace > 0 ? " (" + _romajiReplace + " with old Chinese-character romaji)" : "")
                    + ", not changed while lyrics are off");
            }
            if (_romajiPresent > 0)
            {
                details.Add(Plural(_romajiPresent, "line has", "lines have") + " romaji in Latin letters already"
                    + (_lyricsOn ? ", kept" : ""));
            }
        }

        private void AddNote(List<string> details)
        {
            string note = JavaString.Trim(_result.Detail);
            // the use case's own romaji sentence ("romaji will be made for 74 lines") only repeats the romaji line above
            bool repeatsRomaji = _romajiWork && note.ToLowerInvariant().StartsWith("romaji", StringComparison.Ordinal);
            if (note.Length != 0 && !repeatsRomaji)
            {
                details.Add(note);
            }
        }
    }

    // ---- helpers ----

    /// <summary>
    /// a ROMAJI_ADDED result does not say whether its .lrc is word-synced; when the use case's note calls it
    /// line-synced, say so, otherwise a standard .lrc is taken to be word-synced
    /// </summary>
    private static bool LooksLineSynced(SongLyricsResult result)
    {
        string detail = result.Detail.ToLowerInvariant();
        return detail.Contains("line-synced") || detail.Contains("line synced");
    }

    /// <summary>base plus the first variant that still fits one headline line; just the base when none does</summary>
    private static string WithVariants(string baseText, string joiner, List<string> variants)
    {
        foreach (string variant in variants)
        {
            if (baseText.Length + joiner.Length + variant.Length <= MaxHeadline)
            {
                return baseText + joiner + variant;
            }
        }
        return baseText;
    }

    /// <summary>the text up to the first full stop, without it; empty for nothing (Java: package-private)</summary>
    public static string FirstSentence(string detail)
    {
        string text = detail == null ? "" : JavaString.Trim(detail);
        int lineEnd = text.Length;
        foreach (char lineBreak in new char[] { '\n', '\r' })
        {
            int at = text.IndexOf(lineBreak);
            if (at >= 0 && at < lineEnd)
            {
                lineEnd = at;
            }
        }
        text = JavaString.Trim(text.Substring(0, lineEnd));
        int stop = text.IndexOf(". ", StringComparison.Ordinal);
        if (stop >= 0)
        {
            text = text.Substring(0, stop);
        }
        while (text.EndsWith('.'))
        {
            text = text.Substring(0, text.Length - 1);
        }
        return JavaString.Trim(text);
    }

    /// <summary>a headline that is somehow too long is cut with three dots so a card line never overflows</summary>
    private static string Limit(string headline)
    {
        return headline.Length <= MaxHeadline ? headline : JavaString.Trim(headline.Substring(0, MaxHeadline - 3)) + "...";
    }

    /// <summary>"12 old Chinese-character lines": the wording that tells the user which romaji gets replaced</summary>
    private static string OldLines(int count)
    {
        return Plural(count, "old Chinese-character line", "old Chinese-character lines");
    }

    private static string Plural(int count, string one, string many)
    {
        return count + " " + (count == 1 ? one : many);
    }
}
