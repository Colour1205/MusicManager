package com.musicmanager.use_case.ConvertLRC;

import java.util.List;

public class ConvertLRCInputData {

    public static final String[] AVAILABLE_FORMATS = { "TTML", "HMRC", "LRC" };

    private final String path;
    private final List<String> targetFormats; // currently only supports "TTML" and "HMRC" and "LRC"

    public ConvertLRCInputData(String path, List<String> targetFormats) {
        this.path = path;
        this.targetFormats = targetFormats;
    }

    public String getPath() {
        return path;
    }

    public List<String> getTargetFormats() {
        return targetFormats;
    }
}