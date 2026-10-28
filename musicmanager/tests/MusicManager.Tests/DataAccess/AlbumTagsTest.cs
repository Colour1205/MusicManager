using MusicManager.DataAccess;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeAlbums;
using Xunit;

namespace MusicManager.Tests.DataAccess;

/// <summary>Not a port: reading and writing the ALBUM tag, on MP3 files built in the temp directory.</summary>
public class AlbumTagsTest : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mm-" + Guid.NewGuid());

    public AlbumTagsTest()
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

    private string Mp3(string folder, string name, string artist, string title, string album, string albumArtist = null)
    {
        return TestMp3.Create(Path.Combine(_root, folder), name, artist, title, album, albumArtist);
    }

    [Fact]
    public void TheAlbumTagsOfEveryFileUnderTheFolderAreRead()
    {
        string a = Mp3("Palette", "a.mp3", "IU", "Black Out", "Palette", "IU");
        string b = Mp3("Mixed", "b.mp3", "IU", "Jam Jam", null);
        Mp3("Mixed", "c.mp3", null, null, null); // no tag at all: left out
        File.WriteAllText(Path.Combine(_root, "Mixed", "b.lrc"), "x"); // not audio

        List<SongAlbumTags> songs = AlbumTags.Read(_root);

        Assert.Equal(new[] { b, a }, songs.Select(s => s.Path).ToArray()); // path order: Mixed before Palette
        SongAlbumTags palette = songs.Single(s => s.Path == a);
        Assert.Equal("Palette", palette.Album);
        Assert.Equal(new[] { "IU" }, palette.Artist.ToArray());
        Assert.Equal(new[] { "IU" }, palette.AlbumArtist.ToArray());
        Assert.Equal("Black Out", palette.Title);
        Assert.Null(songs.Single(s => s.Path == b).Album);
    }

    [Fact]
    public void AFolderThatIsNotThereGivesNoSongsOrAnError()
    {
        Assert.Throws<DirectoryNotFoundException>(() => AlbumTags.Read(Path.Combine(_root, "nothing")));
    }

    [Fact]
    public void WritingReplacesTheAlbumAndKeepsTheRest()
    {
        string file = Mp3("A", "a.mp3", "IU", "Black Out", "Palette [Hi-Res 24bit-96khz]");

        bool written = AlbumTags.Write(new SongAlbumUpdate(file, "Palette", "Palette [Hi-Res 24bit-96khz]"));

        Assert.True(written);
        SongAlbumTags read = Assert.Single(AlbumTags.Read(_root));
        Assert.Equal("Palette", read.Album);
        Assert.Equal("Black Out", read.Title);
        Assert.Equal(new[] { "IU" }, read.Artist.ToArray());
    }

    [Fact]
    public void WritingAddsAnAlbumToAFileThatHasNone()
    {
        string file = Mp3("A", "a.mp3", "IU", "Black Out", null);

        Assert.True(AlbumTags.Write(new SongAlbumUpdate(file, "Palette", null)));

        Assert.Equal("Palette", AlbumTags.ReadAlbum(file));
    }

    [Fact]
    public void WritingKeepsAnyScriptAndPunctuation()
    {
        string file = Mp3("A", "a.mp3", "G.E.M.", "光年之外", "old");

        Assert.True(AlbumTags.Write(new SongAlbumUpdate(file, "光年之外 (電影 《Passengers》 中國區主題曲)", "old")));

        Assert.Equal("光年之外 (電影 《Passengers》 中國區主題曲)", AlbumTags.ReadAlbum(file));
    }

    [Fact]
    public void AFileThatIsNotThereCannotBeWritten()
    {
        Assert.False(AlbumTags.Write(new SongAlbumUpdate(Path.Combine(_root, "nothing.mp3"), "Palette", null)));
        Assert.Null(AlbumTags.ReadAlbum(Path.Combine(_root, "nothing.mp3")));
    }
}
