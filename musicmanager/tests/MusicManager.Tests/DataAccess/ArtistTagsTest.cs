using System.Text;
using MusicManager.DataAccess;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeArtists;
using Xunit;
using DataAccessImpl = MusicManager.DataAccess.DataAccess;

namespace MusicManager.Tests.DataAccess;

/// <summary>
/// Not a port: the Java ArtistTags had no test of its own (the use case tests script the data access). These pin down where
/// TagLibSharp and jaudiotagger differ, with the values jaudiotagger gives for the same bytes, on MP3 files that are built here.
/// </summary>
public class ArtistTagsTest : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mm-" + Guid.NewGuid());

    public ArtistTagsTest()
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

    // ---- building MP3 files with exactly the frames wanted ----

    private static byte[] SyncSafe(int n)
    {
        return new[] { (byte)((n >> 21) & 0x7F), (byte)((n >> 14) & 0x7F), (byte)((n >> 7) & 0x7F), (byte)(n & 0x7F) };
    }

    /// <summary>text frame data: the encoding byte, then the values, each with its own byte order mark in UTF-16</summary>
    private static byte[] Text(int encoding, params string[] values)
    {
        List<byte> data = new List<byte> { (byte)encoding };
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                data.AddRange(encoding == 1 ? new byte[] { 0, 0 } : new byte[] { 0 });
            }
            if (encoding == 1)
            {
                data.AddRange(new byte[] { 0xFF, 0xFE });
                data.AddRange(Encoding.Unicode.GetBytes(values[i]));
            }
            else
            {
                data.AddRange(Encoding.UTF8.GetBytes(values[i]));
            }
        }
        return data.ToArray();
    }

    private static byte[] Frame(string id, byte[] data, int version)
    {
        List<byte> frame = new List<byte>(Encoding.ASCII.GetBytes(id));
        frame.AddRange(version == 4
            ? SyncSafe(data.Length)
            : new[] { (byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length });
        frame.AddRange(new byte[] { 0, 0 });
        frame.AddRange(data);
        return frame.ToArray();
    }

    /// <summary>a user text frame (TXXX) with the description and the values, in UTF-8 (version 2.4) or UTF-16 (version 2.3)</summary>
    private static byte[] UserText(string description, int version, params string[] values)
    {
        int encoding = version == 4 ? 3 : 1;
        List<string> all = new List<string> { description };
        all.AddRange(values);
        return Frame("TXXX", Text(encoding, all.ToArray()), version);
    }

    /// <summary>an MP3 file: the ID3v2 tag of the given version with the frames, then 30 valid MPEG frames</summary>
    private string Mp3(string name, int version, params byte[][] frames)
    {
        List<byte> file = new List<byte>();
        if (frames.Length > 0)
        {
            int size = frames.Sum(f => f.Length);
            file.AddRange(new byte[] { (byte)'I', (byte)'D', (byte)'3', (byte)version, 0, 0 });
            file.AddRange(SyncSafe(size));
            foreach (byte[] frame in frames)
            {
                file.AddRange(frame);
            }
        }
        for (int i = 0; i < 30; i++)
        {
            file.AddRange(new byte[] { 0xFF, 0xFB, 0x90, 0x00 }); // MPEG 1 layer 3, 128 kbit/s, 44.1 kHz: 417 bytes a frame
            file.AddRange(new byte[413]);
        }
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, file.ToArray());
        return path;
    }

    private SongArtistTags Read(string path)
    {
        return ArtistTags.Read(_root).Single(s => s.Path == path);
    }

    private static SongArtistUpdate Update(string path, string artist, List<string> artists, string albumArtist)
    {
        return new SongArtistUpdate(path, artist, artists, albumArtist, new List<FieldChange>());
    }

    // ---- reading ----

    [Fact]
    public void ASlashInAnId3v23ArtistIsPartOfTheNameNotASeparator()
    {
        string file = Mp3("a.mp3", 3, Frame("TIT2", Text(1, "T"), 3), Frame("TPE1", Text(1, "Namewee/G.E.M."), 3),
                Frame("TPE2", Text(1, "Alb A/Alb B"), 3));

        SongArtistTags song = Read(file);

        Assert.Equal(new List<string> { "Namewee/G.E.M." }, song.Artist);
        Assert.Equal(new List<string> { "Alb A/Alb B" }, song.AlbumArtist);
    }

    [Fact]
    public void NulSeparatedValuesAreSeparateArtistsInVersion23AndVersion24()
    {
        string v23 = Mp3("v23.mp3", 3, Frame("TPE1", Text(1, "A", "B"), 3));
        string v24 = Mp3("v24.mp3", 4, Frame("TPE1", Text(3, "A", "B"), 4), Frame("TPE2", Text(3, "C", "D"), 4));

        Assert.Equal(new List<string> { "A", "B" }, Read(v23).Artist);
        Assert.Equal(new List<string> { "A", "B" }, Read(v24).Artist);
        Assert.Equal(new List<string> { "C", "D" }, Read(v24).AlbumArtist);
    }

    [Fact]
    public void TheArtistsFieldIsTheUserTextFrameCalledARTISTSAndItsNameIsCaseSensitive()
    {
        string v23 = Mp3("v23.mp3", 3, Frame("TPE1", Text(1, "A/B"), 3), UserText("ARTISTS", 3, "A", "B"));
        string lower = Mp3("lower.mp3", 4, Frame("TPE1", Text(3, "A"), 4), UserText("artists", 4, "x", "y"));

        Assert.Equal(new List<string> { "A/B" }, Read(v23).Artist);
        Assert.Equal(new List<string> { "A", "B" }, Read(v23).Artists);
        Assert.Empty(Read(lower).Artists);
    }

    [Fact]
    public void AMp3FileWithoutAnyTagIsLeftOut()
    {
        string plain = Mp3("plain.mp3", 3);
        string tagged = Mp3("tagged.mp3", 3, Frame("TPE1", Text(1, "A"), 3));

        List<string> paths = ArtistTags.Read(_root).Select(s => s.Path).ToList();

        Assert.Equal(new List<string> { tagged }, paths);
        Assert.DoesNotContain(plain, paths);
    }

    [Fact]
    public void ABlankValueIsLeftOutAndTheOthersKeepTheirSpaces()
    {
        string file = Mp3("blank.mp3", 4, Frame("TPE1", Text(3, "A", " ", " B "), 4), Frame("TPE2", Text(3, "   "), 4));

        SongArtistTags song = Read(file);

        Assert.Equal(new List<string> { "A", " B " }, song.Artist);
        Assert.Empty(song.AlbumArtist);
    }

    // ---- writing ----

    [Fact]
    public void AWrittenUpdateIsReadBackAndTheOtherFramesOfTheTagSurvive()
    {
        string file = Mp3("w.mp3", 3, Frame("TIT2", Text(1, "AC/DC Song"), 3), Frame("TALB", Text(1, "Album/Half"), 3),
                Frame("TPE1", Text(1, "Old"), 3), Frame("TCOM", Text(1, "Composer A", "Composer B"), 3));

        bool ok = ArtistTags.Write(Update(file, "A; B/C", new List<string> { "A", "B", "C/D" }, "AA"));

        Assert.True(ok);
        SongArtistTags song = Read(file);
        Assert.Equal(new List<string> { "A; B/C" }, song.Artist);
        Assert.Equal(new List<string> { "AA" }, song.AlbumArtist);
        Assert.Equal(new List<string> { "A", "B", "C/D" }, song.Artists);
        Music music = new DataAccessImpl().ReadMusicTags(file);
        Assert.Equal("AC/DC Song", music.Title);
        Assert.Equal("Album/Half", music.Album);
        // the frame nobody asked about is written back as it was read, with both of its values
        Assert.Contains(Convert.ToHexString(Text(1, "Composer A", "Composer B")), Convert.ToHexString(File.ReadAllBytes(file)));
    }

    [Fact]
    public void TheArtistFrameOfAnUpdateThatLeavesItAloneKeepsEveryValue()
    {
        string file = Mp3("keep.mp3", 3, Frame("TPE1", Text(1, "A", "B"), 3));

        Assert.True(ArtistTags.Write(Update(file, null, null, "Various")));

        SongArtistTags song = Read(file);
        Assert.Equal(new List<string> { "A", "B" }, song.Artist);
        Assert.Equal(new List<string> { "Various" }, song.AlbumArtist);
    }

    [Fact]
    public void AnEmptyArtistsListRemovesTheField()
    {
        string file = Mp3("remove.mp3", 4, Frame("TPE1", Text(3, "A"), 4), UserText("ARTISTS", 4, "A", "B"));

        Assert.True(ArtistTags.Write(Update(file, null, new List<string>(), null)));

        Assert.Empty(Read(file).Artists);
        Assert.Equal(new List<string> { "A" }, Read(file).Artist);
    }

    [Fact]
    public void AFileWithoutATagGetsOne()
    {
        string file = Mp3("new.mp3", 3);

        Assert.True(ArtistTags.Write(Update(file, "Solo", new List<string> { "Solo" }, null)));

        Assert.Equal(new List<string> { "Solo" }, Read(file).Artist);
        Assert.Equal(new List<string> { "Solo" }, Read(file).Artists);
    }

    [Fact]
    public void AnUpdateThatCannotBeWrittenFailsAndLeavesTheFileAsItWas()
    {
        // ID3v1 has neither an album artist nor an ARTISTS field
        byte[] id3v1 = new byte[128];
        Encoding.ASCII.GetBytes("TAG").CopyTo(id3v1, 0);
        Encoding.ASCII.GetBytes("Old Title").CopyTo(id3v1, 3);
        Encoding.ASCII.GetBytes("Old Artist").CopyTo(id3v1, 33);
        string file = Mp3("v1.mp3", 3);
        using (FileStream stream = new FileStream(file, FileMode.Append))
        {
            stream.Write(id3v1);
        }
        byte[] before = File.ReadAllBytes(file);
        Assert.Equal(new List<string> { "Old Artist" }, Read(file).Artist);

        Assert.False(ArtistTags.Write(Update(file, "New", null, "Album Artist")));
        Assert.False(ArtistTags.Write(Update(file, null, new List<string> { "X" }, null)));

        Assert.Equal(before, File.ReadAllBytes(file));
    }

    [Fact]
    public void AFileThatIsNoAudioCannotBeWritten()
    {
        string file = Path.Combine(_root, "junk.mp3");
        File.WriteAllText(file, "this is not an mp3");

        Assert.False(ArtistTags.Write(Update(file, "A", null, null)));
    }
}
