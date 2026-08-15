package com.musicmanager.use_case.ConvertLRC;

import java.util.List;
import java.util.Queue;

import com.musicmanager.entity.Lyric;
import com.musicmanager.entity.Music;
import com.musicmanager.entity.LyricLine;
import com.musicmanager.entity.WordTimeStamp;

public class ConvertLRCInteractor implements ConvertLRCInputBoundary {

    private ConvertLRCInputData inputData;
    private ConvertLRCDataAccessInterface dataAccess;

    public void convertLRC(ConvertLRCInputData inputData, ConvertLRCDataAccessInterface dataAccess) {
        this.inputData = inputData;
        this.dataAccess = dataAccess;
    }

    public void execute() {
        String path = inputData.getPath();
        Queue<Music> musics = dataAccess.getMusics(path);
        Queue<Lyric> LRCs = dataAccess.getLRCs(musics);

        while (!LRCs.isEmpty()) {
            Lyric currLyric = LRCs.poll();

        }
    }

    /**
     * converts a Lyric to TTML format, mirroring LyricsManager#toTTML
     *
     * @param lyric
     */
    public void convertToTTML(Lyric lyric) {
        List<LyricLine> lines = lyric.getLines();

        StringBuilder ttml = new StringBuilder();
        ttml.append(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><tt xmlns=\"http://www.w3.org/ns/ttml\" xmlns:itunes=\"http://music.apple.com/lyric-ttml-internal\" xmlns:ttm=\"http://www.w3.org/ns/ttml#metadata\" itunes:timing=\"Word\">");
        ttml.append("\n    <head>");
        ttml.append("\n        <metadata>");
        ttml.append("\n            <ttm:agent type=\"person\" xml:id=\"v1\"/>");
        ttml.append("\n        </metadata>");
        ttml.append("\n    </head>");

        String duration = lines.isEmpty() ? formatTTMLTime(0) : formatTTMLTime(lines.get(lines.size() - 1).getEndTimeMs());

        ttml.append("\n    <body dur=\"").append(duration).append("\">");
        ttml.append("\n        <div begin=\"00:00.000\" end=\"").append(duration).append("\">");

        int i = 0;
        for (LyricLine lyricLine : lines) {
            String begTime = formatTTMLTime(lyricLine.getStartTimeMs());
            String endTime = formatTTMLTime(lyricLine.getEndTimeMs());

            ttml.append("\n            <p begin=\"").append(begTime).append("\" end=\"").append(endTime)
                    .append("\" ttm:agent=\"v1\" itunes:key=\"L").append(i + 1).append("\">");

            WordTimeStamp[] wordTimeStamps = lyricLine.getWordTimeStamps();
            if (wordTimeStamps != null && wordTimeStamps.length > 0) {
                // word synced: emit a <span> per word
                for (WordTimeStamp word : wordTimeStamps) {
                    String wordBeg = formatTTMLTime(word.getStartTimeMs());
                    String wordEnd = formatTTMLTime(word.getEndTimeMs());
                    String text = word.getWord().replace("&", "&amp;");

                    ttml.append("\n                <span begin=\"").append(wordBeg).append("\" end=\"")
                            .append(wordEnd).append("\">").append(text).append("</span>");
                }
            } else {
                // line synced: no per-word timing available, emit the line text directly
                ttml.append(lyricLine.getLine().replace("&", "&amp;"));
            }

            ttml.append("\n            </p>");
            i++;
        }

        ttml.append("\n        </div>");
        ttml.append("\n    </body>");
        ttml.append("\n</tt>");

        dataAccess.saveTTML(lyric, ttml.toString());
    }

    /**
     * converts a Lyric to HMRC format, mirroring LyricsManager#toHMRC
     *
     * @param lyric
     */
    public void convertToHMRC(Lyric lyric) {
        int offset = 0;

        StringBuilder hmrc = new StringBuilder();
        hmrc.append("[offset:").append(offset).append("]\n");

        for (LyricLine lyricLine : lyric.getLines()) {
            long lineBeg = lyricLine.getStartTimeMs() + offset;
            long lineDur = lyricLine.getEndTimeMs() - lyricLine.getStartTimeMs();

            hmrc.append("[").append(lineBeg).append(",").append(lineDur).append("]");

            WordTimeStamp[] wordTimeStamps = lyricLine.getWordTimeStamps();
            if (wordTimeStamps != null) {
                for (WordTimeStamp word : wordTimeStamps) {
                    long wordBeg = word.getStartTimeMs() + offset;
                    long wordDur = word.getEndTimeMs() - word.getStartTimeMs();
                    long relativeBeg = wordBeg - lineBeg;

                    hmrc.append("<").append(relativeBeg).append(",").append(wordDur).append(">")
                            .append(word.getWord());
                }
            }

            hmrc.append("\n");
        }

        dataAccess.saveHMRC(lyric, hmrc.toString());
    }

    /**
     * converts a Lyric down to a line synced (none word synced) LRC, mirroring
     * LyricsManager#convertTranslation. WILL REPLACE FILE
     *
     * @param lyric
     */
    public void convertToLRC(Lyric lyric) {
        int offset = 700; // default offset, matches LyricsManager#convertTranslation

        StringBuilder lrc = new StringBuilder();
        lrc.append("[offset:").append(offset).append("]\n");

        for (LyricLine lyricLine : lyric.getLines()) {
            String begTime = formatLRCTime(lyricLine.getStartTimeMs());
            lrc.append("[").append(begTime).append("]").append(lyricLine.getLine()).append("\n");
        }

        dataAccess.saveLRC(lyric, lrc.toString());
    }

    /**
     * formats a millisecond timestamp as mm:ss.mmm, used by TTML begin/end/dur
     * attributes
     *
     * @param ms
     * @return
     */
    private String formatTTMLTime(long ms) {
        long minutes = ms / 60000;
        long seconds = (ms % 60000) / 1000;
        long millis = ms % 1000;
        return String.format("%02d:%02d.%03d", minutes, seconds, millis);
    }

    /**
     * formats a millisecond timestamp as mm:ss.xx, the standard LRC timestamp
     * format
     *
     * @param ms
     * @return
     */
    private String formatLRCTime(long ms) {
        long minutes = ms / 60000;
        long seconds = (ms % 60000) / 1000;
        long centiseconds = (ms % 1000) / 10;
        return String.format("%02d:%02d.%02d", minutes, seconds, centiseconds);
    }
}
