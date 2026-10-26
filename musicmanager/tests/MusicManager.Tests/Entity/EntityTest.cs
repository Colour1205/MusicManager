using System.Text.Json;
using MusicManager.Entity;
using Xunit;

namespace MusicManager.Tests.Entity;

/// <summary>The entity package has no Java tests; these check the logic its classes carry (defaults, copies, fallbacks).</summary>
public class EntityTest
{
    // ---- StandardizeOptions ----

    [Fact]
    public void StandardizeOptionsStartWithTheDocumentedDefaults()
    {
        StandardizeOptions options = new StandardizeOptions();

        Assert.Equal(StandardizeOptions.Mode.Standardize, options.GetMode());
        Assert.True(options.IsLyrics);
        Assert.True(options.IsRomaji);
        Assert.False(options.IsRecycleBin);
        Assert.True(options.IsArtists);
        Assert.True(options.IsUseClaudeForArtists);
        Assert.Equal("; ", options.Separator);
        Assert.Equal("claude-sonnet-5-5", options.Model);
        Assert.Equal("", options.ApiKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ASeparatorThatIsNullOrEmptyReadsAsTheDefault(string separator)
    {
        StandardizeOptions options = new StandardizeOptions();
        options.Separator = separator;

        Assert.Equal(StandardizeOptions.DefaultSeparator, options.Separator);
    }

    [Fact]
    public void ASeparatorWithTextIsKeptAsIs()
    {
        StandardizeOptions options = new StandardizeOptions();
        options.Separator = " / ";
        Assert.Equal(" / ", options.Separator);

        options.Separator = " "; // only empty falls back; a blank separator is a real choice
        Assert.Equal(" ", options.Separator);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t \n")]
    public void AModelThatIsNullOrBlankReadsAsTheDefault(string model)
    {
        StandardizeOptions options = new StandardizeOptions();
        options.Model = model;

        Assert.Equal(StandardizeOptions.DefaultModel, options.Model);
    }

    [Fact]
    public void AModelIsTrimmedWhenRead()
    {
        StandardizeOptions options = new StandardizeOptions();
        options.Model = "  claude-opus-9  ";

        Assert.Equal("claude-opus-9", options.Model);
    }

    [Fact]
    public void ANullApiKeyReadsAsEmpty()
    {
        StandardizeOptions options = new StandardizeOptions();
        options.ApiKey = "sk-test";
        Assert.Equal("sk-test", options.ApiKey);

        options.ApiKey = null;
        Assert.Equal("", options.ApiKey);
    }

    [Fact]
    public void ANullModeBecomesStandardize()
    {
        StandardizeOptions options = new StandardizeOptions();
        options.SetMode(StandardizeOptions.Mode.Convert);
        Assert.Equal(StandardizeOptions.Mode.Convert, options.GetMode());

        options.SetMode(null);
        Assert.Equal(StandardizeOptions.Mode.Standardize, options.GetMode());
    }

    [Fact]
    public void ClaudeIsUsedForRomajiOrForArtistsWithTheClaudeOption()
    {
        StandardizeOptions options = new StandardizeOptions();
        Assert.True(options.UsesClaude());

        options.IsUseClaudeForArtists = false; // romaji alone is enough
        Assert.True(options.UsesClaude());

        options.IsRomaji = false; // lyrics without romaji and artists without Claude: nothing needs it
        Assert.False(options.UsesClaude());

        options.IsUseClaudeForArtists = true; // artists with the Claude option
        Assert.True(options.UsesClaude());

        options.IsArtists = false;
        Assert.False(options.UsesClaude());

        options.IsRomaji = true; // romaji needs the lyrics option too
        options.IsLyrics = false;
        Assert.False(options.UsesClaude());

        options.IsLyrics = true;
        Assert.True(options.UsesClaude());
    }

    [Fact]
    public void TheConvertModeNeverUsesClaude()
    {
        StandardizeOptions options = new StandardizeOptions();
        options.SetMode(StandardizeOptions.Mode.Convert);

        Assert.False(options.UsesClaude());
    }

    [Fact]
    public void ACopyHasTheSameValuesAndIsIndependent()
    {
        StandardizeOptions original = new StandardizeOptions();
        original.SetMode(StandardizeOptions.Mode.Convert);
        original.IsLyrics = false;
        original.IsRomaji = false;
        original.IsRecycleBin = true;
        original.IsArtists = false;
        original.IsUseClaudeForArtists = false;
        original.Separator = " & ";
        original.Model = "some-model";
        original.ApiKey = "sk-test";

        StandardizeOptions copy = original.Copy();

        Assert.NotSame(original, copy);
        Assert.Equal(StandardizeOptions.Mode.Convert, copy.GetMode());
        Assert.False(copy.IsLyrics);
        Assert.False(copy.IsRomaji);
        Assert.True(copy.IsRecycleBin);
        Assert.False(copy.IsArtists);
        Assert.False(copy.IsUseClaudeForArtists);
        Assert.Equal(" & ", copy.Separator);
        Assert.Equal("some-model", copy.Model);
        Assert.Equal("sk-test", copy.ApiKey);

        copy.SetMode(StandardizeOptions.Mode.Standardize);
        copy.IsLyrics = true;
        copy.Separator = "|";
        copy.ApiKey = "other";
        Assert.Equal(StandardizeOptions.Mode.Convert, original.GetMode());
        Assert.False(original.IsLyrics);
        Assert.Equal(" & ", original.Separator);
        Assert.Equal("sk-test", original.ApiKey);
    }

    [Fact]
    public void ACopyOfUnsetTextValuesStillFallsBackToTheDefaults()
    {
        StandardizeOptions original = new StandardizeOptions();
        original.Separator = null;
        original.Model = null;
        original.ApiKey = null;

        StandardizeOptions copy = original.Copy();

        Assert.Equal(StandardizeOptions.DefaultSeparator, copy.Separator);
        Assert.Equal(StandardizeOptions.DefaultModel, copy.Model);
        Assert.Equal("", copy.ApiKey);
    }

    [Fact]
    public void TheApiKeyIsNeverSerialized()
    {
        StandardizeOptions options = new StandardizeOptions();
        options.ApiKey = "sk-secret-value";

        Assert.DoesNotContain("sk-secret-value", JsonSerializer.Serialize(options));
    }

    // ---- AppSettings ----

    [Fact]
    public void AppSettingsWithoutOptionsGetTheDefaultOptions()
    {
        AppSettings settings = new AppSettings("C:\\music", new List<string> { "lrc" });

        Assert.Equal("C:\\music", settings.LastPath);
        Assert.Equal(new List<string> { "lrc" }, settings.DefaultTargetFormats);
        Assert.NotNull(settings.StandardizeOptions);
        Assert.Equal(StandardizeOptions.Mode.Standardize, settings.StandardizeOptions.GetMode());
    }

    [Fact]
    public void NullOptionsBecomeTheDefaultOptionsInTheConstructorAndTheSetter()
    {
        AppSettings settings = new AppSettings("", new List<string>(), null);
        Assert.NotNull(settings.StandardizeOptions);

        StandardizeOptions given = new StandardizeOptions();
        given.IsRecycleBin = true;
        settings.StandardizeOptions = given;
        Assert.Same(given, settings.StandardizeOptions);

        settings.StandardizeOptions = null;
        Assert.NotNull(settings.StandardizeOptions);
        Assert.NotSame(given, settings.StandardizeOptions);
        Assert.False(settings.StandardizeOptions.IsRecycleBin);
    }

    [Fact]
    public void ACopyOfTheSettingsCanBeEditedWithoutTouchingTheOriginal()
    {
        StandardizeOptions options = new StandardizeOptions();
        options.IsArtists = false;
        AppSettings original = new AppSettings("C:\\music", new List<string> { "lrc", "ttml" }, options);

        AppSettings copy = original.Copy();

        Assert.Equal("C:\\music", copy.LastPath);
        Assert.Equal(new List<string> { "lrc", "ttml" }, copy.DefaultTargetFormats);
        Assert.False(copy.StandardizeOptions.IsArtists);

        copy.LastPath = "D:\\other";
        copy.DefaultTargetFormats.Add("hmrc");
        copy.StandardizeOptions.IsArtists = true;
        Assert.Equal("C:\\music", original.LastPath);
        Assert.Equal(new List<string> { "lrc", "ttml" }, original.DefaultTargetFormats);
        Assert.False(original.StandardizeOptions.IsArtists);
    }

    [Fact]
    public void ACopyKeepsMissingFormatsMissing()
    {
        AppSettings original = new AppSettings("x", null);

        Assert.Null(original.Copy().DefaultTargetFormats);
    }

    // ---- Lyric ----

    [Fact]
    public void ALyricStartsWithNoMetadataNoOffsetAndNoWarnings()
    {
        List<LyricLine> lines = new List<LyricLine> { new LyricLine("la", 0, 1000) };
        Lyric lyric = new Lyric(Lyric.SyncType.LineSync, lines, "C:\\a.lrc");

        Assert.Equal(Lyric.SyncType.LineSync, lyric.GetSyncType());
        Assert.Same(lines, lyric.Lines);
        Assert.Equal("C:\\a.lrc", lyric.Path);
        Assert.Empty(lyric.Metadata);
        Assert.Equal(0L, lyric.OffsetMs);
        Assert.Empty(lyric.Warnings);
    }

    [Fact]
    public void LyricMetadataKeepsTheOrderTheTagsWereAdded()
    {
        Lyric lyric = new Lyric(Lyric.SyncType.NoSync, new List<LyricLine>(), null);
        lyric.Metadata["ti"] = "Title";
        lyric.Metadata["ar"] = "Artist";
        lyric.Metadata["al"] = "Album";
        lyric.Metadata["ti"] = "Changed"; // putting an existing key again keeps its place, as a LinkedHashMap does

        Assert.Equal(new List<string> { "ti", "ar", "al" }, new List<string>(lyric.Metadata.Keys));
        Assert.Equal("Changed", lyric.Metadata["ti"]);
    }

    [Fact]
    public void LyricWarningsAreCollectedInOrderAndLinesAndPathCanBeReplaced()
    {
        Lyric lyric = new Lyric(Lyric.SyncType.WordSync, null, null);
        lyric.AddWarning("first");
        lyric.AddWarning("second");
        lyric.OffsetMs = -250;
        lyric.Path = "C:\\b.lrc";
        List<LyricLine> replaced = new List<LyricLine>();
        lyric.Lines = replaced;

        Assert.Equal(new List<string> { "first", "second" }, lyric.Warnings);
        Assert.Equal(-250L, lyric.OffsetMs);
        Assert.Equal("C:\\b.lrc", lyric.Path);
        Assert.Same(replaced, lyric.Lines);
    }

    // ---- LyricLine / WordTimeStamp ----

    [Fact]
    public void ALineIsWordSyncedOnlyWithAtLeastOneWord()
    {
        LyricLine line = new LyricLine("hello world", 1000, 3000);
        Assert.Equal("hello world", line.Line);
        Assert.Equal(1000L, line.StartTimeMs);
        Assert.Equal(3000L, line.EndTimeMs);
        Assert.False(line.IsWordSynced); // null

        line.WordTimeStamps = new WordTimeStamp[0];
        Assert.False(line.IsWordSynced);

        line.WordTimeStamps = new[] { new WordTimeStamp("hello ", 1000, 2000), new WordTimeStamp("world", 2000, 3000) };
        Assert.True(line.IsWordSynced);
    }

    [Fact]
    public void ALineHasARomanizationOnlyWithAtLeastOneWord()
    {
        LyricLine line = new LyricLine("annyeong", 0, 1000);
        Assert.False(line.HasRomanization()); // null
        Assert.Null(line.RomanizedWords);

        line.RomanizedWords = new WordTimeStamp[0];
        Assert.False(line.HasRomanization());

        line.RomanizedWords = new[] { new WordTimeStamp("annyeong", 0, 1000) };
        Assert.True(line.HasRomanization());
    }

    [Fact]
    public void TranslationsIgnoreNullAndEmptyAndTheSetterKeepsACopy()
    {
        LyricLine line = new LyricLine("hola", 0, 1000);
        Assert.Empty(line.Translations);

        line.AddTranslation(null);
        line.AddTranslation("");
        line.AddTranslation("hello");
        line.AddTranslation(" "); // blank is not empty: it is kept
        Assert.Equal(new List<string> { "hello", " " }, line.Translations);

        List<string> source = new List<string> { "one", "two" };
        line.Translations = source;
        source.Add("three");
        Assert.Equal(new List<string> { "one", "two" }, line.Translations);
        Assert.NotSame(source, line.Translations);

        line.Translations = null;
        Assert.NotNull(line.Translations);
        Assert.Empty(line.Translations);
    }

    [Fact]
    public void AWordTimeStampKeepsItsValues()
    {
        WordTimeStamp word = new WordTimeStamp("la", 1500, 2500);

        Assert.Equal("la", word.Word);
        Assert.Equal(1500L, word.StartTimeMs);
        Assert.Equal(2500L, word.EndTimeMs);
    }

    // ---- Music / SongArtistTags ----

    [Fact]
    public void ADefaultMusicHasEmptyTextAndNoCoverArt()
    {
        Music music = new Music();

        Assert.Equal("", music.Title);
        Assert.Empty(music.Artists);
        Assert.Equal("", music.Album);
        Assert.Equal("", music.Path);
        Assert.Null(music.CoverArt);
    }

    [Fact]
    public void TheBuilderStartsWithTheDefaultsAndBuildsWhatWasSet()
    {
        Music defaults = new Music.Builder().Build();
        Assert.Equal("", defaults.Title);
        Assert.Empty(defaults.Artists);
        Assert.Equal("", defaults.Album);
        Assert.Equal("", defaults.Path);
        Assert.Null(defaults.CoverArt);

        List<string> artists = new List<string> { "BTS", "Coldplay" };
        byte[] cover = { 1, 2, 3 };
        Music music = new Music.Builder().SetTitle("Fix You").SetArtists(artists).SetAlbum("X&Y")
                .SetPath("C:\\music\\fix.flac").SetCoverArt(cover).Build();

        Assert.Equal("Fix You", music.Title);
        Assert.Same(artists, music.Artists);
        Assert.Equal("X&Y", music.Album);
        Assert.Equal("C:\\music\\fix.flac", music.Path);
        Assert.Same(cover, music.CoverArt);
    }

    [Fact]
    public void MusicIsComparedByIdentityLikeTheJavaClass()
    {
        Music a = new Music.Builder().SetTitle("same").Build();
        Music b = new Music.Builder().SetTitle("same").Build();

        Assert.NotEqual(a, b);
        Assert.Equal(a, a);
    }

    [Fact]
    public void SongArtistTagsKeepCopiesOfTheGivenLists()
    {
        List<string> artist = new List<string> { "A" };
        SongArtistTags tags = new SongArtistTags("C:\\a.flac", artist, null, new List<string> { "A", "B" });
        artist.Add("changed later");

        Assert.Equal("C:\\a.flac", tags.Path);
        Assert.Equal(new List<string> { "A" }, tags.Artist);
        Assert.Empty(tags.AlbumArtist);
        Assert.Equal(new List<string> { "A", "B" }, tags.Artists);
    }
}
