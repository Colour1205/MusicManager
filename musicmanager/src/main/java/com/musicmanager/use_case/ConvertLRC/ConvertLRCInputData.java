package com.musicmanager.use_case.ConvertLRC;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

import com.musicmanager.entity.Music;

public class ConvertLRCInputData {

    public static final String[] AVAILABLE_FORMATS = { "TTML", "HMRC", "LRC" };

    private final List<Music> musics;
    private final List<String> targetFormats; // currently only supports "TTML" and "HMRC" and "LRC"

    public ConvertLRCInputData(List<Music> musics, List<String> targetFormats) {
        this.musics = musics;
        this.targetFormats = targetFormats;
    }

    public List<Music> getMusics() {
        return musics;
    }

    public List<String> getTargetFormats() {
        return targetFormats;
    }

    /**
     * the single source of truth for "no formats saved/selected yet" fallback
     * behavior, so every screen that needs a default resolves it the same way
     *
     * @param formats
     * @return formats itself if non-empty, otherwise every available format
     */
    public static List<String> orDefaultFormats(List<String> formats) {
        if (formats == null || formats.isEmpty()) {
            return new ArrayList<>(Arrays.asList(AVAILABLE_FORMATS));
        }
        return formats;
    }
}