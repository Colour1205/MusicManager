namespace MusicManager.UseCase.ConvertLRC;

public class ConvertLRCOutputData
{
    public ConvertLRCOutputData()
    {
        SuccessCount = 0;
        FailCount = 0;
        TotalCount = 0;
        FailedPaths = new List<string>();
    }

    public ConvertLRCOutputData(int successCount, int failCount, int totalCount, List<string> failedPaths)
    {
        SuccessCount = successCount;
        FailCount = failCount;
        TotalCount = totalCount;
        FailedPaths = failedPaths;
    }

    public int SuccessCount { get; set; }

    public int FailCount { get; set; }

    public int TotalCount { get; set; }

    public List<string> FailedPaths { get; set; }
}
