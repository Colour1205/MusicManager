using MusicManager.Entity;
using MusicManager.InterfaceAdapter.Library;
using MusicManager.View.Library;
using Xunit;
using Category = MusicManager.InterfaceAdapter.Library.SongStatus.Category;
using Chip = MusicManager.View.Library.LibraryFilter.Chip;
using Tone = MusicManager.InterfaceAdapter.Library.SongStatus.Tone;

namespace MusicManager.Tests.View.Library;

public class LibraryFilterTest
{
    private static Music Song(string title, string album, string path, params string[] artists)
    {
        return new Music(title, new List<string>(artists), album, path, null);
    }

    private static SongStatus Status(Category category)
    {
        return new SongStatus(category, Tone.Muted, category.ToString().ToUpperInvariant(), new List<string>(), 0, 0, 0);
    }

    private readonly Music lovesick = Song("Lovesick Girls", "THE ALBUM", "C:/m/BLACKPINK - Lovesick Girls.mp3", "BLACKPINK");
    private readonly Music cry = Song("Pretty Savage", "THE ALBUM", "C:/m/02 Pretty Savage.flac", "BLACKPINK", "Selena Gomez");
    private readonly Music halo = Song("Halo", "I Am... Sasha Fierce", "C:/m/Beyonc\u00e9 - Halo.mp3", "Beyonc\u00e9");
    private readonly Music supernova = Song("Supernova", "Armageddon", "C:/m/supernova.mp3", "\uc5d0\uc2a4\ud30c");
    private readonly Music unreadable = Song("track07", "", "C:/m/track07.mp3");
    private readonly List<Music> songs;

    private readonly Dictionary<string, SongStatus> statuses = new();

    public LibraryFilterTest()
    {
        songs = new List<Music> { lovesick, cry, halo, supernova, unreadable };
        statuses[lovesick.Path] = Status(Category.Standard);
        statuses[cry.Path] = Status(Category.NeedsWork);
        statuses[halo.Path] = Status(Category.LineSynced);
        statuses[supernova.Path] = Status(Category.NoLyrics);
        statuses[unreadable.Path] = Status(Category.Problem);
    }

    private List<Music> Filter(string search, Chip? chip)
    {
        Func<Music, bool> predicate = LibraryFilter.Predicate(search, chip, statuses);
        var shown = new List<Music>();
        foreach (Music song in songs)
        {
            if (predicate(song))
            {
                shown.Add(song);
            }
        }
        return shown;
    }

    // ------------------------------------------------------------------ search

    [Fact]
    public void EmptySearchAndAllChipShowEverything()
    {
        Assert.Equal(songs, Filter("", Chip.All));
        Assert.Equal(songs, Filter(null, Chip.All));
        Assert.Equal(songs, Filter("   ", Chip.All));
        Assert.Equal(songs, Filter("", null));
    }

    [Fact]
    public void SearchMatchesTitleArtistAlbumAndFileName()
    {
        Assert.Equal(new List<Music> { lovesick }, Filter("lovesick", Chip.All)); // title (and file name)
        Assert.Equal(new List<Music> { cry }, Filter("gomez", Chip.All)); // second artist
        Assert.Equal(new List<Music> { halo }, Filter("fierce", Chip.All)); // album
        Assert.Equal(new List<Music> { cry }, Filter("02 pretty", Chip.All)); // file name only (no "02" in the title)
        Assert.Equal(new List<Music> { unreadable }, Filter("track07.mp3", Chip.All)); // file name with its extension
    }

    [Fact]
    public void SearchIgnoresCase()
    {
        Assert.Equal(new List<Music> { lovesick, cry }, Filter("BlackPink", Chip.All));
        Assert.Equal(new List<Music> { lovesick, cry }, Filter("BLACKPINK", Chip.All));
        Assert.Equal(new List<Music> { lovesick, cry }, Filter("blackpink", Chip.All));
    }

    [Fact]
    public void SearchIgnoresAccentsBothWays()
    {
        Assert.Equal(new List<Music> { halo }, Filter("beyonce", Chip.All));
        Assert.Equal(new List<Music> { halo }, Filter("BEYONC\u00c9", Chip.All));
        Assert.Equal(new List<Music> { halo }, Filter("Beyonc\u00e9", Chip.All));
        Assert.Equal(new List<Music> { halo }, Filter("bey\u0301once", Chip.All)); // typed with a combining accent
    }

    [Fact]
    public void EveryWordOfTheSearchMustMatchSomewhere()
    {
        Assert.Equal(new List<Music> { cry }, Filter("savage blackpink", Chip.All)); // title + artist, any order
        Assert.Equal(new List<Music> { cry }, Filter("blackpink savage", Chip.All));
        Assert.Equal(new List<Music>(), Filter("blackpink halo", Chip.All));
        Assert.Equal(new List<Music> { lovesick }, Filter("  lovesick   girls ", Chip.All)); // extra spaces do not matter
    }

    [Fact]
    public void SearchWorksForKoreanAndFoldsFullWidthLetters()
    {
        Assert.Equal(new List<Music> { supernova }, Filter("\uc5d0\uc2a4\ud30c", Chip.All));
        Music wide = Song("\uff22\uff2c\uff21\uff23\uff2b\uff30\uff29\uff2e\uff2b", "", "C:/m/x.mp3");
        Assert.True(LibraryFilter.MatchesSearch(wide, "blackpink"));
    }

    [Fact]
    public void KanaMarksAreNotStrippedAsAccents()
    {
        Music ga = Song("\u304c\u3093\u3070\u308c", "", "C:/m/ganbare.mp3");
        Assert.True(LibraryFilter.MatchesSearch(ga, "\u304c"));
        Assert.False(LibraryFilter.MatchesSearch(ga, "\u304b")); // か is not が
    }

    [Fact]
    public void MissingFieldsDoNotBreakTheSearch()
    {
        Music bare = new Music();
        Assert.True(LibraryFilter.MatchesSearch(bare, ""));
        Assert.False(LibraryFilter.MatchesSearch(bare, "x"));
        Music nulls = new Music(null, null, null, null, null);
        Assert.False(LibraryFilter.MatchesSearch(nulls, "x"));
        Assert.True(LibraryFilter.Predicate("", Chip.All, null)(nulls));
    }

    // ------------------------------------------------------------------ chips

    [Fact]
    public void EachChipShowsItsCategory()
    {
        Assert.Equal(new List<Music> { cry }, Filter("", Chip.NeedsWork));
        Assert.Equal(new List<Music> { lovesick }, Filter("", Chip.Standard));
        Assert.Equal(new List<Music> { halo }, Filter("", Chip.LineSynced));
        Assert.Equal(new List<Music> { supernova }, Filter("", Chip.NoLyrics));
        Assert.Equal(new List<Music> { unreadable }, Filter("", Chip.Problems));
    }

    [Fact]
    public void SearchAndChipCombineWithAnd()
    {
        Assert.Equal(new List<Music> { cry }, Filter("blackpink", Chip.NeedsWork));
        Assert.Equal(new List<Music> { lovesick }, Filter("blackpink", Chip.Standard));
        Assert.Equal(new List<Music>(), Filter("blackpink", Chip.LineSynced));
        Assert.Equal(new List<Music>(), Filter("beyonce", Chip.NeedsWork));
    }

    [Fact]
    public void SongsNotCheckedYetAreOnlyInAll()
    {
        var none = new Dictionary<string, SongStatus>();
        Assert.Equal(songs.Count, Count(LibraryFilter.Predicate("", Chip.All, none)));
        foreach (Chip chip in Enum.GetValues<Chip>())
        {
            if (chip != Chip.All)
            {
                Assert.True(0 == Count(LibraryFilter.Predicate("", chip, none)), chip.ToString());
            }
        }
        Assert.Equal(0, Count(LibraryFilter.Predicate("", Chip.NeedsWork, null)));
    }

    private int Count(Func<Music, bool> predicate)
    {
        int n = 0;
        foreach (Music song in songs)
        {
            if (predicate(song))
            {
                n++;
            }
        }
        return n;
    }

    // ------------------------------------------------------------------ counts

    [Fact]
    public void CountsCoverEveryChip()
    {
        Dictionary<Chip, int> counts = LibraryFilter.Counts(songs, statuses, "");
        Assert.Equal(5, counts[Chip.All]);
        Assert.Equal(1, counts[Chip.NeedsWork]);
        Assert.Equal(1, counts[Chip.Standard]);
        Assert.Equal(1, counts[Chip.LineSynced]);
        Assert.Equal(1, counts[Chip.NoLyrics]);
        Assert.Equal(1, counts[Chip.Problems]);
    }

    [Fact]
    public void CountsFollowTheSearchAndAgreeWithThePredicate()
    {
        Dictionary<Chip, int> counts = LibraryFilter.Counts(songs, statuses, "blackpink");
        Assert.Equal(2, counts[Chip.All]);
        Assert.Equal(1, counts[Chip.NeedsWork]);
        Assert.Equal(1, counts[Chip.Standard]);
        Assert.Equal(0, counts[Chip.LineSynced]);
        foreach (Chip chip in Enum.GetValues<Chip>())
        {
            Assert.True(Filter("blackpink", chip).Count == counts[chip], chip.ToString());
        }
    }

    [Fact]
    public void CountsWhileNothingIsCheckedYet()
    {
        Dictionary<Chip, int> counts = LibraryFilter.Counts(songs, new Dictionary<string, SongStatus>(), "");
        Assert.Equal(5, counts[Chip.All]);
        Assert.Equal(0, counts[Chip.NeedsWork]);
        Assert.Equal(0, counts[Chip.Problems]);
        Assert.Equal(Enum.GetValues<Chip>().Length, LibraryFilter.Counts(new List<Music>(), statuses, "").Count);
    }

    [Fact]
    public void ProblemsAreDetected()
    {
        Assert.True(LibraryFilter.HasProblems(songs, statuses));
        statuses[unreadable.Path] = Status(Category.NoLyrics);
        Assert.False(LibraryFilter.HasProblems(songs, statuses));
        Assert.False(LibraryFilter.HasProblems(songs, new Dictionary<string, SongStatus>()));
    }

    // ------------------------------------------------------------------ texts

    [Fact]
    public void ChipLabelsShowTheCountWithThousandsSeparators()
    {
        Assert.Equal("Needs work (1,411)", LibraryFilter.ChipLabel(Chip.NeedsWork, 1411));
        Assert.Equal("All (0)", LibraryFilter.ChipLabel(Chip.All, 0));
        Assert.Equal("No lyrics", LibraryFilter.ChipLabel(Chip.NoLyrics, null));
        Assert.Equal("Problems only (3)", LibraryFilter.ChipLabel(Chip.Problems, 3));
        Assert.Equal("Line-synced (60)", LibraryFilter.ChipLabel(Chip.LineSynced, 60));
    }

    [Fact]
    public void CountCaptionTellsSongsAndSelection()
    {
        Assert.Equal("1,698 songs - 312 selected", LibraryFilter.CountCaption(1698, 1698, 312));
        Assert.Equal("1,698 songs - none selected", LibraryFilter.CountCaption(1698, 1698, 0));
        Assert.Equal("212 of 1,698 songs - 3 selected", LibraryFilter.CountCaption(212, 1698, 3));
        Assert.Equal("1 song - none selected", LibraryFilter.CountCaption(1, 1, 0));
        Assert.Equal("0 of 12 songs - none selected", LibraryFilter.CountCaption(0, 12, 0));
        Assert.Equal("No songs", LibraryFilter.CountCaption(0, 0, 0));
    }

    [Fact]
    public void ProcessButtonNamesWhatItWillProcess()
    {
        Assert.Equal("Process 12 selected", LibraryFilter.ProcessText(12, 1698, 1698));
        Assert.Equal("Process 1 selected", LibraryFilter.ProcessText(1, 40, 1698));
        Assert.Equal("Process all 1,698 songs", LibraryFilter.ProcessText(0, 1698, 1698));
        Assert.Equal("Process all 212 shown songs", LibraryFilter.ProcessText(0, 212, 1698));
        Assert.Equal("Process the song", LibraryFilter.ProcessText(0, 1, 1));
        Assert.Equal("Process the shown song", LibraryFilter.ProcessText(0, 1, 9));
        Assert.Equal("Process songs", LibraryFilter.ProcessText(0, 0, 9));
        Assert.Equal("Process songs", LibraryFilter.ProcessText(0, 0, 0));
        Assert.Equal("Process 4 selected", LibraryFilter.ProcessText(4, 0, 9)); // selected songs can be hidden by a filter
    }

    [Fact]
    public void OptionsCaptionListsWhatIsOn()
    {
        StandardizeOptions options = new StandardizeOptions();
        Assert.Equal("Standardize: lyrics, romaji, artists, albums", LibraryFilter.OptionsCaption(options, null, false));

        options.IsAlbums = false;
        Assert.Equal("Standardize: lyrics, romaji, artists", LibraryFilter.OptionsCaption(options, null, false));

        options.IsRomaji = false;
        Assert.Equal("Standardize: lyrics, artists", LibraryFilter.OptionsCaption(options, null, false));

        options.IsArtists = false;
        Assert.Equal("Standardize: lyrics", LibraryFilter.OptionsCaption(options, null, false));

        options.IsLyrics = false;
        options.IsArtists = true;
        Assert.Equal("Standardize: artists", LibraryFilter.OptionsCaption(options, null, false));

        options.IsArtists = false;
        Assert.Equal("Standardize: nothing selected in Options", LibraryFilter.OptionsCaption(options, null, false));

        options.IsAlbums = true;
        Assert.Equal("Standardize: albums", LibraryFilter.OptionsCaption(options, null, false));
    }

    [Fact]
    public void RomajiIsNotMentionedWhenLyricsAreOff()
    {
        StandardizeOptions options = new StandardizeOptions();
        options.IsLyrics = false;
        options.IsRomaji = true;
        options.IsAlbums = false;
        Assert.Equal("Standardize: artists", LibraryFilter.OptionsCaption(options, null, false));
    }

    [Fact]
    public void ConvertModeOnlyCountsWhenTheFeatureIsOn()
    {
        StandardizeOptions options = new StandardizeOptions();
        options.SetMode(StandardizeOptions.Mode.Convert);
        Assert.Equal("Standardize: lyrics, romaji, artists, albums",
                LibraryFilter.OptionsCaption(options, new List<string> { "LRC" }, false));
        Assert.Equal("Convert formats: TTML, LRC", LibraryFilter.OptionsCaption(options, new List<string> { "TTML", "LRC" }, true));
        Assert.Equal("Convert formats: no format selected", LibraryFilter.OptionsCaption(options, new List<string>(), true));
        Assert.Equal("Convert formats: no format selected", LibraryFilter.OptionsCaption(options, null, true));
    }
}
