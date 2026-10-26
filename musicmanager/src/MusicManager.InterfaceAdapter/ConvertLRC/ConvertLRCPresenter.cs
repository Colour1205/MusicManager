using MusicManager.UseCase.ConvertLRC;

namespace MusicManager.InterfaceAdapter.ConvertLRC;

public class ConvertLRCPresenter : IConvertLRCOutputBoundary
{
    private readonly ConvertLRCViewModal _viewModal;

    public ConvertLRCPresenter(ConvertLRCViewModal viewModal)
    {
        _viewModal = viewModal;
    }

    public void Present(ConvertLRCOutputData outputData)
    {
        int successCount = outputData.SuccessCount;
        int failCount = outputData.FailCount;
        int totalCount = outputData.TotalCount;
        List<string> failedPaths = outputData.FailedPaths;

        ConvertLRCState state = _viewModal.State;
        state.SuccessCount = successCount;
        state.FailCount = failCount;
        state.TotalCount = totalCount;
        state.FailedPaths = failedPaths;
        state.Message = BuildMessage(successCount, failCount, totalCount, failedPaths);

        _viewModal.State = state;
        _viewModal.FirePropertyChange();
    }

    public void PresentProgress(ConvertLRCProgressData progressData)
    {
        ConvertLRCState state = _viewModal.State;
        state.LastProgressMusicPath = progressData.MusicPath;
        state.IsLastProgressSuccess = progressData.IsSuccess;

        _viewModal.State = state;
        _viewModal.FirePropertyChange("progress");
    }

    private static string BuildMessage(int successCount, int failCount, int totalCount, List<string> failedPaths)
    {
        if (totalCount == 0)
        {
            return "No lyric files were found to convert.";
        }

        string message = "Converted " + successCount + "/" + totalCount + " lyric file(s) successfully.";
        if (failCount > 0)
        {
            message += " " + failCount + " failed: " + string.Join(", ", failedPaths);
        }
        return message;
    }
}
