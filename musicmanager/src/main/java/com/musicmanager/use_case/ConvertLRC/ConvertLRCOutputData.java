package com.musicmanager.use_case.ConvertLRC;

public class ConvertLRCOutputData {
    private int successCount;
    private int failCount;
    private int totalCount;

    public ConvertLRCOutputData() {
        this.successCount = 0;
        this.failCount = 0;
        this.totalCount = 0;
    }

    public ConvertLRCOutputData(int successCount, int failCount, int totalCount) {
        this.successCount = successCount;
        this.failCount = failCount;
        this.totalCount = totalCount;
    }

    public int getSuccessCount() {
        return successCount;
    }

    public void setSuccessCount(int successCount) {
        this.successCount = successCount;
    }

    public int getFailCount() {
        return failCount;
    }

    public void setFailCount(int failCount) {
        this.failCount = failCount;
    }

    public int getTotalCount() {
        return totalCount;
    }

    public void setTotalCount(int totalCount) {
        this.totalCount = totalCount;
    }

}
