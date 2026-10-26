using System.Text;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeLyrics;
using Xunit;

namespace MusicManager.Tests.UseCase.StandardizeLyrics;

/// <summary>
/// Romaji for songs whose only lyric file is an .lrc (no .ttml, no .hmrc): word-synced, line-level (LDDC's
/// "[t]text[t]") and plain line-synced, the replacement of old Chinese-character romaji, the per-song counts, the scope of
/// a run and the verification that guards every write. Real files in a temp folder, the real parsers and formatter; only
/// Claude is scripted (a subclass of the one DataAccess).
/// </summary>
public class StandardizeLyricsLrcRomajiTest : IDisposable
{
    private const string Header = "[ti:t]\r\n[tool:LDDC v0.9.0]\r\n\r\n";

    // a Java text block: line breaks are "\n", whatever the line endings of this source file are
    private static readonly string Ttml = Lf("""
        <?xml version="1.0" encoding="UTF-8"?><tt xmlns="http://www.w3.org/ns/ttml" xmlns:itunes="http://music.apple.com/lyric-ttml-internal" xmlns:ttm="http://www.w3.org/ns/ttml#metadata" itunes:timing="Word"><head><metadata><ttm:agent type="person" xml:id="v1"/></metadata></head><body dur="00:06.000"><div begin="00:00.000" end="00:06.000">
        <p begin="00:01.000" end="00:03.000" ttm:agent="v1" itunes:key="L1"><span begin="00:01.000" end="00:02.000">안녕 </span><span begin="00:02.000" end="00:03.000">세상</span></p>
        </div></body></tt>
        """);

    /// <summary>standard word-synced, no romaji; the one-word Korean line is not worth a romaji line</summary>
    private const string StandardNoRomaji = Header
        + "[00:01.000]<00:01.000>안녕 <00:02.000>세상<00:03.000>\r\n"
        + "[00:04.000]<00:04.000>Hello <00:05.000>world<00:06.000>\r\n"
        + "[00:07.000]<00:07.000>응<00:08.000>\r\n";

    private const string StandardWithLatinRomaji = Header
        + "[00:01.000]<00:01.000>annyeong <00:02.000>sesang<00:03.000>\r\n"
        + "[00:01.000]<00:01.000>안녕 <00:02.000>세상<00:03.000>\r\n";

    /// <summary>the romaji of an LDDC file made with its romaji option: Chinese characters</summary>
    private const string StandardWithChineseRomaji = Header
        + "[00:01.000]<00:01.000>安宁 <00:02.000>塞桑<00:03.000>\r\n"
        + "[00:01.000]<00:01.000>안녕 <00:02.000>세상<00:03.000>\r\n"
        + "[00:01.000]<00:01.000>你好世界<00:03.000>\r\n"
        + "[00:04.000]<00:04.000>Hello <00:05.000>world<00:06.000>\r\n";

    /// <summary>one line to add romaji to, one with old Chinese-character romaji, one with Latin romaji, one English</summary>
    private const string Mixed = Header
        + "[00:01.000]<00:01.000>안녕 <00:02.000>세상<00:03.000>\r\n"
        + "[00:04.000]<00:04.000>家 <00:05.000>那达<00:06.000>\r\n"
        + "[00:04.000]<00:04.000>가 <00:05.000>나다<00:06.000>\r\n"
        + "[00:07.000]<00:07.000>rama <00:08.000>basa<00:09.000>\r\n"
        + "[00:07.000]<00:07.000>라마 <00:08.000>바사<00:09.000>\r\n"
        + "[00:10.000]<00:10.000>Hello <00:11.000>world<00:12.000>\r\n";

    /// <summary>a Korean line with Chinese characters in it, its romaji made the way the rule asks: the Chinese characters stay</summary>
    private const string KoreanWithChineseKept = Header
        + "[00:01.000]<00:01.000>Name <00:02.000>(以我之名) <00:03.000>(aideul)<00:04.000>\r\n"
        + "[00:01.000]<00:01.000>Name <00:02.000>(以我之名) <00:03.000>(아이들)<00:04.000>\r\n";

    /// <summary>the same line with the romaji an earlier version of this app made: the Chinese characters turned into pinyin</summary>
    private const string KoreanWithChinesePinyin = Header
        + "[00:01.000]<00:01.000>Name <00:02.000>(yiwozhiming) <00:03.000>(aideul)<00:04.000>\r\n"
        + "[00:01.000]<00:01.000>Name <00:02.000>(以我之名) <00:03.000>(아이들)<00:04.000>\r\n";

    /// <summary>the same line with LDDC's romaji: the Hangul in Chinese characters</summary>
    private const string KoreanWithChineseLddc = Header
        + "[00:01.000]<00:01.000>Name <00:02.000>(以我之名) <00:03.000>(啊一的)<00:04.000>\r\n"
        + "[00:01.000]<00:01.000>Name <00:02.000>(以我之名) <00:03.000>(아이들)<00:04.000>\r\n";

    private const string KoreanWithChineseNoRomaji = Header
        + "[00:01.000]<00:01.000>Name <00:02.000>(以我之名) <00:03.000>(아이들)<00:04.000>\r\n";

    /// <summary>word-synced in the old bracket syntax, which is not the standard one</summary>
    private const string LegacyWordSynced = "[00:01.000]안녕 [00:02.000]세상[00:03.000]\r\n"
        + "[00:04.000]Hello [00:05.000]world[00:06.000]\r\n";

    /// <summary>LDDC's line-level output: one timed "word" per line, with a Chinese translation under the first line</summary>
    private const string LddcLineLevel = Header
        + "[00:01.000]안녕 세상[00:03.000]\r\n"
        + "[00:01.000]你好世界[00:03.000]\r\n"
        + "[00:04.000]Hello world[00:06.000]\r\n"
        + "[00:07.000]こんにちは[00:09.000]\r\n";

    private const string LineSynced = "[offset:700]\n[00:01.00]안녕 세상\n[00:01.00]你好世界\n"
        + "[00:04.00]Hello world\n[00:08.00]こんにちは\n";

    private readonly string _library; // @TempDir

    private readonly ScriptedData _data;
    private readonly Capture _output;

    // ---- the fakes ----

    /// <summary>the real DataAccess with Claude replaced, and a log of what the use case read, wrote and deleted</summary>
    private sealed class ScriptedData : RealDataAccess
    {
        private static readonly Dictionary<string, string> Known = new Dictionary<string, string>
        {
            ["안녕"] = "annyeong", ["세상"] = "sesang", ["안녕 세상"] = "annyeong sesang", ["こんにちは"] = "konnichiwa",
            ["잘 자요"] = "jal jayo", ["가"] = "ga", ["나다"] = "nada", ["라마"] = "rama", ["바사"] = "basa",
            ["(아이들)"] = "(aideul)"
        };

        public string Key = "sk-ant-test";
        public readonly List<List<RomajiLine>> RomanizeCalls = new List<List<RomajiLine>>();
        public readonly List<string> Reads = new List<string>();
        public readonly List<string> Writes = new List<string>();
        public readonly List<string> Deletes = new List<string>();
        public int EstimateCalls;
        public int TagReads;
        public Func<List<RomajiLine>, RomajiResult> Claude = EverythingWorks;
        public Func<string, string> Reread = text => text; // what the verification gets to see of the rendered text

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

        public override Lyric ReadLyric(string lyricFilePath)
        {
            Reads.Add(lyricFilePath);
            return base.ReadLyric(lyricFilePath);
        }

        public override Lyric ParseLrcText(string lrcText)
        {
            return base.ParseLrcText(Reread(lrcText));
        }

        public override Music ReadMusicTags(string audioPath)
        {
            TagReads++;
            return base.ReadMusicTags(audioPath);
        }

        public override void WriteLyricFile(string path, string content)
        {
            Writes.Add(path);
            base.WriteLyricFile(path, content);
        }

        public override bool DeleteFile(string path, bool moveToTrash)
        {
            Deletes.Add(path);
            return base.DeleteFile(path, moveToTrash);
        }

        public static string RomanizeWord(string word)
        {
            string stripped = word.Trim();
            return Known.TryGetValue(stripped, out string known) ? known : ScriptedRomaji.ReplaceWithX(stripped);
        }

        /// <summary>a Claude that follows the rule: only Hangul and kana are romanized, Chinese characters (and Latin) are copied</summary>
        public static RomajiResult KeepsChineseCharacters(List<RomajiLine> lines)
        {
            Dictionary<string, List<string>> romaji = new Dictionary<string, List<string>>();
            foreach (RomajiLine line in lines)
            {
                List<string> pieces = new List<string>();
                foreach (string word in line.Words)
                {
                    pieces.Add(RomajiScripts.HasHangulOrKana(word) ? RomanizeWord(word) : word.Trim());
                }
                romaji[line.Key] = pieces;
            }
            return new RomajiResult(romaji, 0, 1, null);
        }

        public static RomajiResult EverythingWorks(List<RomajiLine> lines)
        {
            Dictionary<string, List<string>> romaji = new Dictionary<string, List<string>>();
            foreach (RomajiLine line in lines)
            {
                List<string> pieces = new List<string>();
                foreach (string word in line.Words)
                {
                    pieces.Add(RomanizeWord(word));
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

        public void Present(StandardizeLyricsOutputData outputData)
        {
            Result = outputData;
        }

        public void PresentProgress(int done, int total, string label)
        {
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

    public StandardizeLyricsLrcRomajiTest()
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

    /// <summary>a song with only an .lrc</summary>
    private string LrcOnly(string name, string lrc)
    {
        Audio(name);
        return Sidecar(name, ".lrc", lrc);
    }

    private void Run(bool dryRun, bool romanize)
    {
        Run(dryRun, romanize, null);
    }

    private void Run(bool dryRun, bool romanize, HashSet<string> scope)
    {
        new StandardizeLyricsInteractor(_data, _output).Execute(
            new StandardizeLyricsInputData(_library, dryRun, false, romanize, null, "m", scope));
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

    private static void AssertCounts(SongLyricsResult result, int added, int replaced, int present, int missing)
    {
        // added, replaced, present, missing
        Assert.Equal(new List<int> { added, replaced, present, missing }, new List<int>
        {
            result.RomajiAdded, result.RomajiReplaced, result.RomajiPresent, result.RomajiMissing
        });
    }

    private static List<List<string>> WordsOf(List<RomajiLine> lines)
    {
        return lines.Select(line => line.Words.ToList()).ToList();
    }

    // ---- a reading shared by several characters ----

    [Fact]
    public void AReadingSharedByTwoCharactersIsWrittenOnTheFirstAndLastsForBothAndIsReadBackAsRomaji()
    {
        string lrc = LrcOnly("share", Header
            + "[00:01.000]<00:01.000>今<00:02.000>日<00:03.000>も<00:04.000>\r\n"
            + "[00:05.000]<00:05.000>Hello <00:06.000>world<00:07.000>\r\n");
        _data.Claude = lines =>
        {
            Dictionary<string, List<string>> romaji = new Dictionary<string, List<string>>();
            foreach (RomajiLine line in lines)
            {
                romaji[line.Key] = new List<string> { "kyou", "", "mo" }; // 今日 is one word that cannot be divided
            }
            return new RomajiResult(romaji, 0, 1, null);
        };

        Run(false, true);

        AssertCounts(_output.Only(), 1, 0, 0, 0);
        Assert.Equal(new List<string>
        {
            "[00:01.000]<00:01.000>kyou<00:03.000>mo<00:04.000>",
            "[00:01.000]<00:01.000>今<00:02.000>日<00:03.000>も<00:04.000>",
            "[00:05.000]<00:05.000>Hello <00:06.000>world<00:07.000>"
        }, BodyLines(Read(lrc)));

        // the file just written has nothing left to romanize: the romaji line is recognized as the line's romaji
        _data.RomanizeCalls.Clear();
        Run(false, true);
        Assert.Empty(_data.RomanizeCalls);
        AssertCounts(_output.Only(), 0, 0, 1, 0);
    }

    [Fact]
    public void ARomajiPieceThatIsEmptyKeepsTheSpacingOfItsWordOnTheWordBefore()
    {
        string lrc = LrcOnly("space", Header
            + "[00:01.000]<00:01.000>안 <00:02.000>녕 <00:03.000>세상<00:04.000>\r\n");
        _data.Claude = lines =>
        {
            Dictionary<string, List<string>> romaji = new Dictionary<string, List<string>>();
            foreach (RomajiLine line in lines)
            {
                romaji[line.Key] = new List<string> { "annyeong", "", "sesang" };
            }
            return new RomajiResult(romaji, 0, 1, null);
        };

        Run(false, true);

        Assert.Equal("[00:01.000]<00:01.000>annyeong <00:03.000>sesang<00:04.000>", BodyLines(Read(lrc))[0]);
    }

    // ---- a. a song with only an .lrc ----

    [Fact]
    public void AStandardWordSyncedLrcWithoutRomajiGetsARomajiLineAboveEachKoreanLine()
    {
        string lrc = LrcOnly("a", StandardNoRomaji);

        Run(false, true);

        Assert.Equal(LyricsAction.RomajiAdded, _output.Only().Action);
        AssertCounts(_output.Only(), 1, 0, 0, 0);
        Assert.Equal(new List<string>
        {
            "[00:01.000]<00:01.000>annyeong <00:02.000>sesang<00:03.000>",
            "[00:01.000]<00:01.000>안녕 <00:02.000>세상<00:03.000>",
            "[00:04.000]<00:04.000>Hello <00:05.000>world<00:06.000>",
            "[00:07.000]<00:07.000>응<00:08.000>"
        }, BodyLines(Read(lrc)));
        Assert.Equal(1, _data.RomanizeCalls.Count);
        Assert.Equal(new List<string> { "안녕 ", "세상" }, _data.RomanizeCalls[0][0].Words);
        Assert.Equal(1, _output.Result.Romaji.LinesAdded);
    }

    [Fact]
    public void ALrcThatAlreadyHasLatinRomajiIsLeftAloneAndClaudeIsNotAsked()
    {
        string lrc = LrcOnly("a", StandardWithLatinRomaji);

        Run(false, true);

        Assert.Equal(LyricsAction.AlreadyStandard, _output.Only().Action);
        AssertCounts(_output.Only(), 0, 0, 1, 0);
        Assert.Equal(StandardWithLatinRomaji, Read(lrc));
        Assert.Empty(_data.RomanizeCalls);
        Assert.Empty(_data.Writes);
        Assert.Equal(0, _output.Result.Romaji.LinesAdded);
    }

    [Fact]
    public void RomajiInChineseCharactersIsReplacedAndTheCountsSayReplaced()
    {
        string lrc = LrcOnly("a", StandardWithChineseRomaji);

        Run(false, true);

        Assert.Equal(LyricsAction.RomajiAdded, _output.Only().Action);
        AssertCounts(_output.Only(), 1, 1, 0, 0);
        // nothing new was added: the one line only had old romaji, which is replaced (the counts never overlap)
        Assert.True(_output.Only().Detail.Contains("1 old Chinese-character line replaced with Latin romaji"),
            _output.Only().Detail);
        string text = Read(lrc);
        Assert.False(text.Contains("安宁"), "the old transliteration is gone");
        Assert.True(text.Contains("[00:01.000]<00:01.000>annyeong <00:02.000>sesang<00:03.000>\r\n"
            + "[00:01.000]<00:01.000>안녕 <00:02.000>세상<00:03.000>\r\n[00:01.000]<00:01.000>你好世界<00:03.000>\r\n"),
            text);
        Assert.Equal(1, _output.Result.Romaji.LinesAdded);
        Assert.Equal(1, _output.Result.Romaji.LinesReplaced);
    }

    // ---- Chinese characters in a Korean line stay Chinese; only Korean and Japanese are romanized ----

    [Fact]
    public void ARomajiLineThatKeepsTheChineseCharactersOfAKoreanLineIsLeftAloneAndNeverRedone()
    {
        string lrc = LrcOnly("a", KoreanWithChineseKept);

        Run(false, true);
        Run(false, true); // a second run must find nothing to do either

        Assert.Equal(LyricsAction.AlreadyStandard, _output.Only().Action);
        AssertCounts(_output.Only(), 0, 0, 1, 0);
        Assert.Equal(KoreanWithChineseKept, Read(lrc));
        Assert.Empty(_data.RomanizeCalls);
        Assert.Empty(_data.Writes);
    }

    [Fact]
    public void ANewRomajiLineKeepsTheChineseCharactersOfTheKoreanLine()
    {
        string lrc = LrcOnly("a", KoreanWithChineseNoRomaji);
        _data.Claude = ScriptedData.KeepsChineseCharacters;

        Run(false, true);

        Assert.Equal(LyricsAction.RomajiAdded, _output.Only().Action);
        AssertCounts(_output.Only(), 1, 0, 0, 0);
        Assert.Equal(new List<string>
        {
            "[00:01.000]<00:01.000>Name <00:02.000>(以我之名) <00:03.000>(aideul)<00:04.000>",
            "[00:01.000]<00:01.000>Name <00:02.000>(以我之名) <00:03.000>(아이들)<00:04.000>"
        }, BodyLines(Read(lrc)));

        // and what was written is standard: the next run does nothing
        _data.RomanizeCalls.Clear();
        _data.Writes.Clear();
        Run(false, true);
        Assert.Equal(LyricsAction.AlreadyStandard, _output.Only().Action);
        Assert.Empty(_data.RomanizeCalls);
        Assert.Empty(_data.Writes);
    }

    [Fact]
    public void RomajiThatTurnedAKoreanLinesChineseCharactersIntoPinyinIsRedoneWithThemKept()
    {
        string lrc = LrcOnly("a", KoreanWithChinesePinyin);
        _data.Claude = ScriptedData.KeepsChineseCharacters;

        Run(false, true);

        Assert.Equal(LyricsAction.RomajiAdded, _output.Only().Action);
        AssertCounts(_output.Only(), 1, 1, 0, 0);
        string text = Read(lrc);
        Assert.DoesNotContain("yiwozhiming", text);
        Assert.Contains("[00:01.000]<00:01.000>Name <00:02.000>(以我之名) <00:03.000>(aideul)<00:04.000>\r\n", text);
    }

    [Fact]
    public void LddcRomajiOfAKoreanLineWithChineseCharactersIsCountedAsReplacedInAPreview()
    {
        LrcOnly("a", KoreanWithChineseLddc);
        _data.Claude = ScriptedData.KeepsChineseCharacters;

        Run(true, true);

        Assert.Equal(LyricsAction.RomajiAdded, _output.Only().Action);
        AssertCounts(_output.Only(), 1, 1, 0, 0);
    }

    [Fact]
    public void ANonStandardWordSyncedLrcIsRewrittenWithItsRomaji()
    {
        string lrc = LrcOnly("a", LegacyWordSynced);

        Run(false, true);

        Assert.Equal(LyricsAction.NormalizedLrc, _output.Only().Action);
        AssertCounts(_output.Only(), 1, 0, 0, 0);
        Assert.Equal(new List<string>
        {
            "[00:01.000]<00:01.000>annyeong <00:02.000>sesang<00:03.000>",
            "[00:01.000]<00:01.000>안녕 <00:02.000>세상<00:03.000>",
            "[00:04.000]<00:04.000>Hello <00:05.000>world<00:06.000>"
        }, BodyLines(Read(lrc)));
    }

    [Fact]
    public void AnLddcLineLevelFileGetsAWordSyncedRomajiLineAboveEachKoreanOrJapaneseLine()
    {
        string lrc = LrcOnly("a", LddcLineLevel);

        Run(false, true);

        Assert.Equal(LyricsAction.RomajiAdded, _output.Only().Action);
        AssertCounts(_output.Only(), 2, 0, 0, 0);
        Assert.Equal(new List<string>
        {
            "[00:01.000]<00:01.000>annyeong sesang<00:03.000>",
            "[00:01.000]<00:01.000>안녕 세상<00:03.000>",
            "[00:01.000]<00:01.000>你好世界<00:03.000>",
            "[00:04.000]<00:04.000>Hello world<00:06.000>",
            "[00:07.000]<00:07.000>konnichiwa<00:09.000>",
            "[00:07.000]<00:07.000>こんにちは<00:09.000>"
        }, BodyLines(Read(lrc)));
        // a line of a line-level file goes to Claude as one word: the whole line
        Assert.Equal(new List<List<string>> { new List<string> { "안녕 세상" }, new List<string> { "こんにちは" } },
            WordsOf(_data.RomanizeCalls[0]));
    }

    [Fact]
    public void APlainLineSyncedLrcGetsALineOfRomajiAboveEachKoreanOrJapaneseLineAndKeepsItsOffset()
    {
        string lrc = LrcOnly("a", LineSynced);

        Run(false, true);

        Assert.Equal(LyricsAction.RomajiAdded, _output.Only().Action);
        AssertCounts(_output.Only(), 2, 0, 0, 0);
        string text = Read(lrc);
        Assert.Equal(new List<string>
        {
            "[00:01.000]annyeong sesang",
            "[00:01.000]안녕 세상",
            "[00:01.000]你好世界",
            "[00:04.000]Hello world",
            "[00:08.000]konnichiwa",
            "[00:08.000]こんにちは"
        }, BodyLines(text));
        Assert.True(text.Contains("[offset:700]\r\n"), text);
        Assert.False(text.Contains("<"), "no word timing is invented for a line-synced file");
        Assert.Equal(new List<List<string>> { new List<string> { "안녕 세상" }, new List<string> { "こんにちは" } },
            WordsOf(_data.RomanizeCalls[0]));
    }

    [Fact]
    public void TheFileWithItsRomajiIsReadBackAsPairsSoASecondRunAddsNothing()
    {
        string lddc = LrcOnly("lddc", LddcLineLevel);
        string plain = LrcOnly("plain", LineSynced);
        Run(false, true);
        string lddcAfter = Read(lddc);
        string plainAfter = Read(plain);
        int claudeCalls = _data.RomanizeCalls.Count;
        _data.Writes.Clear();

        Run(false, true);

        Assert.Equal(2, _output.Result.Results.Count);
        foreach (SongLyricsResult result in _output.Result.Results)
        {
            Assert.True(result.Action == LyricsAction.AlreadyStandard
                || result.Action == LyricsAction.KeptLineSynced, result.Action.ToString());
            AssertCounts(result, 0, 0, 2, 0);
        }
        Assert.True(claudeCalls == _data.RomanizeCalls.Count, "Claude was not asked again");
        Assert.True(_data.Writes.Count == 0, "nothing was rewritten");
        Assert.Equal(lddcAfter, Read(lddc));
        Assert.Equal(plainAfter, Read(plain));
    }

    [Fact]
    public void RomajiThatIsAlreadyInLatinLettersInALineLevelFileIsKept()
    {
        string text = "[00:01.000]annyeong sesang[00:03.000]\r\n[00:01.000]안녕 세상[00:03.000]\r\n";
        string lrc = LrcOnly("a", text);

        Run(false, true);

        Assert.Equal(LyricsAction.KeptLineSynced, _output.Only().Action);
        AssertCounts(_output.Only(), 0, 0, 1, 0);
        Assert.Equal(text, Read(lrc));
        Assert.Empty(_data.RomanizeCalls);
    }

    [Fact]
    public void ChineseCharacterRomajiInALineSyncedFileIsReplaced()
    {
        string lrc = LrcOnly("a", "[00:01.00]安宁塞桑\n[00:01.00]안녕 세상\n[00:04.00]Hello world\n");

        Run(false, true);

        Assert.Equal(LyricsAction.RomajiAdded, _output.Only().Action);
        AssertCounts(_output.Only(), 1, 1, 0, 0);
        Assert.Equal(new List<string> { "[00:01.000]annyeong sesang", "[00:01.000]안녕 세상", "[00:04.000]Hello world" },
            BodyLines(Read(lrc)));
    }

    [Fact]
    public void AKoreanTranslationUnderAnEnglishLineIsNotRomanizedAndNothingIsChanged()
    {
        // the same shape as "romaji above the Korean original": the English line is read as the Korean line's
        // (Latin) romaji, so it counts as present and nothing is added, asked or written
        string text = "[00:01.00]Hello world\n[00:01.00]안녕 세상\n[00:05.00]Good night\n[00:05.00]잘 자요\n";
        string lrc = LrcOnly("a", text);

        Run(false, true);

        Assert.Equal(LyricsAction.KeptLineSynced, _output.Only().Action);
        AssertCounts(_output.Only(), 0, 0, 2, 0);
        Assert.Equal(text, Read(lrc));
        Assert.Empty(_data.RomanizeCalls);
    }

    [Fact]
    public void InAWordSyncedFileAKoreanTranslationUnderAnEnglishLineIsLeftAsATranslation()
    {
        string text = Header + "[00:01.000]<00:01.000>Hello <00:02.000>world<00:03.000>\r\n"
            + "[00:01.000]<00:01.000>안녕 세상<00:03.000>\r\n";
        string lrc = LrcOnly("a", text);

        Run(false, true);

        Assert.Equal(LyricsAction.AlreadyStandard, _output.Only().Action);
        AssertCounts(_output.Only(), 0, 0, 0, 0);
        Assert.Equal(text, Read(lrc));
        Assert.Empty(_data.RomanizeCalls);
    }

    [Fact]
    public void UnsyncedPlainTextLyricsAreLeftAloneWithANote()
    {
        string text = "안녕 세상\nHello world\n";
        string lrc = LrcOnly("a", text);

        Run(false, true);

        Assert.Equal(LyricsAction.KeptLineSynced, _output.Only().Action);
        AssertCounts(_output.Only(), 0, 0, 0, 0);
        Assert.True(_output.Only().Detail.Contains("no timing"), _output.Only().Detail);
        Assert.Equal(text, Read(lrc));
        Assert.Empty(_data.RomanizeCalls);
    }

    [Fact]
    public void UnsyncedLyricsGetNoNoteWhenRomajiIsOffOrThereIsNothingToRomanize()
    {
        LrcOnly("korean", "안녕 세상\n");
        LrcOnly("english", "Hello world\n");

        Run(false, false);
        Assert.Equal("", _output.Result.Results[0].Detail);
        Assert.Equal("", _output.Result.Results[1].Detail);

        Run(false, true);
        Assert.Equal(1, _output.Result.Results.Count(r => r.Detail.Contains("no timing")));
    }

    [Fact]
    public void WithoutTheRomajiOptionALineLevelFileIsNotTouched()
    {
        string lrc = LrcOnly("a", LddcLineLevel);

        Run(false, false);

        Assert.Equal(LyricsAction.KeptLineSynced, _output.Only().Action);
        AssertCounts(_output.Only(), 0, 0, 0, 0);
        Assert.Equal(LddcLineLevel, Read(lrc));
    }

    [Fact]
    public void AnUnreadableDeprecatedFileIsKeptWhenALineSyncedLrcGetsItsRomaji()
    {
        string lrc = LrcOnly("a", LineSynced);
        string hmrc = Sidecar("a", ".hmrc", "this is not lyrics at all");

        Run(false, true);

        Assert.Equal(LyricsAction.RomajiAdded, _output.Only().Action);
        Assert.True(File.Exists(hmrc), "an unreadable deprecated file is never deleted");
        Assert.Contains("[00:01.000]annyeong sesang", BodyLines(Read(lrc)));
    }

    // ---- c. scope ----

    [Fact]
    public void OnlyTheSongsInTheScopeAreReadWrittenAndDeleted()
    {
        string a = LrcOnly("a", StandardNoRomaji);
        string b = LrcOnly("b", StandardNoRomaji);
        string bTtml = Sidecar("b", ".ttml", Ttml);
        LrcOnly("c", LineSynced);
        string cHmrc = Sidecar("c", ".hmrc", "[offset:0]\n[1000,2000]<0,1000>안녕 <1000,1000>세상\n");
        string aTtml = Sidecar("a", ".ttml", Ttml);

        Run(false, true, new HashSet<string> { Path.Combine(_library, "Album", "a.flac") });

        Assert.Equal(1, _output.Result.Results.Count);
        Assert.Equal(Path.Combine(_library, "Album", "a.flac"), _output.Only().AudioPath);
        Assert.NotEmpty(_data.Reads);
        Assert.True(_data.Reads.All(path => Path.GetFileName(path).StartsWith("a.", StringComparison.Ordinal)),
            "only the scope's files are read: " + string.Join(", ", _data.Reads));
        Assert.Equal(new List<string> { a }, _data.Writes);
        Assert.Equal(new List<string> { aTtml }, _data.Deletes);
        Assert.True(File.Exists(bTtml), "an out-of-scope song keeps its .ttml");
        Assert.True(File.Exists(cHmrc), "an out-of-scope song keeps its .hmrc");
        Assert.True(StandardNoRomaji == Read(b), "an out-of-scope song is not written");
        Assert.Equal(1, _data.RomanizeCalls.Count);
        Assert.True(_data.RomanizeCalls[0].Count == 1, "Claude is only asked about the scope's lines");
    }

    [Fact]
    public void AnOutOfScopeSongWithDeprecatedFilesAlongsideAStandardLrcLosesNothing()
    {
        LrcOnly("a", StandardWithLatinRomaji);
        LrcOnly("b", StandardWithLatinRomaji);
        string bTtml = Sidecar("b", ".ttml", Ttml);
        string aTtml = Sidecar("a", ".ttml", Ttml);

        Run(false, false, new HashSet<string> { Path.Combine(_library, "Album", "b.flac") });

        Assert.Equal(1, _output.Result.Results.Count);
        Assert.False(File.Exists(bTtml));
        Assert.True(File.Exists(aTtml));
    }

    [Fact]
    public void TheScopeMatchesAnEquivalentSpellingOfThePath()
    {
        LrcOnly("a", StandardNoRomaji);
        string roundabout = Path.Combine(_library, "Album", "..", "Album", "a.flac");

        Run(true, true, new HashSet<string> { roundabout });

        Assert.Equal(1, _output.Result.Results.Count);
    }

    [Fact]
    public void AnEmptyScopeHandlesNothing()
    {
        string lrc = LrcOnly("a", StandardNoRomaji);

        Run(false, true, new HashSet<string>());

        Assert.Empty(_output.Result.Results);
        Assert.Equal(StandardNoRomaji, Read(lrc));
        Assert.Empty(_data.RomanizeCalls);
    }

    [Fact]
    public void ADryRunWithAScopeCountsOnlyTheScopeAndChangesNothing()
    {
        string a = LrcOnly("a", LddcLineLevel);
        string b = LrcOnly("b", StandardNoRomaji);

        Run(true, true, new HashSet<string> { Path.Combine(Path.GetDirectoryName(a), "a.flac") });

        Assert.Equal(1, _output.Result.Results.Count);
        Assert.True(_output.Result.IsDryRun);
        AssertCounts(_output.Only(), 2, 0, 0, 0);
        Assert.Equal(2, _output.Result.Romaji.LinesAdded);
        Assert.Equal(1, _data.EstimateCalls);
        Assert.Empty(_data.RomanizeCalls);
        Assert.Empty(_data.Writes);
        Assert.Empty(_data.Deletes);
        Assert.Equal(LddcLineLevel, Read(a));
        Assert.Equal(StandardNoRomaji, Read(b));
    }

    [Fact]
    public void APreviewDoesNotReadTheAudioTagsButARealRunDoes()
    {
        LrcOnly("a", StandardNoRomaji);
        LrcOnly("b", LineSynced);

        Run(true, true);
        Assert.True(_data.TagReads == 0, "the header tags are not needed to plan a run");

        Run(false, true);
        Assert.Equal(2, _data.TagReads);
    }

    // ---- d. counts ----

    [Fact]
    public void TheCountsOfARealRunAreWhatWasDone()
    {
        string lrc = LrcOnly("a", Mixed);

        Run(false, true);

        SongLyricsResult result = _output.Only();
        Assert.Equal(LyricsAction.RomajiAdded, result.Action);
        AssertCounts(result, 2, 1, 1, 0);
        // 2 lines got romaji, one of them replacing old Chinese-character romaji: 1 new line + 1 replaced
        Assert.True(result.Detail.Contains("romaji added to 1 line, 1 old Chinese-character line replaced"),
            result.Detail);
        RomajiSummary summary = _output.Result.Romaji;
        Assert.Equal(2, summary.LinesAdded);
        Assert.Equal(1, summary.LinesReplaced);
        Assert.Equal(0, summary.LinesMissing);
        Assert.Equal(new List<string>
        {
            "[00:01.000]<00:01.000>annyeong <00:02.000>sesang<00:03.000>",
            "[00:01.000]<00:01.000>안녕 <00:02.000>세상<00:03.000>",
            "[00:04.000]<00:04.000>ga <00:05.000>nada<00:06.000>",
            "[00:04.000]<00:04.000>가 <00:05.000>나다<00:06.000>",
            "[00:07.000]<00:07.000>rama <00:08.000>basa<00:09.000>",
            "[00:07.000]<00:07.000>라마 <00:08.000>바사<00:09.000>",
            "[00:10.000]<00:10.000>Hello <00:11.000>world<00:12.000>"
        }, BodyLines(Read(lrc)));
    }

    [Fact]
    public void TheCountsOfADryRunAreWhatWouldBeDone()
    {
        string lrc = LrcOnly("a", Mixed);

        Run(true, true);

        SongLyricsResult result = _output.Only();
        Assert.Equal(LyricsAction.RomajiAdded, result.Action);
        AssertCounts(result, 2, 1, 1, 0);
        Assert.Equal("", result.Detail); // a preview has no sentence about the lines: the summary and the song window tell them
        RomajiSummary summary = _output.Result.Romaji;
        Assert.True(summary.IsRequested);
        Assert.Equal(2, summary.LinesAdded);
        Assert.Equal(1, summary.LinesReplaced);
        Assert.Equal(0, summary.LinesMissing);
        Assert.Equal(2, summary.UniqueLines);
        Assert.Equal(Mixed, Read(lrc));
        Assert.True(_data.RomanizeCalls.Count == 0, "a preview never calls Claude");
    }

    [Fact]
    public void LinesClaudeCouldNotDoAreCountedAsMissingAndTheirOldRomajiStays()
    {
        // Claude answers for everything but the line with the Chinese-character romaji
        _data.Claude = lines =>
        {
            List<RomajiLine> answerable = lines.Where(line => !line.Key.StartsWith("가", StringComparison.Ordinal)).ToList();
            return ScriptedData.EverythingWorks(answerable);
        };
        string lrc = LrcOnly("a", Mixed);

        Run(false, true);

        SongLyricsResult result = _output.Only();
        AssertCounts(result, 1, 0, 1, 1);
        Assert.True(result.Detail.Contains("romaji added to 1 line"), result.Detail);
        Assert.True(result.Detail.Contains("romaji missing for 1 of 2 lines"), result.Detail);
        Assert.Equal(1, _output.Result.Romaji.LinesAdded);
        Assert.Equal(0, _output.Result.Romaji.LinesReplaced);
        Assert.Equal(1, _output.Result.Romaji.LinesMissing);
        string text = Read(lrc);
        Assert.True(text.Contains("<00:04.000>家 <00:05.000>那达"), "the old romaji stays where no new one was made");
        Assert.Contains("annyeong", text);
    }

    [Fact]
    public void ASongWhoseOnlyLinesCouldNotBeDoneIsLeftAloneAndCountedAsMissing()
    {
        _data.Claude = lines => new RomajiResult(new Dictionary<string, List<string>>(), 0, 1, "rate limit");
        string lrc = LrcOnly("a", LineSynced);

        Run(false, true);

        Assert.Equal(LyricsAction.KeptLineSynced, _output.Only().Action);
        AssertCounts(_output.Only(), 0, 0, 0, 2);
        Assert.Equal(LineSynced, Read(lrc));
        Assert.Equal(2, _output.Result.Romaji.LinesMissing);
    }

    [Fact]
    public void TheTotalsAddUpOverSeveralSongs()
    {
        LrcOnly("a", Mixed);
        LrcOnly("b", LddcLineLevel);
        LrcOnly("c", StandardWithLatinRomaji);

        Run(false, true);

        RomajiSummary summary = _output.Result.Romaji;
        Assert.Equal(4, summary.LinesAdded);
        Assert.Equal(1, summary.LinesReplaced);
        Assert.Equal(0, summary.LinesMissing);
        Assert.True(_data.RomanizeCalls.Count == 1, "one request for the whole library");
    }

    // ---- e. verification guards every write ----

    [Fact]
    public void AWordTimingThatDoesNotSurviveTheRoundTripFailsTheSongAndChangesNothing()
    {
        string lrc = LrcOnly("a", StandardNoRomaji);
        string ttml = Sidecar("a", ".ttml", Ttml);
        _data.Reread = text => text.Replace("<00:02.000>sesang", "<00:02.500>sesang");

        Run(false, true);

        Assert.Equal(LyricsAction.Failed, _output.Only().Action);
        Assert.True(_output.Only().Detail.StartsWith("Verification failed, nothing was changed", StringComparison.Ordinal),
            _output.Only().Detail);
        AssertCounts(_output.Only(), 0, 0, 0, 0);
        Assert.Equal(StandardNoRomaji, Read(lrc));
        Assert.True(File.Exists(ttml), "the deprecated file stays");
        Assert.Empty(_data.Writes);
        Assert.Equal(0, _output.Result.Romaji.LinesAdded);
    }

    [Fact]
    public void RomajiTextThatDoesNotSurviveTheRoundTripFailsALineSyncedSong()
    {
        string lrc = LrcOnly("a", LineSynced);
        _data.Reread = text => text.Replace("]annyeong sesang", "]annyeon sesang");

        Run(false, true);

        Assert.Equal(LyricsAction.Failed, _output.Only().Action);
        Assert.Equal(LineSynced, Read(lrc));
        Assert.Empty(_data.Writes);
    }

    [Fact]
    public void ARomajiLineThatGoesMissingInTheRoundTripFailsALineLevelSong()
    {
        string lrc = LrcOnly("a", LddcLineLevel);
        _data.Reread = text => text.Replace("[00:01.000]<00:01.000>annyeong sesang<00:03.000>\r\n", "");

        Run(false, true);

        Assert.Equal(LyricsAction.Failed, _output.Only().Action);
        Assert.Equal(LddcLineLevel, Read(lrc));
        Assert.Empty(_data.Writes);
    }

    [Fact]
    public void ASongThatFailsDoesNotStopTheOthers()
    {
        string bad = LrcOnly("a", LineSynced);
        string good = LrcOnly("b", StandardNoRomaji);
        _data.Reread = text => text.Replace("]annyeong sesang", "]annyeon sesang");

        Run(false, true);

        Assert.Equal(LyricsAction.Failed, _output.Result.Results[0].Action);
        Assert.Equal(LyricsAction.RomajiAdded, _output.Result.Results[1].Action);
        Assert.Equal(LineSynced, Read(bad));
        Assert.Contains("annyeong", Read(good));
    }
}
