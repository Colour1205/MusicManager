using MusicManager.DataAccess;
using MusicManager.Entity;
using MusicManager.UseCase.AddSongs;
using MusicManager.UseCase.StandardizeLyrics;
using Xunit;
using Gateway = MusicManager.DataAccess.DataAccess;

namespace MusicManager.Tests.DataAccess;

/// <summary>
/// Not a port: songs that were added but not confirmed yet wait in a folder of their own, and the data access shows them
/// at the paths they will have in the library, so that the review previews the library as it will be. Confirming moves them
/// in; not confirming throws them away.
/// </summary>
public class PendingAddTest : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "mm-" + Guid.NewGuid());
    private readonly string _library;
    private readonly string _incoming;
    private readonly string _pendingFolder;
    private readonly Gateway _data;

    public PendingAddTest()
    {
        _library = Path.Combine(_base, "library");
        _incoming = Path.Combine(_base, "incoming");
        _pendingFolder = Path.Combine(_base, "pending");
        Directory.CreateDirectory(_library);
        Directory.CreateDirectory(_incoming);
        _data = new Gateway(new SettingsStore(Path.Combine(_base, "settings.json")),
                new ArtistParseCache(Path.Combine(_base, "artists.json")), new AlbumParseCache(Path.Combine(_base, "albums.json")),
                _pendingFolder);
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

    private string LibrarySong(string folder, string name, string artist, string title, string album)
    {
        return TestMp3.Create(Path.Combine(_library, folder), name, artist, title, album);
    }

    private string Incoming(string folder, string name, string artist, string title, string album)
    {
        return TestMp3.Create(Path.Combine(_incoming, folder), name, artist, title, album);
    }

    private AddSongsOutputData Add(params string[] folders)
    {
        return _data.AddSongs(_library, folders.Select(folder => Path.Combine(_incoming, folder)).ToList(), "flac", null);
    }

    private string[] LibraryEntries()
    {
        return Directory.EnumerateFileSystemEntries(_library, "*", SearchOption.AllDirectories).OrderBy(path => path).ToArray();
    }

    private static string Lrc(string audioPath)
    {
        return Path.ChangeExtension(audioPath, ".lrc");
    }

    [Fact]
    public void AddingLeavesTheLibraryAloneAndKeepsTheSongsInAFolderOfTheirOwn()
    {
        LibrarySong("Old", "old.mp3", "Artist", "Old", "Old");
        string[] before = LibraryEntries();
        Incoming("Dl", "new.mp3", "Artist", "New", "New");

        AddSongsOutputData result = Add("Dl");

        Assert.Equal(before, LibraryEntries());
        Assert.Single(result.NewPaths);
        Assert.Single(Directory.EnumerateDirectories(_pendingFolder)); // the staged songs wait there
    }

    [Fact]
    public void TheReadersSeeTheStagedSongsAtTheirFuturePathsAmongTheLibrarys()
    {
        string old = LibrarySong("Old", "old.mp3", "Artist", "Old", "Old Album");
        Incoming("Dl", "new.mp3", "New Artist", "New", "New Album");
        string future = Path.Combine(_library, "Dl", "new.mp3");

        Add("Dl");

        List<SongArtistTags> artists = _data.ReadArtistTags(_library);
        Assert.Equal(new HashSet<string> { old, future }, artists.Select(song => song.Path).ToHashSet());
        Assert.Equal("New Artist", artists.Single(song => song.Path == future).Artist.Single());
        Assert.Equal("New", artists.Single(song => song.Path == future).Title);

        List<SongAlbumTags> albums = _data.ReadAlbumTags(_library);
        Assert.Equal(2, albums.Count);
        Assert.Equal("New Album", albums.Single(song => song.Path == future).Album);

        Assert.Contains(_data.FindSongs(_library), song => song.AudioPath == future);
        Assert.Equal("New", _data.ReadMusicTags(future).Title);
        Assert.False(File.Exists(future)); // it is not in the library: only the readers look there
    }

    [Fact]
    public void TheLyricsOfAStagedSongAreReadFromWhereTheyWait()
    {
        string picked = Incoming("Dl", "new.mp3", "Artist", "New", "Album");
        File.WriteAllText(Lrc(picked), "[00:01.00]hello\n[00:03.00]world\n");
        string future = Path.Combine(_library, "Dl", "new.mp3");

        Add("Dl");

        SongLyricFiles files = _data.FindSongs(_library).Single(song => song.AudioPath == future);
        Assert.Equal(Lrc(future), files.LrcPath);
        Assert.Null(files.TtmlPath);
        Lyric lyric = _data.ReadLyric(files.LrcPath);
        Assert.Equal(2, lyric.Lines.Count);
        Assert.False(File.Exists(Lrc(future)));
    }

    [Fact]
    public void ASongTheLibraryHasWithoutLyricsShowsTheOnesThatWaitForIt()
    {
        string existing = LibrarySong("5-STAR", "Hall of Fame.mp3", "Stray Kids", "Hall of Fame", "5-STAR");
        string picked = Incoming("Dl", "01 - Hall of Fame.mp3", "Stray Kids", "Hall of Fame", "5-STAR");
        File.WriteAllText(Lrc(picked), "[00:01.00]hello\n");

        AddSongsOutputData result = Add("Dl");

        Assert.Equal(new[] { existing }, result.LyricsAddedTo.ToArray());
        Assert.False(File.Exists(Lrc(existing)));
        SongLyricFiles files = Assert.Single(_data.FindSongs(_library), song => song.AudioPath == existing);
        Assert.Equal(Lrc(existing), files.LrcPath);
        Assert.Single(_data.ReadLyric(files.LrcPath).Lines);
    }

    [Fact]
    public void AnotherFolderIsNotAffectedByWhatWaits()
    {
        Incoming("Dl", "new.mp3", "Artist", "New", "Album");
        Add("Dl");
        string other = Path.Combine(_base, "other");
        TestMp3.Create(other, "x.mp3", "Artist", "X", "Album");

        Assert.Single(_data.ReadArtistTags(other));
        Assert.Single(_data.ReadAlbumTags(other));
        Assert.Single(_data.FindSongs(other));
    }

    [Fact]
    public void ConfirmingPutsTheSongsInTheLibraryAndTheReadersSeeThemOnceNotTwice()
    {
        LibrarySong("Old", "old.mp3", "Artist", "Old", "Old");
        Incoming("Dl", "new.mp3", "Artist", "New", "New");
        string future = Path.Combine(_library, "Dl", "new.mp3");
        Add("Dl");

        AddCommitOutputData commit = _data.CommitAdded();

        Assert.True(commit.IsDone);
        Assert.Equal(1, commit.Songs);
        Assert.True(File.Exists(future));
        Assert.Equal(2, _data.ReadArtistTags(_library).Count);
        Assert.Equal(2, _data.FindSongs(_library).Count);
        Assert.Empty(Directory.EnumerateDirectories(_pendingFolder));
        Assert.True(_data.CommitAdded().IsDone); // nothing is waiting any more: nothing to do, and no complaint
        Assert.Equal(0, _data.CommitAdded().Songs);
    }

    [Fact]
    public void NotConfirmingRemovesTheStagedSongsAndTheReadersForgetThem()
    {
        string old = LibrarySong("Old", "old.mp3", "Artist", "Old", "Old");
        string[] before = LibraryEntries();
        Incoming("Dl", "new.mp3", "Artist", "New", "New");
        Add("Dl");

        _data.DiscardAdded();

        Assert.Equal(before, LibraryEntries());
        Assert.Empty(Directory.EnumerateDirectories(_pendingFolder));
        Assert.Equal(new[] { old }, _data.ReadArtistTags(_library).Select(song => song.Path).ToArray());
        Assert.Single(_data.FindSongs(_library));
        Assert.True(File.Exists(Path.Combine(_incoming, "Dl", "new.mp3"))); // what the user picked was never touched
    }

    [Fact]
    public void AddingAgainThrowsAwayAnAddThatWasNeverDecided()
    {
        Incoming("One", "a.mp3", "Artist", "A", "Album");
        Incoming("Two", "b.mp3", "Artist", "B", "Album");

        Add("One");
        Add("Two");

        Assert.Single(Directory.EnumerateDirectories(_pendingFolder));
        List<SongArtistTags> songs = _data.ReadArtistTags(_library);
        Assert.Equal(Path.Combine(_library, "Two", "b.mp3"), Assert.Single(songs).Path);
    }

    [Fact]
    public void NothingWaitsWhenNothingWasStaged()
    {
        LibrarySong("Album", "a.mp3", "Artist", "Song", "Album");
        Incoming("Dl", "b.mp3", "Artist", "Song", "Album"); // the library has it already

        AddSongsOutputData result = Add("Dl");

        Assert.Equal(0, result.CopiedCount);
        Assert.Single(result.AlreadyInLibrary);
        Assert.False(Directory.Exists(_pendingFolder) && Directory.EnumerateDirectories(_pendingFolder).Any());
        Assert.True(_data.CommitAdded().IsDone);
    }

    [Fact]
    public void ASongPickedFromInsideTheLibraryNeedsNoStaging()
    {
        string inside = LibrarySong("Album", "a.mp3", "Artist", "Song", "Album");

        AddSongsOutputData result = _data.AddSongs(_library, new[] { inside }, "flac", null);

        Assert.Equal(new[] { inside }, result.LibraryPaths.ToArray());
        Assert.Empty(result.NewPaths);
        Assert.False(Directory.Exists(_pendingFolder) && Directory.EnumerateDirectories(_pendingFolder).Any());
    }

    [Fact]
    public void WhatAnEarlierRunLeftWaitingIsClearedWhenTheAppStarts()
    {
        Directory.CreateDirectory(Path.Combine(_pendingFolder, "left over", "Album"));
        File.WriteAllText(Path.Combine(_pendingFolder, "left over", "Album", "a.flac"), "audio");
        Directory.CreateDirectory(Path.Combine(_pendingFolder, "another"));

        _data.DiscardUndecidedAdds();

        Assert.Empty(Directory.EnumerateFileSystemEntries(_pendingFolder));
    }

    [Fact]
    public void ClearingLeftoversWithoutAPendingFolderIsFine()
    {
        _data.DiscardUndecidedAdds();

        Assert.False(Directory.Exists(_pendingFolder));
    }

    [Fact]
    public void ASongTheLibraryAddedBeforeIsNotStagedAgain()
    {
        Incoming("Dl", "new.mp3", "Artist", "New", "New");
        Add("Dl");
        _data.CommitAdded();

        AddSongsOutputData again = Add("Dl");

        Assert.Equal(0, again.CopiedCount);
        Assert.Empty(again.NewPaths);
        Assert.Equal(1, again.AlreadyThereCount + again.AlreadyInLibrary.Count);
    }

    [Fact]
    public void ThePathsOfThePlanAreTheOnesTheFilesHaveAfterConfirming()
    {
        Incoming("Dl", "new.mp3", "Artist", "New", "New");
        File.WriteAllText(Path.ChangeExtension(Path.Combine(_incoming, "Dl", "new.mp3"), ".lrc"), "[00:01.00]hi\n");
        Add("Dl");
        var shown = _data.FindSongs(_library).Single();

        _data.CommitAdded();

        Assert.True(File.Exists(shown.AudioPath));
        Assert.True(File.Exists(shown.LrcPath));
        Assert.Equal(shown.AudioPath, _data.ReadArtistTags(_library).Single().Path);
    }
}
