using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using MusicManager.DataAccess;
using MusicManager.Entity;
using TagLib;
using Xunit;
using TagFile = TagLib.File;

namespace MusicManager.Tests.DataAccess;

/// <summary>the real scanner, on tiny generated WAV files that are tagged (and given a cover) with TagLibSharp</summary>
public class LibraryScannerTest : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mm-" + Guid.NewGuid());

    public LibraryScannerTest()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // the temp folder is cleaned up by the OS sooner or later
        }
    }

    // ---- helpers ----

    /// <summary>a short silent WAV file (the scanner only needs something TagLibSharp accepts)</summary>
    private string Wav(string relativePath)
    {
        // Java's Path.resolve spells a relative path with the platform separator
        string file = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        byte[] silence = new byte[800];
        Array.Fill(silence, (byte)0x80); // 8-bit PCM is unsigned: silence is the middle value
        using (MemoryStream wav = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(wav, Encoding.ASCII))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + silence.Length);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16); // size of the format chunk
            writer.Write((short)1); // PCM
            writer.Write((short)1); // mono
            writer.Write(8000); // sample rate
            writer.Write(8000); // bytes per second
            writer.Write((short)1); // bytes per sample frame
            writer.Write((short)8); // bits per sample
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(silence.Length);
            writer.Write(silence);
            writer.Flush();
            System.IO.File.WriteAllBytes(file, wav.ToArray());
        }
        return file;
    }

    private string Tagged(string relativePath, string title, string album, byte[] cover, params string[] artists)
    {
        string file = Wav(relativePath);
        using (TagFile audio = TagFile.Create(file))
        {
            TagLib.Tag tag = audio.GetTag(TagTypes.Id3v2, true);
            if (title != null)
            {
                tag.Title = title;
            }
            if (album != null)
            {
                tag.Album = album;
            }
            if (artists.Length > 0)
            {
                tag.Performers = artists;
            }
            if (cover != null)
            {
                Picture picture = new Picture(new ByteVector(cover));
                picture.MimeType = "image/png";
                picture.Type = PictureType.FrontCover;
                tag.Pictures = new IPicture[] { picture };
            }
            audio.Save();
        }
        return file;
    }

    private static byte[] CoverPng(int width, int height, Color color)
    {
        using (Bitmap image = new Bitmap(width, height, PixelFormat.Format24bppRgb))
        {
            using (Graphics g = Graphics.FromImage(image))
            using (SolidBrush brush = new SolidBrush(color))
            {
                g.FillRectangle(brush, 0, 0, width, height);
            }
            using (MemoryStream output = new MemoryStream())
            {
                image.Save(output, ImageFormat.Png);
                return output.ToArray();
            }
        }
    }

    private List<Music> Scan()
    {
        return LibraryScanner.List(_root, null, null);
    }

    private static List<string> Names(List<Music> songs)
    {
        return songs.Select(m => Path.GetFileName(m.Path)).ToList();
    }

    private static List<string> Titles(List<Music> songs)
    {
        return songs.Select(m => m.Title).ToList();
    }

    // ---- tags ----

    [Fact]
    public void ReadsTitleArtistsAndAlbumFromTheTag()
    {
        Tagged("one.wav", "  Dynamite ", "BE", null, "BTS", "Coldplay");

        List<Music> songs = Scan();

        Assert.Single(songs);
        Music song = songs[0];
        Assert.Equal("Dynamite", song.Title);
        Assert.Equal(new List<string> { "BTS", "Coldplay" }, song.Artists);
        Assert.Equal("BE", song.Album);
        Assert.Equal(Path.Combine(_root, "one.wav"), song.Path);
        Assert.Null(song.CoverArt);
    }

    [Fact]
    public void ATagWithoutATitleFallsBackToTheFileName()
    {
        Tagged("Some Song.wav", null, null, null, "Someone");

        Music song = Scan()[0];

        Assert.Equal("Some Song", song.Title);
        Assert.Equal(new List<string> { "Someone" }, song.Artists);
        Assert.Equal("", song.Album);
    }

    [Fact]
    public void AFileWithoutAnyTagIsStillListedWithItsFileName()
    {
        Wav("plain.wav");

        Music song = Scan()[0];

        Assert.Equal("plain", song.Title);
        Assert.Empty(song.Artists);
        Assert.Null(song.CoverArt);
    }

    [Fact]
    public void AFileThatIsNotReallyAudioIsStillListed()
    {
        System.IO.File.WriteAllBytes(Path.Combine(_root, "broken.mp3"), Encoding.UTF8.GetBytes("this is not an mp3"));
        System.IO.File.WriteAllBytes(Path.Combine(_root, "empty.flac"), new byte[0]);
        Tagged("fine.wav", "Fine", null, null, "Artist");

        List<Music> songs = Scan();

        Assert.Equal(new List<string> { "broken.mp3", "empty.flac", "fine.wav" }, Names(songs));
        Assert.Equal("broken", songs[0].Title);
        Assert.Equal("empty", songs[1].Title);
        Assert.Equal("Fine", songs[2].Title);
        Assert.Null(songs[0].CoverArt);
    }

    // ---- covers ----

    [Fact]
    public void TheCoverBecomesAThumbnailOfAtMost96Pixels()
    {
        Tagged("cover.wav", "With cover", null, CoverPng(600, 600, Color.FromArgb(255, 0, 0)), "A");

        byte[] thumbnail = Scan()[0].CoverArt;

        Assert.NotNull(thumbnail);
        using (Bitmap image = new Bitmap(new MemoryStream(thumbnail)))
        {
            Assert.Equal(96, image.Width);
            Assert.Equal(96, image.Height);
            Assert.True(image.GetPixel(48, 48).R > 230, "still the red cover");
        }
        Assert.True(thumbnail.Length < 20_000, "a few KB, not the raw cover: " + thumbnail.Length);
    }

    [Fact]
    public void SongsWithTheSameCoverShareOneThumbnail()
    {
        byte[] cover = CoverPng(500, 500, Color.FromArgb(0, 255, 0));
        Tagged("album/1.wav", "One", "Album", cover, "A");
        Tagged("album/2.wav", "Two", "Album", cover, "A");
        Tagged("other/3.wav", "Three", "Other", CoverPng(500, 500, Color.FromArgb(0, 0, 255)), "B");

        List<Music> songs = Scan();

        Assert.Same(songs[0].CoverArt, songs[1].CoverArt); // one decode, one array for the album
        Assert.False(songs[0].CoverArt.SequenceEqual(songs[2].CoverArt));
    }

    [Fact]
    public void ADamagedCoverLeavesTheTagIntact()
    {
        Tagged("bad-cover.wav", "Bad cover", "Album", Encoding.UTF8.GetBytes("not an image at all"), "Artist");

        Music song = Scan()[0];

        Assert.Equal("Bad cover", song.Title);
        Assert.Equal("Album", song.Album);
        Assert.Null(song.CoverArt);
    }

    // ---- which files, in which order ----

    [Fact]
    public void OnlyAudioFilesAreListedWhateverTheCaseOfTheExtension()
    {
        Wav("a.wav");
        Wav("b.WAV");
        System.IO.File.WriteAllBytes(Path.Combine(_root, "a.lrc"), Encoding.UTF8.GetBytes("[00:01.00]x"));
        System.IO.File.WriteAllBytes(Path.Combine(_root, "a.ttml"), Encoding.UTF8.GetBytes("<tt/>"));
        System.IO.File.WriteAllBytes(Path.Combine(_root, "notes.txt"), Encoding.UTF8.GetBytes("x"));
        System.IO.File.WriteAllBytes(Path.Combine(_root, "cover.jpg"), new byte[] { 1 });

        Assert.Equal(new List<string> { "a.wav", "b.WAV" }, Names(Scan()));
    }

    [Fact]
    public void SongsComeFolderByFolderThenByNameIgnoringCase()
    {
        Wav("b.wav");
        Wav("A.wav");
        Wav("c.wav");
        Wav("Album/z.wav");
        Wav("Album/Y.wav");
        Wav("Album/Sub/q.wav");
        Wav("Album 2/a.wav");
        Wav("album3/a.wav");

        List<Music> songs = Scan();

        List<string> relative = new List<string>();
        foreach (Music song in songs)
        {
            relative.Add(Path.GetRelativePath(_root, song.Path).Replace('\\', '/'));
        }
        Assert.Equal(new List<string> { "A.wav", "b.wav", "c.wav", "Album/Y.wav", "Album/z.wav", "Album/Sub/q.wav",
                "Album 2/a.wav", "album3/a.wav" }, relative);
    }

    [Fact]
    public void ManyFilesStayInOrderEvenThoughSeveralThreadsReadThem()
    {
        List<string> expected = new List<string>();
        for (int i = 0; i < 60; i++)
        {
            string name = string.Format("song {0:00}.wav", i);
            Wav(name);
            expected.Add(name);
        }

        Assert.Equal(expected, Names(Scan()));
    }

    [Fact]
    public void AnEmptyOrMissingFolderGivesAnEmptyList()
    {
        Assert.Empty(Scan());
        Assert.Empty(LibraryScanner.List(Path.Combine(_root, "nope"), null, null));
    }

    [Fact]
    public void TheOnlyAudioPathsFilterKeepsJustThoseSongs()
    {
        string a = Wav("a.wav");
        Wav("b.wav");
        string c = Wav("Album/c.wav");

        HashSet<string> wanted = new HashSet<string> { a, c };
        List<Music> songs = LibraryScanner.List(_root, wanted, null);

        Assert.Equal(new List<string> { "a.wav", "c.wav" }, Names(songs));
    }

    [Fact]
    public void TheFilterIgnoresPathsThatAreNotThereAndAnEmptyFilterListsNothing()
    {
        string a = Wav("a.wav");

        HashSet<string> wanted = new HashSet<string> { a, Path.Combine(_root, "gone.wav") };
        Assert.Equal(new List<string> { "a.wav" }, Names(LibraryScanner.List(_root, wanted, null)));
        Assert.Empty(LibraryScanner.List(_root, new HashSet<string>(), null));
    }

    [Fact]
    public void ARefreshSeesTheChangedTag()
    {
        string file = Tagged("a.wav", "Old", null, null, "Artist");
        HashSet<string> only = new HashSet<string> { file };
        Assert.Equal("Old", LibraryScanner.List(_root, only, null)[0].Title);

        using (TagFile audio = TagFile.Create(file))
        {
            audio.GetTag(TagTypes.Id3v2, true).Title = "New";
            audio.Save();
        }

        Assert.Equal("New", LibraryScanner.List(_root, only, null)[0].Title);
    }

    // ---- progress ----

    [Fact]
    public void ProgressIsToldAfterEveryFileCountingUpToTheTotal()
    {
        for (int i = 0; i < 12; i++)
        {
            Wav("s" + i + ".wav");
        }
        List<string> calls = new List<string>();

        LibraryScanner.List(_root, null, (done, total) => { lock (calls) { calls.Add(done + "/" + total); } });

        List<string> expected = new List<string>();
        for (int i = 1; i <= 12; i++)
        {
            expected.Add(i + "/12");
        }
        Assert.Equal(expected, calls); // in order, whichever thread read the file
    }

    [Fact]
    public void ProgressTotalIsTheNumberOfFilesAfterTheFilter()
    {
        string a = Wav("a.wav");
        Wav("b.wav");
        Wav("c.wav");
        List<string> calls = new List<string>();

        LibraryScanner.List(_root, new HashSet<string> { a }, (done, total) => calls.Add(done + "/" + total));

        Assert.Equal(new List<string> { "1/1" }, calls);
    }

    [Fact]
    public void AnEmptyFolderReportsNoProgress()
    {
        List<string> calls = new List<string>();

        LibraryScanner.List(_root, null, (done, total) => calls.Add(done + "/" + total));

        Assert.Empty(calls);
    }

    [Fact]
    public void AProgressListenerThatThrowsDoesNotStopTheScan()
    {
        Wav("a.wav");
        Wav("b.wav");

        List<Music> songs = LibraryScanner.List(_root, null, (done, total) =>
        {
            throw new InvalidOperationException("listener bug");
        });

        Assert.Equal(2, songs.Count);
    }

    /// <summary>
    /// The Java test looked for threads named "library-scan-" with Thread.getAllStackTraces, which .NET cannot list. The
    /// scan joins its threads before it returns, so the same promise is checked from the outside: once List has returned
    /// nothing keeps working for it (no progress call arrives afterwards) and the call is not left waiting for anything.
    /// </summary>
    [Fact]
    public void TheScanThreadsAreGoneOnceTheScanIsDone()
    {
        Wav("a.wav");
        Wav("b.wav");
        int calls = 0;

        LibraryScanner.List(_root, null, (done, total) => Interlocked.Increment(ref calls));
        int atReturn = Volatile.Read(ref calls);
        Thread.Sleep(300);

        Assert.Equal(2, atReturn);
        Assert.Equal(atReturn, Volatile.Read(ref calls));
    }

    [Fact]
    public void TitlesOfTheListingFollowTheOrder()
    {
        Tagged("2.wav", "Second file", null, null, "X");
        Tagged("1.wav", "First file", null, null, "X");

        Assert.Equal(new List<string> { "First file", "Second file" }, Titles(Scan()));
    }
}
