using MusicManager.Entity;
using MusicManager.InterfaceAdapter.Library;
using MusicManager.UseCase.StandardizeLyrics;
using Xunit;
using Category = MusicManager.InterfaceAdapter.Library.SongStatus.Category;
using Tone = MusicManager.InterfaceAdapter.Library.SongStatus.Tone;

namespace MusicManager.Tests.InterfaceAdapter.Library;

/// <summary>what the library shows for a song, for every kind of preview result and every combination of the options</summary>
public class LibraryStatusMapperTest
{
    // ---- helpers ----

    private static readonly StandardizeOptions LyricsAndRomaji = Options(true, true);
    private static readonly StandardizeOptions LyricsOnly = Options(true, false);
    private static readonly StandardizeOptions RomajiOnly = Options(false, true);
    private static readonly StandardizeOptions Neither = Options(false, false);

    private static StandardizeOptions Options(bool lyrics, bool romaji)
    {
        StandardizeOptions options = new StandardizeOptions();
        options.IsLyrics = lyrics;
        options.IsRomaji = romaji;
        return options;
    }

    private static SongLyricsResult Result(LyricsAction action, string detail, int deleted, int added, int replaced,
        int present)
    {
        List<string> files = new List<string>();
        string[] names = { "Song.ttml", "Song.hmrc", "Song.old3", "Song.old4" };
        for (int i = 0; i < deleted; i++)
        {
            files.Add("C:\\music\\album\\" + names[i]);
        }
        return new SongLyricsResult("C:\\music\\album\\Song.flac", action, detail, files, added, replaced, present, 0);
    }

    private static SongLyricsResult Result(LyricsAction action)
    {
        return Result(action, "", 0, 0, 0, 0);
    }

    /// <summary>one row of the table: a result, the options, and what the library must show</summary>
    private static object[] Row(string label, SongLyricsResult result, StandardizeOptions options, Category category,
        Tone tone, string headline, int toAdd, int toReplace, int present)
    {
        return new object[] { label, result, options, category, tone, headline, toAdd, toReplace, present };
    }

    /// <summary>Java assertEquals(expected, actual, message): xUnit's Equal has no message, so the label is kept in the failure text</summary>
    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            Assert.Fail(message + " ==> expected: <" + expected + "> but was: <" + actual + ">");
        }
    }

    /// <summary>Java's List.toString(): "[a, b]"</summary>
    private static string Show(List<string> list)
    {
        return "[" + string.Join(", ", list) + "]";
    }

    // ---- what the card says: Word-synced, Line-synced or nothing ----

    public static IEnumerable<object[]> CardTexts()
    {
        return new List<object[]>
        {
            new object[] { "from ttml", Result(LyricsAction.ConvertedFromTtml, "", 2, 30, 12, 0), LyricsAndRomaji, "Word-synced" },
            new object[] { "from hmrc", Result(LyricsAction.ConvertedFromHmrc, "", 1, 0, 0, 0), LyricsOnly, "Word-synced" },
            new object[] { "rewrite", Result(LyricsAction.NormalizedLrc), LyricsAndRomaji, "Word-synced" },
            new object[] { "rewrite, lyrics off", Result(LyricsAction.NormalizedLrc), Neither, "Word-synced" },
            new object[] { "romaji only", Result(LyricsAction.RomajiAdded, "", 0, 4, 1, 37), LyricsAndRomaji, "Word-synced" },
            new object[] { "already standard", Result(LyricsAction.AlreadyStandard, "", 0, 0, 0, 14), LyricsAndRomaji, "Word-synced" },
            new object[] { "line-synced, romaji to add",
                Result(LyricsAction.RomajiAdded, "line-synced lyrics", 0, 4, 0, 0), LyricsAndRomaji, "Line-synced" },
            new object[] { "kept line-synced", Result(LyricsAction.KeptLineSynced), LyricsAndRomaji, "Line-synced" },
            new object[] { "line-synced from an old file", Result(LyricsAction.LineSyncedFromDeprecated, "", 1, 0, 0, 0),
                LyricsAndRomaji, "Line-synced" },
            new object[] { "line-synced from an old file, lyrics off",
                Result(LyricsAction.LineSyncedFromDeprecated, "", 1, 0, 0, 0), Neither, "Line-synced" },
            new object[] { "no lyrics", Result(LyricsAction.NoLyrics), LyricsAndRomaji, "" },
            new object[] { "problem", Result(LyricsAction.Failed, "The .ttml has no lines", 0, 0, 0, 0), LyricsAndRomaji, "" },
        };
    }

    [Theory]
    [MemberData(nameof(CardTexts))]
    public void TheCardOnlySaysHowTheLyricsAreSynced(string label, SongLyricsResult result, StandardizeOptions options,
        string expected)
    {
        AssertEqual(expected, LibraryStatusMapper.Map(result, options).CardText, label);
    }

    [Fact]
    public void ACardNeverCarriesRomajiCountsOrWhatProcessWouldDo()
    {
        foreach (object[] row in CardTexts())
        {
            string text = LibraryStatusMapper.Map((SongLyricsResult)row[1], (StandardizeOptions)row[2]).CardText;
            Assert.True(text == "" || text == "Word-synced" || text == "Line-synced", (string)row[0] + ": " + text);
        }
    }

    [Fact]
    public void AnUncheckedSongHasNothingToSayAndAConvertResultSaysItself()
    {
        Assert.Equal("", SongStatus.Unknown.CardText);
        Assert.Equal("", LibraryStatusMapper.Map(null, LyricsAndRomaji).CardText);
        SongStatus converted = new SongStatus(Category.Unknown, Tone.Good, "Converted", new List<string>(), 0, 0, 0, "Converted");
        Assert.Equal("Converted", converted.CardText);
    }

    // ---- the table ----

    public static IEnumerable<object[]> Table()
    {
        return new List<object[]>
        {
            // not usable, whatever the options
            Row("failed, first sentence", Result(LyricsAction.Failed, "Both files are empty. Nothing was changed.", 0, 0, 0, 0),
                LyricsAndRomaji, Category.Problem, Tone.Bad, "Problem: Both files are empty", 0, 0, 0),
            Row("failed, options off", Result(LyricsAction.Failed, "The .ttml has no lines", 0, 0, 0, 0),
                Neither, Category.Problem, Tone.Bad, "Problem: The .ttml has no lines", 0, 0, 0),
            Row("failed, no reason", Result(LyricsAction.Failed, "", 0, 0, 0, 0),
                LyricsAndRomaji, Category.Problem, Tone.Bad, "Problem: the lyric files could not be used", 0, 0, 0),
            Row("no lyrics", Result(LyricsAction.NoLyrics), LyricsAndRomaji, Category.NoLyrics, Tone.Muted, "No lyrics", 0, 0, 0),
            Row("no lyrics, options off", Result(LyricsAction.NoLyrics), Neither, Category.NoLyrics, Tone.Muted, "No lyrics", 0, 0, 0),

            // lyrics are built
            Row("from ttml, two old files", Result(LyricsAction.ConvertedFromTtml, "", 2, 0, 0, 0), LyricsAndRomaji,
                Category.NeedsWork, Tone.Active, "Will build the .lrc from .ttml, delete 2 old files", 0, 0, 0),
            Row("from ttml, one old file", Result(LyricsAction.ConvertedFromTtml, "", 1, 0, 0, 0), LyricsOnly,
                Category.NeedsWork, Tone.Active, "Will build the .lrc from .ttml, delete 1 old file", 0, 0, 0),
            Row("from hmrc", Result(LyricsAction.ConvertedFromHmrc, "", 1, 0, 0, 0), LyricsAndRomaji,
                Category.NeedsWork, Tone.Active, "Will build the .lrc from .hmrc, delete 1 old file", 0, 0, 0),
            Row("rewrite nonstandard lrc", Result(LyricsAction.NormalizedLrc), LyricsAndRomaji,
                Category.NeedsWork, Tone.Active, "Will rewrite the .lrc in the standard format", 0, 0, 0),
            Row("line-synced from an old file", Result(LyricsAction.LineSyncedFromDeprecated, "", 1, 0, 0, 0), LyricsAndRomaji,
                Category.NeedsWork, Tone.Active, "Will build a line-synced .lrc, delete 1 old file", 0, 0, 0),

            // lyrics are built and romaji comes with it
            Row("from ttml + romaji add only", Result(LyricsAction.ConvertedFromTtml, "", 1, 30, 0, 0), LyricsAndRomaji,
                Category.NeedsWork, Tone.Active, "Will build the .lrc from .ttml, romaji: add 30", 30, 0, 0),
            Row("from ttml + romaji add and replace", Result(LyricsAction.ConvertedFromTtml, "", 0, 30, 12, 0), LyricsAndRomaji,
                Category.NeedsWork, Tone.Active, "Will build the .lrc from .ttml, romaji: add 18, replace 12", 18, 12, 0),
            Row("rewrite + romaji add, short enough to show", Result(LyricsAction.NormalizedLrc, "", 0, 9, 0, 0),
                LyricsAndRomaji, Category.NeedsWork, Tone.Active,
                "Will rewrite the .lrc in the standard format, romaji: add 9", 9, 0, 0),
            Row("rewrite + romaji replace, too long for the card (the details say it)",
                Result(LyricsAction.NormalizedLrc, "", 0, 9, 9, 0), LyricsAndRomaji, Category.NeedsWork,
                Tone.Active, "Will rewrite the .lrc in the standard format", 0, 9, 0),
            Row("from ttml + romaji turned off", Result(LyricsAction.ConvertedFromTtml, "", 1, 30, 12, 0), LyricsOnly,
                Category.NeedsWork, Tone.Active, "Will build the .lrc from .ttml, delete 1 old file", 0, 0, 0),
            Row("from ttml, lyrics off", Result(LyricsAction.ConvertedFromTtml, "", 1, 30, 12, 0), RomajiOnly,
                Category.Standard, Tone.Muted, "Word-synced .ttml, no standard .lrc", 0, 0, 0),
            Row("from hmrc, lyrics off", Result(LyricsAction.ConvertedFromHmrc), Neither,
                Category.Standard, Tone.Muted, "Word-synced .hmrc, no standard .lrc", 0, 0, 0),
            Row("rewrite, lyrics off", Result(LyricsAction.NormalizedLrc), Neither,
                Category.Standard, Tone.Muted, "Word-synced .lrc, not in the standard format", 0, 0, 0),
            Row("line-synced from old file, lyrics off", Result(LyricsAction.LineSyncedFromDeprecated, "", 1, 0, 0, 0), Neither,
                Category.LineSynced, Tone.Muted, "Line-synced old file only, no .lrc", 0, 0, 0),

            // an .lrc that is already standard
            Row("already standard", Result(LyricsAction.AlreadyStandard), LyricsAndRomaji,
                Category.Standard, Tone.Good, "Word-synced", 0, 0, 0),
            Row("already standard, romaji ok", Result(LyricsAction.AlreadyStandard, "", 0, 0, 0, 14), LyricsAndRomaji,
                Category.Standard, Tone.Good, "Word-synced - romaji ok", 0, 0, 14),
            Row("already standard, lyrics off", Result(LyricsAction.AlreadyStandard, "", 0, 0, 0, 14), Neither,
                Category.Standard, Tone.Good, "Word-synced - romaji ok", 0, 0, 14),
            Row("already standard, old files to delete", Result(LyricsAction.AlreadyStandard, "", 2, 0, 0, 0), LyricsAndRomaji,
                Category.NeedsWork, Tone.Active, "Already standard - will delete 2 old files", 0, 0, 0),
            Row("already standard, old files but lyrics off", Result(LyricsAction.AlreadyStandard, "", 2, 0, 0, 0), RomajiOnly,
                Category.Standard, Tone.Good, "Word-synced", 0, 0, 0),

            // romaji is added to an .lrc that is already standard
            Row("romaji add", Result(LyricsAction.RomajiAdded, "", 0, 30, 0, 0), LyricsAndRomaji,
                Category.NeedsWork, Tone.Active, "Will add romaji to 30 lines", 30, 0, 0),
            Row("romaji add one line", Result(LyricsAction.RomajiAdded, "", 0, 1, 0, 0), LyricsAndRomaji,
                Category.NeedsWork, Tone.Active, "Will add romaji to 1 line", 1, 0, 0),
            Row("romaji replace all (what LDDC made)", Result(LyricsAction.RomajiAdded, "", 0, 40, 40, 0), LyricsAndRomaji,
                Category.NeedsWork, Tone.Active, "Romaji: replace 40 old Chinese-character lines", 0, 40, 0),
            Row("romaji replace one", Result(LyricsAction.RomajiAdded, "", 0, 1, 1, 0), LyricsAndRomaji,
                Category.NeedsWork, Tone.Active, "Romaji: replace 1 old Chinese-character line", 0, 1, 0),
            Row("romaji add and replace", Result(LyricsAction.RomajiAdded, "", 0, 30, 12, 3), LyricsAndRomaji,
                Category.NeedsWork, Tone.Active, "Romaji: add 18, replace 12 old Chinese-character lines", 18, 12, 3),
            Row("romaji add and old files", Result(LyricsAction.RomajiAdded, "", 1, 30, 0, 0), LyricsAndRomaji,
                Category.NeedsWork, Tone.Active, "Will add romaji to 30 lines, delete 1 old file", 30, 0, 0),
            Row("romaji turned off", Result(LyricsAction.RomajiAdded, "", 0, 30, 12, 0), LyricsOnly,
                Category.Standard, Tone.Good, "Word-synced", 0, 0, 0),
            Row("romaji turned off, some already ok", Result(LyricsAction.RomajiAdded, "", 0, 30, 0, 5), LyricsOnly,
                Category.Standard, Tone.Good, "Word-synced - romaji ok", 0, 0, 5),
            Row("romaji turned off, old files", Result(LyricsAction.RomajiAdded, "", 2, 30, 0, 0), LyricsOnly,
                Category.NeedsWork, Tone.Active, "Already standard - will delete 2 old files", 0, 0, 0),
            Row("lyrics off, romaji on", Result(LyricsAction.RomajiAdded, "", 0, 30, 12, 0), RomajiOnly,
                Category.Standard, Tone.Good, "Word-synced - 30 lines without Latin romaji", 0, 0, 0),
            Row("both off", Result(LyricsAction.RomajiAdded, "", 0, 30, 12, 0), Neither,
                Category.Standard, Tone.Good, "Word-synced", 0, 0, 0),
            Row("romaji added to a line-synced lrc, romaji off",
                Result(LyricsAction.RomajiAdded, "Kept the line-synced .lrc", 0, 30, 0, 0), LyricsOnly,
                Category.LineSynced, Tone.Muted, "Line-synced only", 0, 0, 0),
            Row("romaji added to a line-synced lrc",
                Result(LyricsAction.RomajiAdded, "Kept the line-synced .lrc", 0, 30, 0, 0), LyricsAndRomaji,
                Category.NeedsWork, Tone.Active, "Line-synced - romaji for 30 lines", 30, 0, 0),

            // only a line-synced .lrc
            Row("line-synced only", Result(LyricsAction.KeptLineSynced), LyricsAndRomaji,
                Category.LineSynced, Tone.Muted, "Line-synced only", 0, 0, 0),
            Row("line-synced only, lyrics off", Result(LyricsAction.KeptLineSynced), Neither,
                Category.LineSynced, Tone.Muted, "Line-synced only", 0, 0, 0),
            Row("line-synced with romaji to add", Result(LyricsAction.KeptLineSynced, "", 0, 20, 0, 0), LyricsAndRomaji,
                Category.NeedsWork, Tone.Active, "Line-synced - romaji for 20 lines", 20, 0, 0),
            Row("line-synced with one romaji line", Result(LyricsAction.KeptLineSynced, "", 0, 1, 0, 0), LyricsAndRomaji,
                Category.NeedsWork, Tone.Active, "Line-synced - romaji for 1 line", 1, 0, 0),
            Row("line-synced with old romaji to replace", Result(LyricsAction.KeptLineSynced, "", 0, 8, 8, 0), LyricsAndRomaji,
                Category.NeedsWork, Tone.Active, "Line-synced - replace 8 old Chinese-character lines", 0, 8, 0),
            Row("line-synced with both", Result(LyricsAction.KeptLineSynced, "", 0, 20, 5, 0), LyricsAndRomaji,
                Category.NeedsWork, Tone.Active, "Line-synced - romaji: add 15, replace 5", 15, 5, 0),
            Row("line-synced, romaji off", Result(LyricsAction.KeptLineSynced, "", 0, 20, 5, 0), LyricsOnly,
                Category.LineSynced, Tone.Muted, "Line-synced only", 0, 0, 0),
            Row("line-synced, romaji ok", Result(LyricsAction.KeptLineSynced, "", 0, 0, 0, 20), LyricsAndRomaji,
                Category.LineSynced, Tone.Muted, "Line-synced only - romaji ok", 0, 0, 20),
            Row("line-synced, old files to delete", Result(LyricsAction.KeptLineSynced, "", 1, 0, 0, 0), LyricsOnly,
                Category.NeedsWork, Tone.Active, "Line-synced only - will delete 1 old file", 0, 0, 0),
            Row("line-synced, old files but lyrics off", Result(LyricsAction.KeptLineSynced, "", 1, 0, 0, 0), Neither,
                Category.LineSynced, Tone.Muted, "Line-synced only", 0, 0, 0)
        };
    }

    [Theory]
    [MemberData(nameof(Table))]
    public void MapsTheResultAsTheTableSays(string label, SongLyricsResult result, StandardizeOptions options,
        Category category, Tone tone, string headline, int toAdd, int toReplace, int present)
    {
        SongStatus status = LibraryStatusMapper.Map(result, options);

        AssertEqual(headline, status.Headline, label);
        AssertEqual(category, status.GetCategory(), label);
        AssertEqual(tone, status.GetTone(), label);
        AssertEqual(toAdd, status.RomajiToAdd, label + ": romaji to add");
        AssertEqual(toReplace, status.RomajiToReplace, label + ": romaji to replace");
        AssertEqual(present, status.RomajiPresent, label + ": romaji present");
        Assert.False(status.Details.Count == 0, label + ": the song window has something to say");
    }

    // ---- not checked yet ----

    [Fact]
    public void ASongThatWasNotCheckedYetIsUnknown()
    {
        Assert.Same(SongStatus.Unknown, LibraryStatusMapper.Map(null, LyricsAndRomaji));
        Assert.Same(SongStatus.Unknown, LibraryStatusMapper.Map(null, Neither));
        Assert.Equal(Category.Unknown, SongStatus.Unknown.GetCategory());
    }

    // ---- the lines of the song window ----

    private static List<string> Details(SongLyricsResult result, StandardizeOptions options)
    {
        return LibraryStatusMapper.Map(result, options).Details;
    }

    private static bool AnyLineContains(List<string> lines, string text)
    {
        return lines.Any(line => line.Contains(text));
    }

    [Fact]
    public void TheDetailsNameTheFilesThatWillBeDeleted()
    {
        List<string> details = Details(Result(LyricsAction.ConvertedFromTtml, "", 2, 0, 0, 0), LyricsAndRomaji);

        Assert.True(AnyLineContains(details, "Will delete 2 old files: Song.ttml, Song.hmrc"), Show(details));
    }

    [Fact]
    public void WhenLyricsAreOffTheOldFilesAreOnlyMentioned()
    {
        List<string> details = Details(Result(LyricsAction.ConvertedFromTtml, "", 1, 0, 0, 0), Neither);

        Assert.False(AnyLineContains(details, "Will delete"), Show(details));
        Assert.True(AnyLineContains(details, "not deleted while lyrics are off"), Show(details));
    }

    [Fact]
    public void ReplacingOldRomajiIsSaidPlainlyInTheDetails()
    {
        List<string> details = Details(Result(LyricsAction.RomajiAdded, "", 0, 30, 12, 0), LyricsAndRomaji);

        Assert.True(AnyLineContains(details, "Romaji: add 18 lines, replace 12 old Chinese-character lines with Latin letters"),
            Show(details));
        Assert.True(AnyLineContains(details, "LDDC"), "the user is told where such romaji comes from: " + Show(details));
    }

    [Fact]
    public void AddingOnlyDoesNotMentionReplacing()
    {
        List<string> details = Details(Result(LyricsAction.RomajiAdded, "", 0, 30, 0, 0), LyricsAndRomaji);

        Assert.True(AnyLineContains(details, "Romaji: add 30 lines"), Show(details));
        Assert.False(AnyLineContains(details, "Chinese"), Show(details));
    }

    [Fact]
    public void RomajiAlreadyInLatinLettersIsReportedAsKept()
    {
        List<string> details = Details(Result(LyricsAction.RomajiAdded, "", 0, 10, 0, 25), LyricsAndRomaji);

        Assert.True(AnyLineContains(details, "25 lines have romaji in Latin letters already, kept"), Show(details));
    }

    [Fact]
    public void RomajiFactsAreStillShownWhenLyricsAreOff()
    {
        List<string> details = Details(Result(LyricsAction.RomajiAdded, "", 0, 30, 12, 5), RomajiOnly);

        Assert.True(AnyLineContains(details, "30 lines need romaji in Latin letters (12 with old Chinese-character romaji)"),
            Show(details));
        Assert.True(AnyLineContains(details, "not changed while lyrics are off"), Show(details));
        Assert.True(AnyLineContains(details, "5 lines have romaji in Latin letters already"), Show(details));
        Assert.False(AnyLineContains(details, "Will"), Show(details));
    }

    [Fact]
    public void RomajiThatIsOffAddsNoRomajiWork()
    {
        List<string> details = Details(Result(LyricsAction.RomajiAdded, "", 0, 30, 12, 0), LyricsOnly);

        Assert.False(AnyLineContains(details, "Romaji: add"), Show(details));
        Assert.False(AnyLineContains(details, "need romaji"), Show(details));
    }

    [Fact]
    public void TheUseCasesOwnNoteIsKept()
    {
        List<string> details = Details(Result(LyricsAction.NormalizedLrc, "The .ttml had 2 empty lines", 0, 0, 0, 0),
            LyricsAndRomaji);

        Assert.Equal("The .ttml had 2 empty lines", details[details.Count - 1]);
    }

    [Fact]
    public void AProblemKeepsTheWholeReasonInTheDetails()
    {
        List<string> details = Details(Result(LyricsAction.Failed, "Both files are empty. Nothing was changed.", 0, 0, 0, 0),
            LyricsAndRomaji);

        Assert.Equal("Both files are empty. Nothing was changed.", details[0]);
        Assert.True(AnyLineContains(details, "Nothing will be changed or deleted"), Show(details));
    }

    [Fact]
    public void ALongReasonIsCutToOneCardLine()
    {
        string reason = "The word timing in the .ttml runs backwards in line 12 and the file cannot be trusted at all";

        string headline = LibraryStatusMapper.Map(Result(LyricsAction.Failed, reason + ".", 0, 0, 0, 0), LyricsAndRomaji)
            .Headline;

        Assert.True(headline.Length <= LibraryStatusMapper.MaxHeadline && headline.Length > 50, headline);
        Assert.True(headline.EndsWith("...", StringComparison.Ordinal), headline);
        Assert.True(headline.StartsWith("Problem: The word timing", StringComparison.Ordinal), headline);
    }

    [Fact]
    public void FirstSentenceStopsAtTheFirstFullStopOrLine()
    {
        Assert.Equal("One", LibraryStatusMapper.FirstSentence("One. Two."));
        Assert.Equal("One", LibraryStatusMapper.FirstSentence("One.\nTwo"));
        Assert.Equal("One", LibraryStatusMapper.FirstSentence("  One.  "));
        Assert.Equal("v1.2 is not supported", LibraryStatusMapper.FirstSentence("v1.2 is not supported"));
        Assert.Equal("", LibraryStatusMapper.FirstSentence(""));
        Assert.Equal("", LibraryStatusMapper.FirstSentence(null));
    }

    [Fact]
    public void MissingOptionsAreTreatedAsEverythingOff()
    {
        SongStatus status = LibraryStatusMapper.Map(Result(LyricsAction.ConvertedFromTtml, "", 1, 0, 0, 0), null);

        Assert.Equal(Category.Standard, status.GetCategory());
    }

    // ---- every combination ----

    [Fact]
    public void EveryHeadlineFitsOneLineAndEveryLineIsPlainAscii()
    {
        int checkedCount = 0;
        foreach (LyricsAction action in Enum.GetValues<LyricsAction>())
        {
            foreach (StandardizeOptions options in new[] { LyricsAndRomaji, LyricsOnly, RomajiOnly, Neither })
            {
                for (int deleted = 0; deleted <= 4; deleted += 2)
                {
                    foreach (int[] counts in new int[][] { new[] { 0, 0, 0 }, new[] { 1, 0, 0 }, new[] { 30, 12, 5 },
                        new[] { 400, 400, 0 }, new[] { 1234, 1, 99 }, new[] { 99999, 88888, 77777 } })
                    {
                        SongLyricsResult result = Result(action, "A note. Another one.", deleted, counts[0], counts[1], counts[2]);
                        SongStatus status = LibraryStatusMapper.Map(result, options);
                        string context = action + " lyrics=" + options.IsLyrics + " romaji=" + options.IsRomaji
                            + " deleted=" + deleted + " counts=[" + string.Join(", ", counts) + "]";

                        Assert.NotNull(status);
                        Assert.True(status.Headline.Length <= LibraryStatusMapper.MaxHeadline,
                            context + ": " + status.Headline);
                        Assert.False(JavaStringIsBlank(status.Headline), context);
                        Assert.True(IsAscii(status.Headline), context + ": " + status.Headline);
                        foreach (string line in status.Details)
                        {
                            Assert.True(IsAscii(line), context + ": " + line);
                        }
                        if (!options.IsLyrics)
                        {
                            Assert.True(status.GetCategory() != Category.NeedsWork, "nothing is needed with lyrics off: " + context);
                        }
                        if (!options.IsLyrics || !options.IsRomaji)
                        {
                            Assert.True(status.RomajiToAdd == 0, context);
                            Assert.True(status.RomajiToReplace == 0, context);
                        }
                        checkedCount++;
                    }
                }
            }
        }
        Assert.True(checkedCount > 400, "checked " + checkedCount);
    }

    private static bool JavaStringIsBlank(string text)
    {
        return MusicManager.Common.JavaString.IsBlank(text);
    }

    private static bool IsAscii(string text)
    {
        return text.All(c => c < 128);
    }

    [Fact]
    public void TheRomajiNumbersNeverCountALineTwice()
    {
        // 30 lines get a romaji line, 12 of which had old Chinese-character romaji: 18 + 12, not 30 + 12
        SongStatus status = LibraryStatusMapper.Map(Result(LyricsAction.RomajiAdded, "", 0, 30, 12, 0), LyricsAndRomaji);

        Assert.Equal(18, status.RomajiToAdd);
        Assert.Equal(12, status.RomajiToReplace);
    }

    [Fact]
    public void InconsistentCountsFromTheUseCaseAreClampedNotNegative()
    {
        SongStatus status = LibraryStatusMapper.Map(Result(LyricsAction.RomajiAdded, "", 0, 5, 9, -3), LyricsAndRomaji);

        Assert.Equal(0, status.RomajiToAdd);
        Assert.Equal(5, status.RomajiToReplace);
        Assert.Equal(0, status.RomajiPresent);
    }
}
