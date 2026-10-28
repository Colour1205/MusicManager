using MusicManager.Entity;
using MusicManager.UseCase.StandardizeAlbums;
using Xunit;

namespace MusicManager.Tests.UseCase.StandardizeAlbums;

/// <summary>
/// Not a port: the album logic that needs no files and no AI. Where a song's album name comes from, how the spellings are
/// grouped (by NAME, never by album artist), what changes in the tags, and which songs belong in another folder. The paths
/// are made up; none of them is opened.
/// </summary>
public class AlbumStandardizerTest
{
    private const string Root = @"C:\lib";

    private static SongAlbumTags Song(string folder, string file, string album, string artist = "IU", string albumArtist = null)
    {
        string path = folder == null ? Path.Combine(Root, file) : Path.Combine(Root, folder, file);
        return new SongAlbumTags(path, album, new List<string> { artist },
                albumArtist == null ? new List<string>() : new List<string> { albumArtist }, file);
    }

    private static Dictionary<string, ParsedAlbum> Answers(params (string Raw, string Album, bool Low)[] answers)
    {
        return answers.ToDictionary(a => a.Raw, a => new ParsedAlbum(a.Album, a.Low), StringComparer.Ordinal);
    }

    private static AlbumPlan Plan(List<SongAlbumTags> songs, Dictionary<string, ParsedAlbum> parsed, IEnumerable<string> scope = null)
    {
        return AlbumStandardizer.BuildPlan(songs, parsed, true, Root, scope);
    }

    // ---- where the name comes from

    [Fact]
    public void TheRawValueIsTheAlbumTagNormalized()
    {
        Assert.Equal("Palette", AlbumStandardizer.RawOf(Song("x", "1.flac", "Palette"), Root));
        Assert.Equal("Love poem", AlbumStandardizer.RawOf(Song("x", "1.flac", "  Love \u00A0 poem "), Root));
    }

    [Fact]
    public void WithoutAnAlbumTagTheFolderNameStandsIn()
    {
        Assert.Equal("IU - Palette [FLAC]", AlbumStandardizer.RawOf(Song("IU - Palette [FLAC]", "1.flac", null), Root));
        Assert.Equal("Palette", AlbumStandardizer.RawOf(Song("Palette", "1.flac", "   "), Root));
        Assert.True(AlbumStandardizer.IsFromFolder(Song("Palette", "1.flac", null)));
        Assert.False(AlbumStandardizer.IsFromFolder(Song("Palette", "1.flac", "Palette")));
    }

    [Theory]
    [InlineData("New songs")]
    [InlineData("Downloads")]
    [InlineData("music")]
    public void AFolderThatSaysNothingAboutAnAlbumIsNotTakenForItsName(string folder)
    {
        Assert.Null(AlbumStandardizer.RawOf(Song(folder, "1.flac", null), Root));
        Assert.Equal("Palette", AlbumStandardizer.RawOf(Song(folder, "1.flac", "Palette"), Root)); // the tag still counts
    }

    [Fact]
    public void ASongWithNoAlbumTagInTheNewSongsFolderGetsNoAlbumAtAll()
    {
        var songs = new List<SongAlbumTags> { Song("New songs", "Closer.flac", null) };

        AlbumPlan plan = Plan(songs, new Dictionary<string, ParsedAlbum>());

        Assert.Empty(plan.Groups);
        Assert.Empty(AlbumStandardizer.ComputeUpdates(plan));
        Assert.Empty(AlbumStandardizer.ComputeFolderMoves(plan));
    }

    [Fact]
    public void ASongDirectlyInTheOpenedFolderHasNoFolderToTakeANameFrom()
    {
        Assert.Null(AlbumStandardizer.RawOf(Song(null, "1.flac", null), Root));
        Assert.Equal("Palette", AlbumStandardizer.RawOf(Song(null, "1.flac", "Palette"), Root));
        Assert.Null(AlbumStandardizer.RawOf(Song(null, "1.flac", null), Root + @"\")); // a closing separator makes no difference
    }

    [Fact]
    public void TheRawValuesAreCountedWithTheirContextTheCommonestFirst()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("Chat", "1.flac", "CHAT-SHIRE"),
            Song("Palette", "1.flac", "Palette"),
            Song("Palette", "2.flac", "Palette"),
            Song("Palette", "3.flac", "Palette", "G-DRAGON"),
            Song("Folder only", "1.flac", null),
        };

        var raws = AlbumStandardizer.CollectRaw(songs, Root);

        Assert.Equal(new[] { "Palette", "CHAT-SHIRE", "Folder only" }, raws.Keys.ToArray());
        Assert.Equal(3, raws["Palette"].Count);
        Assert.False(raws["Palette"].IsFromFolder);
        Assert.Equal(new[] { "IU", "G-DRAGON" }, raws["Palette"].Artists.ToArray());
        Assert.Equal(new[] { "1.flac", "2.flac", "3.flac" }, raws["Palette"].Titles.ToArray()); // (the titles of these test songs are their file names)
        Assert.Equal(new[] { "Palette" }, raws["Palette"].Folders.ToArray());
        Assert.True(raws["Folder only"].IsFromFolder);
    }

    // ---- without the AI

    [Theory]
    [InlineData("Palette [Hi-Res 24bit-96khz]", "Palette")]
    [InlineData("Love Poem [24bit Hi-Res]", "Love Poem")]
    [InlineData("Tour Concert Love,Poem In Seoul [BluRay 24bit Hi-Res]", "Tour Concert Love,Poem In Seoul")]
    [InlineData("Abbey Road (FLAC)", "Abbey Road")]
    [InlineData("Lilac (2021)", "Lilac")]
    [InlineData("LILAC [2021] [Hi-Res]", "LILAC")]
    [InlineData("20歲的春天 [KR Single]", "20歲的春天 [KR Single]")] // not a note about the file: kept
    [InlineData("[FLAC]", "[FLAC]")] // nothing would be left
    [InlineData("  Spaced   out  ", "Spaced out")]
    public void WithoutTheAiOnlyFileQualityNotesAndYearsAreDropped(string raw, string expected)
    {
        Assert.Equal(expected, AlbumStandardizer.CleanLocally(raw));
        Assert.Equal(expected, AlbumStandardizer.ParseLocally(new[] { raw })[raw].Album);
        Assert.False(AlbumStandardizer.ParseLocally(new[] { raw })[raw].IsLowConfidence);
    }

    [Fact]
    public void TheIdentityOfAnAlbumIgnoresCaseSpacingAndPunctuation()
    {
        Assert.Equal(AlbumStandardizer.IdentityKey("TWICEcoaster : LANE 1"), AlbumStandardizer.IdentityKey("Twicecoaster: Lane 1"));
        Assert.Equal(AlbumStandardizer.IdentityKey("Love poem"), AlbumStandardizer.IdentityKey("LOVE POEM"));
        Assert.Equal(AlbumStandardizer.IdentityKey("Lost And Found"), AlbumStandardizer.IdentityKey("Lost and Found"));
        Assert.NotEqual(AlbumStandardizer.IdentityKey("HEAT"), AlbumStandardizer.IdentityKey("HEAT (Clean)"));
    }

    [Fact]
    public void APlusSignMakesAnotherAlbumAndAnAmpersandIsTheWordAnd()
    {
        Assert.NotEqual(AlbumStandardizer.IdentityKey("Real"), AlbumStandardizer.IdentityKey("Real+"));
        Assert.Equal(AlbumStandardizer.IdentityKey("Real+"), AlbumStandardizer.IdentityKey("Real Plus"));
        Assert.Equal(AlbumStandardizer.IdentityKey("Real+"), AlbumStandardizer.IdentityKey("Real＋")); // a full-width plus
        Assert.Equal(AlbumStandardizer.IdentityKey("Mix & Match"), AlbumStandardizer.IdentityKey("Mix and Match"));
        Assert.Equal("", AlbumStandardizer.IdentityKey(null));
    }

    [Fact]
    public void AlbumsThatDifferOnlyByAPlusAreNotMergedInThePlan()
    {
        var songs = new List<SongAlbumTags> { Song("Real", "1.flac", "Real"), Song("Real+", "1.flac", "Real+") };

        AlbumPlan plan = Plan(songs, Answers(("Real", "Real", false), ("Real+", "Real+", false)));

        Assert.Equal(2, plan.Groups.Count);
        Assert.Empty(AlbumStandardizer.ComputeFolderMoves(plan)); // each is in the folder of its name already
    }

    // ---- the plan

    [Fact]
    public void SpellingsOfOneAlbumBecomeOneGroupWithTheNameMostSongsWereGiven()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("Lost And Found", "1.flac", "Lost And Found"),
            Song("Lost And Found", "2.flac", "Lost And Found"),
            Song("Other", "3.flac", "Lost and Found"),
        };

        AlbumPlan plan = Plan(songs, Answers(("Lost And Found", "Lost And Found", false), ("Lost and Found", "Lost and Found", false)));

        AlbumGroup group = Assert.Single(plan.Groups);
        Assert.Equal("Lost And Found", group.CanonicalName);
        Assert.Equal(3, group.SongCount);
        Assert.Equal(new[] { "Lost And Found", "Lost and Found" }, group.Variants.Select(v => v.Text).ToArray());
        Assert.Equal(new[] { 2, 1 }, group.Variants.Select(v => v.Count).ToArray());
        Assert.True(group.IsLowConfidence); // the answers gave two spellings: the user picks
        Assert.Same(group, plan.GroupOf(songs[2]));
    }

    [Fact]
    public void ValuesTheAnswersGiveOneNameAreNotMarkedForCheckingJustForBeingMerged()
    {
        var songs = new List<SongAlbumTags> { Song("HEAT", "1.flac", "HEAT"), Song("HEAT (Clean)", "1.flac", "HEAT (Clean)") };

        AlbumPlan plan = Plan(songs, Answers(("HEAT", "HEAT", false), ("HEAT (Clean)", "HEAT", false)));

        Assert.False(Assert.Single(plan.Groups).IsLowConfidence);
    }

    [Fact]
    public void ADoubtAboutAValueThatIsLeftAsItIsNeedsNoLook()
    {
        var songs = new List<SongAlbumTags> { Song("A", "1.flac", "光年之外"), Song("B", "1.flac", "Guessed [FLAC]") };

        AlbumPlan plan = Plan(songs, Answers(("光年之外", "光年之外", true), ("Guessed [FLAC]", "Guessed", true)));

        Assert.False(plan.Groups.Single(g => g.CanonicalName == "光年之外").IsLowConfidence);
        Assert.True(plan.Groups.Single(g => g.CanonicalName == "Guessed").IsLowConfidence); // it was changed
    }

    [Fact]
    public void ValuesTheAnswersCallOneAlbumAreOneGroupWhateverTheyLookLike()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("A", "1.flac", "Palette [Hi-Res 24bit-96khz]"),
            Song("B", "1.flac", "Palette"),
            Song("C", "1.flac", "팔레트"),
        };

        AlbumPlan plan = Plan(songs, Answers(("Palette [Hi-Res 24bit-96khz]", "Palette", false), ("Palette", "Palette", false), ("팔레트", "Palette", true)));

        AlbumGroup group = Assert.Single(plan.Groups);
        Assert.Equal("Palette", group.CanonicalName);
        Assert.Equal(3, group.Variants.Count);
        Assert.True(group.IsLowConfidence); // one of the answers was unsure
    }

    [Fact]
    public void AlbumsAreGroupedByNameNotByAlbumArtist()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("OST", "1.flac", "Crash Landing on You OST", "Ha Sung-woon", "Various Artists"),
            Song("OST", "2.flac", "Crash Landing on You OST", "IU", "IU"),
        };

        AlbumPlan plan = Plan(songs, Answers(("Crash Landing on You OST", "Crash Landing on You OST", false)));

        Assert.Equal(2, Assert.Single(plan.Groups).SongCount);
    }

    [Fact]
    public void ADifferentNameIsADifferentAlbum()
    {
        var songs = new List<SongAlbumTags> { Song("A", "1.flac", "HEAT"), Song("B", "1.flac", "HEAT (Clean)") };

        AlbumPlan plan = Plan(songs, Answers(("HEAT", "HEAT", false), ("HEAT (Clean)", "HEAT (Clean)", false)));

        Assert.Equal(2, plan.Groups.Count);
    }

    [Fact]
    public void AValueWithoutAnAnswerIsKeptAsItIsAndMarkedPleaseCheck()
    {
        var songs = new List<SongAlbumTags> { Song("A", "1.flac", "Mystery [FLAC]") };

        AlbumPlan plan = Plan(songs, new Dictionary<string, ParsedAlbum>());

        AlbumGroup group = Assert.Single(plan.Groups);
        Assert.Equal("Mystery", group.CanonicalName);
        Assert.True(group.IsLowConfidence);
    }

    [Fact]
    public void AValueThatIsNoAlbumNameLeavesTheSongOut()
    {
        var songs = new List<SongAlbumTags> { Song("A", "1.flac", "Unknown"), Song("B", "1.flac", "Real") };

        AlbumPlan plan = Plan(songs, Answers(("Unknown", "", false), ("Real", "Real", false)));

        Assert.Equal("Real", Assert.Single(plan.Groups).CanonicalName);
        Assert.Null(plan.GroupOf(songs[0]));
        Assert.Single(plan.Warnings);
        Assert.Empty(AlbumStandardizer.ComputeUpdates(plan).Where(u => u.AudioPath == songs[0].Path));
    }

    [Fact]
    public void TheGroupsComeBiggestFirst()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("Small", "1.flac", "Small"),
            Song("Big", "1.flac", "Big"), Song("Big", "2.flac", "Big"), Song("Big", "3.flac", "Big"),
        };

        AlbumPlan plan = Plan(songs, Answers(("Small", "Small", false), ("Big", "Big", false)));

        Assert.Equal(new[] { "Big", "Small" }, plan.Groups.Select(g => g.CanonicalName).ToArray());
    }

    // ---- what changes in the tags

    [Fact]
    public void OnlyTheTagsThatDifferAreUpdated()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("Palette [Hi-Res 24bit-96khz]", "1.flac", "Palette [Hi-Res 24bit-96khz]"),
            Song("Palette", "1.flac", "Palette"),
            Song("Folder", "1.flac", null), // no tag: the folder name is the album
        };
        AlbumPlan plan = Plan(songs, Answers(("Palette [Hi-Res 24bit-96khz]", "Palette", false), ("Palette", "Palette", false), ("Folder", "Folder", false)));

        List<SongAlbumUpdate> updates = AlbumStandardizer.ComputeUpdates(plan);

        Assert.Equal(2, updates.Count);
        Assert.Equal(songs[0].Path, updates[0].AudioPath);
        Assert.Equal("Palette", updates[0].Album);
        Assert.Equal("Palette [Hi-Res 24bit-96khz]", updates[0].Before);
        Assert.Equal(songs[2].Path, updates[1].AudioPath);
        Assert.Equal("Folder", updates[1].Album);
        Assert.Null(updates[1].Before);
    }

    [Fact]
    public void OnlyTheSongsInTheScopeAreUpdatedButTheNamesComeFromEverySong()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("A", "1.flac", "Palette [Hi-Res 24bit-96khz]"),
            Song("B", "1.flac", "Palette [Hi-Res 24bit-96khz]"),
            Song("C", "1.flac", "Palette"),
        };
        AlbumPlan plan = Plan(songs, Answers(("Palette [Hi-Res 24bit-96khz]", "Palette", false), ("Palette", "Palette", false)), new[] { songs[1].Path });

        List<SongAlbumUpdate> updates = AlbumStandardizer.ComputeUpdates(plan);

        Assert.Equal(songs[1].Path, Assert.Single(updates).AudioPath);
        Assert.Equal(1, plan.ScopedSongCount);
    }

    [Fact]
    public void AnEditedNameChangesWhatIsUpdated()
    {
        var songs = new List<SongAlbumTags> { Song("Palette", "1.flac", "Palette") };
        AlbumPlan plan = Plan(songs, Answers(("Palette", "Palette", false)));
        Assert.Empty(AlbumStandardizer.ComputeUpdates(plan));

        plan.Groups[0].CanonicalName = "PALETTE";

        SongAlbumUpdate update = Assert.Single(AlbumStandardizer.ComputeUpdates(plan));
        Assert.Equal("PALETTE", update.Album);
    }

    // ---- what moves

    [Fact]
    public void AFolderWhoseSongsAreOneAlbumIsRenamedAsAWhole()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("Palette [Hi-Res 24bit-96khz]", "1.flac", "Palette"),
            Song("Palette [Hi-Res 24bit-96khz]", "2.flac", "Palette"),
        };
        AlbumPlan plan = Plan(songs, Answers(("Palette", "Palette", false)));

        AlbumFolderMove move = Assert.Single(AlbumStandardizer.ComputeFolderMoves(plan));

        Assert.Equal(Path.Combine(Root, "Palette [Hi-Res 24bit-96khz]"), move.SourceDirectory);
        Assert.Equal("Palette", move.TargetName);
        Assert.True(move.IsWholeFolder);
        Assert.Equal(songs.Select(s => s.Path).ToArray(), move.AudioPaths.ToArray());
    }

    [Fact]
    public void NewSongsJoinTheFolderTheAlbumAlreadyLivesInWhateverItIsCalled()
    {
        // the library has the album in "5-STAR [Hi-Res]" and those songs may not change now; the new ones were just copied
        var songs = new List<SongAlbumTags>
        {
            Song("5-STAR [Hi-Res]", "Stray Kids - Hall of Fame.flac", "5-STAR"),
            Song("5-STAR [Hi-Res]", "Stray Kids - S-Class.flac", "5-STAR"),
            Song("Stray Kids - 5-STAR", "03 - ITEM.flac", "5-STAR"),
            Song("Stray Kids - 5-STAR", "04 - Super Bowl.flac", "5-STAR"),
        };
        AlbumPlan plan = Plan(songs, Answers(("5-STAR", "5-STAR", false)), new[] { songs[2].Path, songs[3].Path });

        AlbumFolderMove move = Assert.Single(AlbumStandardizer.ComputeFolderMoves(plan));

        Assert.Equal("5-STAR [Hi-Res]", move.TargetName);
        Assert.Equal(Path.Combine(Root, "Stray Kids - 5-STAR"), move.SourceDirectory);
        Assert.True(move.IsWholeFolder);
    }

    [Fact]
    public void AnAlbumRenamedToTheNameOfAnotherOneJoinsThatOnesFolder()
    {
        // "SOUR" lives in "SOUR [Hi-Res]" and may not change; the new song's album tag is a playlist name
        var songs = new List<SongAlbumTags>
        {
            Song("SOUR [Hi-Res]", "Olivia Rodrigo - brutal.flac", "SOUR"),
            Song("SOUR [Hi-Res]", "Olivia Rodrigo - traitor.flac", "SOUR"),
            Song("New songs", "Olivia Rodrigo - good 4 u.flac", "Bittersweet Indie (Explicit)"),
        };
        AlbumPlan plan = Plan(songs, Answers(("SOUR", "SOUR", false), ("Bittersweet Indie (Explicit)", "Bittersweet Indie (Explicit)", true)),
                new[] { songs[2].Path });

        Assert.Equal("Bittersweet Indie (Explicit)", Assert.Single(AlbumStandardizer.ComputeFolderMoves(plan)).TargetName);

        plan.GroupOf(songs[2]).CanonicalName = "SOUR"; // what the user does in the review

        AlbumFolderMove move = Assert.Single(AlbumStandardizer.ComputeFolderMoves(plan));
        Assert.Equal("SOUR [Hi-Res]", move.TargetName);
        Assert.Equal(Path.Combine(Root, "New songs"), move.SourceDirectory);
    }

    [Fact]
    public void AnAlbumRenamedToTheNameOfAnotherOneIsNotTakenForItsFolderWhenTheAlbumsAreMixedInOneFolder()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("Mixed", "a.flac", "SOUR"),
            Song("Mixed", "b.flac", "Other"),
            Song("Mixed", "c.flac", "Other"),
            Song("New songs", "d.flac", "Playlist"),
        };
        AlbumPlan plan = Plan(songs, Answers(("SOUR", "SOUR", false), ("Other", "Other", false), ("Playlist", "Playlist", true)),
                new[] { songs[3].Path });
        plan.GroupOf(songs[3]).CanonicalName = "SOUR";

        Assert.Equal("SOUR", Assert.Single(AlbumStandardizer.ComputeFolderMoves(plan)).TargetName); // "Mixed" is mostly another album
    }

    [Fact]
    public void WhenEverySongMayChangeTheFoldersAreNamedAfterTheAlbum()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("5-STAR [Hi-Res]", "Stray Kids - Hall of Fame.flac", "5-STAR"),
            Song("Stray Kids - 5-STAR", "03 - ITEM.flac", "5-STAR"),
        };

        List<AlbumFolderMove> moves = AlbumStandardizer.ComputeFolderMoves(Plan(songs, Answers(("5-STAR", "5-STAR", false))));

        Assert.Equal(new[] { "5-STAR", "5-STAR" }, moves.Select(m => m.TargetName).ToArray());
    }

    [Fact]
    public void AFolderThatIsMostlyAnotherAlbumIsNotTheAlbumsHome()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("Mixed", "a.flac", "5-STAR"),
            Song("Mixed", "b.flac", "Other"),
            Song("Mixed", "c.flac", "Other"),
            Song("Stray Kids - 5-STAR", "03 - ITEM.flac", "5-STAR"),
        };
        AlbumPlan plan = Plan(songs, Answers(("5-STAR", "5-STAR", false), ("Other", "Other", false)), new[] { songs[3].Path });

        AlbumFolderMove move = Assert.Single(AlbumStandardizer.ComputeFolderMoves(plan));

        Assert.Equal("5-STAR", move.TargetName);
    }

    [Fact]
    public void ASongAlreadyInTheAlbumsHomeFolderDoesNotMove()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("5-STAR [Hi-Res]", "Stray Kids - Hall of Fame.flac", "5-STAR"),
            Song("5-STAR [Hi-Res]", "Stray Kids - S-Class.flac", "5-STAR"),
        };
        AlbumPlan plan = Plan(songs, Answers(("5-STAR", "5-STAR", false)), new[] { songs[1].Path });

        Assert.Empty(AlbumStandardizer.ComputeFolderMoves(plan));
    }

    [Fact]
    public void AFolderThatIsNamedRightIsLeftAlone()
    {
        var songs = new List<SongAlbumTags> { Song("Palette", "1.flac", "Palette") };

        Assert.Empty(AlbumStandardizer.ComputeFolderMoves(Plan(songs, Answers(("Palette", "Palette", false)))));
    }

    [Fact]
    public void TheSongsOfOneAlbumInTwoFoldersAreMovedTogether()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("HEAT", "1.flac", "HEAT"),
            Song("HEAT (Clean)", "1.flac", "HEAT (Clean)"),
        };
        AlbumPlan plan = Plan(songs, Answers(("HEAT", "HEAT", false), ("HEAT (Clean)", "HEAT", false)));

        AlbumFolderMove move = Assert.Single(AlbumStandardizer.ComputeFolderMoves(plan));

        Assert.Equal(Path.Combine(Root, "HEAT (Clean)"), move.SourceDirectory);
        Assert.Equal("HEAT", move.TargetName);
        Assert.True(move.IsWholeFolder);
    }

    [Fact]
    public void AFolderOfSeveralAlbumsMovesItsSongsOneByOne()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("Mixed", "1.flac", "One"),
            Song("Mixed", "2.flac", "Two"),
            Song("Mixed", "3.flac", "Two"),
        };
        AlbumPlan plan = Plan(songs, Answers(("One", "One", false), ("Two", "Two", false)));

        List<AlbumFolderMove> moves = AlbumStandardizer.ComputeFolderMoves(plan);

        Assert.Equal(2, moves.Count);
        Assert.All(moves, move => Assert.False(move.IsWholeFolder));
        Assert.Equal(new[] { "One", "Two" }, moves.Select(m => m.TargetName).OrderBy(n => n).ToArray());
        Assert.Equal(2, moves.Single(m => m.TargetName == "Two").AudioPaths.Count);
    }

    [Fact]
    public void AFolderIsNotRenamedAsAWholeWhenOnlySomeOfItsSongsAreInTheScope()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("Palette [FLAC]", "1.flac", "Palette"),
            Song("Palette [FLAC]", "2.flac", "Other"), // another album, and not for changing now
        };
        AlbumPlan plan = Plan(songs, Answers(("Palette", "Palette", false), ("Other", "Other", false)), new[] { songs[0].Path });

        AlbumFolderMove move = Assert.Single(AlbumStandardizer.ComputeFolderMoves(plan));

        Assert.False(move.IsWholeFolder);
        Assert.Equal(songs[0].Path, Assert.Single(move.AudioPaths));
    }

    [Fact]
    public void ASongThatStaysPutKeepsItsFolderFromBeingRenamedAsAWhole()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("Palette", "1.flac", "Palette"),
            Song("Palette", "2.flac", "Other"), // belongs elsewhere: the folder is not "the album" as a whole
        };
        AlbumPlan plan = Plan(songs, Answers(("Palette", "Palette", false), ("Other", "Other", false)));

        AlbumFolderMove move = Assert.Single(AlbumStandardizer.ComputeFolderMoves(plan));

        Assert.Equal("Other", move.TargetName);
        Assert.False(move.IsWholeFolder);
    }

    [Fact]
    public void SongsDirectlyInTheOpenedFolderAreNotMoved()
    {
        var songs = new List<SongAlbumTags> { Song(null, "1.flac", "Palette"), Song(null, "2.flac", "Palette") };

        Assert.Empty(AlbumStandardizer.ComputeFolderMoves(Plan(songs, Answers(("Palette", "Palette", false)))));
    }

    [Fact]
    public void ACaseOnlyDifferenceIsAMove()
    {
        var songs = new List<SongAlbumTags> { Song("Lost And Found", "1.flac", "Lost And Found") };
        AlbumPlan plan = Plan(songs, Answers(("Lost And Found", "Lost and Found", false)));

        AlbumFolderMove move = Assert.Single(AlbumStandardizer.ComputeFolderMoves(plan));

        Assert.Equal("Lost and Found", move.TargetName);
    }

    [Fact]
    public void ANameWithNothingLeftForAFolderMovesNothing()
    {
        var songs = new List<SongAlbumTags> { Song("Folder", "1.flac", "???") };

        Assert.Empty(AlbumStandardizer.ComputeFolderMoves(Plan(songs, Answers(("???", "???", false)))));
    }

    [Fact]
    public void TheFolderNameIsTheAlbumNameFitForAFolder()
    {
        var songs = new List<SongAlbumTags> { Song("Old", "1.flac", "AC/DC: Live?") };
        AlbumPlan plan = Plan(songs, Answers(("AC/DC: Live?", "AC/DC: Live?", false)));

        Assert.Equal("AC-DC Live", Assert.Single(AlbumStandardizer.ComputeFolderMoves(plan)).TargetName);
    }

    // ---- the folder name

    [Theory]
    [InlineData("Love poem", "Love poem")]
    [InlineData("AC/DC", "AC-DC")]
    [InlineData("What?", "What")]
    [InlineData("Dots...", "Dots")]
    [InlineData("  Spaces  ", "Spaces")]
    [InlineData("CON", "CON_")]
    [InlineData("com1", "com1_")]
    [InlineData("TWICEcoaster : LANE 1", "TWICEcoaster LANE 1")]
    [InlineData("???", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void TheFolderNameIsMadeFit(string album, string expected)
    {
        Assert.Equal(expected, AlbumFolderName.Clean(album));
    }

    [Fact]
    public void SamePathIgnoresCaseAndAClosingSeparator()
    {
        Assert.True(AlbumStandardizer.SamePath(@"C:\Lib\Palette", @"c:\lib\palette\"));
        Assert.False(AlbumStandardizer.SamePath(@"C:\lib\a", @"C:\lib\b"));
        Assert.False(AlbumStandardizer.SamePath(null, @"C:\lib"));
        Assert.False(AlbumStandardizer.SamePath(@"C:\lib", ""));
    }
}
