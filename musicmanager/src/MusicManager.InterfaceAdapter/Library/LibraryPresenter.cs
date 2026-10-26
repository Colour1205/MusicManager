using MusicManager.UseCase.OpenLibrary;

namespace MusicManager.InterfaceAdapter.Library;

public class LibraryPresenter : IOpenLibraryOutputBoundary
{
    private readonly LibraryViewModel _viewModel;

    public LibraryPresenter(LibraryViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public void Present(OpenLibraryOutputData outputData)
    {
        LibraryState state = _viewModel.State;
        state.Path = outputData.Path;
        state.Songs = outputData.Songs;
        state.RefreshedAudioPaths = outputData.OnlyAudioPaths;
        _viewModel.FirePropertyChange(LibraryViewModel.Songs);
    }

    public void PresentProgress(int done, int total)
    {
        LibraryState state = _viewModel.State;
        state.ProgressDone = done;
        state.ProgressTotal = total;
        _viewModel.FirePropertyChange(LibraryViewModel.Progress);
    }

    public void PresentError(string message)
    {
        LibraryState state = _viewModel.State;
        state.ErrorMessage = message;
        _viewModel.FirePropertyChange(LibraryViewModel.Error);
    }
}
