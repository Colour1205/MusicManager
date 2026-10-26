namespace MusicManager.InterfaceAdapter.ConvertLRC;

/// <summary>
/// The state held by ConvertLRCViewModal.
/// </summary>
public class ConvertLRCState
{
    public string Message { get; set; }

    public int SuccessCount { get; set; }

    public int FailCount { get; set; }

    public int TotalCount { get; set; }

    public List<string> FailedPaths { get; set; }

    public string LastProgressMusicPath { get; set; }

    public bool IsLastProgressSuccess { get; set; }
}
