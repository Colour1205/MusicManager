using System.Globalization;
using System.Text;
using MusicManager.UseCase.StandardizeLyrics;
using Xunit;

namespace MusicManager.Tests.UseCase.StandardizeLyrics;

/// <summary>
/// The whole use case over real files with the real parsers and formatter. Only Claude is scripted: a subclass of the
/// one DataAccess answers the romaji requests (every Hangul/kana character becomes "x").
/// </summary>
public class StandardizeLyricsInteractorTest : IDisposable
{
    // a Java text block: line breaks are "\n", whatever the line endings of this source file are
    private static readonly string Ttml = Lf("""
        <?xml version="1.0" encoding="UTF-8"?><tt xmlns="http://www.w3.org/ns/ttml" xmlns:itunes="http://music.apple.com/lyric-ttml-internal" xmlns:ttm="http://www.w3.org/ns/ttml#metadata" itunes:timing="Word"><head><metadata><ttm:agent type="person" xml:id="v1"/></metadata></head><body dur="00:06.000"><div begin="00:00.000" end="00:06.000">
        <p begin="00:01.000" end="00:03.000" ttm:agent="v1" itunes:key="L1"><span begin="00:01.000" end="00:02.000">안녕 </span><span begin="00:02.000" end="00:03.000">세상</span></p>
        <p begin="00:04.000" end="00:06.000" ttm:agent="v1" itunes:key="L2"><span begin="00:04.000" end="00:05.000">Hello </span><span begin="00:05.000" end="00:06.000">world</span></p>
        </div></body></tt>
        """);

    private const string Hmrc = "[offset:0]\n[1000,2000]<0,1000>안녕 <1000,1000>세상\n[4000,2000]<0,1000>Hello <1000,1000>world\n";

    private const string LineSyncedLrc = "[offset:700]\n[00:01.00]안녕 세상\n[00:04.00]Hello world\n";

    /// <summary>LDDC's line-level output: one timed "word" per line</summary>
    private const string LddcLineLevel = "[ti:t]\r\n[tool:LDDC v0.9.0]\r\n\r\n[00:01.000]안녕 세상[00:03.000]\r\n"
        + "[00:04.000]Hello world[00:06.000]\r\n";

    /// <summary>an LDDC word-synced file whose "romaji" is Chinese characters, as LDDC writes it</summary>
    private const string LddcWithChineseTransliteration = "[ti:t]\r\n[tool:LDDC v0.9.0]\r\n\r\n"
        + "[00:01.000]<00:01.000>安宁 <00:02.000>塞桑<00:03.000>\r\n"
        + "[00:01.000]<00:01.000>안녕 <00:02.000>세상<00:03.000>\r\n"
        + "[00:01.000]<00:01.000>你好世界<00:03.000>\r\n"
        + "[00:04.000]<00:04.000>Hello <00:05.000>world<00:06.000>\r\n";

    private const string StandardWithLatinRomaji = "[ti:t]\r\n[tool:LDDC v0.9.0]\r\n\r\n"
        + "[00:01.000]<00:01.000>annyeong <00:02.000>sesang<00:03.000>\r\n"
        + "[00:01.000]<00:01.000>안녕 <00:02.000>세상<00:03.000>\r\n";

    private readonly string _library; // @TempDir

    private readonly ScriptedData _data;
    private readonly Capture _output;

    // ---- the fakes ----

    /// <summary>the real DataAccess with Claude replaced</summary>
    private sealed class ScriptedData : RealDataAccess
    {
        public string Key = "sk-ant-test";
        public readonly List<List<RomajiLine>> RomanizeCalls = new List<List<RomajiLine>>();
        public int EstimateCalls;
        public Func<List<RomajiLine>, RomajiResult> Claude = EverythingWorks;

        public override string ReadApiKeyFromEnvironment()
        {
            return Key;
        }

        public override RomajiEstimate EstimateRomaji(List<RomajiLine> lines)
        {
            EstimateCalls++;
            return new RomajiEstimate(lines.Count, 0, lines.Count == 0 ? 0 : 1);
        }

        public override RomajiResult Romanize(List<RomajiLine> lines, string apiKey, string model,
            RomajiProgressListener progress)
        {
            RomanizeCalls.Add(lines);
            return Claude(lines);
        }

        public static RomajiResult EverythingWorks(List<RomajiLine> lines)
        {
            Dictionary<string, List<string>> romaji = new Dictionary<string, List<string>>();
            foreach (RomajiLine line in lines)
            {
                List<string> pieces = new List<string>();
                foreach (string word in line.Words)
                {
                    pieces.Add(ScriptedRomaji.ReplaceWithX(word.Trim()));
                }
                romaji[line.Key] = pieces;
            }
            return new RomajiResult(romaji, 0, 1, null);
        }
    }

    private sealed class Capture : IStandardizeLyricsOutputBoundary
    {
        public StandardizeLyricsOutputData Result;
        public string Error;
        public readonly List<string> Progress = new List<string>();

        public void Present(StandardizeLyricsOutputData outputData)
        {
            Result = outputData;
        }

        public void PresentProgress(int done, int total, string label)
        {
            Progress.Add(label);
        }

        public void PresentError(string message)
        {
            Error = message;
        }

        public SongLyricsResult Only()
        {
            Assert.Equal(1, Result.Results.Count);
            return Result.Results[0];
        }
    }

    public StandardizeLyricsInteractorTest()
    {
        _library = Path.Combine(Path.GetTempPath(), "mm-" + Guid.NewGuid());
        Directory.CreateDirectory(_library);
        _data = new ScriptedData();
        _output = new Capture();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_library, true);
        }
        catch (IOException)
        {
            // a temp folder that cannot be removed is left to the system
        }
    }

    // ---- helpers ----

    private static string Lf(string text) => text.Replace("\r\n", "\n");

    private string Audio(string name)
    {
        string file = Path.Combine(_library, "Album", name + ".flac");
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.WriteAllBytes(file, new byte[0]);
        return file;
    }

    private string Sidecar(string name, string extension, string text)
    {
        string file = Path.Combine(_library, "Album", name + extension);
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.WriteAllBytes(file, Encoding.UTF8.GetBytes(text));
        return file;
    }

    private string Song(string name, string ttml, string hmrc, string lrc)
    {
        Audio(name);
        if (ttml != null)
        {
            Sidecar(name, ".ttml", ttml);
        }
        if (hmrc != null)
        {
            Sidecar(name, ".hmrc", hmrc);
        }
        return lrc == null ? Path.Combine(_library, "Album", name + ".lrc") : Sidecar(name, ".lrc", lrc);
    }

    private static string Sibling(string file, string name) => Path.Combine(Path.GetDirectoryName(file), name);

    private void Run(bool dryRun, bool romanize)
    {
        new StandardizeLyricsInteractor(_data, _output)
            .Execute(new StandardizeLyricsInputData(_library, dryRun, false, romanize, null, "m"));
    }

    // Files.readString: the bytes as UTF-8 text, a byte order mark is not removed
    private static string Read(string file) => Encoding.UTF8.GetString(File.ReadAllBytes(file));

    private static List<string> BodyLines(string lrc)
    {
        List<string> lines = new List<string>();
        foreach (string line in lrc.Split("\r\n"))
        {
            if (line.StartsWith("[0", StringComparison.Ordinal))
            {
                lines.Add(line);
            }
        }
        return lines;
    }

    private static string Two(int number) => number.ToString("D2", CultureInfo.InvariantCulture);

    // ---- converting ----

    [Fact]
    public void ATtmlBecomesOneStandardLrcAndTheDeprecatedFilesAreDeleted()
    {
        string lrc = Song("a", Ttml, Hmrc, LineSyncedLrc);

        Run(false, false);

        Assert.Equal(LyricsAction.ConvertedFromTtml, _output.Only().Action);
        Assert.Equal(2, _output.Only().DeletedFiles.Count);
        Assert.False(File.Exists(Sibling(lrc, "a.ttml")));
        Assert.False(File.Exists(Sibling(lrc, "a.hmrc")));
        List<string> body = BodyLines(Read(lrc));
        Assert.Equal("[00:01.000]<00:01.000>안녕 <00:02.000>세상<00:03.000>", body[0]);
        Assert.Equal("[00:04.000]<00:04.000>Hello <00:05.000>world<00:06.000>", body[1]);
        Assert.True(StandardLrcFormatter.IsStandardWordSynced(Read(lrc)));
    }

    [Fact]
    public void TheHeaderTagsComeFromTheFileNameWhenTheAudioHasNoTags()
    {
        string lrc = Song("a", Ttml, null, null);

        Run(false, false);

        Assert.True(Read(lrc).StartsWith("[ti:", StringComparison.Ordinal), Read(lrc));
        Assert.Contains("[tool:Music Manager]", Read(lrc));
    }

    [Fact]
    public void WhenThereIsNoTtmlTheHmrcIsUsed()
    {
        string lrc = Song("a", null, Hmrc, LineSyncedLrc);

        Run(false, false);

        Assert.Equal(LyricsAction.ConvertedFromHmrc, _output.Only().Action);
        Assert.StartsWith("[00:01.000]<00:01.000>안녕 ", BodyLines(Read(lrc))[0], StringComparison.Ordinal);
        Assert.False(File.Exists(Sibling(lrc, "a.hmrc")));
    }

    [Fact]
    public void ADryRunReportsWhatWouldHappenAndChangesNothing()
    {
        string lrc = Song("a", Ttml, Hmrc, LineSyncedLrc);

        Run(true, true);

        Assert.Equal(LyricsAction.ConvertedFromTtml, _output.Only().Action);
        Assert.True(_output.Only().DeletedFiles.Count == 2, "the files that would go are listed");
        Assert.True(File.Exists(Sibling(lrc, "a.ttml")));
        Assert.True(File.Exists(Sibling(lrc, "a.hmrc")));
        Assert.Equal(LineSyncedLrc, Read(lrc));
        Assert.True(_data.RomanizeCalls.Count == 0, "a preview never calls Claude");
        Assert.Equal(1, _data.EstimateCalls);
        Assert.True(_output.Result.IsDryRun);
    }

    [Fact]
    public void AFileAlreadyInTheStandardOnlyLosesItsDeprecatedFiles()
    {
        string lrc = Song("a", Ttml, Hmrc, StandardWithLatinRomaji);

        Run(false, false);

        Assert.Equal(LyricsAction.AlreadyStandard, _output.Only().Action);
        Assert.True(StandardWithLatinRomaji == Read(lrc), "the standard file is not rewritten");
        Assert.False(File.Exists(Sibling(lrc, "a.ttml")));
    }

    [Fact]
    public void LineLevelLddcOutputIsLineSyncedSoItIsKeptAsItIs()
    {
        string lrc = Song("a", null, null, LddcLineLevel);

        // without the romaji option; with it, a line-level file gets romaji too (see StandardizeLyricsLrcRomajiTest)
        Run(false, false);

        Assert.Equal(LyricsAction.KeptLineSynced, _output.Only().Action);
        Assert.Equal(LddcLineLevel, Read(lrc));
        Assert.True(_data.RomanizeCalls.Count == 0, "nothing is asked of Claude when romaji is off");
    }

    [Fact]
    public void ASongWithoutLyricsIsReportedAndAnOrphanLrcIsNotTouched()
    {
        Audio("no lyrics");
        string orphan = Sidecar("orphan", ".lrc", LineSyncedLrc); // no audio file shares its name

        Run(false, false);

        Assert.Equal(LyricsAction.NoLyrics, _output.Only().Action);
        Assert.Equal(LineSyncedLrc, Read(orphan));
    }

    [Fact]
    public void UnreadableLyricsFailWithoutDeletingAnything()
    {
        string ttml = Sidecar("a", ".ttml", "this is not lyrics at all");
        Audio("a");

        Run(false, false);

        Assert.Equal(LyricsAction.Failed, _output.Only().Action);
        Assert.True(File.Exists(ttml));
    }

    [Fact]
    public void RepeatedNotesAboutOneFileAreCountedInsteadOfListed()
    {
        StringBuilder ttml = new StringBuilder("<?xml version=\"1.0\"?><tt xmlns=\"http://www.w3.org/ns/ttml\" "
            + "xmlns:itunes=\"http://music.apple.com/lyric-ttml-internal\" itunes:timing=\"Word\"><body><div>");
        for (int i = 0; i < 12; i++)
        {
            ttml.Append("<p begin=\"00:").Append(Two(i + 1)).Append(".000\" end=\"00:")
                .Append(Two(i + 2)).Append(".000\"><span begin=\"00:")
                .Append(Two(i + 1)).Append(".000\" end=\"00:").Append(Two(i + 2))
                .Append(".000\">a b �</span><span begin=\"00:").Append(Two(i + 1))
                .Append(".500\" end=\"00:").Append(Two(i + 2)).Append(".000\">c</span></p>");
        }
        ttml.Append("</div></body></tt>");
        Song("a", ttml.ToString(), null, null);

        Run(false, false);

        string detail = _output.Only().Detail;
        Assert.True(detail.Length < 300, "one counted note, not twelve: " + detail);
    }

    // ---- other kinds of libraries ----

    [Fact]
    public void UpperCaseExtensionsAndOtherAudioFormatsAreFoundToo()
    {
        string album = Directory.CreateDirectory(Path.Combine(_library, "Album")).FullName;
        File.WriteAllBytes(Path.Combine(album, "Song.MP3"), new byte[0]);
        File.WriteAllBytes(Path.Combine(album, "Song.TTML"), Encoding.UTF8.GetBytes(Ttml));
        File.WriteAllBytes(Path.Combine(album, "Other.m4a"), new byte[0]);
        File.WriteAllBytes(Path.Combine(album, "Other.Hmrc"), Encoding.UTF8.GetBytes(Hmrc));

        Run(false, false);

        Assert.Equal(2, _output.Result.Results.Count);
        Assert.Equal(2, _output.Result.Count(LyricsAction.ConvertedFromTtml) + _output.Result.Count(LyricsAction.ConvertedFromHmrc));
        Assert.True(File.Exists(Path.Combine(album, "Song.lrc")));
        Assert.True(File.Exists(Path.Combine(album, "Other.lrc")));
        Assert.False(File.Exists(Path.Combine(album, "Song.TTML")));
        Assert.False(File.Exists(Path.Combine(album, "Other.Hmrc")));
    }

    [Fact]
    public void SameNamedSongsInDifferentAlbumsAreHandledSeparately()
    {
        foreach (string album in new[] { "One", "Two" })
        {
            string folder = Directory.CreateDirectory(Path.Combine(_library, album)).FullName;
            File.WriteAllBytes(Path.Combine(folder, "Intro.flac"), new byte[0]);
            File.WriteAllBytes(Path.Combine(folder, "Intro.ttml"), Encoding.UTF8.GetBytes(Ttml));
        }

        Run(false, false);

        Assert.Equal(2, _output.Result.Results.Count);
        Assert.True(File.Exists(Path.Combine(_library, "One", "Intro.lrc")));
        Assert.True(File.Exists(Path.Combine(_library, "Two", "Intro.lrc")));
    }

    [Fact]
    public void AnOldLrcInAnotherEncodingIsKeptByteForByte()
    {
        Audio("a");
        string lrc = Path.Combine(_library, "Album", "a.lrc");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        byte[] gbk = Encoding.GetEncoding("GB18030").GetBytes("[00:01.00]你好世界\n[00:04.00]再见\n");
        File.WriteAllBytes(lrc, gbk);

        Run(false, true);

        Assert.Equal(LyricsAction.KeptLineSynced, _output.Only().Action);
        Assert.True(gbk.SequenceEqual(File.ReadAllBytes(lrc)), "an untouched file stays untouched");
    }

    [Fact]
    public void ATtmlWithAByteOrderMarkAndWindowsLineEndingsIsRead()
    {
        Audio("a");
        byte[] body = Encoding.UTF8.GetBytes(Ttml.Replace("\n", "\r\n"));
        byte[] withBom = new byte[body.Length + 3];
        withBom[0] = 0xEF;
        withBom[1] = 0xBB;
        withBom[2] = 0xBF;
        Array.Copy(body, 0, withBom, 3, body.Length);
        File.WriteAllBytes(Path.Combine(_library, "Album", "a.ttml"), withBom);

        Run(false, false);

        Assert.Equal(LyricsAction.ConvertedFromTtml, _output.Only().Action);
    }

    // Java: assumeTrue(os.name contains "win"), "an open file cannot be deleted on Windows; elsewhere it can". The port
    // is a Windows application and its tests run on Windows only, so the condition always holds and the test just runs.
    [Fact]
    public void ADeprecatedFileThatCannotBeDeletedIsReportedAndTheLrcIsStillWritten()
    {
        string lrc = Song("a", Ttml, null, null);
        string ttml = Sibling(lrc, "a.ttml");

        // a stream opened without FileShare.Delete (like java.io's FileInputStream) makes Windows refuse to delete the file
        using (FileStream holdsTheFileOpen = new FileStream(ttml, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Run(false, false);
            Assert.NotNull(holdsTheFileOpen);
        }

        Assert.Equal(LyricsAction.ConvertedFromTtml, _output.Only().Action);
        Assert.Contains("could not delete a.ttml", _output.Only().Detail);
        Assert.True(File.Exists(ttml), "kept, and reported");
        Assert.True(StandardLrcFormatter.IsStandardWordSynced(Read(lrc)));
    }

    // ---- romaji ----

    [Fact]
    public void RomajiIsMadeForEveryKoreanLineAndWrittenAboveTheOriginal()
    {
        string lrc = Song("a", Ttml, null, null);

        Run(false, true);

        Assert.Equal(LyricsAction.ConvertedFromTtml, _output.Only().Action);
        Assert.Contains("romaji added to 1 line", _output.Only().Detail);
        List<string> body = BodyLines(Read(lrc));
        Assert.Equal(3, body.Count);
        Assert.Equal("[00:01.000]<00:01.000>xx <00:02.000>xx<00:03.000>", body[0]);
        Assert.Equal("[00:01.000]<00:01.000>안녕 <00:02.000>세상<00:03.000>", body[1]);
        Assert.Equal("[00:04.000]<00:04.000>Hello <00:05.000>world<00:06.000>", body[2]);
        Assert.Equal(1, _output.Result.Romaji.LinesAdded);
        Assert.Equal(0, _output.Result.Romaji.LinesMissing);
    }

    [Fact]
    public void TheFileWithRomajiReadsBackAsTheSameLyric()
    {
        string lrc = Song("a", Ttml, null, null);
        Run(false, true);

        // a second run finds nothing left to do
        Run(false, true);

        Assert.Equal(LyricsAction.AlreadyStandard, _output.Only().Action);
        Assert.True(_data.RomanizeCalls.Count == 1, "the second run did not ask Claude again");
        Assert.True(File.Exists(lrc));
    }

    [Fact]
    public void AChorusSungInSeveralSongsIsSentToClaudeOnce()
    {
        Song("a", Ttml, null, null);
        Song("b", Ttml, null, null);
        Song("c", Ttml, null, null);

        Run(false, true);

        Assert.True(_data.RomanizeCalls.Count == 1, "one request for the whole library");
        Assert.True(_data.RomanizeCalls[0].Count == 1, "the three songs share one Korean line");
        Assert.Equal(3, _output.Result.Romaji.LinesAdded);
    }

    [Fact]
    public void RomajiInChineseCharactersIsReplacedByLatinLetters()
    {
        string lrc = Song("a", null, null, LddcWithChineseTransliteration);

        Run(false, true);

        Assert.Equal(LyricsAction.RomajiAdded, _output.Only().Action);
        string text = Read(lrc);
        Assert.False(text.Contains("安宁"), "the Chinese-character transliteration is gone");
        Assert.True(text.Contains("<00:01.000>xx <00:02.000>xx<00:03.000>\r\n[00:01.000]<00:01.000>안녕 "), text);
        Assert.True(text.Contains("你好世界"), "the Chinese translation stays");
        Assert.Contains("Hello <00:05.000>world", text);
    }

    [Fact]
    public void AFileThatAlreadyHasLatinRomajiIsNotSentToClaude()
    {
        string lrc = Song("a", null, null, StandardWithLatinRomaji);

        Run(false, true);

        Assert.Equal(LyricsAction.AlreadyStandard, _output.Only().Action);
        Assert.Equal(StandardWithLatinRomaji, Read(lrc));
        Assert.Empty(_data.RomanizeCalls);
    }

    [Fact]
    public void WithoutTheRomajiOptionTheLyricsAreStandardizedWithoutIt()
    {
        string lrc = Song("a", Ttml, null, null);

        Run(false, false);

        Assert.Equal(2, BodyLines(Read(lrc)).Count);
        Assert.Empty(_data.RomanizeCalls);
        Assert.False(_output.Result.Romaji.IsRequested);
    }

    [Fact]
    public void WithoutAnApiKeyRomajiStopsBeforeChangingAnything()
    {
        _data.Key = null;
        string lrc = Song("a", Ttml, Hmrc, LineSyncedLrc);

        Run(false, true);

        Assert.NotNull(_output.Error);
        Assert.True(_output.Error.Contains("API key"), _output.Error);
        Assert.Null(_output.Result);
        Assert.True(File.Exists(Sibling(lrc, "a.ttml")));
        Assert.Equal(LineSyncedLrc, Read(lrc));
    }

    [Fact]
    public void APreviewDoesNotNeedAnApiKey()
    {
        _data.Key = null;
        Song("a", Ttml, null, null);

        Run(true, true);

        Assert.Null(_output.Error);
        Assert.NotNull(_output.Result);
    }

    [Fact]
    public void IfClaudeCannotBeUsedAtAllNothingIsChanged()
    {
        _data.Claude = lines => new RomajiResult(new Dictionary<string, List<string>>(), 0, 0, "The API key was rejected (check ANTHROPIC_API_KEY)");
        string lrc = Song("a", Ttml, Hmrc, LineSyncedLrc);

        Run(false, true);

        Assert.NotNull(_output.Error);
        Assert.True(_output.Error.Contains("rejected") && _output.Error.Contains("Nothing was changed"), _output.Error);
        Assert.True(File.Exists(Sibling(lrc, "a.ttml")), "the deprecated files are still there");
        Assert.Equal(LineSyncedLrc, Read(lrc));
    }

    [Fact]
    public void IfClaudeStopsHalfwayTheSongsAreStillWrittenAndTheMissingLinesAreCounted()
    {
        _data.Claude = lines => new RomajiResult(new Dictionary<string, List<string>>(), 0, 1, "rate limit - run it again");
        string lrc = Song("a", Ttml, null, null);

        Run(false, true);

        Assert.Equal(LyricsAction.ConvertedFromTtml, _output.Only().Action);
        Assert.Contains("romaji missing for 1 of 1 lines", _output.Only().Detail);
        Assert.True(BodyLines(Read(lrc)).Count == 2, "standardized, just without the romaji yet");
        Assert.Equal(1, _output.Result.Romaji.LinesMissing);
        Assert.Equal("rate limit - run it again", _output.Result.Romaji.StoppedBecause);
    }

    [Fact]
    public void AnExistingStandardFileIsLeftAloneWhenNoRomajiCouldBeMade()
    {
        _data.Claude = lines => new RomajiResult(new Dictionary<string, List<string>>(), 0, 1, "rate limit");
        string lrc = Song("a", null, null, LddcWithChineseTransliteration);

        Run(false, true);

        Assert.Equal(LyricsAction.AlreadyStandard, _output.Only().Action);
        Assert.True(LddcWithChineseTransliteration == Read(lrc), "no change without the replacement romaji");
    }

    [Fact]
    public void TheRomajiPreviewCountsTheLinesInTheSummary()
    {
        Song("a", Ttml, null, null);

        Run(true, true);

        RomajiSummary summary = _output.Result.Romaji;
        Assert.True(summary.IsRequested);
        Assert.Equal(1, summary.UniqueLines);
        Assert.Equal(1, summary.Requests);
        Assert.DoesNotContain("romaji", _output.Only().Detail); // a preview has no sentence about the lines: the summary and the song window tell them
    }

    [Fact]
    public void ProgressIsReportedForEverySongAndTheRomajiStep()
    {
        Song("a", Ttml, null, null);
        Song("b", Ttml, null, null);

        Run(false, true);

        Assert.True(_output.Progress.Count >= 4, string.Join(", ", _output.Progress));
        Assert.True(_output.Progress.Any(label => label.StartsWith("Writing ", StringComparison.Ordinal)),
            string.Join(", ", _output.Progress));
    }
}
