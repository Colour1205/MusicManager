package com.musicmanager.interface_adapter.ConvertLRC;

import java.util.List;

/**
 * The state held by ConvertLRCViewModal.
 */
public class ConvertLRCState {
    private String message;
    private int successCount;
    private int failCount;
    private int totalCount;
    private List<String> failedPaths;

    private String lastProgressMusicPath;
    private boolean lastProgressSuccess;

    public String getMessage() {
        return message;
    }

    public void setMessage(String message) {
        this.message = message;
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

    public String getLastProgressMusicPath() {
        return lastProgressMusicPath;
    }

    public void setLastProgressMusicPath(String lastProgressMusicPath) {
        this.lastProgressMusicPath = lastProgressMusicPath;
    }

    public boolean isLastProgressSuccess() {
        return lastProgressSuccess;
    }

    public void setLastProgressSuccess(boolean lastProgressSuccess) {
        this.lastProgressSuccess = lastProgressSuccess;
    }
}
