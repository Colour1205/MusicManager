using MusicManager.UseCase.AddSongs;

namespace MusicManager.InterfaceAdapter.AddSongs;

/// <summary>
/// Turns the result of adding songs into the display-ready AddSongsState and fires a property change per kind of event (see
/// AddSongsViewModel's constants). The interactor runs on a background thread, so every list is built fresh and then swapped in.
/// </summary>
public class AddSongsPresenter : IAddSongsOutputBoundary
{
    private const int MaxProblemsShown = 5;

    private readonly AddSongsViewModel _viewModel;

    public AddSongsPresenter(AddSongsViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public void PresentAdded(AddSongsOutputData outputData)
    {
        AddSongsState state = _viewModel.State;
        state.LibraryPaths = new List<string>(outputData.LibraryPaths);
        state.NewPaths = new List<string>(outputData.NewPaths);
        state.LyricsAddedTo = new List<string>(outputData.LyricsAddedTo);
        state.CopiedCount = outputData.CopiedCount;
        state.FoundCount = outputData.FoundCount;
        state.ProblemCount = outputData.Problems.Count;
        state.Summary = Summary(outputData);
        _viewModel.FirePropertyChange(AddSongsViewModel.Added);
    }

    public void PresentCommitted(AddCommitOutputData outputData)
    {
        AddSongsState state = _viewModel.State;
        state.CommittedSongs = outputData.Songs;
        state.CommitProblem = outputData.Problem;
        _viewModel.FirePropertyChange(AddSongsViewModel.Committed);
    }

    /// <summary>what was found, then, if some songs could not be added, which and why</summary>
    public static string Summary(AddSongsOutputData output)
    {
        var parts = new List<string>();
        if (output.FoundCount == 0)
        {
            parts.Add("There is no audio file in what you picked.");
        }
        else if (output.CopiedCount > 0 || output.AlreadyThereCount > 0)
        {
            string text = output.CopiedCount == 0 ? "" : Songs(output.CopiedCount) + " to add to the library";
            text = text.Length == 0 ? "" : char.ToUpperInvariant(text[0]) + text.Substring(1);
            if (output.AlreadyThereCount > 0)
            {
                string already = Songs(output.AlreadyThereCount) + (output.AlreadyThereCount == 1 ? " was" : " were")
                        + " in the library already";
                text += text.Length == 0 ? already[0].ToString().ToUpperInvariant() + already.Substring(1) : " (" + already + ")";
            }
            parts.Add(text + ".");
        }
        if (output.OtherFormatsCount != 0)
        {
            parts.Add(Files(output.OtherFormatsCount) + " left out: the same " + (output.OtherFormatsCount == 1 ? "song is" : "songs are")
                    + " there in a format that was chosen instead.");
        }
        if (output.AlreadyInLibrary.Count != 0)
        {
            List<string> which = output.AlreadyInLibrary.Take(MaxProblemsShown)
                    .Select(song => System.IO.Path.GetFileName(song.Path) + ": " + song.Detail).ToList();
            string more = output.AlreadyInLibrary.Count > MaxProblemsShown
                    ? "\n... and " + (output.AlreadyInLibrary.Count - MaxProblemsShown) + " more" : "";
            parts.Add(Songs(output.AlreadyInLibrary.Count) + (output.AlreadyInLibrary.Count == 1 ? " was" : " were")
                    + " not copied, the library has " + (output.AlreadyInLibrary.Count == 1 ? "it" : "them") + " already:\n"
                    + string.Join("\n", which) + more
                    + (output.LyricsAddedTo.Count == 0 ? "" : "\nLyrics will be added to " + Songs(output.LyricsAddedTo.Count)
                            + (output.LyricsAddedTo.Count == 1 ? " that has none." : " that have none.")));
        }
        if (output.Problems.Count != 0)
        {
            List<string> why = output.Problems.Take(MaxProblemsShown)
                    .Select(problem => System.IO.Path.GetFileName(problem.Path) + ": " + problem.Detail).ToList();
            string more = output.Problems.Count > MaxProblemsShown ? "\n... and " + (output.Problems.Count - MaxProblemsShown) + " more" : "";
            parts.Add(Songs(output.Problems.Count) + " could not be added:\n" + string.Join("\n", why) + more);
        }
        return string.Join("\n\n", parts);
    }

    private static string Songs(int count)
    {
        return count + (count == 1 ? " song" : " songs");
    }

    private static string Files(int count)
    {
        return count + (count == 1 ? " file" : " files");
    }

    public void PresentProgress(string message, int done, int total)
    {
        AddSongsState state = _viewModel.State;
        state.ProgressDone = done;
        state.ProgressTotal = total;
        _viewModel.FirePropertyChange(AddSongsViewModel.Progress);
    }

    public void PresentError(string message)
    {
        AddSongsState state = _viewModel.State;
        state.ErrorMessage = message;
        _viewModel.FirePropertyChange(AddSongsViewModel.Error);
    }
}
