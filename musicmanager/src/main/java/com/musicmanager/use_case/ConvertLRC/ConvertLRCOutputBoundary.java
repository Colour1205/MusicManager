package com.musicmanager.use_case.ConvertLRC;

public interface ConvertLRCOutputBoundary {
    /*
     * This method is responsible for presenting the result of the LRC conversion
     * process.
     */
    public void present(ConvertLRCOutputData outputData);

    /*
     * reports the outcome of converting a single song as soon as it finishes,
     * rather than waiting for the whole batch to complete.
     */
    void presentProgress(ConvertLRCProgressData progressData);
}
