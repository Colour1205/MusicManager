package com.musicmanager.use_case.ConvertLRC;

/**
 * reports the outcome of converting a single song, identified by its music
 * file path, as it happens rather than waiting for the whole batch.
 */
public class ConvertLRCProgressData {
    private final String musicPath;
    private final boolean success;

    public ConvertLRCProgressData(String musicPath, boolean success) {
        this.musicPath = musicPath;
        this.success = success;
    }

    public String getMusicPath() {
        return musicPath;
    }

    public boolean isSuccess() {
        return success;
    }
}
