using MusicManager.Entity;
using MusicManager.UseCase.StandardizeAlbums;
using MusicManager.UseCase.StandardizeArtists;
using MusicManager.View.Library;
using Xunit;

namespace MusicManager.Tests.View.Library;

/// <summary>
/// The folder tree of the review: what the library's folders look like after the album moves and the renames. The paths are
/// made up and nothing is opened; the folders "that are in the library" are told to the preview.
/// </summary>
public class FolderPreviewTest
{
    private const string Root = @"C:\lib";

    private static string P(string folder, string file)
    {
        return folder == null ? Path.Combine(Root, file) : Path.Combine(Root, folder, file);
    }

    private static SongAlbumTags Song(string folder, string file, string album)
    {
        return new SongAlbumTags(P(folder, file), album, new List<string> { "IU" }, new List<string>(), file);
    }

    private static Dictionary<string, ParsedAlbum> Answers(params (string Raw, string Album)[] answers)
    {
        return answers.ToDictionary(a => a.Raw, a => new ParsedAlbum(a.Album, false), StringComparer.Ordinal);
    }

    private static FolderPreview Preview(List<SongAlbumTags> songs, Dictionary<string, ParsedAlbum> parsed,
            IEnumerable<string> scope = null, IEnumerable<string> newPaths = null, IEnumerable<SongRename> renames = null,
            params string[] existing)
    {
        AlbumPlan plan = AlbumStandardizer.BuildPlan(songs, parsed, true, Root, scope);
        var folders = new HashSet<string>(existing.Select(name => Path.Combine(Root, name)), StringComparer.OrdinalIgnoreCase);
        return FolderPreview.Build(plan, AlbumStandardizer.ComputeFolderMoves(plan), newPaths, renames, folders.Contains);
    }

    private static FolderNode Child(FolderNode node, string name)
    {
        return Assert.Single(node.Children, child => child.Name == name);
    }

    [Fact]
    public void AFolderThatIsRenamedAsAWholeShowsItsNewNameAndItsSongs()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("Bittersweet Indie (Explicit)", "1.flac", "SOUR"),
            Song("Bittersweet Indie (Explicit)", "2.flac", "SOUR"),
        };

        FolderPreview preview = Preview(songs, Answers(("SOUR", "SOUR")), null, null, null, "Bittersweet Indie (Explicit)");

        FolderNode folder = Child(preview.Root, "SOUR");
        Assert.Equal("renamed from \"Bittersweet Indie (Explicit)\"", folder.Detail);
        Assert.Equal(FolderTone.Changed, folder.Tone);
        FolderNode note = Assert.Single(folder.Children);
        Assert.Equal(FolderNodeKind.Note, note.Kind);
        Assert.Equal("2 songs in this folder", note.Name);
        Assert.Equal(1, preview.FoldersChanged);
        Assert.Equal(2, preview.SongsMoved);
        Assert.Equal("lib", preview.Root.Name);
        Assert.Equal(FolderNodeKind.Library, preview.Root.Kind);
    }

    [Fact]
    public void ANewSongThatJoinsAFolderTheLibraryHasIsShownInThatFolder()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("SOUR", "brutal.flac", "SOUR"),
            Song("SOUR", "traitor.flac", "SOUR"),
            Song("New songs", "good 4 u.flac", "SOUR"),
        };
        string added = songs[2].Path;

        FolderPreview preview = Preview(songs, Answers(("SOUR", "SOUR")), new[] { added }, new[] { added }, null, "SOUR");

        FolderNode sour = Child(preview.Root, "SOUR");
        Assert.Equal("1 song added", sour.Detail);
        FolderNode row = Child(sour, "good 4 u.flac");
        Assert.Equal("new song", row.Detail); // the folder it was copied into is not in the library: it is not mentioned
        Assert.Equal(FolderNodeKind.Song, row.Kind);
        Assert.Equal("2 other songs in this folder", Child(sour, "2 other songs in this folder").Name);
        FolderNode left = Child(preview.Root, "New songs");
        Assert.Equal("no songs left", left.Detail);
        Assert.Equal(FolderTone.Gone, left.Tone);
        Assert.Equal(1, preview.NewSongs);
        Assert.Equal(2, preview.FoldersChanged);
    }

    [Fact]
    public void FoldersThatDoNotExistYetAreNewFolders()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("Mixed", "1.flac", "One"),
            Song("Mixed", "2.flac", "Two"),
            Song("Mixed", "3.flac", "Two"),
        };

        FolderPreview preview = Preview(songs, Answers(("One", "One"), ("Two", "Two")));

        Assert.Equal("new folder", Child(preview.Root, "One").Detail);
        FolderNode two = Child(preview.Root, "Two");
        Assert.Equal("new folder", two.Detail);
        Assert.Equal(new[] { "2.flac", "3.flac" }, two.Children.Where(c => c.Kind == FolderNodeKind.Song).Select(c => c.Name).ToArray());
        Assert.Equal("moved from \"Mixed\"", Child(two, "2.flac").Detail);
        Assert.Equal("no songs left", Child(preview.Root, "Mixed").Detail);
        Assert.Equal(3, preview.SongsMoved);
    }

    [Fact]
    public void TheFolderOfTheEditedNameIsWhereTheSongsAreShown()
    {
        // the album tag is a playlist name; the user renames the album in the review and the tree follows
        var songs = new List<SongAlbumTags>
        {
            Song("SOUR [Hi-Res]", "brutal.flac", "SOUR"),
            Song("SOUR [Hi-Res]", "traitor.flac", "SOUR"),
            Song("New songs", "good 4 u.flac", "Bittersweet Indie (Explicit)"),
        };
        string added = songs[2].Path;
        AlbumPlan plan = AlbumStandardizer.BuildPlan(songs,
                Answers(("SOUR", "SOUR"), ("Bittersweet Indie (Explicit)", "Bittersweet Indie (Explicit)")), true, Root, new[] { added });
        Func<string, bool> exists = dir => dir.EndsWith("SOUR [Hi-Res]", StringComparison.OrdinalIgnoreCase);

        FolderPreview before = FolderPreview.Build(plan, AlbumStandardizer.ComputeFolderMoves(plan), new[] { added }, null, exists);
        plan.GroupOf(songs[2]).CanonicalName = "SOUR";
        FolderPreview after = FolderPreview.Build(plan, AlbumStandardizer.ComputeFolderMoves(plan), new[] { added }, null, exists);

        Assert.Equal("new folder", Child(before.Root, "Bittersweet Indie (Explicit)").Detail); // "New songs" is only where the Add copied them
        Assert.DoesNotContain(before.Root.Children, child => child.Name == "New songs");
        Assert.DoesNotContain(after.Root.Children, child => child.Name == "Bittersweet Indie (Explicit)");
        Assert.Equal("1 song added", Child(after.Root, "SOUR [Hi-Res]").Detail);
    }

    [Fact]
    public void ASongThatWouldTakeTheNameOfAnotherStaysAndSaysWhy()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("One", "1.flac", "One"),
            Song("Mixed", "1.flac", "One"),
            Song("Mixed", "2.flac", "Two"),
        };

        FolderPreview preview = Preview(songs, Answers(("One", "One"), ("Two", "Two")), new[] { songs[1].Path, songs[2].Path },
                null, null, "One");

        FolderNode mixed = Child(preview.Root, "Mixed");
        FolderNode stuck = Child(mixed, "1.flac");
        Assert.Equal(FolderTone.Problem, stuck.Tone);
        Assert.Contains("is in \"One\" already", stuck.Detail);
        Assert.Equal(1, preview.SongsStuck);
        Assert.Equal("new folder", Child(preview.Root, "Two").Detail);
    }

    [Fact]
    public void TheSongsOfAFolderThatIsShownHaveTheirNewFileNames()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("Mixed", "1.flac", "One"),
            Song("Mixed", "two.flac", "Two"),
        };
        var renames = new List<SongRename> { new SongRename(songs[1].Path, "IU - Two") };

        FolderPreview preview = Preview(songs, Answers(("One", "One"), ("Two", "Two")), null, null, renames);

        FolderNode row = Child(Child(preview.Root, "Two"), "IU - Two.flac");
        Assert.Equal("moved from \"Mixed\"  ·  renamed from \"two.flac\"", row.Detail);
        Assert.Equal(1, preview.SongsRenamed);
    }

    [Fact]
    public void AFolderWhoseSongsOnlyGetNewFileNamesIsNotAFolderChange()
    {
        var songs = new List<SongAlbumTags> { Song("Palette", "1.flac", "Palette") };
        var renames = new List<SongRename> { new SongRename(songs[0].Path, "IU - One") };

        FolderPreview preview = Preview(songs, Answers(("Palette", "Palette")), null, null, renames);

        Assert.False(preview.HasChanges);
        Assert.Equal(0, preview.FoldersChanged);
        Assert.Equal("No folders change", ReviewText.FoldersHeadline(preview));
    }

    [Fact]
    public void TheFoldersBetweenTheRootAndAChangedFolderAreShown()
    {
        var songs = new List<SongAlbumTags>
        {
            new SongAlbumTags(Path.Combine(Root, "IU", "Palette (FLAC)", "1.flac"), "Palette", new List<string> { "IU" }, null, "1"),
        };

        FolderPreview preview = Preview(songs, Answers(("Palette", "Palette")), null, null, null, Path.Combine("IU", "Palette (FLAC)"));

        FolderNode artist = Child(preview.Root, "IU");
        Assert.Equal("", artist.Detail);
        Assert.Equal(FolderTone.Plain, artist.Tone);
        Assert.Equal("renamed from \"Palette (FLAC)\"", Child(artist, "Palette").Detail);
        Assert.Equal(1, preview.FoldersChanged);
    }

    [Fact]
    public void NewSongsThatCannotBeFiledAreInANewFolderOfTheirOwn()
    {
        var songs = new List<SongAlbumTags> { Song("New songs", "Closer.flac", null) };

        FolderPreview preview = Preview(songs, Answers(), new[] { songs[0].Path }, new[] { songs[0].Path });

        FolderNode folder = Child(preview.Root, "New songs");
        Assert.Equal("new folder", folder.Detail);
        Assert.Equal("new song", Child(folder, "Closer.flac").Detail);
    }

    [Fact]
    public void TheFoldersAreSortedAndTheSongsComeAfterThem()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("Mixed", "b.flac", "Beta"),
            Song("Mixed", "a.flac", "Alpha"),
            Song("Mixed", "c.flac", "Alpha"),
        };

        FolderPreview preview = Preview(songs, Answers(("Alpha", "Alpha"), ("Beta", "Beta")));

        Assert.Equal(new[] { "Alpha", "Beta", "Mixed" }, preview.Root.Children.Select(c => c.Name).ToArray()); // "Mixed" has none left: last
        Assert.Equal(FolderTone.Gone, preview.Root.Children[2].Tone);
    }

    [Fact]
    public void TheHeadlineAndTheDetailTellWhatHappens()
    {
        var songs = new List<SongAlbumTags>
        {
            Song("Mixed", "1.flac", "One"),
            Song("Mixed", "2.flac", "Two"),
        };

        FolderPreview preview = Preview(songs, Answers(("One", "One"), ("Two", "Two")));

        Assert.Equal("3 folders change", ReviewText.FoldersHeadline(preview)); // two new ones, and "Mixed" is left without songs
        Assert.StartsWith("2 songs move to the folder named after the album\n", ReviewText.FoldersDetail(preview));
        Assert.Contains("Only the folders that change are shown", ReviewText.FoldersDetail(preview));
    }

    [Fact]
    public void ANodeSaysItsNameAndWhatHappensToIt()
    {
        var node = new FolderNode(FolderNodeKind.Folder, "SOUR", "new folder", FolderTone.Changed);

        Assert.Equal("SOUR, new folder", node.ToString());
        Assert.Equal("SOUR", new FolderNode(FolderNodeKind.Folder, "SOUR", "", FolderTone.Plain).ToString());
        node.Children.Add(new FolderNode(FolderNodeKind.Song, "a.flac", "", FolderTone.Plain));
        Assert.Equal(2, node.TotalCount);
    }
}
