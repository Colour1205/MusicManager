using MusicManager.Entity;
using MusicManager.InterfaceAdapter.Library;
using MusicManager.UseCase.StandardizeArtists;
using MusicManager.UseCase.StandardizeLyrics;
using Xunit;
using FileLine = MusicManager.InterfaceAdapter.Library.SongLyricsPreview.FileLine;
using LineLine = MusicManager.InterfaceAdapter.Library.SongLyricsPreview.LineLine;
using TagLine = MusicManager.InterfaceAdapter.Library.SongTagsPreview.TagLine;
using Tone = MusicManager.InterfaceAdapter.Library.SongStatus.Tone;

namespace MusicManager.Tests.InterfaceAdapter.Library;

/// <summary>what the song window shows for one song: the files, the changed lyric lines and the tag changes</summary>
public class SongPreviewMapperTest
{
    // ---- helpers ----

    private const string Dir = "C:\\music\\album\\";
    private static readonly StandardizeOptions Delete = Options(false);
    private static readonly StandardizeOptions Recycle = Options(true);

    private static StandardizeOptions Options(bool recycleBin)
    {
        StandardizeOptions options = new StandardizeOptions();
        options.IsRecycleBin = recycleBin;
        return options;
    }

    private static LyricLineChange Added(long timeMs, string original, string newRomaji)
    {
        return new LyricLineChange(LyricLineChange.Kind.RomajiAdded, timeMs, original, null, newRomaji);
    }

    private static LyricLineChange Replaced(long timeMs, string original, string oldRomaji, string newRomaji)
    {
        return new LyricLineChange(LyricLineChange.Kind.RomajiReplaced, timeMs, original, oldRomaji, newRomaji);
    }

    private static SongLyricsResult Result(LyricsAction action, string detail, string written, List<string> deleted,
        List<LyricLineChange> changes)
    {
        return new SongLyricsResult(Dir + "Song.flac", action, detail, deleted, 0, 0, 0, 0, written, changes);
    }

    private static SongLyricsResult Result(LyricsAction action)
    {
        return Result(action, "", null, null, null);
    }

    private static List<LyricLineChange> ManyAdded(int count)
    {
        List<LyricLineChange> changes = new List<LyricLineChange>();
        for (int i = 0; i < count; i++)
        {
            changes.Add(Added(i * 1000L, "line " + i, "romaji " + i));
        }
        return changes;
    }

    private static List<LyricLineChange> ManyReplaced(int count)
    {
        List<LyricLineChange> changes = new List<LyricLineChange>();
        for (int i = 0; i < count; i++)
        {
            changes.Add(Replaced(i * 1000L, "line " + i, "old " + i, "romaji " + i));
        }
        return changes;
    }

    private static FieldChange Change(string field, string before, string after)
    {
        return new FieldChange(field, before, after);
    }

    private static SongArtistUpdate Update(params FieldChange[] changes)
    {
        return new SongArtistUpdate(Dir + "Song.flac", null, null, null, new List<FieldChange>(changes));
    }

    // ---- lyrics: files ----

    [Theory]
    [InlineData(LyricsAction.ConvertedFromTtml, "new word-synced lyrics, built from the .ttml")]
    [InlineData(LyricsAction.ConvertedFromHmrc, "new word-synced lyrics, built from the .hmrc")]
    [InlineData(LyricsAction.NormalizedLrc, "rewritten in the standard word-synced format")]
    [InlineData(LyricsAction.LineSyncedFromDeprecated, "new line-synced lyrics, built from the old file")]
    [InlineData(LyricsAction.RomajiAdded, "romaji updated")]
    public void WrittenFileNoteFollowsTheAction(LyricsAction action, string note)
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(Result(action, "", Dir + "Song.lrc", null, null), Delete);

        Assert.Single(preview.Files);
        FileLine file = preview.Files[0];
        Assert.Equal(FileLine.Kind.Write, file.GetKind());
        Assert.Equal("Song.lrc", file.Name);
        Assert.Equal(note, file.Note);
        Assert.Equal(Tone.Active, preview.Tone);
        Assert.Equal("", preview.Message);
        Assert.Equal("", preview.LinesTitle);
    }

    [Fact]
    public void WrittenFileOfAnotherActionGetsAGenericNote()
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.AlreadyStandard, "", Dir + "Song.lrc", null, null), Delete);

        Assert.Equal("written", preview.Files[0].Note);
        Assert.Equal(Tone.Active, preview.Tone);
    }

    [Fact]
    public void FileNamesLoseTheirFoldersWhateverTheSeparator()
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.ConvertedFromTtml, "", "/home/me/album/Song.lrc",
                new List<string> { "/home/me/album/Song.ttml", "D:\\other\\Song.hmrc", "Bare.old" }, null),
            Delete);

        Assert.Equal("Song.lrc", preview.Files[0].Name);
        Assert.Equal("Song.ttml", preview.Files[1].Name);
        Assert.Equal("Song.hmrc", preview.Files[2].Name);
        Assert.Equal("Bare.old", preview.Files[3].Name);
    }

    [Fact]
    public void WrittenFileComesFirstThenEveryDeletedFile()
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.ConvertedFromTtml, "", Dir + "Song.lrc",
                new List<string> { Dir + "Song.ttml", Dir + "Song.hmrc" }, null),
            Delete);

        Assert.Equal(3, preview.Files.Count);
        Assert.Equal(FileLine.Kind.Write, preview.Files[0].GetKind());
        Assert.Equal(FileLine.Kind.Delete, preview.Files[1].GetKind());
        Assert.Equal("Song.ttml", preview.Files[1].Name);
        Assert.Equal(FileLine.Kind.Delete, preview.Files[2].GetKind());
        Assert.Equal("Song.hmrc", preview.Files[2].Name);
    }

    [Fact]
    public void DeletedFilesAreDeletedWithoutTheRecycleBin()
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.AlreadyStandard, "", null, new List<string> { Dir + "Song.ttml" }, null), Delete);

        Assert.Single(preview.Files);
        Assert.Equal("deleted", preview.Files[0].Note);
        Assert.Equal(Tone.Active, preview.Tone);
        Assert.Equal("", preview.Message);
    }

    [Fact]
    public void DeletedFilesGoToTheRecycleBinWhenThatIsOn()
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.AlreadyStandard, "", null, new List<string> { Dir + "Song.ttml", Dir + "Song.hmrc" }, null),
            Recycle);

        Assert.Equal("goes to the Recycle Bin", preview.Files[0].Note);
        Assert.Equal("goes to the Recycle Bin", preview.Files[1].Note);
    }

    [Fact]
    public void TheWrittenFileNoteDoesNotDependOnTheRecycleBin()
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.ConvertedFromHmrc, "", Dir + "Song.lrc", new List<string> { Dir + "Song.hmrc" }, null),
            Recycle);

        Assert.Equal("new word-synced lyrics, built from the .hmrc", preview.Files[0].Note);
        Assert.Equal("goes to the Recycle Bin", preview.Files[1].Note);
    }

    [Fact]
    public void WithoutOptionsFilesAreDeleted()
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.AlreadyStandard, "", null, new List<string> { Dir + "Song.ttml" }, null), null);

        Assert.Equal("deleted", preview.Files[0].Note);
    }

    [Fact]
    public void BlankPathsAndNullEntriesAreLeftOut()
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.AlreadyStandard, "", "  ", new List<string> { null, "", " ", Dir + "Song.ttml" }, null),
            Delete);

        Assert.Single(preview.Files);
        Assert.Equal("Song.ttml", preview.Files[0].Name);
    }

    // ---- lyrics: lines ----

    [Fact]
    public void EveryChangedLineIsShownWithItsTimeAndTexts()
    {
        List<LyricLineChange> changes = new List<LyricLineChange>
        {
            Added(7_000, "\uc548\ub155", "annyeong"),
            Replaced(65_000, "\u3053\u3093\u306b\u3061\u306f", "\u4eca\u65e5\u306f", "konnichiwa")
        };

        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.RomajiAdded, "", Dir + "Song.lrc", null, changes), Delete);

        Assert.Equal(2, preview.Lines.Count);
        LineLine first = preview.Lines[0];
        Assert.Equal("0:07", first.Time);
        Assert.Equal("\uc548\ub155", first.Original);
        Assert.Null(first.OldRomaji);
        Assert.Equal("annyeong", first.NewRomaji);
        LineLine second = preview.Lines[1];
        Assert.Equal("1:05", second.Time);
        Assert.Equal("\u3053\u3093\u306b\u3061\u306f", second.Original);
        Assert.Equal("\u4eca\u65e5\u306f", second.OldRomaji);
        Assert.Equal("konnichiwa", second.NewRomaji);
        Assert.Equal(Tone.Active, preview.Tone);
        Assert.Equal("", preview.Message);
    }

    [Theory]
    [InlineData(0L, "0:00")]
    [InlineData(999L, "0:00")]
    [InlineData(1000L, "0:01")]
    [InlineData(7000L, "0:07")]
    [InlineData(7999L, "0:07")]
    [InlineData(59999L, "0:59")]
    [InlineData(60000L, "1:00")]
    [InlineData(65000L, "1:05")]
    [InlineData(750000L, "12:30")]
    [InlineData(3599000L, "59:59")]
    [InlineData(3600000L, "60:00")]
    [InlineData(3665000L, "61:05")]
    [InlineData(-5000L, "0:00")]
    public void TimesAreMinutesAndSeconds(long timeMs, string expected)
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.RomajiAdded, "", null, null, new List<LyricLineChange> { Added(timeMs, "x", "y") }), Delete);

        Assert.Equal(expected, preview.Lines[0].Time);
    }

    [Fact]
    public void LinesAreKeptInTheOrderTheyCome()
    {
        List<LyricLineChange> changes = new List<LyricLineChange>
        {
            Added(30_000, "c", "c"), Added(10_000, "a", "a"), Added(20_000, "b", "b")
        };

        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.RomajiAdded, "", null, null, changes), Delete);

        Assert.Equal("c", preview.Lines[0].Original);
        Assert.Equal("a", preview.Lines[1].Original);
        Assert.Equal("b", preview.Lines[2].Original);
    }

    [Fact]
    public void TitleForAddedLinesOnly()
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.RomajiAdded, "", null, null, ManyAdded(74)), Delete);

        Assert.Equal("74 lines get romaji", preview.LinesTitle);
        Assert.Equal(74, preview.Lines.Count);
    }

    [Fact]
    public void TitleForOneAddedLine()
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.RomajiAdded, "", null, null, ManyAdded(1)), Delete);

        Assert.Equal("1 line gets romaji", preview.LinesTitle);
    }

    [Fact]
    public void TitleForReplacedLinesOnly()
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.RomajiAdded, "", null, null, ManyReplaced(45)), Delete);

        Assert.Equal("45 old Chinese-character lines are replaced with Latin letters", preview.LinesTitle);
    }

    [Fact]
    public void TitleForOneReplacedLine()
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.RomajiAdded, "", null, null, ManyReplaced(1)), Delete);

        Assert.Equal("1 old Chinese-character line is replaced with Latin letters", preview.LinesTitle);
    }

    [Fact]
    public void TitleForBothKindsNeverCountsALineTwice()
    {
        List<LyricLineChange> changes = new List<LyricLineChange>(ManyAdded(2));
        changes.AddRange(ManyReplaced(45));

        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.RomajiAdded, "", null, null, changes), Delete);

        Assert.Equal("2 lines get romaji, 45 old Chinese-character lines are replaced with Latin letters",
            preview.LinesTitle);
        Assert.Equal(47, preview.Lines.Count);
    }

    [Fact]
    public void TitleForOneOfEachKind()
    {
        List<LyricLineChange> changes = new List<LyricLineChange>(ManyAdded(1));
        changes.AddRange(ManyReplaced(1));

        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.RomajiAdded, "", null, null, changes), Delete);

        Assert.Equal("1 line gets romaji, 1 old Chinese-character line is replaced with Latin letters",
            preview.LinesTitle);
    }

    [Fact]
    public void NoLineChangesGiveNoTitle()
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.ConvertedFromTtml, "", Dir + "Song.lrc", null, new List<LyricLineChange>()), Delete);

        Assert.Equal("", preview.LinesTitle);
        Assert.True(preview.Lines.Count == 0);
    }

    [Fact]
    public void LinesWithoutRomajiAreCounted()
    {
        List<LyricLineChange> changes = new List<LyricLineChange>
        {
            Added(1000, "a", "A"), Added(2000, "b", null), Replaced(3000, "c", "old", null), Replaced(4000, "d", "old", "D")
        };

        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.RomajiAdded, "no Claude key", null, null, changes), Delete);

        Assert.Equal(2, preview.LinesWithoutRomaji);
        Assert.Null(preview.Lines[1].NewRomaji);
        Assert.Null(preview.Lines[2].NewRomaji);
        Assert.Equal("old", preview.Lines[2].OldRomaji);
    }

    [Fact]
    public void AllRomajiPresentMeansNoneMissing()
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.RomajiAdded, "", null, null, ManyAdded(3)), Delete);

        Assert.Equal(0, preview.LinesWithoutRomaji);
    }

    [Fact]
    public void NullLineEntriesAreSkipped()
    {
        List<LyricLineChange> changes = new List<LyricLineChange> { null, Added(1000, "a", "A"), null };

        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.RomajiAdded, "", null, null, changes), Delete);

        Assert.Single(preview.Lines);
        Assert.Equal("1 line gets romaji", preview.LinesTitle);
    }

    // ---- lyrics: notes ----

    [Fact]
    public void TheUseCasesNotesAreKept()
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.ConvertedFromTtml, "  TTML line L2: removed corrupted characters (x3)  ",
                Dir + "Song.lrc", null, null),
            Delete);

        Assert.Single(preview.Notes);
        Assert.Equal("TTML line L2: removed corrupted characters (x3)", preview.Notes[0]);
    }

    [Fact]
    public void NoNoteForABlankDetail()
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.ConvertedFromTtml, "   ", Dir + "Song.lrc", null, null), Delete);

        Assert.True(preview.Notes.Count == 0);
    }

    [Fact]
    public void AFailureReasonIsTheMessageNotANote()
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.Failed, "Both files are empty", null, null, null), Delete);

        Assert.True(preview.Notes.Count == 0);
        Assert.Equal("The lyrics cannot be processed: Both files are empty", preview.Message);
    }

    [Fact]
    public void NotesAreKeptWhenNothingChanges()
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.KeptLineSynced, "the lyrics have no timing, so no romaji was added", null, null,
                null),
            Delete);

        Assert.Single(preview.Notes);
        Assert.Equal("the lyrics have no timing, so no romaji was added", preview.Notes[0]);
    }

    // ---- lyrics: nothing to show ----

    [Fact]
    public void AlreadyStandardChangesNothing()
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(Result(LyricsAction.AlreadyStandard), Delete);

        Assert.Equal("Nothing changes: in standard format (word-synced)",
            preview.Message);
        Assert.Equal(Tone.Muted, preview.Tone);
        Assert.True(preview.Files.Count == 0);
        Assert.True(preview.Lines.Count == 0);
        Assert.Equal("", preview.LinesTitle);
        Assert.Equal(0, preview.LinesWithoutRomaji);
    }

    [Fact]
    public void KeptLineSyncedChangesNothing()
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(Result(LyricsAction.KeptLineSynced), Delete);

        Assert.Equal("Nothing changes: in standard format (line-synced)",
            preview.Message);
        Assert.Equal(Tone.Muted, preview.Tone);
    }

    [Fact]
    public void NoLyricsSaysSo()
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(Result(LyricsAction.NoLyrics), Delete);

        Assert.Equal("There is no lyric file in the same folder", preview.Message);
        Assert.Equal(Tone.Muted, preview.Tone);
    }

    [Fact]
    public void FailedIsBadWithItsReason()
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(
            Result(LyricsAction.Failed, "  The .ttml has no lines  ", null, null, null), Delete);

        Assert.Equal("The lyrics cannot be processed: The .ttml has no lines", preview.Message);
        Assert.Equal(Tone.Bad, preview.Tone);
    }

    [Fact]
    public void FailedWithoutAReasonSaysTheFilesCouldNotBeUsed()
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(Result(LyricsAction.Failed), Delete);

        Assert.Equal("The lyrics cannot be processed: The lyric files could not be used", preview.Message);
        Assert.Equal(Tone.Bad, preview.Tone);
    }

    [Fact]
    public void AMissingResultHasNothingToPreview()
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(null, Delete);

        Assert.Equal("Nothing to preview.", preview.Message);
        Assert.Equal(Tone.Muted, preview.Tone);
        Assert.True(preview.Files.Count == 0);
        Assert.True(preview.Lines.Count == 0);
        Assert.True(preview.Notes.Count == 0);
    }

    [Theory]
    [InlineData(LyricsAction.ConvertedFromTtml)]
    [InlineData(LyricsAction.ConvertedFromHmrc)]
    [InlineData(LyricsAction.NormalizedLrc)]
    [InlineData(LyricsAction.LineSyncedFromDeprecated)]
    [InlineData(LyricsAction.RomajiAdded)]
    public void OtherActionsWithNothingToShowChangeNothing(LyricsAction action)
    {
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(Result(action), Delete);

        Assert.Equal("Nothing changes in the lyrics.", preview.Message);
        Assert.Equal(Tone.Muted, preview.Tone);
    }

    [Fact]
    public void ANullActionWithNothingToShowChangesNothing()
    {
        // Java passes a null action; a C# enum cannot be null, so this is a value no LyricsAction has (the switch default)
        SongLyricsPreview preview = SongPreviewMapper.Lyrics(Result((LyricsAction)(-1), "", null, null, null), Delete);

        Assert.Equal("Nothing changes in the lyrics.", preview.Message);
        Assert.Equal(Tone.Muted, preview.Tone);
    }

    [Fact]
    public void AnyResultIsHandledWithoutThrowing()
    {
        foreach (LyricsAction action in Enum.GetValues<LyricsAction>())
        {
            foreach (StandardizeOptions options in new StandardizeOptions[] { null, Delete, Recycle })
            {
                Assert.Null(Record.Exception(() => SongPreviewMapper.Lyrics(Result(action), options)));
                // Java also passes a LyricLineChange with a null kind; a C# enum cannot be null, so default (RomajiAdded,
                // which is what a null kind counted as) stands in for it
                Assert.Null(Record.Exception(() => SongPreviewMapper.Lyrics(
                    Result(action, null, Dir + "Song.lrc", new List<string> { Dir + "a", null },
                        new List<LyricLineChange> { null, new LyricLineChange(default, 0, null, null, null) }),
                    options)));
            }
        }
    }

    [Fact]
    public void NullDetailAndListsAreFine()
    {
        SongLyricsResult result = new SongLyricsResult(null, LyricsAction.AlreadyStandard, null, null, 0, 0, 0, 0, null,
            null);

        SongLyricsPreview preview = SongPreviewMapper.Lyrics(result, Delete);

        Assert.True(preview.Files.Count == 0);
        Assert.True(preview.Lines.Count == 0);
        Assert.True(preview.Notes.Count == 0);
        Assert.Equal(Tone.Muted, preview.Tone);
    }

    // ---- lyrics: off and failed ----

    [Fact]
    public void LyricsOffCarriesOnlyItsMessage()
    {
        SongLyricsPreview preview = SongPreviewMapper.LyricsOff("The lyrics are off in Options.");

        Assert.Equal("The lyrics are off in Options.", preview.Message);
        Assert.Equal(Tone.Muted, preview.Tone);
        Assert.True(preview.Files.Count == 0);
        Assert.True(preview.Lines.Count == 0);
        Assert.True(preview.Notes.Count == 0);
        Assert.Equal("", preview.LinesTitle);
    }

    [Fact]
    public void LyricsFailedCarriesOnlyItsReason()
    {
        SongLyricsPreview preview = SongPreviewMapper.LyricsFailed("The preview could not be made");

        Assert.Equal("The preview could not be made", preview.Message);
        Assert.Equal(Tone.Bad, preview.Tone);
        Assert.True(preview.Files.Count == 0);
        Assert.True(preview.Lines.Count == 0);
    }

    // ---- tags ----

    [Fact]
    public void ArtistChangeIsShownWithItsLabel()
    {
        SongTagsPreview preview = SongPreviewMapper.Tags(
            new List<SongArtistUpdate> { Update(Change("ARTIST", "TWICE (\ud2b8\uc640\uc774\uc2a4)", "TWICE")) }, Delete);

        Assert.Single(preview.Changes);
        TagLine line = preview.Changes[0];
        Assert.Equal("Artist", line.Label);
        Assert.Equal("TWICE (\ud2b8\uc640\uc774\uc2a4)", line.Before);
        Assert.Equal("TWICE", line.After);
        Assert.Equal("", preview.Message);
    }

    [Fact]
    public void LabelsAndOrderAreFixedWhateverOrderThePlanUsed()
    {
        SongTagsPreview preview = SongPreviewMapper.Tags(
            new List<SongArtistUpdate>
            {
                Update(Change("ARTISTS", "A; B", "A\nB"), Change("ALBUMARTIST", "X", "Y"),
                    Change("ARTIST", "P", "Q"))
            },
            Delete);

        Assert.Equal(3, preview.Changes.Count);
        Assert.Equal("Artist", preview.Changes[0].Label);
        Assert.Equal("Album artist", preview.Changes[1].Label);
        Assert.Equal("Artists", preview.Changes[2].Label);
        Assert.Equal("A; B", preview.Changes[2].Before);
        Assert.Equal("A\nB", preview.Changes[2].After);
    }

    [Fact]
    public void OtherFieldsGetTheirNameTitleCasedAndComeLast()
    {
        SongTagsPreview preview = SongPreviewMapper.Tags(
            new List<SongArtistUpdate>
            {
                Update(Change("COMPOSER", "a", "b"), Change("ARTIST", "c", "d"),
                    Change("album_sort", "e", "f"))
            },
            Delete);

        Assert.Equal(3, preview.Changes.Count);
        Assert.Equal("Artist", preview.Changes[0].Label);
        Assert.Equal("Composer", preview.Changes[1].Label);
        Assert.Equal("Album sort", preview.Changes[2].Label);
    }

    [Fact]
    public void FieldNamesAreMatchedWithoutRegardToCase()
    {
        SongTagsPreview preview = SongPreviewMapper.Tags(
            new List<SongArtistUpdate> { Update(Change("albumartist", "a", "b"), Change("Artist", "c", "d")) }, Delete);

        Assert.Equal("Artist", preview.Changes[0].Label);
        Assert.Equal("Album artist", preview.Changes[1].Label);
    }

    [Fact]
    public void AMissingFieldNameIsCalledTag()
    {
        SongTagsPreview preview = SongPreviewMapper.Tags(
            new List<SongArtistUpdate> { Update(Change(null, "a", "b")) }, Delete);

        Assert.Equal("Tag", preview.Changes[0].Label);
    }

    [Fact]
    public void IdenticalPairsAreLeftOut()
    {
        SongTagsPreview preview = SongPreviewMapper.Tags(
            new List<SongArtistUpdate>
            {
                Update(Change("ARTIST", "TWICE", "TWICE"), Change("ALBUMARTIST", "X", "Y"),
                    Change("ARTISTS", null, ""))
            },
            Delete);

        Assert.Single(preview.Changes);
        Assert.Equal("Album artist", preview.Changes[0].Label);
    }

    [Fact]
    public void OnlyIdenticalPairsMeansNothingChanges()
    {
        SongTagsPreview preview = SongPreviewMapper.Tags(
            new List<SongArtistUpdate> { Update(Change("ARTIST", "TWICE", "TWICE")) }, Delete);

        Assert.True(preview.Changes.Count == 0);
        Assert.Equal("Nothing changes in the tags.", preview.Message);
    }

    [Fact]
    public void BlankValuesAreShownAsEmpty()
    {
        SongTagsPreview preview = SongPreviewMapper.Tags(
            new List<SongArtistUpdate>
            {
                Update(Change("ALBUMARTIST", null, "TWICE"), Change("ARTISTS", "A; B", "   "),
                    Change("ARTIST", "", "TWICE"))
            },
            Delete);

        Assert.Equal("(empty)", preview.Changes[0].Before);
        Assert.Equal("TWICE", preview.Changes[0].After);
        Assert.Equal("(empty)", preview.Changes[1].Before);
        Assert.Equal("TWICE", preview.Changes[1].After);
        Assert.Equal("A; B", preview.Changes[2].Before);
        Assert.Equal("(empty)", preview.Changes[2].After);
    }

    [Fact]
    public void NoUpdatesMeansNothingChanges()
    {
        foreach (List<SongArtistUpdate> updates in new List<SongArtistUpdate>[] { null, new List<SongArtistUpdate>() })
        {
            SongTagsPreview preview = SongPreviewMapper.Tags(updates, Delete);

            Assert.True(preview.Changes.Count == 0);
            Assert.Equal("Nothing changes in the tags.", preview.Message);
        }
    }

    [Fact]
    public void AnUpdateWithoutChangesMeansNothingChanges()
    {
        SongArtistUpdate withNull = new SongArtistUpdate("x", "a", null, null, null);
        SongArtistUpdate withEmpty = new SongArtistUpdate("x", "a", null, null, new List<FieldChange>());

        SongTagsPreview preview = SongPreviewMapper.Tags(new List<SongArtistUpdate> { null, withNull, withEmpty }, null);

        Assert.True(preview.Changes.Count == 0);
        Assert.Equal("Nothing changes in the tags.", preview.Message);
    }

    [Fact]
    public void NullChangeEntriesAreSkipped()
    {
        SongTagsPreview preview = SongPreviewMapper.Tags(
            new List<SongArtistUpdate> { Update(null, Change("ARTIST", "a", "b"), null) }, Delete);

        Assert.Single(preview.Changes);
    }

    [Fact]
    public void ChangesOfSeveralUpdatesAreAllShown()
    {
        SongTagsPreview preview = SongPreviewMapper.Tags(
            new List<SongArtistUpdate> { Update(Change("ARTISTS", "a", "b")), Update(Change("ARTIST", "c", "d")) }, Delete);

        Assert.Equal(2, preview.Changes.Count);
        Assert.Equal("Artist", preview.Changes[0].Label);
        Assert.Equal("Artists", preview.Changes[1].Label);
    }

    [Fact]
    public void TagsOffCarriesOnlyItsMessage()
    {
        SongTagsPreview preview = SongPreviewMapper.TagsOff();

        Assert.Equal("Artist names are off in Options.", preview.Message);
        Assert.True(preview.Changes.Count == 0);
    }

    [Fact]
    public void TagsFailedCarriesOnlyItsReason()
    {
        SongTagsPreview preview = SongPreviewMapper.TagsFailed("The tags could not be read");

        Assert.Equal("The tags could not be read", preview.Message);
        Assert.True(preview.Changes.Count == 0);
    }
}
