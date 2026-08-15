package com.colour1205.savelrc;

import java.io.*;
import java.util.*;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

public class LyricsManager {
    private LinkedList<String> lines;
    private boolean isFailed = false;

    /**
     * constructor
     * 
     * @param file
     */
    public LyricsManager(File file) {
        lines = new LinkedList<>();
        try {
            readLRC(file);
        } catch (Exception e) {
            System.out.println(e);
        }
    }

    public String convertTranslation() {
        String lyrics = "[offset:700]\n"; // default: add 500 ms offset

        // create a copy of lines
        LinkedList<String> LRClines = new LinkedList<>();
        for (String i : lines) {
            LRClines.add(i);
        }

        int line = 0;

        try {
            while (!LRClines.isEmpty()) {
                line++;
                String thisLine = LRClines.poll().replace("﻿", "");

                // continue if header
                String pattern = "^\\[\\d+:\\d+(?:\\.\\d+)?\\]";
                Pattern regex = Pattern.compile(pattern);
                Matcher matcher = regex.matcher(thisLine);
                if (!matcher.find())
                    continue;

                int a = thisLine.indexOf("]");
                int b = thisLine.lastIndexOf("]");

                // if line not time synced
                if (a == -1 || b == -1) {
                    lyrics = lyrics + thisLine + "\n";
                    continue;
                }

                // debug System.out.println("thisLine: " + thisLine);

                String begTime = thisLine.substring(1, a);

                // if b != a, then lyric is word synced
                if (a == b)
                    lyrics = lyrics + thisLine;
                else {
                    lyrics = lyrics + "[" + begTime + "]";
                    // for each word in line
                    while (thisLine.length() - 1 != a) {
                        String thisWord = thisLine.substring(a + 1, thisLine.substring(a + 1).indexOf("[") + 1 + a);

                        // remove thisWord from thisLine
                        thisLine = thisLine.substring(thisLine.substring(a + 1).indexOf("[") + a + 1);
                        lyrics = lyrics + thisWord;
                    }
                }

                // lyrics = modifyTranslationStyle(lyrics, LRClines, a, b, begTime);
                // deprecated: using new separation in new line with same timestamp

                lyrics = lyrics + "\n";
            }
        } catch (Exception e) {
            e.printStackTrace();
            isFailed = true;
            System.out.println("error line: " + line);
        }

        return lyrics;
    }

    private String modifyTranslationStyle(String lyrics, LinkedList<String> LRClines, int a, int b, String begTime) {
        /* check if next line is translations */
        // if this line contains same timestamp as nextline, then next line is a
        // translation
        if (LRClines.size() > 0) { // if lines has next line
            String nextLine = LRClines.get(0);
            // debug System.out.println("nextLine: " + nextLine);

            String nextLineBegTime = nextLine.substring(1, a);
            if (nextLineBegTime.equals(begTime)) { // this line is a translation
                String translateText;
                if (a == b) { // if line synced
                    translateText = nextLine.substring(a + 1);
                } else { // if word synced
                    translateText = nextLine.substring(a + 1,
                            nextLine.substring(a + 1).indexOf("[") + 1 + a);
                }
                lyrics = lyrics + "^" + translateText;

                LRClines.poll();
            }
        }
        return lyrics;
    }

    /**
     * check if word synced
     * 
     * @return
     */
    public boolean isWordSynced() {
        // create a copy of lines
        LinkedList<String> LRClines = new LinkedList<>();
        for (String i : lines) {
            LRClines.add(i);
        }

        // remove header lines
        String pattern = "^\\[\\d+:\\d+(?:\\.\\d+)?\\]";
        Pattern regex = Pattern.compile(pattern);

        while (!LRClines.isEmpty()) {
            String thisLine = LRClines.poll();
            Matcher matcher = regex.matcher(thisLine);
            if (!matcher.find())
                continue; // skip header lines

            int a = thisLine.indexOf("]");
            int b = thisLine.lastIndexOf("]");

            // if b != a, then lyric is word synced
            if (a != b)
                return true;
        }

        return false;
    }

    /**
     * converts LRC to TTML format
     * 
     * @return
     */
    public String toTTML() {

        try {

            // create a copy of lines
            LinkedList<String> LRClines = new LinkedList<>();
            for (String i : lines) {
                LRClines.add(i);
            }

            // remove header lines
            String pattern = "^\\[\\d+:\\d+(?:\\.\\d+)?\\]";
            Pattern regex = Pattern.compile(pattern);

            while (!LRClines.isEmpty()) {
                String thisLine = LRClines.get(0);
                Matcher matcher = regex.matcher(thisLine);
                if (matcher.find())
                    break; // skip header lines
                LRClines.poll();
            }

            String lyrics;

            // header section
            lyrics = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><tt xmlns=\"http://www.w3.org/ns/ttml\" xmlns:itunes=\"http://music.apple.com/lyric-ttml-internal\" xmlns:ttm=\"http://www.w3.org/ns/ttml#metadata\" itunes:timing=\"Word\">";
            lyrics = lyrics + "\n    <head>";
            lyrics = lyrics + "\n        <metadata>";
            lyrics = lyrics + "\n            <ttm:agent type=\"person\" xml:id=\"v1\"/>";
            lyrics = lyrics + "\n        </metadata>";
            lyrics = lyrics + "\n    </head>";

            // get duration
            String lastLine = LRClines.get(LRClines.size() - 1);
            int beg = lastLine.lastIndexOf("[");
            int end = lastLine.lastIndexOf("]");

            String duration = lastLine.substring(beg + 1, end);

            lyrics = lyrics + "\n    <body dur=\"" + duration + "\">";

            // get begin time
            lyrics = lyrics + "\n        <div begin=\"00:00.000\" end=\"" + duration + "\">";

            // loop body
            int i = 0;
            while (!LRClines.isEmpty()) {
                String thisLine = LRClines.poll();

                // add header for this line
                int a = thisLine.indexOf("]");
                int b = thisLine.lastIndexOf("[");

                // if b <= a, then it is invalid format
                if (b <= a) {
                    isFailed = true;
                    System.out.println("line missing start or end time: " + a + ":" + b);
                    return "";
                }

                String begTime = thisLine.substring(1, a);
                String endTime = thisLine.substring(b + 1, thisLine.length() - 1);

                lyrics = lyrics + "\n            <p begin=\"" + begTime + "\" end=\"" + endTime
                        + "\" ttm:agent=\"v1\" itunes:key=\"L" + (i + 1) + "\">";

                // for each word in line
                while (thisLine.length() - 1 != a) {
                    String thisTime = thisLine.substring(1, a);
                    String thisWord = thisLine.substring(a + 1, thisLine.substring(a + 1).indexOf("[") + 1 + a);

                    // if this word contains ampersand characters, escape them
                    thisWord = thisWord.replace("&", "&amp;");

                    // remove thisWord from thisLine
                    thisLine = thisLine.substring(thisLine.substring(a + 1).indexOf("[") + a + 1);

                    String nextTime = thisLine.substring(1, a);
                    // add to lyrics
                    lyrics = lyrics + "\n                <span begin=\"" + thisTime + "\" end=\"" + nextTime + "\">"
                            + thisWord + "</span>";

                }

                /* check if next line is translations */
                // if this line contains same timestamp as nextline, then next line is a
                // translation
                if (LRClines.size() > 0) { // if lines has next line
                    String nextLine = LRClines.get(0);

                    String nextLineBegTime = nextLine.substring(1, a);
                    if (nextLineBegTime.equals(begTime)) { // this line is a translation
                        String translateText = nextLine.substring(a + 1,
                                nextLine.substring(a + 1).indexOf("[") + 1 + a);
                        lyrics = lyrics + "\n                <span ttm:role=\"x-translation\" xml:lang=\"zh-CN\">"
                                + translateText + "</span>";
                        LRClines.poll();
                    }
                }

                lyrics = lyrics + "\n            </p>";

                i++;

            }

            // closing lines
            lyrics = lyrics + "\n        </div>";
            lyrics = lyrics + "\n    </body>";
            lyrics = lyrics + "\n</tt>";

            // return the convertedLyric
            return lyrics;

        } catch (Exception e) {
            isFailed = true;
            e.printStackTrace();
            Scanner sc = new Scanner(System.in);
            sc.next();
            sc.close();
        }
        return "";
    }

    public String toHMRC(String lyric) {

        Scanner f = new Scanner(lyric);

        // remove header lines
        for (int i = 0; i < 8; i++) {
            if (f.hasNextLine())
                f.nextLine();
        }

        int offset = 0;

        String lyrics = "[offset:" + offset + "]\n";

        int lineBeg = 0; // Declare lineBeg here
        int lineDur = 0;

        int lineNum = 0;

        String debugLine = "";
        while (f.hasNextLine()) {
            lineNum++;

            try {
                String thisLine = f.nextLine();
                debugLine = thisLine;
                // System.out.println(thisLine); // debug

                // start of each line
                if (thisLine.contains("<p begin=")) {

                    int a = thisLine.indexOf("begin=") + 7;
                    String begTime = thisLine.substring(a, a + 9);
                    // System.out.println(begTime); // debug

                    int begMS = convertToMilliseconds(begTime) + offset;
                    lineBeg = begMS;

                    int b = thisLine.indexOf("end=") + 5;
                    String endTime = thisLine.substring(b, b + 9);
                    // System.out.println(endTime); // debug

                    int endMS = convertToMilliseconds(endTime) + offset;

                    int dur = endMS - begMS;

                    lineDur = dur;

                    lyrics += "[" + begMS + "," + dur + "]";
                }

                // body of each line
                if (thisLine.contains("<span begin=")) {
                    int a = thisLine.indexOf("begin=") + 7;
                    String begTime = thisLine.substring(a, a + 9);
                    // System.out.println(begTime); // debug

                    int begMS = convertToMilliseconds(begTime) + offset;

                    int b = thisLine.indexOf("end=") + 5;
                    String endTime = thisLine.substring(b, b + 9);
                    // System.out.println(endTime); // debug

                    int endMS = convertToMilliseconds(endTime) + offset;

                    int dur = endMS - begMS;

                    int relativeBeg = begMS - lineBeg;

                    a = thisLine.indexOf("\">");
                    b = thisLine.indexOf("</span>");

                    String word;

                    if (a == -1 || b == -1) {
                        word = "";
                    } else {
                        word = thisLine.substring(a + 2, b);
                    }

                    lyrics += "<" + relativeBeg + "," + dur + ">" + word;
                }

                // if translation line
                if (thisLine.contains("ttm:role=")) {
                    int a = thisLine.indexOf("\">") + 2;
                    int b = thisLine.indexOf("</span>");
                    lyrics += "<" + lineDur + ",0>^" + thisLine.substring(a, b);
                }

                // end of each line
                if (thisLine.contains("</p>")) {
                    lyrics += "\n";
                }

            } catch (Exception e) {
                System.out.println("line: " + lineNum);
                System.out.println(debugLine);
                e.printStackTrace();
            }

        }

        f.close();

        return lyrics;

    }

    public static int convertToMilliseconds(String timestamp) {
        // Split the timestamp into minutes, seconds, and milliseconds
        String[] parts = timestamp.split(":|\\.");

        int minutes = Integer.parseInt(parts[0]);
        int seconds = Integer.parseInt(parts[1]);
        int milliseconds = Integer.parseInt(parts[2]);

        // Convert the total time to milliseconds
        int totalMilliseconds = (minutes * 60 * 1000) + (seconds * 1000) + milliseconds;
        return totalMilliseconds;
    }

    /**
     * reads lines of LRC from file
     * 
     * @param file
     * @throws Exception
     */
    private void readLRC(File file) throws Exception {
        Scanner lrcRead = new Scanner(file, "UTF-8");

        while (lrcRead.hasNextLine()) {
            lines.add(lrcRead.nextLine());
        }

        lrcRead.close();
    }

    /**
     * getter for failed count
     * 
     * @return
     */
    public boolean isFailed() {
        return isFailed;
    }

}
