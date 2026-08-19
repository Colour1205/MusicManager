package com.musicmanager.entity;

import java.util.List;

/**
 * persisted user preferences: the last music folder browsed to, and the
 * default target formats picked in the settings dialog.
 */
public class AppSettings {
    private String lastPath;
    private List<String> defaultTargetFormats;

    public AppSettings(String lastPath, List<String> defaultTargetFormats) {
        this.lastPath = lastPath;
        this.defaultTargetFormats = defaultTargetFormats;
    }

    public String getLastPath() {
        return lastPath;
    }

    public void setLastPath(String lastPath) {
        this.lastPath = lastPath;
    }

    public List<String> getDefaultTargetFormats() {
        return defaultTargetFormats;
    }

    public void setDefaultTargetFormats(List<String> defaultTargetFormats) {
        this.defaultTargetFormats = defaultTargetFormats;
    }
}
