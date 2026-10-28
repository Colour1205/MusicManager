using MusicManager.InterfaceAdapter.AddSongs;
using MusicManager.UseCase.AddSongs;
using Xunit;

namespace MusicManager.Tests.InterfaceAdapter.AddSongs;

/// <summary>Not a port: what the add-songs presenter puts in the state, and which events it fires.</summary>
public class AddSongsPresenterTest
{
    private readonly AddSongsViewModel _viewModel = new AddSongsViewModel();
    private readonly List<string> _events = new List<string>();

    private AddSongsPresenter Presenter()
    {
        _viewModel.AddPropertyChangeListener(evt => _events.Add(evt.PropertyName));
        return new AddSongsPresenter(_viewModel);
    }

    private static AddSongsOutputData Output(int copied, int already, int found, params AddSongsOutputData.Problem[] problems)
    {
        var paths = new List<string>();
        for (int i = 0; i < copied + already; i++)
        {
            paths.Add(@"C:\lib\Album\" + i + ".flac");
        }
        return new AddSongsOutputData(paths, copied, already, found, problems.ToList());
    }

    [Fact]
    public void TheAddedSongsAreInTheStateWithASentence()
    {
        Presenter().PresentAdded(Output(3, 0, 3));

        AddSongsState state = _viewModel.State;
        Assert.Equal(new[] { "added" }, _events.ToArray());
        Assert.Equal(3, state.LibraryPaths.Count);
        Assert.Equal(3, state.CopiedCount);
        Assert.Equal(3, state.FoundCount);
        Assert.Equal(0, state.ProblemCount);
        Assert.Equal("3 songs to add to the library.", state.Summary);
    }

    [Fact]
    public void TheStagedSongsAreKeptApartFromTheOnesTheLibraryHasAlready()
    {
        var paths = new List<string> { @"C:\lib\New songs\A.flac", @"C:\lib\Old\B.flac" };
        var staged = new List<string> { @"C:\lib\New songs\A.flac" };

        Presenter().PresentAdded(new AddSongsOutputData(paths, 1, 1, 2, 0, null, null, staged, new List<AddSongsOutputData.Problem>()));

        Assert.Equal(paths, _viewModel.State.LibraryPaths);
        Assert.Equal(staged, _viewModel.State.NewPaths);
    }

    [Fact]
    public void TheResultOfConfirmingIsInTheStateAndFiresItsOwnEvent()
    {
        AddSongsPresenter presenter = Presenter();

        presenter.PresentCommitted(new AddCommitOutputData(5, null));

        Assert.Equal(new[] { "committed" }, _events.ToArray());
        Assert.Equal(5, _viewModel.State.CommittedSongs);
        Assert.Null(_viewModel.State.CommitProblem);

        presenter.PresentCommitted(new AddCommitOutputData(0, "no room"));

        Assert.Equal(0, _viewModel.State.CommittedSongs);
        Assert.Equal("no room", _viewModel.State.CommitProblem);
    }

    [Fact]
    public void SongsInTheLibraryAlreadyAreMentioned()
    {
        Assert.Equal("1 song to add to the library (2 songs were in the library already).", AddSongsPresenter.Summary(Output(1, 2, 3)));
        Assert.Equal("1 song was in the library already.", AddSongsPresenter.Summary(Output(0, 1, 1)));
    }

    [Fact]
    public void WhatCouldNotBeAddedIsListedWithItsReason()
    {
        string summary = AddSongsPresenter.Summary(Output(1, 0, 2, new AddSongsOutputData.Problem(@"C:\in\a.flac", "it is not there")));

        Assert.Equal("1 song to add to the library.\n\n1 song could not be added:\na.flac: it is not there", summary);
    }

    [Fact]
    public void OnlyTheFirstFewProblemsAreSpelledOut()
    {
        var problems = Enumerable.Range(1, 7).Select(i => new AddSongsOutputData.Problem(@"C:\in\" + i + ".flac", "no")).ToArray();

        string summary = AddSongsPresenter.Summary(Output(0, 0, 7, problems));

        Assert.StartsWith("7 songs could not be added:\n1.flac: no", summary, StringComparison.Ordinal);
        Assert.EndsWith("\n... and 2 more", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("6.flac", summary);
    }

    [Fact]
    public void FilesLeftOutForAnotherFormatAreMentioned()
    {
        var paths = new List<string> { @"C:\lib\A.flac", @"C:\lib\A.flac" };

        string one = AddSongsPresenter.Summary(new AddSongsOutputData(paths, 2, 0, 2, 1, new List<AddSongsOutputData.Problem>()));
        string two = AddSongsPresenter.Summary(new AddSongsOutputData(paths, 2, 0, 2, 2, new List<AddSongsOutputData.Problem>()));

        Assert.Equal("2 songs to add to the library.\n\n1 file left out: the same song is there in a format that was chosen instead.", one);
        Assert.Equal("2 songs to add to the library.\n\n2 files left out: the same songs are there in a format that was chosen instead.", two);
    }

    [Fact]
    public void NoAudioAtAllIsSaidPlainly()
    {
        Assert.Equal("There is no audio file in what you picked.", AddSongsPresenter.Summary(Output(0, 0, 0)));
    }

    [Fact]
    public void ProgressAndErrorsAreFired()
    {
        AddSongsPresenter presenter = Presenter();

        presenter.PresentProgress("Adding songs...", 2, 5);
        presenter.PresentError("it broke");

        Assert.Equal(new[] { "progress", "error" }, _events.ToArray());
        Assert.Equal(2, _viewModel.State.ProgressDone);
        Assert.Equal(5, _viewModel.State.ProgressTotal);
        Assert.Equal("it broke", _viewModel.State.ErrorMessage);
    }
}
