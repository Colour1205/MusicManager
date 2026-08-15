package com.musicmanager.use_case.ConvertLRC;

import java.util.Map;
import java.util.Queue;

import com.musicmanager.entity.Lyric;
import com.musicmanager.entity.Music;

public interface ConvertLRCDataAccessInterface {

    /**
     * scans path recursively for music files
     *
     * @param path
     * @return
     */
    Queue<Music> getMusics(String path);

    /**
     * finds and parses the lyric file belonging to each Music, detecting
     * whether it is word synced, line synced, or has no sync at all. musics
     * with no lyric file detected are skipped. the returned map preserves
     * which Music each Lyric came from, so callers can report progress back
     * to the right song.
     *
     * @param musics
     * @return
     */
    Map<Music, Lyric> getLRCs(Queue<Music> musics);

    void saveTTML(Lyric lyric, String content);

    void saveHMRC(Lyric lyric, String content);

    /* none word synced LRC, WILL REPLACE FILE */
    void saveLRC(Lyric lyric, String content);
}
