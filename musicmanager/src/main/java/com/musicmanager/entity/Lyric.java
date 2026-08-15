package com.musicmanager.entity;

import java.util.List;


public class Lyric {
    public enum SyncType {
        LINE_SYNC, WORD_SYNC, NO_SYNC
    }

    private SyncType syncType;
    private List<LyricLine> lines;
    private String path; // path of the source lyric file, used to save converted output next to it

    public Lyric(SyncType syncType, List<LyricLine> lines, String path) {
        this.syncType = syncType;
        this.lines = lines;
        this.path = path;
    }

    public SyncType getSyncType() {
        return syncType;
    }

    public List<LyricLine> getLines() {
        return lines;
    }

    public String getPath() {
        return path;
    }
}


