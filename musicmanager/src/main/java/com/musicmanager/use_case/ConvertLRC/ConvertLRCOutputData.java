package com.musicmanager.use_case.ConvertLRC;

import java.util.ArrayList;
import java.util.List;

public class ConvertLRCOutputData {
    private int successCount;
    private int failCount;
    private int totalCount;
    private List<String> failedPaths;

    public ConvertLRCOutputData() {
        this.successCount = 0;
        this.failCount = 0;
        this.totalCount = 0;
        this.failedPaths = new ArrayList<>();
    }

    public ConvertLRCOutputData(int successCount, int failCount, int totalCount, List<String> failedPaths) {
        this.successCount = successCount;
        this.failCount = failCount;
        this.totalCount = totalCount;
        this.failedPaths = failedPaths;
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

    public List<String> getFailedPaths() {
        return failedPaths;
    }

    public void setFailedPaths(List<String> failedPaths) {
        this.failedPaths = failedPaths;
    }

}
