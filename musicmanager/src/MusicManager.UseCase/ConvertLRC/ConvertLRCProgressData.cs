namespace MusicManager.UseCase.ConvertLRC;

/// <summary>
/// reports the outcome of converting a single song, identified by its music file path, as it happens rather than
/// waiting for the whole batch.
/// </summary>
public class ConvertLRCProgressData
{
    public ConvertLRCProgressData(string musicPath, bool success)
    {
        MusicPath = musicPath;
        IsSuccess = success;
    }

    public string MusicPath { get; }

    public bool IsSuccess { get; }
}
