package com.musicmanager.use_case.ConvertLRC;

public interface ConvertLRCOutputBoundary {
    /*
     * This method is responsible for presenting the result of the LRC conversion
     * process.
     */
    public void present(ConvertLRCOutputData outputData);
}
