using MusicManager.Entity;
using MusicManager.UseCase.StandardizeAlbums;
using MusicManager.View.Library;
using Xunit;

namespace MusicManager.Tests.View.Library;

/// <summary>
/// The choices the album box offers while a name is typed (each says the folder the songs go into), what typing or picking
/// one does to the album, and the note under an album that goes into a folder with another name than its own. The paths are
/// made up and nothing is opened.
/// </summary>
public class AlbumSuggestionsTest
{
    private const string Root = @"C:\lib";

    private static SongAlbumTags Song(string folder, string file, string album)
    {
        return new SongAlbumTags(Path.Combine(Root, folder, file), album, new List<string> { "IU" }, new List<string>(), file);
    }

    private static Dictionary<string, ParsedAlbum> Answers(params (string Raw, string Album)[] answers)
    {
        return answers.ToDictionary(a => a.Raw, a => new ParsedAlbum(a.Album, false), StringComparer.Ordinal);
    }

    /// <summary>"SOUR" lives in "SOUR [Hi-Res]" and may not change; a new song comes with a playlist name as its album</summary>
    private static (AlbumPlan Plan, AlbumGroup Playlist, List<SongAlbumTags> Songs) SourLibrary()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("SOUR [Hi-Res]", "brutal.flac", "SOUR"),
            Song("SOUR [Hi-Res]", "traitor.flac", "SOUR"),
            Song("Palette", "Palette.flac", "Palette"),
            Song("New songs", "good 4 u.flac", "Bittersweet Indie (Explicit)"),
        };
        AlbumPlan plan = AlbumStandardizer.BuildPlan(songs,
                Answers(("SOUR", "SOUR"), ("Palette", "Palette"), ("Bittersweet Indie (Explicit)", "Bittersweet Indie (Explicit)")),
                true, Root, new[] { songs[3].Path });
        return (plan, plan.GroupOf(songs[3]), songs);
    }

    // ---- the choices

    [Fact]
    public void TypingSourOffersAFolderNamedSourAndTheAlbumThatLivesInTheOtherOne()
    {
        var (plan, playlist, _) = SourLibrary();

        List<AlbumSuggestion> suggestions = AlbumSuggestions.For(plan, playlist, "SOUR");

        Assert.Equal(2, suggestions.Count);
        // what was typed comes first, and goes into a folder with that name
        Assert.Equal("SOUR", suggestions[0].Name);
        Assert.Equal("SOUR", suggestions[0].Folder);
        Assert.Equal("as typed", suggestions[0].Detail);
        Assert.False(suggestions[0].IsOwn);
        // the album the library has goes into the folder it is in
        Assert.Equal("SOUR", suggestions[1].Name);
        Assert.Equal("SOUR [Hi-Res]", suggestions[1].Folder);
        Assert.Equal("folder \"SOUR [Hi-Res]\"", suggestions[1].Detail); // the folder is not the name: it is said
    }

    [Fact]
    public void WithNothingTypedTheAlbumsOwnNamesComeFirstThenTheOtherAlbums()
    {
        var (plan, playlist, _) = SourLibrary();

        List<AlbumSuggestion> suggestions = AlbumSuggestions.For(plan, playlist, "");

        Assert.Equal(new[] { "Bittersweet Indie (Explicit)", "SOUR", "Palette" }, suggestions.Select(s => s.Name).ToArray());
        Assert.Equal("", suggestions[0].Detail); // nothing to add to the name
        Assert.True(suggestions[0].IsOwn);
        Assert.Equal("folder \"SOUR [Hi-Res]\"", suggestions[1].Detail);
        Assert.Equal("", suggestions[2].Detail);
    }

    [Fact]
    public void TheNameInTheBoxIsTheCurrentNameWhenTheBoxGetsTheFocus()
    {
        // the box is not empty: it has the name in it, and the choices are those that fit it
        var (plan, playlist, _) = SourLibrary();

        AlbumSuggestion only = Assert.Single(AlbumSuggestions.For(plan, playlist, "Bittersweet Indie (Explicit)"));

        Assert.Equal("as typed", only.Detail);
        Assert.True(only.IsOwn);
    }

    [Fact]
    public void TheSpellingsTheSongsHaveAreOfferedToo()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("a", "1.flac", "Palette [Hi-Res 24bit-96khz]"),
            Song("a", "2.flac", "Palette [Hi-Res 24bit-96khz]"),
            Song("b", "3.flac", "PALETTE"),
        };
        AlbumPlan plan = AlbumStandardizer.BuildPlan(songs,
                Answers(("Palette [Hi-Res 24bit-96khz]", "Palette"), ("PALETTE", "Palette")), true, Root, null);
        AlbumGroup group = plan.Groups.Single();

        List<AlbumSuggestion> suggestions = AlbumSuggestions.For(plan, group, "");

        Assert.Equal(new[] { "Palette", "Palette [Hi-Res 24bit-96khz]", "PALETTE" }, suggestions.Select(s => s.Name).ToArray());
        Assert.All(suggestions, s => Assert.Equal("", s.Detail));
        Assert.All(suggestions, s => Assert.True(s.IsOwn));
    }

    [Fact]
    public void AnAlbumDoesNotOfferItselfAsAnotherAlbum()
    {
        var (plan, _, songs) = SourLibrary();
        AlbumGroup sour = plan.GroupOf(songs[0]);

        AlbumSuggestion only = Assert.Single(AlbumSuggestions.For(plan, sour, "SOUR"));

        Assert.True(only.IsOwn);
        Assert.Equal("as typed", only.Detail);
    }

    [Fact]
    public void AnAlbumRenamedByTheProgramToAnotherOnesNameGoesIntoThatOnesFolderAndSaysSo()
    {
        var (plan, playlist, _) = SourLibrary();
        playlist.CanonicalName = "SOUR"; // the name is SOUR, the folder is not chosen: the program files it where SOUR lives

        List<AlbumSuggestion> suggestions = AlbumSuggestions.For(plan, playlist, "SOUR");

        Assert.Equal(2, suggestions.Count);
        Assert.Equal("SOUR", suggestions[0].Folder); // what is typed
        Assert.Equal("folder \"SOUR [Hi-Res]\"", suggestions[1].Detail);
        Assert.True(suggestions[1].IsOwn);
    }

    [Fact]
    public void AFolderTheUserChoseIsTheFolderOfTheCurrentName()
    {
        var (plan, playlist, _) = SourLibrary();
        playlist.CanonicalName = "SOUR";
        playlist.FolderName = "SOUR"; // typed by the user

        List<AlbumSuggestion> suggestions = AlbumSuggestions.For(plan, playlist, "SOUR");

        Assert.Equal(2, suggestions.Count);
        Assert.Equal("as typed", suggestions[0].Detail); // what is typed is the current name: one row
        Assert.Equal("SOUR [Hi-Res]", suggestions[1].Folder);
    }

    [Theory]
    [InlineData("sour hires")]
    [InlineData("hi-res")]
    [InlineData("sou")]
    [InlineData("OUR")]
    public void TheMatchIgnoresCaseAndPunctuationAndLooksAtTheFolderToo(string typed)
    {
        var (plan, playlist, _) = SourLibrary();

        Assert.Contains(AlbumSuggestions.For(plan, playlist, typed), s => s.Name == "SOUR" && s.Folder == "SOUR [Hi-Res]");
    }

    [Fact]
    public void WhatWasTypedIsOfferedEvenWhenNothingMatches()
    {
        var (plan, playlist, _) = SourLibrary();

        AlbumSuggestion only = Assert.Single(AlbumSuggestions.For(plan, playlist, "zzz"));

        Assert.Equal("zzz", only.Name);
        Assert.Equal("zzz", only.Folder);
    }

    [Fact]
    public void WhatWasTypedComesFirstThenTheBestMatch()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("a", "1.flac", "Love Yourself"),
            Song("a", "2.flac", "Love Yourself"),
            Song("a", "3.flac", "Love Yourself"),
            Song("b", "4.flac", "Yourself"),
            Song("c", "5.flac", "Self Love"),
            Song("d", "6.flac", "Elf"),
        };
        AlbumPlan plan = AlbumStandardizer.BuildPlan(songs,
                Answers(("Love Yourself", "Love Yourself"), ("Yourself", "Yourself"), ("Self Love", "Self Love"), ("Elf", "Elf")),
                true, Root, new[] { songs[5].Path });
        AlbumGroup editing = plan.GroupOf(songs[5]);

        List<AlbumSuggestion> suggestions = AlbumSuggestions.For(plan, editing, "yourself");

        // as typed, then the name that is the same, then the ones that have it inside
        Assert.Equal(new[] { "yourself", "Yourself", "Love Yourself" }, suggestions.Select(s => s.Name).ToArray());
    }

    [Fact]
    public void NoMoreThanTheMaximumIsShown()
    {
        var songs = new List<SongAlbumTags>();
        var answers = new List<(string, string)>();
        for (int i = 0; i < 30; i++)
        {
            songs.Add(Song("f" + i, "1.flac", "Album " + i));
            answers.Add(("Album " + i, "Album " + i));
        }
        AlbumPlan plan = AlbumStandardizer.BuildPlan(songs, Answers(answers.ToArray()), true, Root, null);

        Assert.Equal(AlbumSuggestions.MaxShown, AlbumSuggestions.For(plan, plan.Groups[0], "").Count);
        Assert.Equal(AlbumSuggestions.MaxShown, AlbumSuggestions.For(plan, plan.Groups[0], "album").Count);
    }

    [Fact]
    public void WhenEverySongMayChangeNoLibraryIsMentionedAndTheSameChoiceIsOneRow()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("a", "1.flac", "One"),
            Song("a", "2.flac", "One"),
            Song("b", "3.flac", "Two"),
        };
        AlbumPlan plan = AlbumStandardizer.BuildPlan(songs, Answers(("One", "One"), ("Two", "Two")), true, Root, null);

        AlbumSuggestion one = Assert.Single(AlbumSuggestions.For(plan, plan.GroupOf(songs[2]), "One"));

        Assert.Equal("as typed", one.Detail);
    }

    [Fact]
    public void AnAlbumInAFolderUnderAnotherFolderCannotBeJoinedSoItsRowNamesANewFolder()
    {
        var songs = new List<SongAlbumTags>
        {
            new SongAlbumTags(Path.Combine(Root, "IU", "Palette [FLAC]", "1.flac"), "Palette", new List<string> { "IU" }, null, "1"),
            new SongAlbumTags(Path.Combine(Root, "IU", "Palette [FLAC]", "2.flac"), "Palette", new List<string> { "IU" }, null, "2"),
            new SongAlbumTags(Path.Combine(Root, "New songs", "3.flac"), "Playlist", new List<string> { "IU" }, null, "3"),
        };
        AlbumPlan plan = AlbumStandardizer.BuildPlan(songs, Answers(("Palette", "Palette"), ("Playlist", "Playlist")), true, Root,
                new[] { songs[2].Path });

        // the folder of Palette is not next to the folder of the new song: the song cannot join it, so no row says it does
        AlbumSuggestion palette = Assert.Single(AlbumSuggestions.For(plan, plan.GroupOf(songs[2]), "Palette"));

        Assert.Equal("Palette", palette.Folder);
    }

    // ---- what typing or picking does to the album

    [Fact]
    public void ATypedNameHasAFolderNamedAfterItEvenWhenTheAlbumLivesInAnotherFolder()
    {
        var (plan, playlist, songs) = SourLibrary();

        string shown = AlbumNameChoice.Apply(playlist, " SOUR ", null, out bool changed);

        Assert.Equal("SOUR", shown);
        Assert.True(changed);
        Assert.Equal("SOUR", playlist.FolderName);
        // the songs go into a folder named SOUR, not into SOUR [Hi-Res]
        Assert.Equal("SOUR", Assert.Single(AlbumStandardizer.ComputeFolderMoves(plan)).TargetName);
        Assert.Equal(songs[3].Path, Assert.Single(AlbumStandardizer.ComputeFolderMoves(plan).Single().AudioPaths));
    }

    [Fact]
    public void ThePickedRowOfTheAlbumThatLivesInAFolderPutsTheSongsInThatFolder()
    {
        var (plan, playlist, _) = SourLibrary();
        AlbumSuggestion picked = AlbumSuggestions.For(plan, playlist, "SOUR").Single(s => s.Folder == "SOUR [Hi-Res]");

        AlbumNameChoice.Apply(playlist, picked.Name, picked.Folder, out bool changed);

        Assert.True(changed);
        Assert.Equal("SOUR", playlist.CanonicalName);
        Assert.Equal("SOUR [Hi-Res]", Assert.Single(AlbumStandardizer.ComputeFolderMoves(plan)).TargetName);
    }

    [Fact]
    public void LeavingTheBoxWithoutEditingChangesNothing()
    {
        var (plan, playlist, _) = SourLibrary();
        playlist.CanonicalName = "SOUR"; // by the program: it goes where SOUR lives

        AlbumNameChoice.Apply(playlist, "SOUR", null, out bool changed);

        Assert.False(changed);
        Assert.Null(playlist.FolderName); // the program's choice stands
        Assert.Equal("SOUR [Hi-Res]", Assert.Single(AlbumStandardizer.ComputeFolderMoves(plan)).TargetName);
    }

    [Fact]
    public void PickingTheRowOfTheCurrentNameWithAnotherFolderChangesTheFolder()
    {
        var (plan, playlist, _) = SourLibrary();
        playlist.CanonicalName = "SOUR";

        AlbumNameChoice.Apply(playlist, "SOUR", "SOUR", out bool changed);

        Assert.True(changed);
        Assert.Equal("SOUR", Assert.Single(AlbumStandardizer.ComputeFolderMoves(plan)).TargetName);
    }

    [Fact]
    public void AnEmptyBoxKeepsTheOldNameAndFolder()
    {
        var (_, playlist, _) = SourLibrary();
        playlist.FolderName = "Kept";

        string shown = AlbumNameChoice.Apply(playlist, "   ", null, out bool changed);

        Assert.Equal("Bittersweet Indie (Explicit)", shown);
        Assert.False(changed);
        Assert.Equal("Kept", playlist.FolderName);
    }

    [Fact]
    public void ATypedNameThatMakesNoFolderNameLeavesTheFolderToTheProgram()
    {
        var (_, playlist, _) = SourLibrary();

        AlbumNameChoice.Apply(playlist, "???", null, out bool changed);

        Assert.True(changed);
        Assert.Equal("???", playlist.CanonicalName);
        Assert.Null(playlist.FolderName);
    }

    // ---- the note under the album

    [Fact]
    public void AnAlbumTheProgramPutsIntoAnotherFoldersNameSaysSo()
    {
        var (plan, playlist, _) = SourLibrary();
        playlist.CanonicalName = "SOUR";

        Dictionary<string, string> notes = AlbumFolderNotes.For(plan, AlbumStandardizer.ComputeFolderMoves(plan));

        Assert.Equal("Goes into the folder \"SOUR [Hi-Res]\", where this album is already", notes[playlist.Key]);
    }

    [Fact]
    public void AFolderTheUserChoseNeedsNoNote()
    {
        var (plan, playlist, _) = SourLibrary();
        AlbumNameChoice.Apply(playlist, "SOUR", "SOUR [Hi-Res]", out _);

        Assert.Equal("SOUR [Hi-Res]", Assert.Single(AlbumStandardizer.ComputeFolderMoves(plan)).TargetName);
        Assert.Empty(AlbumFolderNotes.For(plan, AlbumStandardizer.ComputeFolderMoves(plan)));
    }

    [Fact]
    public void AnAlbumThatGoesIntoTheFolderOfItsNameHasNoNote()
    {
        var (plan, playlist, _) = SourLibrary(); // "Bittersweet Indie (Explicit)" makes a folder of that name

        Assert.Empty(AlbumFolderNotes.For(plan, AlbumStandardizer.ComputeFolderMoves(plan)));
        Assert.Equal("Bittersweet Indie (Explicit)", Assert.Single(AlbumStandardizer.ComputeFolderMoves(plan)).TargetName);
        Assert.NotNull(playlist);
    }

    [Fact]
    public void WithNoMovesThereAreNoNotes()
    {
        var (plan, _, _) = SourLibrary();

        Assert.Empty(AlbumFolderNotes.For(plan, new List<AlbumFolderMove>()));
    }

    [Fact]
    public void TheNameOfAHomeFolderIsFoundByTheNameOfTheAlbum()
    {
        var (plan, _, _) = SourLibrary();
        Dictionary<string, string> homes = AlbumStandardizer.HomeFolders(plan);

        Assert.Equal(Path.Combine(Root, "SOUR [Hi-Res]"), AlbumStandardizer.HomeFolderOfName(homes, "sour"));
        Assert.Equal(Path.Combine(Root, "SOUR [Hi-Res]"), AlbumStandardizer.HomeFolderOfName(homes, " SOUR "));
        Assert.Null(AlbumStandardizer.HomeFolderOfName(homes, "Nothing"));
        Assert.Null(AlbumStandardizer.HomeFolderOfName(homes, ""));
        Assert.Null(AlbumStandardizer.HomeFolderOfName(homes, null));
    }
}
