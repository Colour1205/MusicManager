package com.musicmanager.entity;

public class WordTimeStamp {
    private String word;
    private long startTimeMs;
    private long endTimeMs;

    public WordTimeStamp(String word, long startTimeMs, long endTimeMs) {
        this.word = word;
        this.startTimeMs = startTimeMs;
        this.endTimeMs = endTimeMs;
    }

    public String getWord() {
        return word;
    }

    public long getStartTimeMs() {
        return startTimeMs;
    }

    public long getEndTimeMs() {
        return endTimeMs;
    }
}