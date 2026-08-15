package com.musicmanager.use_case.ConvertLRC;

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
     * with no lyric file detected are skipped.
     *
     * @param musics
     * @return
     */
    Queue<Lyric> getLRCs(Queue<Music> musics);

    void saveTTML(Lyric lyric, String content);

    void saveHMRC(Lyric lyric, String content);

    /* none word synced LRC, WILL REPLACE FILE */
    void saveLRC(Lyric lyric, String content);
}
