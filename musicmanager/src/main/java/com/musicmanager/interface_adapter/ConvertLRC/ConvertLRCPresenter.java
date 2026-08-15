package com.musicmanager.interface_adapter.ConvertLRC;

import java.util.List;

import com.musicmanager.use_case.ConvertLRC.ConvertLRCOutputBoundary;
import com.musicmanager.use_case.ConvertLRC.ConvertLRCOutputData;
import com.musicmanager.use_case.ConvertLRC.ConvertLRCProgressData;

public class ConvertLRCPresenter implements ConvertLRCOutputBoundary {
    private final ConvertLRCViewModal viewModal;

    public ConvertLRCPresenter(ConvertLRCViewModal viewModal) {
        this.viewModal = viewModal;
    }

    @Override
    public void present(ConvertLRCOutputData outputData) {
        int successCount = outputData.getSuccessCount();
        int failCount = outputData.getFailCount();
        int totalCount = outputData.getTotalCount();
        List<String> failedPaths = outputData.getFailedPaths();

        ConvertLRCState state = viewModal.getState();
        state.setSuccessCount(successCount);
        state.setFailCount(failCount);
        state.setTotalCount(totalCount);
        state.setFailedPaths(failedPaths);
        state.setMessage(buildMessage(successCount, failCount, totalCount, failedPaths));

        viewModal.setState(state);
        viewModal.firePropertyChange();
    }

    @Override
    public void presentProgress(ConvertLRCProgressData progressData) {
        ConvertLRCState state = viewModal.getState();
        state.setLastProgressMusicPath(progressData.getMusicPath());
        state.setLastProgressSuccess(progressData.isSuccess());

        viewModal.setState(state);
        viewModal.firePropertyChange("progress");
    }

    private String buildMessage(int successCount, int failCount, int totalCount, List<String> failedPaths) {
        if (totalCount == 0) {
            return "No lyric files were found to convert.";
        }

        String message = "Converted " + successCount + "/" + totalCount + " lyric file(s) successfully.";
        if (failCount > 0) {
            message += " " + failCount + " failed: " + String.join(", ", failedPaths);
        }
        return message;
    }
}
