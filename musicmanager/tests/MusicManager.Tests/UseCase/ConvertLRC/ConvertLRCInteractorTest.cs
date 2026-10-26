using MusicManager.Entity;
using MusicManager.UseCase.ConvertLRC;
using Xunit;

namespace MusicManager.Tests.UseCase.ConvertLRC;

/// <summary>The legacy Convert LRC interactor has no Java tests; these pin down what it writes and how it counts.</summary>
public class ConvertLRCInteractorTest
{
    private sealed class FakeData : IConvertLRCDataAccess
    {
        public readonly Dictionary<Music, Lyric> Lyrics = new Dictionary<Music, Lyric>();
        public readonly List<string> Saved = new List<string>(); // "FORMAT:lyric path"
        public readonly Dictionary<string, string> Content = new Dictionary<string, string>(); // "FORMAT:lyric path" -> text
        public List<Music> Asked;
        public Func<string, Exception> FailOn = format => null;

        public Queue<Music> GetMusics(string path)
        {
            return new Queue<Music>();
        }

        public IDictionary<Music, Lyric> GetLRCs(Queue<Music> musics)
        {
            Asked = new List<Music>(musics);
            return Lyrics;
        }

        public void SaveTTML(Lyric lyric, string content) => Save("TTML", lyric, content);

        public void SaveHMRC(Lyric lyric, string content) => Save("HMRC", lyric, content);

        public void SaveLRC(Lyric lyric, string content) => Save("LRC", lyric, content);

        private void Save(string format, Lyric lyric, string content)
        {
            Exception failure = FailOn(format + ":" + lyric.Path);
            if (failure != null)
            {
                throw failure;
            }
            Saved.Add(format + ":" + lyric.Path);
            Content[format + ":" + lyric.Path] = content;
        }
    }

    private sealed class Capture : IConvertLRCOutputBoundary
    {
        public ConvertLRCOutputData Presented;
        public readonly List<string> Progress = new List<string>(); // "music path:true|false"

        public void Present(ConvertLRCOutputData outputData)
        {
            Presented = outputData;
        }

        public void PresentProgress(ConvertLRCProgressData progressData)
        {
            Progress.Add(progressData.MusicPath + ":" + (progressData.IsSuccess ? "true" : "false"));
        }
    }

    private readonly FakeData _data = new FakeData();
    private readonly Capture _output = new Capture();
    private readonly ConvertLRCInteractor _interactor = new ConvertLRCInteractor();

    public ConvertLRCInteractorTest()
    {
        _interactor.ConvertLRC(_data, _output);
    }

    private static Music Song(string path)
    {
        return new Music.Builder().SetTitle(path).SetPath(path).Build();
    }

    private static LyricLine Line(string text, long start, long end, params WordTimeStamp[] words)
    {
        LyricLine line = new LyricLine(text, start, end);
        if (words.Length > 0)
        {
            line.WordTimeStamps = words;
        }
        return line;
    }

    /// <summary>a word synced lyric: a line with an ampersand, and a line that starts after the first minute</summary>
    private static Lyric WordSynced(string path)
    {
        return new Lyric(Lyric.SyncType.WordSync, new List<LyricLine>
        {
            Line("Hello A&B", 1000, 3500, new WordTimeStamp("Hello ", 1000, 2000), new WordTimeStamp("A&B", 2000, 3500)),
            Line("Bye", 61234, 62000, new WordTimeStamp("Bye", 61234, 62000)),
        }, path);
    }

    private static Lyric LineSynced(string path)
    {
        return new Lyric(Lyric.SyncType.LineSync, new List<LyricLine>
        {
            Line("Fish & chips", 1000, 3000),
        }, path);
    }

    private static string Join(params string[] lines) => string.Join("\n", lines);

    private void Run(List<Music> musics, params string[] formats)
    {
        _interactor.Execute(new ConvertLRCInputData(musics, new List<string>(formats)));
    }

    // ---- the three outputs ----

    [Fact]
    public void WordSyncedLyricBecomesTtmlWithASpanPerWord()
    {
        Music song = Song("a.flac");
        _data.Lyrics[song] = WordSynced("a.lrc");

        Run(new List<Music> { song }, "TTML");

        string expected = Join(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><tt xmlns=\"http://www.w3.org/ns/ttml\" xmlns:itunes=\"http://music.apple.com/lyric-ttml-internal\" xmlns:ttm=\"http://www.w3.org/ns/ttml#metadata\" itunes:timing=\"Word\">",
                "    <head>",
                "        <metadata>",
                "            <ttm:agent type=\"person\" xml:id=\"v1\"/>",
                "        </metadata>",
                "    </head>",
                "    <body dur=\"01:02.000\">",
                "        <div begin=\"00:00.000\" end=\"01:02.000\">",
                "            <p begin=\"00:01.000\" end=\"00:03.500\" ttm:agent=\"v1\" itunes:key=\"L1\">",
                "                <span begin=\"00:01.000\" end=\"00:02.000\">Hello </span>",
                "                <span begin=\"00:02.000\" end=\"00:03.500\">A&amp;B</span>",
                "            </p>",
                "            <p begin=\"01:01.234\" end=\"01:02.000\" ttm:agent=\"v1\" itunes:key=\"L2\">",
                "                <span begin=\"01:01.234\" end=\"01:02.000\">Bye</span>",
                "            </p>",
                "        </div>",
                "    </body>",
                "</tt>");
        Assert.Equal(expected, _data.Content["TTML:a.lrc"]);
    }

    [Fact]
    public void LineSyncedLyricBecomesTtmlWithTheTextInsideTheParagraph()
    {
        Music song = Song("a.flac");
        _data.Lyrics[song] = LineSynced("a.lrc");

        Run(new List<Music> { song }, "TTML");

        string ttml = _data.Content["TTML:a.lrc"];
        Assert.Contains("<p begin=\"00:01.000\" end=\"00:03.000\" ttm:agent=\"v1\" itunes:key=\"L1\">Fish &amp; chips\n            </p>", ttml);
        Assert.DoesNotContain("<span", ttml);
    }

    [Fact]
    public void ALyricWithoutLinesStillGetsAZeroDuration()
    {
        Music song = Song("a.flac");
        _data.Lyrics[song] = new Lyric(Lyric.SyncType.NoSync, new List<LyricLine>(), "a.lrc");

        Run(new List<Music> { song }, "TTML");

        string ttml = _data.Content["TTML:a.lrc"];
        Assert.Contains("<body dur=\"00:00.000\">", ttml);
        Assert.Contains("<div begin=\"00:00.000\" end=\"00:00.000\">", ttml);
        Assert.DoesNotContain("<p ", ttml);
    }

    [Fact]
    public void WordSyncedLyricBecomesHmrcWithRelativeWordTimes()
    {
        Music song = Song("a.flac");
        _data.Lyrics[song] = WordSynced("a.lrc");

        Run(new List<Music> { song }, "HMRC");

        string expected = Join(
                "[offset:0]",
                "[1000,2500]<0,1000>Hello <1000,1500>A&B",
                "[61234,766]<0,766>Bye",
                "");
        Assert.Equal(expected, _data.Content["HMRC:a.lrc"]);
    }

    [Fact]
    public void ALineSyncedLyricBecomesHmrcLinesWithoutWords()
    {
        Music song = Song("a.flac");
        _data.Lyrics[song] = LineSynced("a.lrc");

        Run(new List<Music> { song }, "HMRC");

        Assert.Equal("[offset:0]\n[1000,2000]\n", _data.Content["HMRC:a.lrc"]);
    }

    [Fact]
    public void LyricBecomesALineSyncedLrcWithTheDefaultOffset()
    {
        Music song = Song("a.flac");
        _data.Lyrics[song] = WordSynced("a.lrc");

        Run(new List<Music> { song }, "LRC");

        string expected = Join(
                "[offset:700]",
                "[00:01.00]Hello A&B",
                "[01:01.23]Bye",
                "");
        Assert.Equal(expected, _data.Content["LRC:a.lrc"]);
    }

    // ---- the batch ----

    [Fact]
    public void EveryRequestedFormatIsSavedInTheOrderGiven()
    {
        Music song = Song("a.flac");
        _data.Lyrics[song] = WordSynced("a.lrc");

        Run(new List<Music> { song }, "LRC", "TTML", "HMRC");

        Assert.Equal(new List<string> { "LRC:a.lrc", "TTML:a.lrc", "HMRC:a.lrc" }, _data.Saved);
        Assert.Equal(1, _output.Presented.SuccessCount);
        Assert.Equal(0, _output.Presented.FailCount);
        Assert.Equal(1, _output.Presented.TotalCount);
        Assert.Empty(_output.Presented.FailedPaths);
        Assert.Equal(new List<string> { "a.flac:true" }, _output.Progress);
    }

    [Fact]
    public void TheDataAccessIsAskedForEverySongInOrder()
    {
        Music first = Song("1.flac");
        Music second = Song("2.flac");

        Run(new List<Music> { first, second });

        Assert.Equal(2, _data.Asked.Count);
        Assert.Same(first, _data.Asked[0]);
        Assert.Same(second, _data.Asked[1]);
    }

    [Fact]
    public void ASongWithoutALyricFileIsReportedAsFailedAndTheBatchContinues()
    {
        Music missing = Song("missing.flac");
        Music found = Song("found.flac");
        _data.Lyrics[found] = WordSynced("found.lrc");

        Run(new List<Music> { missing, found }, "TTML");

        Assert.Equal(new List<string> { "missing.flac:false", "found.flac:true" }, _output.Progress);
        Assert.Equal(1, _output.Presented.SuccessCount);
        Assert.Equal(1, _output.Presented.FailCount);
        Assert.Equal(2, _output.Presented.TotalCount);
        Assert.Equal(new List<string> { "missing.flac" }, _output.Presented.FailedPaths);
        Assert.Equal(new List<string> { "TTML:found.lrc" }, _data.Saved);
    }

    [Fact]
    public void AFailingSaveFailsOnlyThatSongAndNamesItsLyricFile()
    {
        Music bad = Song("bad.flac");
        Music good = Song("good.flac");
        _data.Lyrics[bad] = WordSynced("bad.lrc");
        _data.Lyrics[good] = WordSynced("good.lrc");
        _data.FailOn = key => key == "HMRC:bad.lrc" ? new IOException("disk full") : null;

        Run(new List<Music> { bad, good }, "TTML", "HMRC");

        Assert.Equal(new List<string> { "bad.flac:false", "good.flac:true" }, _output.Progress);
        Assert.Equal(1, _output.Presented.SuccessCount);
        Assert.Equal(1, _output.Presented.FailCount);
        // the failed path is the lyric file here (not the music file), as in the Java
        Assert.Equal(new List<string> { "bad.lrc" }, _output.Presented.FailedPaths);
        Assert.Equal(new List<string> { "TTML:bad.lrc", "TTML:good.lrc", "HMRC:good.lrc" }, _data.Saved);
    }

    [Fact]
    public void AnUnknownFormatIsIgnored()
    {
        Music song = Song("a.flac");
        _data.Lyrics[song] = WordSynced("a.lrc");

        Run(new List<Music> { song }, "SRT");

        Assert.Empty(_data.Saved);
        Assert.Equal(1, _output.Presented.SuccessCount);
    }

    [Fact]
    public void ANegativeTimeKeepsJavasZeroPaddingOfTheSign()
    {
        Music song = Song("a.flac");
        _data.Lyrics[song] = new Lyric(Lyric.SyncType.LineSync, new List<LyricLine>
        {
            Line("early", -1500, 10),
            Line("tiny", -5, 10),
        }, "a.lrc");

        Run(new List<Music> { song }, "TTML", "LRC");

        // Java's %02d / %03d count the minus sign inside the width: -1 stays "-1", -5 with width 3 is "-05"
        string ttml = _data.Content["TTML:a.lrc"];
        Assert.Contains("<p begin=\"00:-1.-500\" end=\"00:00.010\"", ttml);
        Assert.Contains("<p begin=\"00:00.-05\" end=\"00:00.010\"", ttml);
        Assert.Equal(Join("[offset:700]", "[00:-1.-50]early", "[00:00.00]tiny", ""), _data.Content["LRC:a.lrc"]);
    }

    // ---- the input data ----

    [Fact]
    public void NoFormatsMeansEveryAvailableFormat()
    {
        Assert.Equal(new List<string> { "TTML", "HMRC", "LRC" }, ConvertLRCInputData.OrDefaultFormats(null));
        Assert.Equal(new List<string> { "TTML", "HMRC", "LRC" }, ConvertLRCInputData.OrDefaultFormats(new List<string>()));
    }

    [Fact]
    public void ChosenFormatsAreKeptAsTheyAre()
    {
        List<string> chosen = new List<string> { "LRC" };

        Assert.Same(chosen, ConvertLRCInputData.OrDefaultFormats(chosen));
    }
}
