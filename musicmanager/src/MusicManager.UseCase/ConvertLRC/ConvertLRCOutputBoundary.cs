namespace MusicManager.UseCase.ConvertLRC;

public interface IConvertLRCOutputBoundary
{
    /// <summary>
    /// This method is responsible for presenting the result of the LRC conversion process.
    /// </summary>
    void Present(ConvertLRCOutputData outputData);

    /// <summary>
    /// reports the outcome of converting a single song as soon as it finishes, rather than waiting for the whole batch
    /// to complete.
    /// </summary>
    void PresentProgress(ConvertLRCProgressData progressData);
}
