using System.Text;
using MusicManager.DataAccess;
using MusicManager.UseCase.StandardizeArtists;
using Xunit;

namespace MusicManager.Tests.DataAccess;

/// <summary>
/// Renaming a song's files to "Artist - Title" after its tags: the audio file and the lyric files that share its name, never
/// over another file, and whole or not at all. The audio files are MP3s built here with just the title and artist frames.
/// </summary>
public class SongRenamerTest : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mm-" + Guid.NewGuid());

    public SongRenamerTest()
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

    // ---- an MP3 with an ID3v2.4 tag of the frames wanted ----

    private static byte[] SyncSafe(int n)
    {
        return new[] { (byte)((n >> 21) & 0x7F), (byte)((n >> 14) & 0x7F), (byte)((n >> 7) & 0x7F), (byte)(n & 0x7F) };
    }

    private static byte[] TextFrame(string id, string value)
    {
        List<byte> data = new List<byte> { 3 }; // UTF-8
        data.AddRange(Encoding.UTF8.GetBytes(value));
        List<byte> frame = new List<byte>(Encoding.ASCII.GetBytes(id));
        frame.AddRange(SyncSafe(data.Count));
        frame.AddRange(new byte[] { 0, 0 });
        frame.AddRange(data);
        return frame.ToArray();
    }

    private string Mp3(string folder, string fileName, string artist, string title)
    {
        List<byte[]> frames = new List<byte[]>();
        if (artist != null)
        {
            frames.Add(TextFrame("TPE1", artist));
        }
        if (title != null)
        {
            frames.Add(TextFrame("TIT2", title));
        }
        List<byte> file = new List<byte>();
        if (frames.Count > 0)
        {
            file.AddRange(new byte[] { (byte)'I', (byte)'D', (byte)'3', 4, 0, 0 });
            file.AddRange(SyncSafe(frames.Sum(f => f.Length)));
            foreach (byte[] frame in frames)
            {
                file.AddRange(frame);
            }
        }
        for (int i = 0; i < 30; i++)
        {
            file.AddRange(new byte[] { 0xFF, 0xFB, 0x90, 0x00 });
            file.AddRange(new byte[413]);
        }
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, fileName);
        File.WriteAllBytes(path, file.ToArray());
        return path;
    }

    private static string Sidecar(string audio, string extension, string text = "[00:01.000]x")
    {
        string path = Path.ChangeExtension(audio, extension);
        File.WriteAllText(path, text);
        return path;
    }

    private static List<string> Names(string folder)
    {
        return Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    // ---- tests ----

    [Fact]
    public void TheAudioFileAndItsLyricFilesAreRenamedToArtistDashTitle()
    {
        string audio = Mp3(_root, "old name.mp3", "IU", "Good Day");
        Sidecar(audio, ".lrc");
        Sidecar(audio, ".ttml");
        Sidecar(audio, ".hmrc");
        string other = Mp3(_root, "old name 2.mp3", "IU", "Other");

        SongRenameResult result = SongRenamer.Rename(audio, " & ");

        Assert.Equal(SongRenameResult.Kind.Renamed, result.Status);
        Assert.Equal(Path.Combine(_root, "IU - Good Day.mp3"), result.NewAudioPath);
        Assert.Equal(new List<string> { "IU - Good Day.hmrc", "IU - Good Day.lrc", "IU - Good Day.mp3", "IU - Good Day.ttml", "old name 2.mp3" },
                Names(_root));
        Assert.True(File.Exists(other)); // another song is not touched, whatever its name starts with
    }

    [Fact]
    public void SeveralArtistsAreJoinedWithTheSeparatorAndASlashBecomesADash()
    {
        string audio = Mp3(_root, "x.mp3", "K/DA", "POP/STARS: the \"remix\"? (JP Ver. / Live)");

        SongRenameResult result = SongRenamer.Rename(audio, " & ");

        Assert.Equal("K-DA - POP-STARS the remix (JP Ver. - Live).mp3", Path.GetFileName(result.NewAudioPath));
    }

    [Fact]
    public void AFileThatIsNamedAfterItsTagsAlreadyIsLeftAlone()
    {
        string audio = Mp3(_root, "IU - Good Day.mp3", "IU", "Good Day");
        DateTime before = File.GetLastWriteTimeUtc(audio);

        SongRenameResult result = SongRenamer.Rename(audio, " & ");

        Assert.Equal(SongRenameResult.Kind.AlreadyNamed, result.Status);
        Assert.Equal(new List<string> { "IU - Good Day.mp3" }, Names(_root));
        Assert.Equal(before, File.GetLastWriteTimeUtc(audio));
    }

    [Fact]
    public void ASongWithoutATitleOrWithoutAnArtistKeepsItsName()
    {
        string noTitle = Mp3(_root, "a.mp3", "IU", null);
        string noArtist = Mp3(_root, "b.mp3", null, "Title");
        string noTags = Mp3(_root, "c.mp3", null, null);

        Assert.Equal(SongRenameResult.Kind.NoTags, SongRenamer.Rename(noTitle, " & ").Status);
        Assert.Equal(SongRenameResult.Kind.NoTags, SongRenamer.Rename(noArtist, " & ").Status);
        Assert.Equal(SongRenameResult.Kind.NoTags, SongRenamer.Rename(noTags, " & ").Status);
        Assert.Equal(new List<string> { "a.mp3", "b.mp3", "c.mp3" }, Names(_root));
    }

    [Fact]
    public void NothingIsOverwrittenAndNothingIsRenamedWhenTheNewNameIsTaken()
    {
        string audio = Mp3(_root, "old.mp3", "IU", "Good Day");
        Sidecar(audio, ".lrc", "mine");
        string taken = Mp3(_root, "IU - Good Day.mp3", "Someone", "Else");
        Sidecar(taken, ".lrc", "theirs");

        SongRenameResult result = SongRenamer.Rename(audio, " & ");

        Assert.Equal(SongRenameResult.Kind.Exists, result.Status);
        Assert.Contains("IU - Good Day.", result.Detail);
        Assert.Contains("is there already", result.Detail);
        Assert.Equal(new List<string> { "IU - Good Day.lrc", "IU - Good Day.mp3", "old.lrc", "old.mp3" }, Names(_root));
        Assert.Equal("theirs", File.ReadAllText(Path.Combine(_root, "IU - Good Day.lrc")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(_root, "old.lrc")));
    }

    [Fact]
    public void ALyricFileOfTheNewNameStopsTheRenameToo()
    {
        string audio = Mp3(_root, "old.mp3", "IU", "Good Day");
        File.WriteAllText(Path.Combine(_root, "IU - Good Day.lrc"), "an orphan"); // no audio file of that name, but a lyric file
        Sidecar(audio, ".lrc");

        SongRenameResult result = SongRenamer.Rename(audio, " & ");

        Assert.Equal(SongRenameResult.Kind.Exists, result.Status);
        Assert.Equal("an orphan", File.ReadAllText(Path.Combine(_root, "IU - Good Day.lrc")));
        Assert.True(File.Exists(audio));
    }

    [Fact]
    public void AChangeOfCaseOnlyIsARename()
    {
        string audio = Mp3(_root, "iu - good day.mp3", "IU", "Good Day");
        Sidecar(audio, ".lrc");

        SongRenameResult result = SongRenamer.Rename(audio, " & ");

        Assert.Equal(SongRenameResult.Kind.Renamed, result.Status);
        Assert.Equal(new List<string> { "IU - Good Day.lrc", "IU - Good Day.mp3" }, Names(_root));
    }

    [Fact]
    public void WhenAFileOfTheSongCannotBeMovedTheOnesAlreadyMovedAreBack()
    {
        string audio = Mp3(_root, "old.mp3", "IU", "Good Day");
        string lrc = Sidecar(audio, ".lrc");

        SongRenameResult result;
        using (new FileStream(lrc, FileMode.Open, FileAccess.Read, FileShare.None)) // open elsewhere: it cannot be renamed
        {
            result = SongRenamer.Rename(audio, " & ");
        }

        Assert.Equal(SongRenameResult.Kind.Failed, result.Status);
        Assert.NotEmpty(result.Detail);
        Assert.Equal(new List<string> { "old.lrc", "old.mp3" }, Names(_root)); // the song is whole, with its old name
    }

    [Fact]
    public void ANameThatWouldMakeThePathTooLongIsNotUsed()
    {
        string folder = Path.Combine(_root, new string('d', 120), new string('e', 100));
        string audio = Mp3(folder, "old.mp3", "IU", new string('t', 80));

        SongRenameResult result = SongRenamer.Rename(audio, " & ");

        Assert.Equal(SongRenameResult.Kind.TooLong, result.Status);
        Assert.True(File.Exists(audio));
    }

    [Fact]
    public void AFileThatIsNotThereAnyMoreIsReportedAsFailed()
    {
        SongRenameResult result = SongRenamer.Rename(Path.Combine(_root, "gone.mp3"), " & ");

        Assert.Equal(SongRenameResult.Kind.Failed, result.Status);
    }

    [Fact]
    public void ASecondAudioFileWithTheSameNameGoesWithTheSong()
    {
        string audio = Mp3(_root, "old.mp3", "IU", "Good Day");
        File.WriteAllBytes(Path.Combine(_root, "old.flac"), new byte[10]); // the same song in another format

        SongRenamer.Rename(audio, " & ");

        Assert.Equal(new List<string> { "IU - Good Day.flac", "IU - Good Day.mp3" }, Names(_root));
    }
}
