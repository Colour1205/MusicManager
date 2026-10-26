using System.Diagnostics;
using MusicManager.Entity;
using MusicManager.InterfaceAdapter.Library;
using MusicManager.View.Library;
using Xunit;
using static MusicManager.Tests.View.Library.GridTestSupport;

namespace MusicManager.Tests.View.Library;

/// <summary>The data behind the grid: shown songs, selection and statuses, no UI.</summary>
public class SongGridModelTest
{
    private readonly SongGridModel model;

    public SongGridModelTest()
    {
        model = new SongGridModel();
        model.SetSongs(Songs(10));
    }

    private List<string> ShownPaths()
    {
        var paths = new List<string>();
        foreach (Music music in model.ShownSongs())
        {
            paths.Add(music.Path);
        }
        return paths;
    }

    private static HashSet<string> Paths(params int[] indexes)
    {
        var paths = new HashSet<string>();
        foreach (int i in indexes)
        {
            paths.Add(PathOf(i));
        }
        return paths;
    }

    [Fact]
    public void ShowsAllSongsInOrder()
    {
        Assert.Equal(10, model.SongCount());
        Assert.Equal(10, model.ShownCount());
        Assert.Equal(PathOf(0), model.ShownRow(0).Path);
        Assert.Equal(PathOf(9), model.ShownRow(9).Path);
        Assert.Equal(PathOf(3), ShownPaths()[3]);
    }

    [Fact]
    public void RowsHoldTheTextsOfTheCard()
    {
        SongGridModel.Row row = model.ShownRow(2);
        Assert.Equal("Song 2", row.Title);
        Assert.True("Artist 2, Guest" == row.Artists, "artists are joined with a comma and a space");
        Assert.Equal("Artist 1", model.ShownRow(1).Artists);
    }

    [Fact]
    public void ASongWithoutATitleShowsItsFileName()
    {
        model.SetSongs(new List<Music>
        {
            new Music("", new List<string>(), "", "C:\\music\\Some Band - Some Song.flac", null),
            new Music(null, null, null, "/home/me/music/other.mp3", null)
        });
        Assert.Equal("Some Band - Some Song", model.ShownRow(0).Title);
        Assert.Equal("other", model.ShownRow(1).Title);
        Assert.Equal("", model.ShownRow(1).Artists);
    }

    [Fact]
    public void FilterKeepsTheOrderAndSelectionIsNotTouched()
    {
        model.SetSelectedPaths(Paths(1, 2));
        Assert.True(model.SetFilter(m => m.Title.EndsWith("2", StringComparison.Ordinal) || m.Title.EndsWith("5", StringComparison.Ordinal)));
        Assert.Equal(new List<string> { PathOf(2), PathOf(5) }, ShownPaths());
        Assert.Equal(Paths(1, 2), model.SelectedPaths()); // a hidden song stays selected
        Assert.Equal(2, model.ShownCount());
        Assert.Equal(10, model.SongCount());
        Assert.False(model.SetFilter(m => m.Title.EndsWith("2", StringComparison.Ordinal) || m.Title.EndsWith("5", StringComparison.Ordinal)),
                "same result = no change");
        Assert.True(model.SetFilter(null));
        Assert.Equal(10, model.ShownCount());
    }

    [Fact]
    public void ShownSongsIsASnapshot()
    {
        IReadOnlyList<Music> before = model.ShownSongs();
        model.SetFilter(m => false);
        Assert.True(10 == before.Count, "an earlier list does not change under the caller");
        Assert.Empty(model.ShownSongs());
        // Java: UnsupportedOperationException from remove(0); the .NET read-only wrapper throws NotSupportedException
        Assert.Throws<NotSupportedException>(() => ((IList<Music>)before).RemoveAt(0));
    }

    [Fact]
    public void RefilterFollowsWhatTheFilterLooksAt()
    {
        var wanted = new HashSet<string> { PathOf(1) };
        model.SetFilter(m => wanted.Contains(m.Path));
        Assert.Equal(1, model.ShownCount());
        wanted.Add(PathOf(7));
        Assert.True(model.Refilter());
        Assert.Equal(new List<string> { PathOf(1), PathOf(7) }, ShownPaths());
        Assert.False(model.Refilter());
    }

    [Fact]
    public void SettingSongsKeepsTheSelectionOfSongsThatAreStillThere()
    {
        model.SetSelectedPaths(Paths(1, 5, 8));
        Assert.True(model.SetSongs(Songs(6)), "song 8 left, so the selection changed");
        Assert.Equal(Paths(1, 5), model.SelectedPaths());
        Assert.False(model.SetSongs(Songs(6)), "nothing was lost the second time");
        Assert.Equal(Paths(1, 5), model.SelectedPaths());
        model.SetSongs(null);
        Assert.Equal(0, model.SongCount());
        Assert.Equal(new HashSet<string>(), model.SelectedPaths());
    }

    [Fact]
    public void NullEntriesAreSkipped()
    {
        var list = new List<Music>(Songs(3));
        list.Insert(1, null);
        model.SetSongs(list);
        Assert.Equal(3, model.SongCount());
    }

    [Fact]
    public void MergeReplacesSongsWithTheSamePathKeepingOrderAndSelection()
    {
        model.SetSelectedPaths(Paths(3));
        Music refreshed = new Music("Renamed", new List<string> { "New Artist" }, "New Album", PathOf(3), null);
        Music stranger = Song(99);
        model.MergeSongs(new List<Music> { refreshed, stranger });
        Assert.True(10 == model.SongCount(), "a song that is not in the library is not added");
        Assert.Same(refreshed, model.ShownRow(3).Music);
        Assert.Equal("Renamed", model.ShownRow(3).Title);
        Assert.Equal("New Artist", model.ShownRow(3).Artists);
        Assert.Equal(PathOf(4), model.ShownRow(4).Path);
        Assert.Equal(Paths(3), model.SelectedPaths());
    }

    [Fact]
    public void MergeRunsTheFilterAgainOnTheNewData()
    {
        model.SetFilter(m => m.Title.StartsWith("Song", StringComparison.Ordinal));
        Assert.Equal(10, model.ShownCount());
        model.MergeSongs(new List<Music> { new Music("Other", new List<string>(), "", PathOf(0), null) });
        Assert.Equal(9, model.ShownCount());
        Assert.DoesNotContain(PathOf(0), ShownPaths());
    }

    [Fact]
    public void MergeOfNothingChangesNothing()
    {
        SongGridModel.Row before = model.ShownRow(0);
        model.MergeSongs(new List<Music>());
        model.MergeSongs(null);
        Assert.Same(before, model.ShownRow(0));
    }

    [Fact]
    public void ToggleFlipsOneSong()
    {
        Assert.True(model.Toggle(4));
        Assert.Equal(Paths(4), model.SelectedPaths());
        Assert.True(model.IsSelected(PathOf(4)));
        Assert.True(model.Toggle(4));
        Assert.Equal(new HashSet<string>(), model.SelectedPaths());
        Assert.False(model.Toggle(-1));
        Assert.False(model.Toggle(10));
    }

    [Fact]
    public void ToggleCountsPositionsAmongTheShownSongs()
    {
        model.SetFilter(m => m.Title == "Song 6" || m.Title == "Song 7");
        model.Toggle(1);
        Assert.Equal(Paths(7), model.SelectedPaths());
    }

    [Fact]
    public void SelectAllVisibleSelectsOnlyWhatIsShownAndKeepsTheRest()
    {
        model.SetSelectedPaths(Paths(0));
        model.SetFilter(m => m.Title == "Song 3" || m.Title == "Song 4");
        Assert.True(model.SelectAllVisible());
        Assert.Equal(Paths(0, 3, 4), model.SelectedPaths());
        Assert.False(model.SelectAllVisible(), "nothing new to select");
    }

    [Fact]
    public void ClearSelectionClearsEvenTheHiddenOnes()
    {
        model.SetSelectedPaths(Paths(1, 2));
        model.SetFilter(m => false);
        Assert.True(model.ClearSelection());
        Assert.Equal(0, model.SelectedCount());
        Assert.False(model.ClearSelection());
    }

    [Fact]
    public void SetSelectedPathsIgnoresPathsThatAreNotInTheLibrary()
    {
        Assert.True(model.SetSelectedPaths(new HashSet<string> { PathOf(2), "C:\\nowhere.flac" }));
        Assert.Equal(Paths(2), model.SelectedPaths());
        Assert.False(model.SetSelectedPaths(new HashSet<string> { PathOf(2) }), "the same selection is no change");
        Assert.True(model.SetSelectedPaths(new HashSet<string>()));
        Assert.False(model.SetSelectedPaths(null));
    }

    [Fact]
    public void SelectedPathsIsACopy()
    {
        model.SetSelectedPaths(Paths(1));
        HashSet<string> copy = model.SelectedPaths();
        model.Toggle(2);
        Assert.Equal(Paths(1), copy);
        Assert.Equal(Paths(1, 2), model.SelectedPaths());
        Assert.NotSame(copy, model.SelectedPaths());
    }

    [Fact]
    public void RangeSelectsOrDeselectsBothEndsIncluded()
    {
        Assert.True(model.SetRange(2, 5, true));
        Assert.Equal(Paths(2, 3, 4, 5), model.SelectedPaths());
        Assert.True(model.SetRange(4, 3, false), "the ends may come in either order");
        Assert.Equal(Paths(2, 5), model.SelectedPaths());
        Assert.False(model.SetRange(0, 0, false));
        Assert.True(model.SetRange(8, 50, true), "a range past the end stops at the last song");
        Assert.Equal(Paths(2, 5, 8, 9), model.SelectedPaths());
    }

    [Fact]
    public void SongsWithoutAStatusAreUnknown()
    {
        Assert.Same(SongStatus.Unknown, model.StatusOf(PathOf(1)));
        Assert.Same(SongStatus.Unknown, model.StatusOf(null));
        SongStatus status = new SongStatus(SongStatus.Category.Standard, SongStatus.Tone.Good, "Word-synced", new List<string>(), 0, 0,
                0);
        model.SetStatuses(new Dictionary<string, SongStatus> { [PathOf(1)] = status });
        Assert.Same(status, model.StatusOf(PathOf(1)));
        Assert.Same(SongStatus.Unknown, model.StatusOf(PathOf(2)));
    }

    [Fact]
    public void SettingStatusesReplacesAllOfThem()
    {
        SongStatus a = new SongStatus(SongStatus.Category.Standard, SongStatus.Tone.Good, "A", new List<string>(), 0, 0, 0);
        SongStatus b = new SongStatus(SongStatus.Category.NoLyrics, SongStatus.Tone.Muted, "B", new List<string>(), 0, 0, 0);
        model.SetStatuses(new Dictionary<string, SongStatus> { [PathOf(1)] = a, [PathOf(2)] = a });
        model.SetStatuses(new Dictionary<string, SongStatus> { [PathOf(2)] = b });
        Assert.Same(SongStatus.Unknown, model.StatusOf(PathOf(1)));
        Assert.Same(b, model.StatusOf(PathOf(2)));
        model.SetStatus(PathOf(1), a);
        Assert.Same(a, model.StatusOf(PathOf(1)));
        model.SetStatus(PathOf(1), null);
        Assert.Same(SongStatus.Unknown, model.StatusOf(PathOf(1)));
        model.SetStatuses(null);
        Assert.Same(SongStatus.Unknown, model.StatusOf(PathOf(2)));
    }

    [Fact]
    public void ShownIndexOfFindsPositionsAmongTheShownSongs()
    {
        Assert.Equal(4, model.ShownIndexOf(PathOf(4)));
        model.SetFilter(m => m.Title == "Song 4");
        Assert.Equal(0, model.ShownIndexOf(PathOf(4)));
        Assert.Equal(-1, model.ShownIndexOf(PathOf(5)));
        Assert.Equal(-1, model.ShownIndexOf(null));
    }

    [Fact]
    public void TwoThousandSongsAreCheap()
    {
        List<Music> big = Songs(2000);
        var clock = Stopwatch.StartNew();
        model.SetSongs(big);
        model.SetFilter(m => m.Title.Contains("1", StringComparison.Ordinal));
        model.SelectAllVisible();
        long millis = clock.ElapsedMilliseconds;
        Assert.True(millis < 250, "took " + millis + " ms");
        Assert.Equal(2000, model.SongCount());
    }
}
