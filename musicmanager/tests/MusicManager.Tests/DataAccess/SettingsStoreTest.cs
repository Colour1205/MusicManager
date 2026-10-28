using System.Text;
using System.Text.Json;
using MusicManager.Common;
using MusicManager.DataAccess;
using MusicManager.Entity;
using Xunit;
using DataAccessImpl = MusicManager.DataAccess.DataAccess;

namespace MusicManager.Tests.DataAccess;

public class SettingsStoreTest : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-" + Guid.NewGuid());

    public SettingsStoreTest()
    {
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
            // the temp folder is cleaned up by the OS sooner or later
        }
    }

    private string FilePath()
    {
        return Path.Combine(_dir, "settings.json");
    }

    private SettingsStore Store()
    {
        return new SettingsStore(FilePath());
    }

    private void Write(string text)
    {
        File.WriteAllText(FilePath(), text, new UTF8Encoding(false));
    }

    private static void AssertDefaults(AppSettings settings)
    {
        Assert.Equal("", settings.LastPath);
        Assert.Equal(new List<string>(), settings.DefaultTargetFormats);
        AssertDefaultOptions(settings.StandardizeOptions);
    }

    private static void AssertDefaultOptions(StandardizeOptions options)
    {
        AssertSameOptions(new StandardizeOptions(), options);
    }

    private static void AssertSameOptions(StandardizeOptions expected, StandardizeOptions actual)
    {
        Assert.Equal(expected.GetMode(), actual.GetMode());
        Assert.Equal(expected.IsLyrics, actual.IsLyrics);
        Assert.Equal(expected.IsRomaji, actual.IsRomaji);
        Assert.Equal(expected.IsRecycleBin, actual.IsRecycleBin);
        Assert.Equal(expected.IsArtists, actual.IsArtists);
        Assert.Equal(expected.IsUseClaudeForArtists, actual.IsUseClaudeForArtists);
        Assert.Equal(expected.IsAlbums, actual.IsAlbums);
        Assert.Equal(expected.IsUseClaudeForAlbums, actual.IsUseClaudeForAlbums);
        Assert.Equal(expected.PreferredFormat, actual.PreferredFormat);
        Assert.Equal(expected.Separator, actual.Separator);
        Assert.Equal(expected.Model, actual.Model);
    }

    private static StandardizeOptions UnusualOptions()
    {
        StandardizeOptions options = new StandardizeOptions();
        options.SetMode(StandardizeOptions.Mode.Convert);
        options.IsLyrics = false;
        options.IsRomaji = false;
        options.IsRecycleBin = true;
        options.IsArtists = false;
        options.IsUseClaudeForArtists = false;
        options.IsAlbums = false;
        options.IsUseClaudeForAlbums = false;
        options.PreferredFormat = "wav";
        options.Separator = " / ";
        options.Model = "claude-opus-5-5";
        options.ApiKey = "sk-ant-api03-SECRET-KEY-abcdefghijklmnop";
        return options;
    }

    private List<string> Leftovers()
    {
        return Directory.GetFileSystemEntries(_dir).Where(p => p != FilePath()).ToList();
    }

    // ---- reading ----

    [Fact]
    public void AMissingFileGivesTheDefaults()
    {
        AssertDefaults(Store().Load());
    }

    [Fact]
    public void TheDefaultsAreFreshObjectsTheCallerMayChange()
    {
        AppSettings first = Store().Load();
        first.DefaultTargetFormats.Add("lrc");
        first.StandardizeOptions.IsRomaji = false;

        AssertDefaults(Store().Load());
    }

    [Fact]
    public void WhatWasSavedIsReadBack()
    {
        AppSettings settings = new AppSettings("C:\\Users\\Colour\\Music \"best\" & 音楽\\日本語",
                new List<string> { "ttml", "lrc" }, UnusualOptions());

        Store().Save(settings);
        AppSettings read = Store().Load();

        Assert.Equal("C:\\Users\\Colour\\Music \"best\" & 音楽\\日本語", read.LastPath);
        Assert.Equal(new List<string> { "ttml", "lrc" }, read.DefaultTargetFormats);
        AssertSameOptions(UnusualOptions(), read.StandardizeOptions);
    }

    [Fact]
    public void TheApiKeyIsNeverWritten()
    {
        Store().Save(new AppSettings("x", new List<string>(), UnusualOptions()));

        string text = File.ReadAllText(FilePath(), Encoding.UTF8);

        Assert.DoesNotContain("SECRET", text);
        Assert.DoesNotContain("apikey", text.ToLowerInvariant());
        Assert.Equal("", Store().Load().StandardizeOptions.ApiKey);
    }

    [Fact]
    public void TheFileHasTheThreeKeysTheSpecNames()
    {
        Store().Save(new AppSettings("D:\\Music", new List<string> { "lrc" }, UnusualOptions()));

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(FilePath(), Encoding.UTF8));
        JsonElement root = document.RootElement;

        Assert.Equal("D:\\Music", root.GetProperty("lastPath").GetString());
        Assert.Equal("lrc", root.GetProperty("defaultTargetFormats")[0].GetString());
        JsonElement options = root.GetProperty("standardizeOptions");
        foreach (string key in new[] { "mode", "lyrics", "romaji", "recycleBin", "artists", "useClaudeForArtists",
                "albums", "useClaudeForAlbums", "preferredFormat", "separator", "model" })
        {
            Assert.True(options.TryGetProperty(key, out _), "missing " + key);
        }
        Assert.Equal("CONVERT", options.GetProperty("mode").GetString());
        Assert.False(options.TryGetProperty("apiKey", out _));
    }

    [Fact]
    public void AnOldFileWithoutTheAlbumOptionsGetsThemOn()
    {
        Write("{\"lastPath\": \"x\", \"standardizeOptions\": {\"lyrics\": false, \"artists\": true}}");

        StandardizeOptions options = Store().Load().StandardizeOptions;

        Assert.False(options.IsLyrics);
        Assert.True(options.IsAlbums);
        Assert.True(options.IsUseClaudeForAlbums);
        Assert.Equal("flac", options.PreferredFormat);
    }

    [Fact]
    public void TheFolderIsCreatedWhenSaving()
    {
        string nested = Path.Combine(_dir, "a", "b", "settings.json");

        Assert.True(new SettingsStore(nested).Save(new AppSettings("x", new List<string>())));

        Assert.True(File.Exists(nested));
        Assert.Equal("x", new SettingsStore(nested).Load().LastPath);
    }

    [Fact]
    public void AnOldFileWithoutStandardizeOptionsKeepsItsValuesAndGetsDefaultOptions()
    {
        // exactly what the previous, hand-written saver produced
        Write("{\n  \"lastPath\": \"C:\\\\Users\\\\Colour\\\\Music\",\n  \"defaultTargetFormats\": [\"ttml\", \"lrc\"]\n}\n");

        AppSettings read = Store().Load();

        Assert.Equal("C:\\Users\\Colour\\Music", read.LastPath);
        Assert.Equal(new List<string> { "ttml", "lrc" }, read.DefaultTargetFormats);
        AssertDefaultOptions(read.StandardizeOptions);
    }

    [Fact]
    public void AnOldFileWithAnEscapedQuoteInThePathIsRead()
    {
        Write("{\n  \"lastPath\": \"D:\\\\My \\\"Best\\\" Music\",\n  \"defaultTargetFormats\": []\n}\n");

        Assert.Equal("D:\\My \"Best\" Music", Store().Load().LastPath);
    }

    [Fact]
    public void ANewFileWithoutFormatsOrPathGivesEmptyValues()
    {
        Write("{\"standardizeOptions\": {\"romaji\": false}}");

        AppSettings read = Store().Load();

        Assert.Equal("", read.LastPath);
        Assert.Equal(new List<string>(), read.DefaultTargetFormats);
        Assert.False(read.StandardizeOptions.IsRomaji);
        Assert.True(read.StandardizeOptions.IsLyrics, "a key that is missing keeps its default");
        Assert.Equal("; ", read.StandardizeOptions.Separator);
    }

    [Fact]
    public void UnknownKeysAreIgnored()
    {
        Write("{\"lastPath\": \"E:\\\\Songs\", \"theme\": \"dark\", \"windowSize\": [1, 2], "
                + "\"standardizeOptions\": {\"recycleBin\": true, \"futureFlag\": {\"a\": 1}, \"apiKey\": \"sk-leak\"}}");

        AppSettings read = Store().Load();

        Assert.Equal("E:\\Songs", read.LastPath);
        Assert.True(read.StandardizeOptions.IsRecycleBin);
        Assert.Equal("", read.StandardizeOptions.ApiKey); // a key in the file is never taken as the API key
    }

    [Fact]
    public void AnUnknownModeNameIsTheStandardizeMode()
    {
        Write("{\"standardizeOptions\": {\"mode\": \"TELEPORT\", \"romaji\": false}}");

        StandardizeOptions options = Store().Load().StandardizeOptions;

        Assert.Equal(StandardizeOptions.Mode.Standardize, options.GetMode());
        Assert.False(options.IsRomaji);
    }

    [Fact]
    public void NullAndBlankOptionStringsFallBackToTheDefaults()
    {
        Write("{\"standardizeOptions\": {\"separator\": null, \"model\": \"  \"}}");

        StandardizeOptions options = Store().Load().StandardizeOptions;

        Assert.Equal("; ", options.Separator);
        Assert.Equal("claude-sonnet-5-5", options.Model);
    }

    [Fact]
    public void AByteOrderMarkFromAnEditorIsHarmless()
    {
        File.WriteAllBytes(FilePath(), Encoding.UTF8.GetBytes("\uFEFF{\"lastPath\": \"F:\\\\Tunes\"}"));

        Assert.Equal("F:\\Tunes", Store().Load().LastPath);
    }

    // ---- damaged files never throw ----

    [Theory]
    [InlineData("")]
    [InlineData("   \n")]
    [InlineData("not json at all")]
    [InlineData("{")]
    [InlineData("{\"lastPath\": ")]
    [InlineData("[1, 2, 3]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"text\"")]
    [InlineData("{\"lastPath\": \"unterminated")]
    [InlineData("}{")]
    [InlineData("<settings/>")]
    [InlineData("{{{{")]
    public void AnUnreadableFileGivesTheDefaults(string content)
    {
        Write(content);

        AssertDefaults(Store().Load());
    }

    [Fact]
    public void BytesThatAreNotUtf8GiveTheDefaults()
    {
        File.WriteAllBytes(FilePath(), new byte[] { 0xFF, 0xFE, 0x00, 0xC3, 0x28, 0x80, 0x7B });

        AssertDefaults(Store().Load());
    }

    [Fact]
    public void AFolderWhereTheFileShouldBeGivesTheDefaults()
    {
        Directory.CreateDirectory(FilePath());

        AssertDefaults(Store().Load());
    }

    [Fact]
    public void OneDamagedKeyDoesNotCostTheOthers()
    {
        Write("{\"lastPath\": \"G:\\\\Music\", \"defaultTargetFormats\": \"ttml\", "
                + "\"standardizeOptions\": {\"lyrics\": {\"deep\": [1]}, \"romaji\": false}}");

        AppSettings read = Store().Load();

        Assert.Equal("G:\\Music", read.LastPath);
        Assert.Equal(new List<string>(), read.DefaultTargetFormats);
        AssertDefaultOptions(read.StandardizeOptions);
    }

    [Fact]
    public void WrongTypesForThePathAndTheFormatsAreTolerated()
    {
        Write("{\"lastPath\": {\"a\": 1}, \"defaultTargetFormats\": [\"ttml\", null, 5, [\"x\"], {\"y\": 1}], "
                + "\"standardizeOptions\": []}");

        AppSettings read = Store().Load();

        Assert.Equal("", read.LastPath);
        Assert.Equal(new List<string> { "ttml", "5" }, read.DefaultTargetFormats);
        AssertDefaultOptions(read.StandardizeOptions);
    }

    // ---- writing ----

    [Fact]
    public void SavingReplacesTheWholeFileAndLeavesNoTempFileBehind()
    {
        SettingsStore store = Store();
        store.Save(new AppSettings(JavaString.Repeat("a very long path ", 40),
                new List<string> { "ttml", "hmrc", "lrc" }, UnusualOptions()));
        store.Save(new AppSettings("short", new List<string>()));

        string text = File.ReadAllText(FilePath(), Encoding.UTF8);

        using JsonDocument document = JsonDocument.Parse(text); // still valid JSON, with nothing left of the longer file
        Assert.NotNull(document);
        Assert.Equal("short", store.Load().LastPath);
        Assert.Equal(new List<string>(), store.Load().DefaultTargetFormats);
        AssertDefaultOptions(store.Load().StandardizeOptions);
        Assert.Equal(new List<string>(), Leftovers());
    }

    [Fact]
    public void NullsInTheSettingsAreSavedAsEmptyValues()
    {
        AppSettings settings = new AppSettings(null, null);

        Assert.True(Store().Save(settings));

        AppSettings read = Store().Load();
        Assert.Equal("", read.LastPath);
        Assert.Equal(new List<string>(), read.DefaultTargetFormats);
    }

    [Fact]
    public void AFailedSaveReportsItAndLeavesNothingBehind()
    {
        // a non-empty folder in the file's place cannot be replaced by a file
        Directory.CreateDirectory(FilePath());
        File.WriteAllText(Path.Combine(FilePath(), "keep.txt"), "x");

        Assert.False(Store().Save(new AppSettings("x", new List<string>())));

        Assert.True(Directory.Exists(FilePath()));
        Assert.True(File.Exists(Path.Combine(FilePath(), "keep.txt")));
        Assert.Equal(new List<string>(), Leftovers());
    }

    [Fact]
    public void SavesFromSeveralThreadsLeaveAValidFile()
    {
        SettingsStore store = Store();
        Exception failure = null;
        List<Thread> threads = new List<Thread>();
        for (int t = 0; t < 6; t++)
        {
            int id = t;
            Thread thread = new Thread(() =>
            {
                try
                {
                    for (int i = 0; i < 25; i++)
                    {
                        store.Save(new AppSettings("path " + id + "/" + i, new List<string> { "lrc" },
                                new StandardizeOptions()));
                        // never a half-written file
                        Assert.StartsWith("path ", store.Load().LastPath, StringComparison.Ordinal);
                    }
                }
                catch (Exception e)
                {
                    failure = e;
                }
            });
            threads.Add(thread);
            thread.Start();
        }
        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        Assert.Null(failure);
        Assert.StartsWith("path ", Store().Load().LastPath, StringComparison.Ordinal);
        Assert.Equal(new List<string>(), Leftovers());
    }

    // ---- where it lives, and DataAccess ----

    [Fact]
    public void TheDefaultLocationIsUnderTheUserHome()
    {
        UserHome.Path = _dir;
        try
        {
            SettingsStore.AtDefaultLocation().Save(new AppSettings("H:\\Home", new List<string>()));
            Assert.Equal("H:\\Home", SettingsStore.AtDefaultLocation().Load().LastPath);
        }
        finally
        {
            UserHome.Path = null;
        }

        Assert.True(File.Exists(Path.Combine(_dir, ".musicmanager", "settings.json")));
    }

    [Fact]
    public void DataAccessLoadsAndSavesThroughTheStore()
    {
        DataAccessImpl dataAccess = new DataAccessImpl(Store(), new ArtistParseCache(Path.Combine(_dir, "artist-cache.json")));
        AssertDefaults(dataAccess.Load());

        dataAccess.Save(new AppSettings("I:\\Library", new List<string> { "lrc" }, UnusualOptions()));

        AppSettings read = dataAccess.Load();
        Assert.Equal("I:\\Library", read.LastPath);
        Assert.Equal(new List<string> { "lrc" }, read.DefaultTargetFormats);
        AssertSameOptions(UnusualOptions(), read.StandardizeOptions);
        AssertSameOptions(UnusualOptions(), Store().Load().StandardizeOptions);
    }
}
