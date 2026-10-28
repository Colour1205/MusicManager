using MusicManager.DataAccess;
using MusicManager.UseCase.AddSongs;
using Xunit;

namespace MusicManager.Tests.DataAccess;

/// <summary>
/// Not a port: staging the songs a user picked for the library folder. Each goes into a folder named after the folder it
/// came from with its lyric files and the cover; nothing is overwritten and the originals stay. The songs wait in a staging
/// folder that is laid out as the library will be: most tests here stage and then commit (the library afterwards), the
/// last group is about what happens before that.
/// </summary>
public class LibraryImporterTest : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "mm-" + Guid.NewGuid());
    private readonly string _library;
    private readonly string _incoming;

    public LibraryImporterTest()
    {
        _library = Path.Combine(_base, "library");
        _incoming = Path.Combine(_base, "incoming");
        Directory.CreateDirectory(_library);
        Directory.CreateDirectory(_incoming);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_base, true);
        }
        catch (IOException)
        {
            // the temp folder is cleaned up by the OS sooner or later
        }
    }

    private string Write(string folder, string name, string content = "audio")
    {
        string dir = Path.Combine(_incoming, folder);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private PendingAdd _pending;
    private int _stagings;

    private PendingAdd NewPending()
    {
        return new PendingAdd(_library, Path.Combine(_base, "staging", (++_stagings).ToString()));
    }

    /// <summary>stages the songs and leaves them staged (the library is not touched)</summary>
    private AddSongsOutputData Stage(string format, params string[] sources)
    {
        _pending = NewPending();
        return LibraryImporter.Add(_pending, sources, format, null);
    }

    /// <summary>puts what was staged into the library, as the user's confirmation does</summary>
    private AddSongsOutputData Commit(AddSongsOutputData result)
    {
        PendingAdd.CommitResult commit = _pending.Commit();
        Assert.Null(commit.Problem);
        return result;
    }

    /// <summary>stages the songs and puts them into the library at once, so that what is checked is the library after an add</summary>
    private AddSongsOutputData Add(params string[] sources)
    {
        return Commit(Stage(PreferredFormat.Default, sources));
    }

    [Fact]
    public void AFolderArrivesAsAFolderOfTheLibraryWithItsLyricsAndCover()
    {
        Write("Palette [FLAC]", "IU - Black Out.flac");
        Write("Palette [FLAC]", "IU - Black Out.lrc", "lyrics");
        Write("Palette [FLAC]", "IU - Jam Jam.flac");
        Write("Palette [FLAC]", "IU - Jam Jam.ttml", "ttml");
        Write("Palette [FLAC]", "cover.jpg", "cover");
        Write("Palette [FLAC]", "notes.txt", "not copied");

        AddSongsOutputData result = Add(Path.Combine(_incoming, "Palette [FLAC]"));

        string folder = Path.Combine(_library, "Palette [FLAC]");
        Assert.Equal(2, result.CopiedCount);
        Assert.Equal(2, result.FoundCount);
        Assert.Equal(0, result.AlreadyThereCount);
        Assert.Empty(result.Problems);
        Assert.Equal(new[] { Path.Combine(folder, "IU - Black Out.flac"), Path.Combine(folder, "IU - Jam Jam.flac") },
                result.LibraryPaths.ToArray());
        Assert.Equal("lyrics", File.ReadAllText(Path.Combine(folder, "IU - Black Out.lrc")));
        Assert.Equal("ttml", File.ReadAllText(Path.Combine(folder, "IU - Jam Jam.ttml")));
        Assert.Equal("cover", File.ReadAllText(Path.Combine(folder, "cover.jpg")));
        Assert.False(File.Exists(Path.Combine(folder, "notes.txt")));
        Assert.True(File.Exists(Path.Combine(_incoming, "Palette [FLAC]", "IU - Black Out.flac"))); // the originals stay
    }

    [Fact]
    public void TheFoldersInsideAFolderBecomeFoldersOfTheirOwn()
    {
        Write(Path.Combine("Artist", "Album One"), "a.flac");
        Write(Path.Combine("Artist", "Album Two", "Disc 1"), "b.flac");
        Write("Artist", "loose.flac");

        AddSongsOutputData result = Add(Path.Combine(_incoming, "Artist"));

        Assert.Equal(3, result.CopiedCount);
        Assert.True(File.Exists(Path.Combine(_library, "Album One", "a.flac")));
        Assert.True(File.Exists(Path.Combine(_library, "Disc 1", "b.flac")));
        Assert.True(File.Exists(Path.Combine(_library, "Artist", "loose.flac")));
    }

    [Fact]
    public void PickedSongsGoToTheFolderTheyCameFrom()
    {
        string one = Write("Some Album", "one.mp3");
        string two = Write("Other Album", "two.flac");

        AddSongsOutputData result = Add(one, two);

        Assert.Equal(2, result.CopiedCount);
        Assert.True(File.Exists(Path.Combine(_library, "Some Album", "one.mp3")));
        Assert.True(File.Exists(Path.Combine(_library, "Other Album", "two.flac")));
    }

    [Theory]
    [InlineData("Downloads")]
    [InlineData("Desktop")]
    [InlineData("music")]
    public void SongsFromAFolderWithNoNameWorthKeepingGoToTheNewSongsFolder(string folder)
    {
        string song = Write(folder, "loose.flac");

        Add(song);

        Assert.True(File.Exists(Path.Combine(_library, LibraryImporter.LooseSongsFolder, "loose.flac")));
    }

    [Fact]
    public void ASongAlreadyInTheLibraryIsNotCopiedButStillComesBack()
    {
        string inside = Path.Combine(_library, "Palette", "IU - Black Out.flac");
        Directory.CreateDirectory(Path.GetDirectoryName(inside));
        File.WriteAllText(inside, "audio");

        AddSongsOutputData result = Add(inside);

        Assert.Equal(0, result.CopiedCount);
        Assert.Equal(1, result.AlreadyThereCount);
        Assert.Equal(inside, Assert.Single(result.LibraryPaths));
    }

    [Fact]
    public void ASongAddedBeforeIsRecognizedByItsNameAndSize()
    {
        string song = Write("Palette", "IU - Black Out.flac", "same");
        Add(song);

        AddSongsOutputData again = Add(song);

        Assert.Equal(0, again.CopiedCount);
        Assert.Equal(1, again.AlreadyThereCount);
        Assert.Equal(Path.Combine(_library, "Palette", "IU - Black Out.flac"), Assert.Single(again.LibraryPaths));
        Assert.Empty(again.Problems);
    }

    [Fact]
    public void AnotherFileWithTheSameNameIsNeverOverwritten()
    {
        string folder = Path.Combine(_library, "Palette");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "IU - Black Out.flac"), "the one in the library");
        string song = Write("Palette", "IU - Black Out.flac", "a different, longer file");

        AddSongsOutputData result = Add(song);

        Assert.Equal("the one in the library", File.ReadAllText(Path.Combine(folder, "IU - Black Out.flac")));
        Assert.Equal(0, result.CopiedCount);
        AddSongsOutputData.Problem problem = Assert.Single(result.Problems);
        Assert.Equal(song, problem.Path);
        Assert.Contains("different file", problem.Detail);
        Assert.Empty(result.LibraryPaths);
    }

    [Fact]
    public void ALyricFileAlreadyThereIsKept()
    {
        string folder = Path.Combine(_library, "Palette");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "a.lrc"), "mine");
        Write("Palette", "a.flac");
        Write("Palette", "a.lrc", "theirs");

        Add(Path.Combine(_incoming, "Palette"));

        Assert.Equal("mine", File.ReadAllText(Path.Combine(folder, "a.lrc")));
    }

    [Fact]
    public void WhatCannotBeAddedIsReportedAndTheRestGoesOn()
    {
        string good = Write("Palette", "good.flac");
        string text = Write("Palette", "notes.txt");
        string missing = Path.Combine(_incoming, "nothing.flac");

        AddSongsOutputData result = Add(text, missing, good);

        Assert.Equal(1, result.CopiedCount);
        Assert.Equal(2, result.Problems.Count);
        Assert.Contains(result.Problems, p => p.Path == text && p.Detail.Contains("not an audio file"));
        Assert.Contains(result.Problems, p => p.Path == missing && p.Detail.Contains("not there"));
        Assert.True(File.Exists(Path.Combine(_library, "Palette", "good.flac")));
    }

    [Fact]
    public void AFolderWithoutAudioFindsNothing()
    {
        Write("Docs", "notes.txt");

        AddSongsOutputData result = Add(Path.Combine(_incoming, "Docs"));

        Assert.Equal(0, result.FoundCount);
        Assert.Empty(result.LibraryPaths);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void ASongPickedTwiceIsCopiedOnce()
    {
        string song = Write("Palette", "a.flac");

        AddSongsOutputData result = Add(song, Path.Combine(_incoming, "Palette"), song);

        Assert.Equal(1, result.FoundCount);
        Assert.Equal(1, result.CopiedCount);
    }

    [Fact]
    public void ProgressIsToldAfterEverySong()
    {
        Write("Palette", "a.flac");
        Write("Palette", "b.flac");
        var told = new List<string>();

        LibraryImporter.Add(NewPending(), new[] { Path.Combine(_incoming, "Palette") }, PreferredFormat.Default,
                (done, total) => told.Add(done + "/" + total));

        Assert.Equal(new[] { "1/2", "2/2" }, told.ToArray());
    }

    // ---- a song in several formats

    private AddSongsOutputData AddPreferring(string format, params string[] sources)
    {
        return Commit(Stage(format, sources));
    }

    [Fact]
    public void FlacIsAddedAndNotTheMp3OfTheSameSongByDefault()
    {
        Write("Album", "01 Song.flac", "flac audio");
        Write("Album", "01 Song.mp3", "mp3");
        Write("Album", "01 Song.lrc", "lyrics");
        Write("Album", "02 Only Mp3.mp3", "mp3 only");

        AddSongsOutputData result = Add(Path.Combine(_incoming, "Album"));

        Assert.Equal(2, result.FoundCount);
        Assert.Equal(2, result.CopiedCount);
        Assert.Equal(1, result.OtherFormatsCount);
        string folder = Path.Combine(_library, "Album");
        Assert.True(File.Exists(Path.Combine(folder, "01 Song.flac")));
        Assert.False(File.Exists(Path.Combine(folder, "01 Song.mp3")));
        Assert.True(File.Exists(Path.Combine(folder, "01 Song.lrc")));
        Assert.True(File.Exists(Path.Combine(folder, "02 Only Mp3.mp3"))); // a song with one format is added whatever it is
    }

    [Fact]
    public void FlacBeatsWavWhenFlacIsPreferred()
    {
        Write("Album", "a.wav", new string('w', 5000));
        Write("Album", "a.flac", "small flac");

        AddSongsOutputData result = AddPreferring("flac", Path.Combine(_incoming, "Album"));

        Assert.True(File.Exists(Path.Combine(_library, "Album", "a.flac")));
        Assert.False(File.Exists(Path.Combine(_library, "Album", "a.wav")));
        Assert.Equal(1, result.OtherFormatsCount);
    }

    [Fact]
    public void ThePreferredFormatIsAddedEvenWhenAnotherIsBigger()
    {
        Write("Album", "a.flac", new string('f', 5000));
        Write("Album", "a.mp3", "small");
        Write("Album", "a.wav", new string('w', 9000));

        AddPreferring("mp3", Path.Combine(_incoming, "Album"));

        Assert.True(File.Exists(Path.Combine(_library, "Album", "a.mp3")));
        Assert.False(File.Exists(Path.Combine(_library, "Album", "a.flac")));
        Assert.False(File.Exists(Path.Combine(_library, "Album", "a.wav")));
    }

    [Fact]
    public void WithNoneInThePreferredFormatTheLargestFileIsAdded()
    {
        // wav is preferred but the folder has only flac and mp3
        Write("Album", "a.flac", new string('f', 5000));
        Write("Album", "a.mp3", new string('m', 800));
        Write("Album", "b.flac", new string('f', 100));
        Write("Album", "b.mp3", new string('m', 900));

        AddSongsOutputData result = AddPreferring("wav", Path.Combine(_incoming, "Album"));

        string folder = Path.Combine(_library, "Album");
        Assert.True(File.Exists(Path.Combine(folder, "a.flac")));
        Assert.False(File.Exists(Path.Combine(folder, "a.mp3")));
        Assert.True(File.Exists(Path.Combine(folder, "b.mp3"))); // here the mp3 happens to be the larger file
        Assert.False(File.Exists(Path.Combine(folder, "b.flac")));
        Assert.Equal(2, result.OtherFormatsCount);
    }

    [Fact]
    public void WavIsAddedWhenWavIsPreferredAndThere()
    {
        Write("Album", "a.flac", new string('f', 5000));
        Write("Album", "a.wav", "tiny");

        AddPreferring("wav", Path.Combine(_incoming, "Album"));

        Assert.True(File.Exists(Path.Combine(_library, "Album", "a.wav")));
        Assert.False(File.Exists(Path.Combine(_library, "Album", "a.flac")));
    }

    [Fact]
    public void TheSameFileNameInAnotherFolderIsAnotherSong()
    {
        Write(Path.Combine("Artist", "One"), "Intro.flac");
        Write(Path.Combine("Artist", "Two"), "Intro.mp3");

        AddSongsOutputData result = Add(Path.Combine(_incoming, "Artist"));

        Assert.Equal(2, result.CopiedCount);
        Assert.Equal(0, result.OtherFormatsCount);
        Assert.True(File.Exists(Path.Combine(_library, "One", "Intro.flac")));
        Assert.True(File.Exists(Path.Combine(_library, "Two", "Intro.mp3")));
    }

    // ---- songs the library has already

    private string LibrarySong(string folder, string name, string artist, string title, string album)
    {
        return TestMp3.Create(Path.Combine(_library, folder), name, artist, title, album);
    }

    private string Incoming(string folder, string name, string artist, string title, string album)
    {
        return TestMp3.Create(Path.Combine(_incoming, folder), name, artist, title, album);
    }

    [Fact]
    public void ASongTheLibraryHasAlreadyIsNotCopiedAgainWhateverItsFileIsCalled()
    {
        string existing = LibrarySong("5-STAR", "Stray Kids - Hall of Fame.mp3", "Stray Kids", "Hall of Fame", "5-STAR");
        Incoming("Stray Kids - 5-STAR", "01 - Hall of Fame.mp3", "Stray Kids", "Hall of Fame", "5-STAR");
        Incoming("Stray Kids - 5-STAR", "02 - S-Class.mp3", "Stray Kids", "S-Class", "5-STAR");

        AddSongsOutputData result = Add(Path.Combine(_incoming, "Stray Kids - 5-STAR"));

        Assert.Equal(2, result.FoundCount);
        Assert.Equal(1, result.CopiedCount);
        AddSongsOutputData.Problem duplicate = Assert.Single(result.AlreadyInLibrary);
        Assert.EndsWith("01 - Hall of Fame.mp3", duplicate.Path);
        Assert.Contains(Path.Combine("5-STAR", "Stray Kids - Hall of Fame.mp3"), duplicate.Detail);
        Assert.False(File.Exists(Path.Combine(_library, "Stray Kids - 5-STAR", "01 - Hall of Fame.mp3")));
        Assert.True(File.Exists(Path.Combine(_library, "Stray Kids - 5-STAR", "02 - S-Class.mp3")));
        Assert.True(File.Exists(existing));
        Assert.Single(result.LibraryPaths);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void ASongTheLibraryHasWithoutLyricsGetsTheLyricsOfTheCopyThatWasLeftOut()
    {
        string existing = LibrarySong("5-STAR", "Stray Kids - Hall of Fame.mp3", "Stray Kids", "Hall of Fame", "5-STAR");
        string picked = Incoming("Dl", "01 - Hall of Fame.mp3", "Stray Kids", "Hall of Fame", "5-STAR");
        File.WriteAllText(Path.ChangeExtension(picked, ".lrc"), "new lyrics");

        AddSongsOutputData result = Add(Path.Combine(_incoming, "Dl"));

        Assert.Equal(0, result.CopiedCount);
        Assert.Single(result.AlreadyInLibrary);
        Assert.Equal("new lyrics", File.ReadAllText(Path.ChangeExtension(existing, ".lrc")));
        Assert.Equal(new[] { existing }, result.LyricsAddedTo.ToArray());
        Assert.Equal(new[] { existing }, result.LibraryPaths.ToArray()); // it is standardized with the new lyrics
        Assert.True(File.Exists(Path.ChangeExtension(picked, ".lrc"))); // the original stays
        Assert.Contains("Lyrics will be added to 1 song that has none.", MusicManager.InterfaceAdapter.AddSongs.AddSongsPresenter.Summary(result));
    }

    [Fact]
    public void ASongTheLibraryHasWithLyricsKeepsThem()
    {
        string existing = LibrarySong("5-STAR", "Stray Kids - Hall of Fame.mp3", "Stray Kids", "Hall of Fame", "5-STAR");
        File.WriteAllText(Path.ChangeExtension(existing, ".lrc"), "my lyrics");
        string picked = Incoming("Dl", "01 - Hall of Fame.mp3", "Stray Kids", "Hall of Fame", "5-STAR");
        File.WriteAllText(Path.ChangeExtension(picked, ".lrc"), "new lyrics");

        AddSongsOutputData result = Add(Path.Combine(_incoming, "Dl"));

        Assert.Equal("my lyrics", File.ReadAllText(Path.ChangeExtension(existing, ".lrc")));
        Assert.Empty(result.LyricsAddedTo);
        Assert.Empty(result.LibraryPaths);
    }

    [Fact]
    public void ALyricFileInAnotherFormatCountsAsLyricsToo()
    {
        string existing = LibrarySong("5-STAR", "Stray Kids - Hall of Fame.mp3", "Stray Kids", "Hall of Fame", "5-STAR");
        File.WriteAllText(Path.ChangeExtension(existing, ".ttml"), "my ttml");
        string picked = Incoming("Dl", "01 - Hall of Fame.mp3", "Stray Kids", "Hall of Fame", "5-STAR");
        File.WriteAllText(Path.ChangeExtension(picked, ".lrc"), "new lyrics");

        AddSongsOutputData result = Add(Path.Combine(_incoming, "Dl"));

        Assert.False(File.Exists(Path.ChangeExtension(existing, ".lrc")));
        Assert.Empty(result.LyricsAddedTo);
    }

    [Fact]
    public void ASongWithNoLyricsOnEitherSideIsLeftAsItIs()
    {
        LibrarySong("5-STAR", "Stray Kids - Hall of Fame.mp3", "Stray Kids", "Hall of Fame", "5-STAR");
        Incoming("Dl", "01 - Hall of Fame.mp3", "Stray Kids", "Hall of Fame", "5-STAR");

        AddSongsOutputData result = Add(Path.Combine(_incoming, "Dl"));

        Assert.Empty(result.LyricsAddedTo);
        Assert.Empty(result.LibraryPaths);
        Assert.DoesNotContain("Lyrics will be added", MusicManager.InterfaceAdapter.AddSongs.AddSongsPresenter.Summary(result));
    }

    [Fact]
    public void TheSameSongOnAnotherAlbumIsAnotherSong()
    {
        LibrarySong("Singles", "Stray Kids - Mixtape Time Out.mp3", "Stray Kids", "Mixtape: Time Out", "Mixtape : Time Out");
        Incoming("5-STAR", "12 - Mixtape - Time Out.mp3", "Stray Kids", "Mixtape: Time Out", "5-STAR");

        AddSongsOutputData result = Add(Path.Combine(_incoming, "5-STAR"));

        Assert.Equal(1, result.CopiedCount);
        Assert.Empty(result.AlreadyInLibrary);
    }

    [Fact]
    public void TheLibraryKnowsASongDespiteCaseAccentsPunctuationAndHowTheArtistsAreJoined()
    {
        LibrarySong("Album", "Artist & Friend - Song.mp3", "Artist & Friend", "SONG (Live)", "Álbum!");
        Incoming("Dl", "x.mp3", "artist/friend", "song live", "album");

        AddSongsOutputData result = Add(Path.Combine(_incoming, "Dl"));

        Assert.Equal(0, result.CopiedCount);
        Assert.Single(result.AlreadyInLibrary);
    }

    [Fact]
    public void ASongWithoutATitleCanNotBeToldFromTheLibrarysAndIsCopied()
    {
        LibrarySong("Album", "a.mp3", "Artist", "Song", "Album");
        TestMp3.Create(Path.Combine(_incoming, "Dl"), "b.mp3", "Artist", null, "Album");

        AddSongsOutputData result = Add(Path.Combine(_incoming, "Dl"));

        Assert.Equal(1, result.CopiedCount);
        Assert.Empty(result.AlreadyInLibrary);
    }

    [Fact]
    public void WhenEveryPickedSongIsInTheLibraryNothingIsCopiedAndNoFolderIsMade()
    {
        LibrarySong("Album", "a.mp3", "Artist", "Song", "Album");
        Incoming("Dl", "b.mp3", "Artist", "Song", "Album");

        AddSongsOutputData result = Add(Path.Combine(_incoming, "Dl"));

        Assert.Equal(1, result.FoundCount);
        Assert.Equal(0, result.CopiedCount);
        Assert.Empty(result.LibraryPaths);
        Assert.False(Directory.Exists(Path.Combine(_library, "Dl")));
        Assert.StartsWith("1 song was not copied, the library has it already:",
                MusicManager.InterfaceAdapter.AddSongs.AddSongsPresenter.Summary(result), StringComparison.Ordinal);
    }

    [Fact]
    public void ProgressCountsTheLibraryBeingReadAndThenTheCopying()
    {
        LibrarySong("Album", "a.mp3", "Artist", "Other", "Album");
        Incoming("Dl", "b.mp3", "Artist", "Song", "Album");
        var told = new List<string>();

        LibraryImporter.Add(NewPending(), new[] { Path.Combine(_incoming, "Dl") }, PreferredFormat.Default,
                (done, total) => told.Add(done + "/" + total));

        Assert.Contains("0/2", told); // the one library song being read, and the one to copy
        Assert.Equal("1/1", told[told.Count - 1]); // then the copy
    }

    // ---- the same song under different names (a folder from a download site)

    private string Tagged(string folder, string name, string artist, string title, int extraFrames = 0)
    {
        return TestMp3.Create(Path.Combine(_incoming, folder), name, artist, title, "Album", null, extraFrames);
    }

    [Fact]
    public void FilesWithTheSameTitleAndArtistInOneFolderAreOneSongWhateverTheyAreCalled()
    {
        Tagged("Song", "Artist - Song.mp3", "Artist", "Song", 0);
        Tagged("Song", "Artist - Song master.mp3", "Artist", "Song", 40);
        Tagged("Song", "Artist-Song.mp3", "Artist", "Song", 10);

        AddSongsOutputData result = Add(Path.Combine(_incoming, "Song"));

        Assert.Equal(1, result.CopiedCount);
        Assert.Equal(2, result.OtherFormatsCount);
        Assert.True(File.Exists(Path.Combine(_library, "Song", "Artist - Song master.mp3"))); // the largest of the same format
    }

    [Fact]
    public void AFileWithoutAReadableTagIsOnlyRecognizedByItsName()
    {
        Tagged("Song", "a.mp3", "Artist", "Song", 5);
        Write("Song", "b.wav", "wave"); // no readable tag, and a name of its own
        Tagged("Song", "c.mp3", "Artist", "Song", 30);

        AddSongsOutputData result = AddPreferring("mp3", Path.Combine(_incoming, "Song"));

        Assert.Equal(2, result.CopiedCount); // the two tagged ones are one song, the wav is another file as far as anyone can tell
        Assert.Equal(1, result.OtherFormatsCount);
        Assert.True(File.Exists(Path.Combine(_library, "Song", "b.wav")));
        Assert.True(File.Exists(Path.Combine(_library, "Song", "c.mp3")));
        Assert.False(File.Exists(Path.Combine(_library, "Song", "a.mp3")));
    }

    [Fact]
    public void DifferentTitlesOrDifferentArtistsOrDifferentFoldersAreDifferentSongs()
    {
        Tagged("Album", "one.mp3", "Artist", "Song One");
        Tagged("Album", "two.mp3", "Artist", "Song Two");
        Tagged("Album", "three.mp3", "Someone Else", "Song One");
        Tagged("Other", "four.mp3", "Artist", "Song One");

        AddSongsOutputData result = Add(Path.Combine(_incoming, "Album"), Path.Combine(_incoming, "Other"));

        Assert.Equal(4, result.CopiedCount);
        Assert.Equal(0, result.OtherFormatsCount);
    }

    [Fact]
    public void TheCaseOfTheNameMakesNoDifference()
    {
        Write("Album", "Song.FLAC", "flac");
        Write("Album", "song.mp3", "mp3");

        AddSongsOutputData result = Add(Path.Combine(_incoming, "Album"));

        Assert.Equal(1, result.CopiedCount);
        Assert.Equal(1, result.OtherFormatsCount);
    }

    [Fact]
    public void TwoFilesPickedByHandAreOneSongToo()
    {
        string flac = Write("Album", "a.flac", "flac");
        string mp3 = Write("Album", "a.mp3", "mp3");

        AddSongsOutputData result = Add(mp3, flac);

        Assert.Equal(1, result.CopiedCount);
        Assert.Equal(1, result.OtherFormatsCount);
        Assert.Equal(Path.Combine(_library, "Album", "a.flac"), Assert.Single(result.LibraryPaths));
    }

    [Fact]
    public void TheDroppedFormatsDoNotCountAsFound()
    {
        Write("Album", "a.flac", "f");
        Write("Album", "a.mp3", "m");

        AddSongsOutputData result = Add(Path.Combine(_incoming, "Album"));

        Assert.Equal(1, result.FoundCount);
        Assert.Equal(1, result.OtherFormatsCount);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void ATooLongDestinationLeavesTheSongOut()
    {
        string folder = new string('x', 120);
        string song = Write(folder, new string('y', 150) + ".flac");
        string library = Path.Combine(_base, new string('l', 40));
        Directory.CreateDirectory(library);

        AddSongsOutputData result = LibraryImporter.Add(new PendingAdd(library, Path.Combine(_base, "staging", "long")),
                new[] { song }, PreferredFormat.Default, null);

        Assert.Equal(0, result.CopiedCount);
        Assert.Contains("characters long", Assert.Single(result.Problems).Detail);
    }

    // ---- nothing goes into the library until the add is confirmed

    private string[] LibraryEntries()
    {
        return Directory.EnumerateFileSystemEntries(_library, "*", SearchOption.AllDirectories).OrderBy(path => path).ToArray();
    }

    [Fact]
    public void StagingChangesNothingInTheLibraryAndReportsThePathsTheSongsWillHave()
    {
        Write("Palette", "a.flac");
        Write("Palette", "a.lrc", "lyrics");
        Write("Palette", "cover.jpg", "cover");

        AddSongsOutputData result = Stage(PreferredFormat.Default, Path.Combine(_incoming, "Palette"));

        Assert.Empty(LibraryEntries()); // not a file, not a folder
        Assert.Equal(1, result.CopiedCount);
        string song = Path.Combine(_library, "Palette", "a.flac");
        Assert.Equal(new[] { song }, result.NewPaths.ToArray());
        Assert.Equal(new[] { song }, result.LibraryPaths.ToArray());
        Assert.True(File.Exists(_pending.StagedPathOf(song)));
        Assert.True(File.Exists(_pending.StagedPathOf(Path.Combine(_library, "Palette", "a.lrc"))));
        Assert.True(File.Exists(_pending.StagedPathOf(Path.Combine(_library, "Palette", "cover.jpg"))));
        Assert.True(File.Exists(Path.Combine(_incoming, "Palette", "a.flac"))); // the originals stay
    }

    [Fact]
    public void ConfirmingMovesEverythingWhereItWasShownAndClearsTheStagingFolder()
    {
        Write("Palette", "a.flac", "audio");
        Write("Palette", "a.lrc", "lyrics");
        Write("Palette", "cover.jpg", "cover");
        Stage(PreferredFormat.Default, Path.Combine(_incoming, "Palette"));

        PendingAdd.CommitResult commit = _pending.Commit();

        Assert.Null(commit.Problem);
        Assert.Equal(3, commit.Files);
        Assert.Equal(1, commit.Songs);
        string folder = Path.Combine(_library, "Palette");
        Assert.Equal("audio", File.ReadAllText(Path.Combine(folder, "a.flac")));
        Assert.Equal("lyrics", File.ReadAllText(Path.Combine(folder, "a.lrc")));
        Assert.Equal("cover", File.ReadAllText(Path.Combine(folder, "cover.jpg")));
        Assert.False(Directory.Exists(_pending.StagingRoot));
    }

    [Fact]
    public void NotConfirmingLeavesTheLibraryExactlyAsItWas()
    {
        LibrarySong("Old", "old.mp3", "Artist", "Old", "Old");
        string[] before = LibraryEntries();
        Write("Palette", "a.flac");
        Stage(PreferredFormat.Default, Path.Combine(_incoming, "Palette"));

        _pending.Discard();

        Assert.Equal(before, LibraryEntries());
        Assert.False(Directory.Exists(_pending.StagingRoot));
        Assert.True(File.Exists(Path.Combine(_incoming, "Palette", "a.flac")));
    }

    [Fact]
    public void AConfirmationIsRefusedWhenAFileOfTheLibraryIsInTheWayAndNothingMoves()
    {
        Write("Palette", "a.flac", "first");
        Write("Palette", "b.flac", "second");
        Stage(PreferredFormat.Default, Path.Combine(_incoming, "Palette"));
        // meanwhile the library got a file of that name
        Directory.CreateDirectory(Path.Combine(_library, "Palette"));
        File.WriteAllText(Path.Combine(_library, "Palette", "b.flac"), "other");

        PendingAdd.CommitResult commit = _pending.Commit();

        Assert.Contains("b.flac", commit.Problem);
        Assert.Contains("nothing was added", commit.Problem);
        Assert.False(File.Exists(Path.Combine(_library, "Palette", "a.flac"))); // all or none
        Assert.Equal("other", File.ReadAllText(Path.Combine(_library, "Palette", "b.flac")));
        Assert.True(File.Exists(_pending.StagedPathOf(Path.Combine(_library, "Palette", "a.flac")))); // still waiting
    }

    [Fact]
    public void AMoveThatFailsHalfWayIsUndone()
    {
        Write("A first", "x.flac", "x");
        Write("Album", "a.flac", "a");
        File.WriteAllText(Path.Combine(_library, "Album"), "a file, where the album's folder would have to be");
        Stage(PreferredFormat.Default, Path.Combine(_incoming, "A first"), Path.Combine(_incoming, "Album"));

        PendingAdd.CommitResult commit = _pending.Commit();

        Assert.NotNull(commit.Problem);
        Assert.False(Directory.Exists(Path.Combine(_library, "A first"))); // the first song went and came back, its folder is gone
        Assert.Equal("a file, where the album's folder would have to be", File.ReadAllText(Path.Combine(_library, "Album")));
        Assert.True(File.Exists(_pending.StagedPathOf(Path.Combine(_library, "A first", "x.flac"))));
        Assert.True(File.Exists(_pending.StagedPathOf(Path.Combine(_library, "Album", "a.flac"))));
    }

    [Fact]
    public void TheLyricsForASongTheLibraryHasWaitWithTheNewSongs()
    {
        string existing = LibrarySong("5-STAR", "Stray Kids - Hall of Fame.mp3", "Stray Kids", "Hall of Fame", "5-STAR");
        string picked = Incoming("Dl", "01 - Hall of Fame.mp3", "Stray Kids", "Hall of Fame", "5-STAR");
        File.WriteAllText(Path.ChangeExtension(picked, ".lrc"), "new lyrics");

        AddSongsOutputData result = Stage(PreferredFormat.Default, Path.Combine(_incoming, "Dl"));

        Assert.False(File.Exists(Path.ChangeExtension(existing, ".lrc"))); // not in the library yet
        Assert.Equal("new lyrics", File.ReadAllText(_pending.StagedPathOf(Path.ChangeExtension(existing, ".lrc"))));
        Assert.Equal(new[] { existing }, result.LyricsAddedTo.ToArray());
        Assert.Empty(result.NewPaths); // no song is added, only its lyrics
    }

    [Fact]
    public void TheSameSongPickedFromTwoPlacesIsStagedOnceAndReportedOnce()
    {
        string one = Write(Path.Combine("One", "Singles"), "a.flac", "same");
        string two = Write(Path.Combine("Two", "Singles"), "a.flac", "same");

        AddSongsOutputData result = Stage(PreferredFormat.Default, one, two);

        Assert.Equal(1, result.CopiedCount);
        Assert.Equal(1, result.AlreadyThereCount);
        Assert.Equal(new[] { Path.Combine(_library, "Singles", "a.flac") }, result.LibraryPaths.ToArray());
    }
}
