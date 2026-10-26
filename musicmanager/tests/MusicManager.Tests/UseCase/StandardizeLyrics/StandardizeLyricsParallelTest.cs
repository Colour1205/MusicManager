using System.Text;
using MusicManager.UseCase.StandardizeLyrics;
using Xunit;

namespace MusicManager.Tests.UseCase.StandardizeLyrics;

/// <summary>
/// A library is checked (and written) several songs at a time. Over real files: the result must be the one a single worker
/// gives, song for song, with the progress numbers counting up one by one, and the files written must be identical.
/// </summary>
public class StandardizeLyricsParallelTest : IDisposable
{
    private const string Hmrc = "[offset:0]\n[1000,2000]<0,1000>안녕 <1000,1000>세상\n[4000,2000]<0,1000>Hello <1000,1000>world\n";
    private const string LineSyncedLrc = "[offset:700]\n[00:01.00]안녕 세상\n[00:04.00]Hello world\n";

    private const string StandardWithLatinRomaji = "[ti:t]\r\n[tool:LDDC v0.9.0]\r\n\r\n"
        + "[00:01.000]<00:01.000>annyeong <00:02.000>sesang<00:03.000>\r\n"
        + "[00:01.000]<00:01.000>안녕 <00:02.000>세상<00:03.000>\r\n";

    private const string BrokenLrc = "[ti:broken\r\n";

    private readonly string _root;

    public StandardizeLyricsParallelTest()
    {
        _root = Path.Combine(Path.GetTempPath(), "mm-" + Guid.NewGuid());
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
            // a temp folder that cannot be removed is left to the system
        }
    }

    /// <summary>records everything the use case reports, from whatever thread; the progress calls must arrive one at a time</summary>
    private sealed class Capture : IStandardizeLyricsOutputBoundary
    {
        public StandardizeLyricsOutputData Result;
        public readonly List<(int Done, int Total, string Label)> Progress = new List<(int, int, string)>();
        public int InsideProgress;
        public int MaxInsideProgress;

        public void Present(StandardizeLyricsOutputData outputData)
        {
            Result = outputData;
        }

        public void PresentProgress(int done, int total, string label)
        {
            int inside = Interlocked.Increment(ref InsideProgress);
            MaxInsideProgress = Math.Max(MaxInsideProgress, inside);
            Thread.Sleep(0);
            lock (Progress)
            {
                Progress.Add((done, total, label));
            }
            Interlocked.Decrement(ref InsideProgress);
        }

        public void PresentError(string message)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>
    /// a library of songs of every kind: a .hmrc, a line-synced .lrc, an .lrc that is standard already, a broken .lrc, a song with
    /// no lyric file at all, and a .ttml beside a stale .lrc
    /// </summary>
    private string Library(string name, int songs)
    {
        string folder = Path.Combine(_root, name);
        for (int i = 0; i < songs; i++)
        {
            string album = Path.Combine(folder, "Album " + i / 10);
            Directory.CreateDirectory(album);
            string audio = Path.Combine(album, "Song " + i.ToString("D3") + ".flac");
            File.WriteAllBytes(audio, new byte[0]);
            string stem = Path.Combine(album, "Song " + i.ToString("D3"));
            switch (i % 6)
            {
                case 0:
                    File.WriteAllText(stem + ".hmrc", Hmrc, new UTF8Encoding(false));
                    break;
                case 1:
                    File.WriteAllText(stem + ".lrc", LineSyncedLrc, new UTF8Encoding(false));
                    break;
                case 2:
                    File.WriteAllText(stem + ".lrc", StandardWithLatinRomaji, new UTF8Encoding(false));
                    File.WriteAllText(stem + ".hmrc", Hmrc, new UTF8Encoding(false));
                    break;
                case 3:
                    File.WriteAllText(stem + ".lrc", BrokenLrc, new UTF8Encoding(false));
                    break;
                case 4:
                    break; // no lyric file at all
                default:
                    File.WriteAllText(stem + ".hmrc", Hmrc.Replace("안녕", "안녕" + i), new UTF8Encoding(false));
                    break;
            }
        }
        return folder;
    }

    private static StandardizeLyricsOutputData Run(string folder, bool dryRun, int workers, Capture output)
    {
        new StandardizeLyricsInteractor(new RealDataAccess(), output, workers).Execute(
            new StandardizeLyricsInputData(folder, dryRun, false, false, null, "m", null));
        return output.Result;
    }

    private static List<string> Describe(StandardizeLyricsOutputData result)
    {
        return result.Results.Select(r => Path.GetFileName(r.AudioPath) + "|" + r.Action + "|" + r.Detail + "|"
            + string.Join(",", r.DeletedFiles.Select(Path.GetFileName)) + "|" + r.RomajiAdded + "|" + r.RomajiPresent).ToList();
    }

    [Fact]
    public void ADryRunOverManySongsGivesTheResultsOfOneWorkerInTheOrderOfTheSongs()
    {
        string folder = Library("lib", 120);
        Capture one = new Capture();
        Capture many = new Capture();

        List<string> alone = Describe(Run(folder, true, 1, one));
        List<string> together = Describe(Run(folder, true, 8, many));

        Assert.Equal(120, alone.Count);
        Assert.Equal(alone, together);
        Assert.Contains(alone, line => line.Contains("|NoLyrics|"));
        Assert.True(alone.Select(line => line.Split('|')[1]).Distinct().Count() >= 4, "the library has songs of several kinds");
    }

    [Fact]
    public void ProgressCountsUpOneByOneAndOneCallAtATimeWhateverTheNumberOfWorkers()
    {
        string folder = Library("lib", 90);
        Capture output = new Capture();

        Run(folder, true, 8, output);

        List<(int Done, int Total, string Label)> seen = output.Progress;
        // a dry run has the checking phase and then the "writing" of the songs that have something to write
        List<(int Done, int Total, string Label)> checking = seen.Where(p => p.Total == 90 && !p.Label.StartsWith("Writing")).ToList();
        Assert.Equal(90, checking.Count);
        Assert.Equal(Enumerable.Range(1, 90).ToList(), checking.Select(p => p.Done).ToList());
        List<(int Done, int Total, string Label)> writing = seen.Where(p => p.Label.StartsWith("Writing")).ToList();
        Assert.Equal(Enumerable.Range(1, writing.Count).ToList(), writing.Select(p => p.Done).ToList());
        Assert.Equal(1, output.MaxInsideProgress);
    }

    [Fact]
    public void ARealRunWithManyWorkersWritesAndDeletesExactlyWhatOneWorkerDoes()
    {
        string a = Library("one", 60);
        string b = Library("many", 60);

        List<string> alone = Describe(Run(a, false, 1, new Capture()));
        List<string> together = Describe(Run(b, false, 8, new Capture()));

        Assert.Equal(alone, together);
        List<string> filesA = Directory.EnumerateFiles(a, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(a, f)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        List<string> filesB = Directory.EnumerateFiles(b, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(b, f)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(filesA, filesB);
        foreach (string relative in filesA.Where(f => f.EndsWith(".lrc", StringComparison.Ordinal)))
        {
            Assert.Equal(File.ReadAllBytes(Path.Combine(a, relative)), File.ReadAllBytes(Path.Combine(b, relative)));
        }
        Assert.DoesNotContain(filesB, f => f.EndsWith(".hmrc", StringComparison.Ordinal)); // the deprecated files are gone
    }

    [Fact]
    public void AFailureInOneSongNeverStopsTheOthers()
    {
        string folder = Library("lib", 30);
        FailingOn failing = new FailingOn("Song 007");
        Capture output = new Capture();

        new StandardizeLyricsInteractor(failing, output, 8).Execute(
            new StandardizeLyricsInputData(folder, true, false, false, null, "m", null));

        Assert.Equal(30, output.Result.Results.Count);
        Assert.Equal(LyricsAction.Failed, output.Result.Results.Single(r => r.AudioPath.Contains("Song 007")).Action);
        Assert.Equal(29, output.Result.Results.Count(r => !r.AudioPath.Contains("Song 007") || r.Action != LyricsAction.Failed) );
    }

    /// <summary>a data access whose reading of one song's lyric file throws, as a disk error would</summary>
    private sealed class FailingOn : RealDataAccess
    {
        private readonly string _name;

        public FailingOn(string name)
        {
            _name = name;
        }

        public override MusicManager.Entity.Lyric ReadLyric(string lyricFilePath)
        {
            if (lyricFilePath.Contains(_name, StringComparison.Ordinal))
            {
                throw new IOException("disk error");
            }
            return base.ReadLyric(lyricFilePath);
        }
    }

    [Fact]
    public void AnEmptyFolderAndASingleSongWorkWithManyWorkers()
    {
        string empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);
        Capture none = new Capture();
        Assert.Empty(Run(empty, true, 8, none).Results);

        string one = Library("one", 1);
        Assert.Single(Run(one, true, 8, new Capture()).Results);
    }
}
