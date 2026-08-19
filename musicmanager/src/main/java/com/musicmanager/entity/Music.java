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
    private byte[] coverArt; // raw embedded artwork image bytes from the file's tag, or null if none

    public Music() {
        this.title = "";
        this.artists = new LinkedList<String>();
        this.album = "";
        this.path = "";
        this.coverArt = null;
    }

    public Music(String title, LinkedList<String> artists, String album, String path, byte[] coverArt) {
        this.title = title;
        this.artists = artists;
        this.album = album;
        this.path = path;
        this.coverArt = coverArt;
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

    public byte[] getCoverArt() {
        return coverArt;
    }

    public void setCoverArt(byte[] coverArt) {
        this.coverArt = coverArt;
    }

    public static class Builder {
        private String title;
        private LinkedList<String> artists;
        private String album;
        private String path;
        private byte[] coverArt;

        public Builder() {
            this.title = "";
            this.artists = new LinkedList<String>();
            this.album = "";
            this.path = "";
            this.coverArt = null;
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

        public Builder setCoverArt(byte[] coverArt) {
            this.coverArt = coverArt;
            return this;
        }

        public Music build() {
            return new Music(title, artists, album, path, coverArt);
        }
    }
}
