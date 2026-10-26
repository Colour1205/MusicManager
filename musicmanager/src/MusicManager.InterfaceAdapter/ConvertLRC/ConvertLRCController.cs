using MusicManager.Entity;
using MusicManager.UseCase.ConvertLRC;

namespace MusicManager.InterfaceAdapter.ConvertLRC;

public class ConvertLRCController
{
    private readonly IConvertLRCInputBoundary _interactor;

    public ConvertLRCController(IConvertLRCInputBoundary interactor)
    {
        _interactor = interactor;
    }

    /// <summary>execute ConvertLRC use case for the given songs (all of them, or just one)</summary>
    public void Execute(List<Music> musics, List<string> formats)
    {
        ConvertLRCInputData inputData = new ConvertLRCInputData(musics, formats);
        _interactor.Execute(inputData);
    }
}
