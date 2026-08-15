package com.musicmanager.entity;

import java.util.LinkedList;

/* Music class represents a music entity in the application. It can be used to store information about a music track, such as its title, artist, album, and path
 * it is expected the lyric file are also under the same path but with a lyric extension. (i.e. .lrc, .txt, etc.)
 */
public class Music {
    private String title;
    private LinkedList<String> artists;
    private String album;
    private String path;
    
    public Music() {
        this.title = "";
        this.artists = new LinkedList<String>();
        this.album = "";
        this.path = "";
    }

    public Music(String title, LinkedList<String> artists, String album, String path) {
        this.title = title;
        this.artists = artists;
        this.album = album;
        this.path = path;
    }

    public String getTitle() {
        return title;
    }

    public void setTitle(String title) {
        this.title = title;
    }

    public LinkedList<String> getArtists() {
        return artists;
    }

    public void setArtists(LinkedList<String> artists) {
        this.artists = artists;
    }

    public String getAlbum() {
        return album;
    }

    public void setAlbum(String album) {
        this.album = album;
    }

    public String getPath() {
        return path;
    }

    public void setPath(String path) {
        this.path = path;
    }

    public static class Builder {
        private String title;
        private LinkedList<String> artists;
        private String album;
        private String path;

        public Builder() {
            this.title = "";
            this.artists = new LinkedList<String>();
            this.album = "";
            this.path = "";
        }

        public Builder setTitle(String title) {
            this.title = title;
            return this;
        }

        public Builder setArtists(LinkedList<String> artists) {
            this.artists = artists;
            return this;
        }

        public Builder setAlbum(String album) {
            this.album = album;
            return this;
        }

        public Builder setPath(String path) {
            this.path = path;
            return this;
        }

        public Music build() {
            return new Music(title, artists, album, path);
        }
    }
}
