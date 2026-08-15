package com.musicmanager.entity;

public class LyricLine {
    private String line;
    private long startTimeMs; // line start time
    private long endTimeMs; // line end time
    private WordTimeStamp[] wordTimeStamps; // empty if line synced

    public LyricLine(String line, long startTimeMs, long endTimeMs) {
        this.line = line;
        this.startTimeMs = startTimeMs;
        this.endTimeMs = endTimeMs;
    }

    public void setWordTimeStamps(WordTimeStamp[] wordTimeStamps) {
        this.wordTimeStamps = wordTimeStamps;
    }

    public String getLine() {
        return line;
    }

    public long getStartTimeMs() {
        return startTimeMs;
    }

    public long getEndTimeMs() {
        return endTimeMs;
    }

    public WordTimeStamp[] getWordTimeStamps() {
        return wordTimeStamps;
    }
}
