package com.musicmanager.data_access;

import java.io.File;
import java.io.FileNotFoundException;
import java.io.PrintWriter;
import java.util.ArrayList;
import java.util.LinkedList;
import java.util.List;
import java.util.Queue;
import java.util.Scanner;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

import com.musicmanager.entity.Lyric;
import com.musicmanager.entity.Lyric.SyncType;
import com.musicmanager.entity.LyricLine;
import com.musicmanager.entity.Music;
import com.musicmanager.entity.WordTimeStamp;
import com.musicmanager.use_case.ConvertLRC.ConvertLRCDataAccessInterface;

/**
 * single data access gateway for the application, implementing the data
 * access interface(s) declared by each use case. currently implements
 * ConvertLRCDataAccessInterface: scans a directory for music files
 * (mirroring FileManager#listFiles) and parses each one's lyric file into a
 * Lyric entity, detecting its sync type the way LyricsManager#isWordSynced
 * and LyricsManager#toTTML infer it from the LRC timestamp brackets.
 */
public class DataAccess implements ConvertLRCDataAccessInterface {

    private static final String[] AUDIO_EXTENSIONS = { ".mp3", ".flac", ".wav", ".m4a", ".ogg", ".aac" };

    // metadata header lines, e.g. [ar:Artist], [ti:Title], [offset:700]
    private static final Pattern HEADER_PATTERN = Pattern.compile("^\\[[A-Za-z]+:[^\\]]*\\]$");

    // a single LRC timestamp bracket, e.g. [01:23.456] or [01:23]
    private static final Pattern TIME_PATTERN = Pattern.compile("\\[(\\d+):(\\d+)(?:\\.(\\d+))?\\]");

    @Override
    public Queue<Music> getMusics(String path) {
        Queue<Music> musics = new LinkedList<>();
        listMusicFiles(new File(path), musics);
        return musics;
    }

    @Override
    public Queue<Lyric> getLRCs(Queue<Music> musics) {
        Queue<Lyric> lyrics = new LinkedList<>();

        for (Music music : musics) {
            File lrcFile = findLyricFile(music.getPath());

            // if no lyric file is detected for this music, skip it for now
            if (lrcFile == null) {
                continue;
            }

            Lyric lyric = parseLRC(lrcFile);
            if (lyric != null) {
                lyrics.add(lyric);
            }
        }

        return lyrics;
    }

    @Override
    public void saveTTML(Lyric lyric, String content) {
        saveToFile(lyric.getPath(), content, "ttml");
    }

    @Override
    public void saveHMRC(Lyric lyric, String content) {
        saveToFile(lyric.getPath(), content, "hmrc");
    }

    @Override
    public void saveLRC(Lyric lyric, String content) {
        saveToFile(lyric.getPath(), content, "lrc"); // WILL REPLACE FILE
    }

    /**
     * recursively walks a directory, collecting a Music for every audio file
     * found, mirroring FileManager#listFiles
     *
     * @param file
     * @param musics
     */
    private void listMusicFiles(File file, Queue<Music> musics) {
        if (file == null || !file.exists()) {
            return;
        }

        if (!file.isDirectory()) {
            if (isAudioFile(file.getName())) {
                musics.add(new Music.Builder()
                        .setTitle(stripExtension(file.getName()))
                        .setPath(file.getPath())
                        .build());
            }
            return;
        }

        File[] children = file.listFiles();
        if (children == null) {
            return;
        }

        for (File child : children) {
            listMusicFiles(child, musics);
        }
    }

    private boolean isAudioFile(String fileName) {
        String lower = fileName.toLowerCase();
        for (String extension : AUDIO_EXTENSIONS) {
            if (lower.endsWith(extension)) {
                return true;
            }
        }
        return false;
    }

    /**
     * a music's lyric file is expected to sit next to it with the same name
     * and a ".lrc" extension, per Music's javadoc
     *
     * @param musicPath
     * @return the lyric file, or null if none is detected
     */
    private File findLyricFile(String musicPath) {
        File lrcFile = new File(stripExtension(musicPath) + ".lrc");
        return lrcFile.exists() ? lrcFile : null;
    }

    /**
     * parses a .lrc file into a Lyric, classifying it as word synced, line
     * synced, or no sync depending on the timestamp brackets found
     *
     * @param file
     * @return
     */
    private Lyric parseLRC(File file) {
        List<String> rawLines = new ArrayList<>();
        try (Scanner scanner = new Scanner(file, "UTF-8")) {
            while (scanner.hasNextLine()) {
                rawLines.add(scanner.nextLine().replace("﻿", ""));
            }
        } catch (FileNotFoundException e) {
            return null;
        }

        List<ParsedLine> drafts = new ArrayList<>();
        boolean anyWordSynced = false;
        boolean anyLineSynced = false;

        for (String raw : rawLines) {
            String trimmed = raw.trim();
            if (trimmed.isEmpty() || HEADER_PATTERN.matcher(trimmed).matches()) {
                continue;
            }

            Matcher matcher = TIME_PATTERN.matcher(raw);
            List<Long> times = new ArrayList<>();
            List<Integer> ends = new ArrayList<>();
            while (matcher.find()) {
                times.add(toMillis(matcher));
                ends.add(matcher.end());
            }

            if (times.isEmpty()) {
                // no timestamp at all: plain, unsynced lyric text
                drafts.add(new ParsedLine(raw, 0L, 0L, null));
            } else if (times.size() == 1) {
                // one timestamp: line synced, end time resolved in a second pass below
                String text = raw.substring(ends.get(0));
                drafts.add(new ParsedLine(text, times.get(0), null, null));
                anyLineSynced = true;
            } else {
                // 2+ timestamps: word synced, each bracket pair frames one word and the
                // final bracket marks the line's end time, mirroring toTTML's word loop
                List<WordTimeStamp> words = new ArrayList<>();
                StringBuilder text = new StringBuilder();
                for (int i = 0; i < times.size() - 1; i++) {
                    int textStart = ends.get(i);
                    int textEnd = raw.indexOf('[', textStart);
                    String word = textEnd == -1 ? raw.substring(textStart) : raw.substring(textStart, textEnd);
                    words.add(new WordTimeStamp(word, times.get(i), times.get(i + 1)));
                    text.append(word);
                }
                drafts.add(new ParsedLine(text.toString(), times.get(0), times.get(times.size() - 1), words));
                anyWordSynced = true;
            }
        }

        // line synced entries have no explicit end time; borrow the next line's start
        for (int i = 0; i < drafts.size(); i++) {
            ParsedLine draft = drafts.get(i);
            if (draft.endTimeMs == null) {
                draft.endTimeMs = (i + 1 < drafts.size()) ? drafts.get(i + 1).startTimeMs : draft.startTimeMs;
            }
        }

        SyncType syncType;
        if (anyWordSynced) {
            syncType = SyncType.WORD_SYNC;
        } else if (anyLineSynced) {
            syncType = SyncType.LINE_SYNC;
        } else {
            syncType = SyncType.NO_SYNC;
        }

        List<LyricLine> lines = new ArrayList<>();
        for (ParsedLine draft : drafts) {
            LyricLine line = new LyricLine(draft.text, draft.startTimeMs, draft.endTimeMs);
            if (draft.words != null) {
                line.setWordTimeStamps(draft.words.toArray(new WordTimeStamp[0]));
            }
            lines.add(line);
        }

        return new Lyric(syncType, lines, file.getPath());
    }

    private long toMillis(Matcher matcher) {
        long minutes = Long.parseLong(matcher.group(1));
        long seconds = Long.parseLong(matcher.group(2));
        long millis = parseFractionToMillis(matcher.group(3));
        return minutes * 60000 + seconds * 1000 + millis;
    }

    private long parseFractionToMillis(String fraction) {
        if (fraction == null || fraction.isEmpty()) {
            return 0;
        }
        String normalized = fraction.length() >= 3 ? fraction.substring(0, 3) : (fraction + "000").substring(0, 3);
        return Long.parseLong(normalized);
    }

    private String stripExtension(String path) {
        int dot = path.lastIndexOf('.');
        return dot == -1 ? path : path.substring(0, dot);
    }

    private void saveToFile(String sourcePath, String content, String extension) {
        if (sourcePath == null) {
            return;
        }
        try (PrintWriter writer = new PrintWriter(stripExtension(sourcePath) + "." + extension, "UTF-8")) {
            writer.print(content);
        } catch (Exception e) {
            // best effort, mirrors FileManager#saveToFile
        }
    }

    /** intermediate holder for a parsed LRC line before end times are resolved */
    private static class ParsedLine {
        private final String text;
        private final long startTimeMs;
        private Long endTimeMs;
        private final List<WordTimeStamp> words;

        private ParsedLine(String text, long startTimeMs, Long endTimeMs, List<WordTimeStamp> words) {
            this.text = text;
            this.startTimeMs = startTimeMs;
            this.endTimeMs = endTimeMs;
            this.words = words;
        }
    }
}
