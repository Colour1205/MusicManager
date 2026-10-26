namespace MusicManager.InterfaceAdapter.Standardize;

/// <summary>
/// One controller together with the view model its presenter writes to. The use cases run on the thread that calls the
/// controller and report through that one view model, so two jobs that must not see each other's events (the song
/// window's preview, a scan, a review) each get a pipeline of their own.
/// </summary>
public sealed class StandardizePipeline
{
    private readonly StandardizeController _controller;
    private readonly StandardizeViewModel _viewModel;

    public StandardizePipeline(StandardizeController controller, StandardizeViewModel viewModel)
    {
        _controller = controller;
        _viewModel = viewModel;
    }

    public StandardizeController Controller => _controller;

    public StandardizeViewModel ViewModel => _viewModel;
}
