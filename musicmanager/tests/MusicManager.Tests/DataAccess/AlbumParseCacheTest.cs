using System.Text;
using MusicManager.DataAccess;
using MusicManager.UseCase.StandardizeAlbums;
using Xunit;

namespace MusicManager.Tests.DataAccess;

/// <summary>Not a port: the album answers Claude gave are remembered on disk, and a damaged file is the same as an empty one.</summary>
public class AlbumParseCacheTest : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-" + Guid.NewGuid());

    public AlbumParseCacheTest()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
            // a temp folder left behind is harmless
        }
    }

    private string CacheFile => Path.Combine(_dir, "sub", "album-cache.json");

    [Fact]
    public void AnswersSurviveAFlushAndAreReadBack()
    {
        var cache = new AlbumParseCache(CacheFile);
        cache.Put("Palette [Hi-Res 24bit-96khz]", new ParsedAlbum("Palette", false));
        cache.Put("光年之外", new ParsedAlbum("光年之外", true));
        cache.Flush();

        var again = new AlbumParseCache(CacheFile);

        Assert.Equal(2, again.Size());
        ParsedAlbum palette = again.Get("Palette [Hi-Res 24bit-96khz]");
        Assert.Equal("Palette", palette.Album);
        Assert.False(palette.IsLowConfidence);
        Assert.True(again.Get("光年之外").IsLowConfidence);
        Assert.Null(again.Get("unknown"));
        Assert.Null(again.Get(null));
    }

    [Fact]
    public void NothingIsWrittenUntilSomethingChanged()
    {
        new AlbumParseCache(CacheFile).Flush();

        Assert.False(File.Exists(CacheFile));
    }

    [Fact]
    public void ABlankValueIsNotRememberedButABlankAnswerIs()
    {
        var cache = new AlbumParseCache(CacheFile);
        cache.Put("   ", new ParsedAlbum("x", false));
        cache.Put(null, new ParsedAlbum("x", false));
        cache.Put("Unknown", new ParsedAlbum("", false));
        cache.Put("Other", null);

        Assert.Equal(1, cache.Size());
        Assert.True(cache.Get("Unknown").IsNotAnAlbum);
    }

    [Fact]
    public void TheNamesAreEachListedOnceInTheOrderTheyWereFirstGiven()
    {
        var cache = new AlbumParseCache(CacheFile);
        cache.Put("a", new ParsedAlbum("Palette", false));
        cache.Put("b", new ParsedAlbum("Palette", false));
        cache.Put("c", new ParsedAlbum("", false));
        cache.Put("d", new ParsedAlbum("Lilac", false));

        Assert.Equal(new[] { "Palette", "Lilac" }, cache.Names().ToArray());
    }

    [Fact]
    public void ADamagedFileIsTheSameAsAnEmptyOneAndIsReplaced()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CacheFile));
        File.WriteAllText(CacheFile, "this is not json", new UTF8Encoding(false));
        var cache = new AlbumParseCache(CacheFile);

        Assert.Equal(0, cache.Size());
        cache.Put("x", new ParsedAlbum("X", false));
        cache.Flush();

        Assert.Equal("X", new AlbumParseCache(CacheFile).Get("x").Album);
    }

    [Fact]
    public void AnEntryThatIsNotWellFormedIsDroppedAndTheOthersKept()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CacheFile));
        File.WriteAllText(CacheFile, "{\"good\":{\"album\":\"Good\",\"lowConfidence\":false},\"bad\":42}", new UTF8Encoding(false));

        var cache = new AlbumParseCache(CacheFile);

        Assert.Equal("Good", cache.Get("good").Album);
        Assert.Null(cache.Get("bad"));
    }

    [Fact]
    public void AByteOrderMarkInFrontIsIgnored()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CacheFile));
        File.WriteAllText(CacheFile, "{\"a\":{\"album\":\"A\",\"lowConfidence\":true}}", new UTF8Encoding(true));

        Assert.True(new AlbumParseCache(CacheFile).Get("a").IsLowConfidence);
    }
}
