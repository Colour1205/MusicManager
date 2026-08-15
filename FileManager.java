package com.colour1205.savelrc;

import java.io.*;
import java.util.*;

public class FileManager {
    private LinkedList<File> LRCFiles;
    private LinkedList<String> TTMLFiles;

    /**
     * Creates a new instance of FileManager
     */
    FileManager() {
        LRCFiles = new LinkedList<>();
        TTMLFiles = new LinkedList<>();
    }

    /**
     * scan all valid files in path
     * 
     * @param path path to scan
     */
    public void scanFiles(String path) {
        File file = new File(path);

        listFiles(file);
    }

    /**
     * save lyrics to file
     * 
     * @param lyrics    lyric to be saved
     * @param file      filed to be saved
     * @param extension format of file
     * @throws Exception
     */
    public void saveToFile(String lyrics, File file, String extension) {
        try {
            String path = file.getPath();
            int locExt = path.lastIndexOf('.');
            path = path.substring(0, locExt);

            PrintWriter w = new PrintWriter(path + "." + extension);
            w.print(lyrics);

            w.close();
        } catch (Exception e) {
        }

    }

    /**
     * method that checks if TTML file exists for file
     * 
     * @param file
     * @return
     */
    public boolean haveTTML(File file) {
        if (TTMLFiles.contains(file.getPath().substring(0, (int) file.getPath().length() - 4)))
            return true;
        return false;
    }

    /**
     * helper method that recursively loops through provided path to add files
     * 
     * @param file file to traverse
     */
    private void listFiles(File file) {
        // base case
        // if file is not a directory
        if (!file.isDirectory()) {
            String FileName = file.getName();
            // if it is LRC file, add to list
            if (FileName.contains(".lrc")) {
                LRCFiles.add(file);
            } else if (FileName.contains(".ttml")) {
                TTMLFiles.add(file.getPath().substring(0, file.getPath().length() - 5));
            }
            return;
        }

        // if file is a directory, look inside it
        for (File f : file.listFiles()) {
            // if is directory, search subdirectories
            listFiles(f);
        }
    }

    /**
     * returns next file in the list
     * 
     * @return
     */
    public File getNextFile() {
        return LRCFiles.poll();
    }

    /**
     * returns if there is next file in list
     * 
     * @return
     */
    public boolean hasNextFile() {
        if (LRCFiles.isEmpty())
            return false;
        return true;
    }

    /**
     * returns the number of files in the list
     * 
     * @return
     */
    public int getTotalFiles() {
        return LRCFiles.size();
    }
}
