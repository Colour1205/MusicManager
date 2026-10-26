using System.Text;
using System.Text.Json.Nodes;
using MusicManager.Common;
using MusicManager.DataAccess;
using MusicManager.UseCase.StandardizeArtists;
using Xunit;
using static MusicManager.Tests.DataAccess.ClaudeTestSupport;

namespace MusicManager.Tests.DataAccess;

public class ArtistParseCacheTest : IDisposable
{
    private readonly string _dir;

    public ArtistParseCacheTest()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mm-" + Guid.NewGuid());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        UserHome.Path = null;
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
            // a temp folder left behind is harmless
        }
    }

    private string CacheFile()
    {
        return Path.Combine(_dir, "artist-cache.json");
    }

    private ArtistParseCache Cache()
    {
        return new ArtistParseCache(CacheFile());
    }

    private void Write(string text)
    {
        File.WriteAllText(CacheFile(), text, new UTF8Encoding(false));
    }

    private static ParsedArtist Artist(string text, string english)
    {
        return new ParsedArtist(text, english, false, false);
    }

    private static void AssertArtist(string text, string english, bool low, bool nativeName, ParsedArtist actual)
    {
        Assert.Equal(text, actual.Text);
        Assert.Equal(english, actual.English);
        Assert.Equal(low, actual.IsLowConfidence);
        Assert.Equal(nativeName, actual.IsKeepNative);
    }

    private List<string> Leftovers()
    {
        return Directory.GetFileSystemEntries(_dir).Where(p => p != CacheFile()).ToList();
    }

    // ---- remembering ----

    [Fact]
    public void AnEmptyCacheKnowsNothing()
    {
        ArtistParseCache cache = Cache();

        Assert.Null(cache.Get("IU"));
        Assert.Null(cache.Get(null));
        Assert.Equal(0, cache.Size());
    }

    [Fact]
    public void AStoredValueIsFoundByItsExactRawValue()
    {
        ArtistParseCache cache = Cache();
        cache.Put("에스파 (aespa)", new List<ParsedArtist> { Artist("에스파 (aespa)", "aespa") });

        Assert.Equal("aespa", cache.Get("에스파 (aespa)")[0].English);
        Assert.True(cache.Get("에스파 (AESPA)") == null, "the raw value is the key, exactly");
        Assert.True(cache.Get("에스파 (aespa) ") == null, "also with its spaces");
        Assert.Equal(1, cache.Size());
    }

    [Fact]
    public void WhatIsReturnedIsACopyTheCallerMayChange()
    {
        ArtistParseCache cache = Cache();
        cache.Put("IU", new List<ParsedArtist> { Artist("IU", "IU") });

        cache.Get("IU").Add(Artist("x", "X"));
        cache.Get("IU").Clear();

        Assert.Single(cache.Get("IU"));
    }

    [Fact]
    public void TheListThatWasStoredIsCopiedToo()
    {
        ArtistParseCache cache = Cache();
        List<ParsedArtist> artists = new List<ParsedArtist> { Artist("IU", "IU") };
        cache.Put("IU", artists);

        artists.Clear();

        Assert.Single(cache.Get("IU"));
    }

    [Fact]
    public void ALaterAnswerReplacesAnEarlierOne()
    {
        ArtistParseCache cache = Cache();
        cache.Put("G.E.M.", new List<ParsedArtist> { Artist("G.E.M.", "G.E.M.") });
        cache.Put("G.E.M.", new List<ParsedArtist> { new ParsedArtist("G.E.M.", "G.E.M. 邓紫棋", false, true) });

        Assert.Equal("G.E.M. 邓紫棋", cache.Get("G.E.M.")[0].English);
        Assert.Equal(1, cache.Size());
    }

    [Fact]
    public void BlankValuesAndEmptyAnswersAreNotWorthKeeping()
    {
        ArtistParseCache cache = Cache();

        cache.Put(null, new List<ParsedArtist> { Artist("x", "X") });
        cache.Put("", new List<ParsedArtist> { Artist("x", "X") });
        cache.Put("   ", new List<ParsedArtist> { Artist("x", "X") });
        cache.Put("IU", new List<ParsedArtist>());
        cache.Put("aespa", null);

        Assert.Equal(0, cache.Size());
        cache.Flush();
        Assert.False(File.Exists(CacheFile()), "nothing to write");
    }

    // ---- on disk ----

    [Fact]
    public void WhatWasFlushedIsReadByTheNextRunWithEveryField()
    {
        ArtistParseCache first = Cache();
        first.Put("G.E.M. 邓紫棋 / 王嘉尔", new List<ParsedArtist>
        {
            new ParsedArtist("G.E.M. 邓紫棋", "G.E.M. 邓紫棋", false, true),
            new ParsedArtist(" 王嘉尔", "Jackson Wang", true, false)
        });
        first.Put("quote\"s \\ and\nnewline\t🎵", new List<ParsedArtist> { Artist("quote\"s", "Quote's") });
        first.Put("IU", new List<ParsedArtist> { Artist("IU", "IU") });
        first.Flush();

        ArtistParseCache second = Cache();

        Assert.Equal(3, second.Size());
        List<ParsedArtist> pair = second.Get("G.E.M. 邓紫棋 / 王嘉尔");
        Assert.Equal(2, pair.Count);
        AssertArtist("G.E.M. 邓紫棋", "G.E.M. 邓紫棋", false, true, pair[0]);
        AssertArtist(" 王嘉尔", "Jackson Wang", true, false, pair[1]);
        AssertArtist("quote\"s", "Quote's", false, false, second.Get("quote\"s \\ and\nnewline\t🎵")[0]);
        AssertArtist("IU", "IU", false, false, second.Get("IU")[0]);
    }

    [Fact]
    public void TheFileIsAMapOfRawValuesToTheFourFieldsOfEachArtist()
    {
        ArtistParseCache cache = Cache();
        cache.Put("에스파 (aespa)", new List<ParsedArtist> { new ParsedArtist("에스파 (aespa)", "aespa", true, false) });
        cache.Flush();

        JsonObject root = Obj(File.ReadAllText(CacheFile(), Encoding.UTF8));

        JsonArray artists = root.Arr("에스파 (aespa)");
        Assert.Single(artists);
        JsonObject item = artists[0].AsObject();
        Assert.Equal(4, item.Count);
        Assert.Equal("에스파 (aespa)", item.Str("text"));
        Assert.Equal("aespa", item.Str("english"));
        Assert.True(item.Bool("lowConfidence"));
        Assert.False(item.Bool("keepNative"));
        Assert.True(File.ReadAllText(CacheFile(), Encoding.UTF8).Contains("에스파"), "written as UTF-8, not escaped");
    }

    [Fact]
    public void TheFolderIsCreatedAndNoTempFileIsLeftBehind()
    {
        string nested = Path.Combine(_dir, "deep", "er", "artist-cache.json");
        ArtistParseCache cache = new ArtistParseCache(nested);
        cache.Put("IU", new List<ParsedArtist> { Artist("IU", "IU") });
        cache.Flush();
        cache.Put("aespa", new List<ParsedArtist> { Artist("aespa", "aespa") });
        cache.Flush();

        Assert.Equal(2, new ArtistParseCache(nested).Size());
        Assert.Equal(new List<string> { nested }, Directory.GetFileSystemEntries(Path.GetDirectoryName(nested)).ToList());
    }

    [Fact]
    public void FlushingWithoutChangesWritesNothing()
    {
        ArtistParseCache cache = Cache();
        cache.Flush();
        Assert.False(File.Exists(CacheFile()));

        cache.Put("IU", new List<ParsedArtist> { Artist("IU", "IU") });
        cache.Flush();
        File.Delete(CacheFile());
        cache.Flush(); // nothing changed since the last flush

        Assert.True(!File.Exists(CacheFile()), "an unchanged cache is not written again");
    }

    [Fact]
    public void ReadingDoesNotRewriteTheFile()
    {
        ArtistParseCache writer = Cache();
        writer.Put("IU", new List<ParsedArtist> { Artist("IU", "IU") });
        writer.Flush();
        string before = File.ReadAllText(CacheFile(), Encoding.UTF8);
        Write(before + "\n\n"); // a marker only a rewrite would remove

        ArtistParseCache reader = Cache();
        Assert.Equal(1, reader.Size());
        reader.Flush();

        Assert.Equal(before + "\n\n", File.ReadAllText(CacheFile(), Encoding.UTF8));
    }

    [Fact]
    public void NewEntriesAreAddedToWhatTheFileAlreadyHolds()
    {
        ArtistParseCache first = Cache();
        first.Put("IU", new List<ParsedArtist> { Artist("IU", "IU") });
        first.Flush();

        ArtistParseCache second = Cache();
        second.Put("aespa", new List<ParsedArtist> { Artist("aespa", "aespa") });
        second.Flush();

        ArtistParseCache third = Cache();
        Assert.Equal(2, third.Size());
        Assert.NotNull(third.Get("IU"));
        Assert.NotNull(third.Get("aespa"));
    }

    // ---- damaged files ----

    [Theory]
    [InlineData("")]
    [InlineData("  \n")]
    [InlineData("garbage")]
    [InlineData("{")]
    [InlineData("{\"IU\": [")]
    [InlineData("[1, 2]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"IU\"")]
    [InlineData("}{")]
    public void AnUnreadableFileIsAnEmptyCache(string content)
    {
        Write(content);

        Assert.Equal(0, Cache().Size());
        Assert.Null(Cache().Get("IU"));
    }

    [Fact]
    public void BytesThatAreNotUtf8AreAnEmptyCache()
    {
        File.WriteAllBytes(CacheFile(), new byte[] { 0xFF, 0xFE, 0x00, 0xC3, 0x28, 0x80 });

        Assert.Equal(0, Cache().Size());
    }

    [Fact]
    public void ADamagedFileIsReplacedByTheNextFlush()
    {
        Write("{ this is not json");
        ArtistParseCache cache = Cache();
        cache.Put("IU", new List<ParsedArtist> { Artist("IU", "IU") });

        cache.Flush();

        Assert.Equal("IU", Cache().Get("IU")[0].English);
        Assert.NotNull(Obj(File.ReadAllText(CacheFile(), Encoding.UTF8)));
    }

    [Fact]
    public void ADamagedFileIsReplacedEvenWhenNothingWasAdded()
    {
        Write("{ this is not json");
        ArtistParseCache cache = Cache();
        Assert.Equal(0, cache.Size());

        cache.Flush();

        Assert.Equal("{}", File.ReadAllText(CacheFile(), Encoding.UTF8).Trim());
    }

    [Fact]
    public void OneBadEntryDoesNotCostTheGoodOnes()
    {
        Write("""
                {
                  "IU": [{"text": "IU", "english": "IU", "lowConfidence": false, "keepNative": false}],
                  "empty list": [],
                  "not a list": {"text": "x", "english": "X"},
                  "no english": [{"text": "x"}],
                  "blank english": [{"text": "x", "english": "  "}],
                  "blank text": [{"text": " ", "english": "X"}],
                  "number": [{"text": 5, "english": "X"}],
                  "null item": [null],
                  "string item": ["x"],
                  "second bad": [{"text": "a", "english": "A"}, {"text": "b"}],
                  "aespa": [{"text": "에스파", "english": "aespa", "lowConfidence": true}],
                  "extras": [{"text": "x", "english": "X", "color": "red", "keepNative": "yes"}]
                }
                """);

        ArtistParseCache cache = Cache();

        Assert.Equal(3, cache.Size());
        AssertArtist("IU", "IU", false, false, cache.Get("IU")[0]);
        AssertArtist("에스파", "aespa", true, false, cache.Get("aespa")[0]);
        AssertArtist("x", "X", false, false, cache.Get("extras")[0]);
        foreach (string bad in new List<string> { "empty list", "not a list", "no english", "blank english", "blank text",
                "number", "null item", "string item", "second bad" })
        {
            Assert.True(cache.Get(bad) == null, bad);
        }

        cache.Flush();
        Assert.True(Cache().Size() == 3, "the bad entries are gone after the file is rewritten");
    }

    [Fact]
    public void AByteOrderMarkIsHarmless()
    {
        File.WriteAllBytes(CacheFile(), new UTF8Encoding(false).GetBytes("\xFEFF" + "{\"IU\": [{\"text\": \"IU\", \"english\": \"IU\"}]}"));

        Assert.Equal("IU", Cache().Get("IU")[0].English);
    }

    [Fact]
    public void AFolderWhereTheFileShouldBeIsAnEmptyCacheThatStillWorksInMemory()
    {
        Directory.CreateDirectory(CacheFile());
        File.WriteAllText(Path.Combine(CacheFile(), "keep.txt"), "x");
        ArtistParseCache cache = Cache();

        Assert.Equal(0, cache.Size());
        cache.Put("IU", new List<ParsedArtist> { Artist("IU", "IU") });
        cache.Flush(); // cannot be written; no exception

        Assert.Equal("IU", cache.Get("IU")[0].English);
        Assert.True(File.Exists(Path.Combine(CacheFile(), "keep.txt")));
        Assert.Equal(new List<string>(), Leftovers());
    }

    [Fact]
    public void AFlushThatFailsIsTriedAgainNextTime()
    {
        Directory.CreateDirectory(CacheFile());
        File.WriteAllText(Path.Combine(CacheFile(), "keep.txt"), "x");
        ArtistParseCache cache = Cache();
        cache.Put("IU", new List<ParsedArtist> { Artist("IU", "IU") });
        cache.Flush(); // fails: a folder is in the way

        File.Delete(Path.Combine(CacheFile(), "keep.txt"));
        Directory.Delete(CacheFile());
        cache.Flush();

        Assert.Equal("IU", Cache().Get("IU")[0].English);
    }

    // ---- threads and location ----

    [Fact]
    public void SeveralThreadsCanFillAndFlushTheCacheAtOnce()
    {
        ArtistParseCache cache = Cache();
        Exception[] failure = { null };
        List<Thread> threads = new List<Thread>();
        for (int t = 0; t < 8; t++)
        {
            int id = t;
            Thread thread = new Thread(() =>
            {
                try
                {
                    for (int i = 0; i < 40; i++)
                    {
                        cache.Put("artist " + id + "/" + i, new List<ParsedArtist> { Artist("a", "A " + id + "/" + i) });
                        Assert.NotNull(cache.Get("artist " + id + "/" + i));
                        cache.Flush();
                    }
                }
                catch (Exception e)
                {
                    failure[0] = e;
                }
            });
            threads.Add(thread);
            thread.Start();
        }
        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        Assert.Null(failure[0]);
        Assert.Equal(320, cache.Size());
        Assert.True(Cache().Size() == 320, "all of it reached the disk");
        Assert.Equal(new List<string>(), Leftovers());
    }

    [Fact]
    public void TheDefaultLocationIsUnderTheUserHome()
    {
        UserHome.Path = _dir;
        try
        {
            ArtistParseCache cache = ArtistParseCache.AtDefaultLocation();
            cache.Put("IU", new List<ParsedArtist> { Artist("IU", "IU") });
            cache.Flush();
        }
        finally
        {
            UserHome.Path = null;
        }

        Assert.True(File.Exists(Path.Combine(_dir, ".musicmanager", "artist-cache.json")));
    }
}
