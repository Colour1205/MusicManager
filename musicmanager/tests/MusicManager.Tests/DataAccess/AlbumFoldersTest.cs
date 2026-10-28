using MusicManager.DataAccess;
using MusicManager.UseCase.StandardizeAlbums;
using Xunit;

namespace MusicManager.Tests.DataAccess;

/// <summary>
/// Not a port: putting songs in the folder named after their album, on real folders in the temp directory. A folder that is
/// all of one album is renamed with everything in it; otherwise the songs move one by one with the lyric files that share
/// their name; nothing is ever overwritten.
/// </summary>
public class AlbumFoldersTest : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mm-" + Guid.NewGuid());

    public AlbumFoldersTest()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // the temp folder is cleaned up by the OS sooner or later
        }
    }

    private string Folder(string name)
    {
        string path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private string File(string folder, string name, string content = "x")
    {
        string path = Path.Combine(Folder(folder), name);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    private static AlbumFolderMove Move(string source, string target, bool whole, params string[] songs)
    {
        return new AlbumFolderMove(source, target, songs.ToList(), whole);
    }

    // ---- the folder is the album

    [Fact]
    public void AFolderThatIsAllOfOneAlbumIsRenamedWithEverythingInIt()
    {
        string song1 = File("Palette [Hi-Res]", "IU - Black Out.flac");
        string song2 = File("Palette [Hi-Res]", "IU - Jam Jam.flac");
        File("Palette [Hi-Res]", "IU - Jam Jam.lrc");
        File("Palette [Hi-Res]", "cover.jpg");

        AlbumFolderMoveResult result = AlbumFolders.Move(Move(Path.Combine(_root, "Palette [Hi-Res]"), "Palette", true, song1, song2));

        Assert.True(result.IsFolderRenamed);
        Assert.Empty(result.NotMoved);
        Assert.False(Directory.Exists(Path.Combine(_root, "Palette [Hi-Res]")));
        Assert.True(System.IO.File.Exists(Path.Combine(_root, "Palette", "IU - Black Out.flac")));
        Assert.True(System.IO.File.Exists(Path.Combine(_root, "Palette", "IU - Jam Jam.lrc")));
        Assert.True(System.IO.File.Exists(Path.Combine(_root, "Palette", "cover.jpg")));
        Assert.Equal(Path.Combine(_root, "Palette", "IU - Jam Jam.flac"), result.Moved[song2]);
        Assert.Equal(2, result.Moved.Count);
    }

    [Fact]
    public void ACaseOnlyDifferenceRenamesTheFolder()
    {
        string song = File("Lost And Found", "IU - Boo.flac");

        AlbumFolderMoveResult result = AlbumFolders.Move(Move(Path.Combine(_root, "Lost And Found"), "Lost and Found", true, song));

        Assert.True(result.IsFolderRenamed);
        string[] names = Directory.GetDirectories(_root).Select(Path.GetFileName).ToArray();
        Assert.Equal(new[] { "Lost and Found" }, names); // the case on disk changed
        Assert.True(System.IO.File.Exists(Path.Combine(_root, "Lost and Found", "IU - Boo.flac")));
    }

    [Fact]
    public void AWholeFolderGoesIntoTheFolderThatIsThereAlreadyAndNothingIsOverwritten()
    {
        File("HEAT", "i-dle - I DO.flac", "original");
        string clash = File("HEAT (Clean)", "i-dle - I DO.flac", "clean");
        string fresh = File("HEAT (Clean)", "i-dle - Flip It.flac");
        File("HEAT (Clean)", "i-dle - Flip It.lrc");

        AlbumFolderMoveResult result = AlbumFolders.Move(Move(Path.Combine(_root, "HEAT (Clean)"), "HEAT", true, clash, fresh));

        Assert.False(result.IsFolderRenamed);
        Assert.Equal("original", System.IO.File.ReadAllText(Path.Combine(_root, "HEAT", "i-dle - I DO.flac")));
        Assert.True(System.IO.File.Exists(clash)); // stays, with its reason
        AlbumFolderMoveResult.Skipped skipped = Assert.Single(result.NotMoved);
        Assert.Equal(clash, skipped.AudioPath);
        Assert.Contains("already", skipped.Detail);
        Assert.True(System.IO.File.Exists(Path.Combine(_root, "HEAT", "i-dle - Flip It.flac")));
        Assert.True(System.IO.File.Exists(Path.Combine(_root, "HEAT", "i-dle - Flip It.lrc")));
        Assert.Equal(Path.Combine(_root, "HEAT", "i-dle - Flip It.flac"), Assert.Single(result.Moved).Value);
        Assert.True(Directory.Exists(Path.Combine(_root, "HEAT (Clean)"))); // the one that stayed keeps its folder
    }

    [Fact]
    public void WhenTheLastSongLeavesItsCoverFollowsAndTheFolderGoes()
    {
        File("5-STAR", "Stray Kids - Hall of Fame.flac");
        string song = File("Stray Kids - 5-STAR", "01 - ITEM.flac");
        File("Stray Kids - 5-STAR", "cover.jpg", "cover");
        File("Stray Kids - 5-STAR", "Thumbs.db");

        AlbumFolderMoveResult result = AlbumFolders.Move(Move(Path.Combine(_root, "Stray Kids - 5-STAR"), "5-STAR", true, song));

        Assert.Single(result.Moved);
        Assert.Equal("cover", System.IO.File.ReadAllText(Path.Combine(_root, "5-STAR", "cover.jpg")));
        Assert.False(Directory.Exists(Path.Combine(_root, "Stray Kids - 5-STAR")));
    }

    [Fact]
    public void ACoverTheAlbumFolderHasAlreadyIsNotReplacedAndItsTwinStaysBehind()
    {
        File("5-STAR", "Stray Kids - Hall of Fame.flac");
        File("5-STAR", "cover.jpg", "mine");
        string song = File("Stray Kids - 5-STAR", "01 - ITEM.flac");
        File("Stray Kids - 5-STAR", "cover.jpg", "theirs");

        AlbumFolders.Move(Move(Path.Combine(_root, "Stray Kids - 5-STAR"), "5-STAR", true, song));

        Assert.Equal("mine", System.IO.File.ReadAllText(Path.Combine(_root, "5-STAR", "cover.jpg")));
        Assert.Equal("theirs", System.IO.File.ReadAllText(Path.Combine(_root, "Stray Kids - 5-STAR", "cover.jpg")));
    }

    [Fact]
    public void AFolderThatStillHasSongsKeepsItsCover()
    {
        File("5-STAR", "Stray Kids - Hall of Fame.flac");
        string moving = File("Mixed", "A - One.flac");
        File("Mixed", "A - Two.flac");
        File("Mixed", "cover.jpg", "cover");

        AlbumFolders.Move(Move(Path.Combine(_root, "Mixed"), "5-STAR", false, moving));

        Assert.True(System.IO.File.Exists(Path.Combine(_root, "Mixed", "cover.jpg")));
        Assert.False(System.IO.File.Exists(Path.Combine(_root, "5-STAR", "cover.jpg")));
    }

    [Fact]
    public void TheEmptyFolderThatIsLeftIsRemoved()
    {
        File("Palette", "IU - Black Out.flac");
        string song = File("Palette [FLAC]", "IU - Jam Jam.flac");

        AlbumFolderMoveResult result = AlbumFolders.Move(Move(Path.Combine(_root, "Palette [FLAC]"), "Palette", true, song));

        Assert.Single(result.Moved);
        Assert.False(Directory.Exists(Path.Combine(_root, "Palette [FLAC]")));
    }

    [Fact]
    public void AFolderWithOtherFilesLeftInItStays()
    {
        string song = File("Palette [FLAC]", "IU - Jam Jam.flac");
        File("Palette [FLAC]", "booklet.pdf");
        File("Palette", "IU - Black Out.flac");

        AlbumFolders.Move(Move(Path.Combine(_root, "Palette [FLAC]"), "Palette", true, song));

        Assert.True(System.IO.File.Exists(Path.Combine(_root, "Palette [FLAC]", "booklet.pdf")));
    }

    // ---- song by song

    [Fact]
    public void SongsOfOneFolderGoToTheirOwnAlbumFoldersWithTheirLyrics()
    {
        string one = File("Mixed", "A - One.flac");
        File("Mixed", "A - One.lrc");
        File("Mixed", "A - One.ttml");
        string two = File("Mixed", "A - Two.flac");
        File("Mixed", "cover.jpg");
        string source = Path.Combine(_root, "Mixed");

        AlbumFolderMoveResult first = AlbumFolders.Move(Move(source, "One", false, one));
        AlbumFolderMoveResult second = AlbumFolders.Move(Move(source, "Two", false, two));

        Assert.False(first.IsFolderRenamed);
        Assert.True(System.IO.File.Exists(Path.Combine(_root, "One", "A - One.flac")));
        Assert.True(System.IO.File.Exists(Path.Combine(_root, "One", "A - One.lrc")));
        Assert.True(System.IO.File.Exists(Path.Combine(_root, "One", "A - One.ttml")));
        Assert.True(System.IO.File.Exists(Path.Combine(_root, "Two", "A - Two.flac")));
        Assert.Equal(Path.Combine(_root, "One", "A - One.flac"), first.Moved[one]);
        Assert.Equal(Path.Combine(_root, "Two", "A - Two.flac"), second.Moved[two]);
        Assert.True(System.IO.File.Exists(Path.Combine(source, "cover.jpg"))); // not a song's file: it stays
    }

    [Fact]
    public void AFolderMadeForSongsThatCouldNotGoThereIsNotLeftBehind()
    {
        string song = File("Mixed", "A - One.flac");
        File("One", "A - One.flac", "taken");
        System.IO.Directory.Delete(Path.Combine(_root, "One"), true);
        Directory.CreateDirectory(Path.Combine(_root, "One"));
        System.IO.File.WriteAllText(Path.Combine(_root, "One", "A - One.flac"), "taken");

        AlbumFolderMoveResult result = AlbumFolders.Move(Move(Path.Combine(_root, "Mixed"), "One", false, song));

        Assert.Empty(result.Moved);
        Assert.Single(result.NotMoved);
        Assert.Equal("taken", System.IO.File.ReadAllText(Path.Combine(_root, "One", "A - One.flac")));
        Assert.True(System.IO.File.Exists(song));
    }

    [Fact]
    public void ASongWithoutItsFileIsSkippedWithAReason()
    {
        string gone = Path.Combine(_root, "Mixed", "A - Gone.flac");
        string here = File("Mixed", "A - Here.flac");

        AlbumFolderMoveResult result = AlbumFolders.Move(Move(Path.Combine(_root, "Mixed"), "One", false, gone, here));

        Assert.Equal(gone, Assert.Single(result.NotMoved).AudioPath);
        Assert.Contains("not there", result.NotMoved[0].Detail);
        Assert.Single(result.Moved);
    }

    [Fact]
    public void AMissingFolderSkipsEverySong()
    {
        string source = Path.Combine(_root, "Nothing");

        AlbumFolderMoveResult result = AlbumFolders.Move(Move(source, "Album", true, Path.Combine(source, "a.flac"), Path.Combine(source, "b.flac")));

        Assert.Empty(result.Moved);
        Assert.Equal(2, result.NotMoved.Count);
        Assert.False(Directory.Exists(Path.Combine(_root, "Album")));
    }

    [Fact]
    public void ATooLongPathLeavesTheSongWhereItIs()
    {
        string song = File("Mixed", "A - One.flac");
        string longName = new string('x', 300);

        AlbumFolderMoveResult result = AlbumFolders.Move(Move(Path.Combine(_root, "Mixed"), longName, false, song));

        Assert.Empty(result.Moved);
        Assert.Contains("characters long", Assert.Single(result.NotMoved).Detail);
        Assert.True(System.IO.File.Exists(song));
    }

    [Fact]
    public void TheWholeFolderIsNotRenamedWhenThePathWouldBeTooLong()
    {
        string song = File("Old", "A - One.flac");

        AlbumFolderMoveResult result = AlbumFolders.Move(Move(Path.Combine(_root, "Old"), new string('x', 300), true, song));

        Assert.False(result.IsFolderRenamed);
        Assert.Contains("characters long", Assert.Single(result.NotMoved).Detail);
        Assert.True(Directory.Exists(Path.Combine(_root, "Old")));
    }
}
