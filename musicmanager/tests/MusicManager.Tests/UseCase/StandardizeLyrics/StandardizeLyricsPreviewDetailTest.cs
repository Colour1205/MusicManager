using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeLyrics;
using Xunit;
using Kind = MusicManager.UseCase.StandardizeLyrics.LyricLineChange.Kind;

namespace MusicManager.Tests.UseCase.StandardizeLyrics;

/// <summary>
/// The detailed preview made for one song's window: a dry run that changes nothing but lists the lines whose romaji is
/// added or replaced and makes the romaji for them. Real files in a temp folder, the real parsers and formatter; only
/// Claude is scripted (a subclass of the one DataAccess), apart from the one test that lets the real data access serve
/// its saved answers.
/// </summary>
public class StandardizeLyricsPreviewDetailTest : IDisposable
{
    private const string Header = "[ti:t]\r\n[tool:LDDC v0.9.0]\r\n\r\n";

    // a Java text block: line breaks are "\n", whatever the line endings of this source file are
    private static readonly string Ttml = Lf("""
        <?xml version="1.0" encoding="UTF-8"?><tt xmlns="http://www.w3.org/ns/ttml" xmlns:itunes="http://music.apple.com/lyric-ttml-internal" xmlns:ttm="http://www.w3.org/ns/ttml#metadata" itunes:timing="Word"><head><metadata><ttm:agent type="person" xml:id="v1"/></metadata></head><body dur="00:06.000"><div begin="00:00.000" end="00:06.000">
        <p begin="00:01.000" end="00:03.000" ttm:agent="v1" itunes:key="L1"><span begin="00:01.000" end="00:02.000">안녕 </span><span begin="00:02.000" end="00:03.000">세상</span></p>
        </div></body></tt>
        """);

    private const string Hmrc = "[offset:0]\n[1000,2000]<0,1000>안녕 <1000,1000>세상\n";

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

    /// <summary>LDDC's line-level output: one timed "word" per line, with a Chinese translation under the first line</summary>
    private const string LddcLineLevel = Header
        + "[00:01.000]안녕 세상[00:03.000]\r\n"
        + "[00:01.000]你好世界[00:03.000]\r\n"
        + "[00:04.000]Hello world[00:06.000]\r\n"
        + "[00:07.000]こんにちは[00:09.000]\r\n";

    private const string LineSynced = "[offset:700]\n[00:01.00]안녕 세상\n[00:01.00]你好世界\n"
        + "[00:04.00]Hello world\n[00:08.00]こんにちは\n";

    private const string LineSyncedChineseRomaji = "[00:01.00]安宁塞桑\n[00:01.00]안녕 세상\n"
        + "[00:04.00]Hello world\n";

    private const string NoKey = "no Anthropic API key (Process asks Claude for it)";

    private readonly string _library; // @TempDir

    private readonly string _home; // @TempDir: stands in for the user's home when the real data access is used

    private readonly ScriptedData _data;
    private readonly Capture _output;

    // ---- the fakes ----

    /// <summary>the real DataAccess with Claude replaced, and a log of what the use case read, wrote and deleted</summary>
    private class ScriptedData : RealDataAccess
    {
        private static readonly Dictionary<string, string> Known = new Dictionary<string, string>
        {
            ["안녕"] = "annyeong", ["세상"] = "sesang", ["안녕 세상"] = "annyeong sesang", ["こんにちは"] = "konnichiwa",
            ["가"] = "ga", ["나다"] = "nada", ["라마"] = "rama", ["바사"] = "basa"
        };

        public string Key = "sk-ant-test";
        public readonly List<List<RomajiLine>> RomanizeCalls = new List<List<RomajiLine>>();
        public readonly List<string> KeysUsed = new List<string>();
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
            KeysUsed.Add(apiKey);
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

        /// <summary>what the real data access answers with no key: only what is saved, and the reason for the rest</summary>
        public static RomajiResult NothingWithoutAKey(List<RomajiLine> lines)
        {
            return new RomajiResult(new Dictionary<string, List<string>>(), 0, 0, "no Anthropic API key");
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

    public StandardizeLyricsPreviewDetailTest()
    {
        _library = Path.Combine(Path.GetTempPath(), "mm-" + Guid.NewGuid());
        Directory.CreateDirectory(_library);
        _home = Path.Combine(Path.GetTempPath(), "mm-" + Guid.NewGuid());
        Directory.CreateDirectory(_home);
        _data = new ScriptedData();
        _output = new Capture();
    }

    public void Dispose()
    {
        foreach (string folder in new[] { _library, _home })
        {
            try
            {
                Directory.Delete(folder, true);
            }
            catch (IOException)
            {
                // a temp folder that cannot be removed is left to the system
            }
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

    // Files.readString: the bytes as UTF-8 text, a byte order mark is not removed
    private static string Read(string file) => Encoding.UTF8.GetString(File.ReadAllBytes(file));

    private void Preview(bool romanize)
    {
        Preview(_data, romanize, null);
    }

    private void Preview(IStandardizeLyricsDataAccess access, bool romanize, HashSet<string> scope)
    {
        new StandardizeLyricsInteractor(access, _output).Execute(
            new StandardizeLyricsInputData(_library, true, false, romanize, null, "m", scope, true));
    }

    private void PlainDryRun()
    {
        new StandardizeLyricsInteractor(_data, _output)
            .Execute(new StandardizeLyricsInputData(_library, true, false, true, null, "m"));
    }

    private void RealRun()
    {
        new StandardizeLyricsInteractor(_data, _output)
            .Execute(new StandardizeLyricsInputData(_library, false, false, true, null, "m"));
    }

    private static List<int> Counts(SongLyricsResult result)
    {
        return new List<int> { result.RomajiAdded, result.RomajiReplaced, result.RomajiPresent, result.RomajiMissing };
    }

    private static void AssertChange(LyricLineChange change, Kind kind, long timeMs, string original, string oldRomaji,
        string newRomaji)
    {
        Assert.Equal(kind, change.GetKind());
        Assert.Equal(timeMs, change.TimeMs);
        Assert.Equal(original, change.Original);
        Assert.Equal(oldRomaji, change.OldRomaji);
        Assert.Equal(newRomaji, change.NewRomaji);
    }

    // java.nio.file.Path equality: the same file however it is spelled (Windows ignores case)
    private static void AssertSamePath(string expected, string actual)
    {
        Assert.Equal(Path.GetFullPath(expected), Path.GetFullPath(actual), true);
    }

    // ---- nothing is touched ----

    [Fact]
    public void ADetailedPreviewNeverWritesDeletesOrReadsTags()
    {
        string converted = Audio("a");
        Sidecar("a", ".ttml", Ttml);
        Sidecar("a", ".hmrc", Hmrc);
        string replaced = LrcOnly("b", StandardWithChineseRomaji);
        string withDeprecated = LrcOnly("c", StandardWithLatinRomaji);
        Sidecar("c", ".ttml", Ttml);
        Dictionary<string, byte[]> before = new Dictionary<string, byte[]>();
        foreach (string file in Directory.EnumerateFiles(_library, "*", SearchOption.AllDirectories))
        {
            before[file] = File.ReadAllBytes(file);
        }

        Preview(true);

        Assert.Equal(3, _output.Result.Results.Count);
        Assert.True(_output.Result.IsDryRun);
        Assert.True(_data.Writes.Count == 0, "nothing is written");
        Assert.True(_data.Deletes.Count == 0, "nothing is deleted");
        Assert.True(_data.TagReads == 0, "the audio tags are not read");
        Assert.True(_data.RomanizeCalls.Count == 1, "the romaji for all the lines is asked once");
        List<string> after = Directory.EnumerateFiles(_library, "*", SearchOption.AllDirectories).ToList();
        Assert.True(before.Keys.ToHashSet().SetEquals(after), "no file appeared or went");
        foreach (string file in after)
        {
            Assert.True(before[file].SequenceEqual(File.ReadAllBytes(file)), file);
        }
        // what a real run would delete is still listed
        SongLyricsResult conversion = _output.Result.Results[0];
        Assert.Equal(LyricsAction.ConvertedFromTtml, conversion.Action);
        Assert.Equal(2, conversion.DeletedFiles.Count);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(converted), "a.ttml")));
        Assert.True(File.Exists(replaced));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(withDeprecated), "c.ttml")));
    }

    // ---- the lines ----

    [Fact]
    public void ALineWithOldChineseCharacterRomajiIsReplacedAndAPlainLineIsAdded()
    {
        LrcOnly("a", Mixed);

        Preview(true);

        SongLyricsResult song = _output.Only();
        Assert.Equal(LyricsAction.RomajiAdded, song.Action);
        Assert.Equal(2, song.LineChanges.Count);
        AssertChange(song.LineChanges[0], Kind.RomajiAdded, 1000, "안녕 세상", null, "annyeong sesang");
        AssertChange(song.LineChanges[1], Kind.RomajiReplaced, 4000, "가 나다", "家 那达", "ga nada");
        Assert.Equal(new List<int> { 2, 1, 1, 0 }, Counts(song)); // added counts the replaced line too
    }

    [Fact]
    public void AReplacedLineShowsTheOldChineseCharactersAboveTheKoreanLine()
    {
        LrcOnly("a", StandardWithChineseRomaji);

        Preview(true);

        Assert.Equal(1, _output.Only().LineChanges.Count);
        AssertChange(_output.Only().LineChanges[0], Kind.RomajiReplaced, 1000, "안녕 세상", "安宁 塞桑",
            "annyeong sesang");
        Assert.Equal(new List<int> { 1, 1, 0, 0 }, Counts(_output.Only()));
    }

    [Fact]
    public void TheLinesOfATtmlConversionAreListedAndItsFileIsTheOneThatWouldBeWritten()
    {
        Audio("a");
        Sidecar("a", ".ttml", Ttml);

        Preview(true);

        Assert.Equal(LyricsAction.ConvertedFromTtml, _output.Only().Action);
        Assert.Equal(1, _output.Only().LineChanges.Count);
        AssertChange(_output.Only().LineChanges[0], Kind.RomajiAdded, 1000, "안녕 세상", null,
            "annyeong sesang");
        AssertSamePath(Path.Combine(_library, "Album", "a.lrc"), _output.Only().WrittenLrcPath);
        Assert.False(File.Exists(Path.Combine(_library, "Album", "a.lrc")), "it is only what would be written");
    }

    [Fact]
    public void ALineSyncedLrcGetsItsLineChangesToo()
    {
        LrcOnly("added", LineSynced);
        LrcOnly("replaced", LineSyncedChineseRomaji);

        Preview(true);

        SongLyricsResult added = _output.Result.Results[0];
        Assert.Equal(LyricsAction.RomajiAdded, added.Action);
        Assert.Equal(2, added.LineChanges.Count);
        AssertChange(added.LineChanges[0], Kind.RomajiAdded, 1000, "안녕 세상", null, "annyeong sesang");
        AssertChange(added.LineChanges[1], Kind.RomajiAdded, 8000, "こんにちは", null, "konnichiwa");
        SongLyricsResult replaced = _output.Result.Results[1];
        Assert.Equal(1, replaced.LineChanges.Count);
        AssertChange(replaced.LineChanges[0], Kind.RomajiReplaced, 1000, "안녕 세상", "安宁塞桑",
            "annyeong sesang");
    }

    [Fact]
    public void ALineLevelFileGetsItsLineChangesToo()
    {
        LrcOnly("a", LddcLineLevel);

        Preview(true);

        Assert.Equal(2, _output.Only().LineChanges.Count);
        AssertChange(_output.Only().LineChanges[0], Kind.RomajiAdded, 1000, "안녕 세상", null, "annyeong sesang");
        AssertChange(_output.Only().LineChanges[1], Kind.RomajiAdded, 7000, "こんにちは", null, "konnichiwa");
    }

    [Fact]
    public void TheNewRomajiOfACompletePreviewComesWithoutAnyNote()
    {
        LrcOnly("a", Mixed);

        Preview(true);

        Assert.True("" == _output.Only().Detail, "the lines are listed instead of described");
    }

    [Fact]
    public void ASongWithNothingToDoHasNoLineChangesAndClaudeIsNotAsked()
    {
        LrcOnly("a", StandardWithLatinRomaji);

        Preview(true);

        Assert.Equal(LyricsAction.AlreadyStandard, _output.Only().Action);
        Assert.Empty(_output.Only().LineChanges);
        Assert.Empty(_data.RomanizeCalls);
    }

    [Fact]
    public void WithoutTheRomajiOptionThereAreNoLineChangesAndNoKeyOrClaudeIsNeeded()
    {
        _data.Key = null;
        Audio("a");
        Sidecar("a", ".ttml", Ttml);

        Preview(false);

        Assert.Null(_output.Error);
        Assert.Equal(LyricsAction.ConvertedFromTtml, _output.Only().Action);
        Assert.Empty(_output.Only().LineChanges);
        Assert.Equal(new List<int> { 0, 0, 0, 0 }, Counts(_output.Only()));
        Assert.Empty(_data.RomanizeCalls);
        Assert.Equal(0, _data.EstimateCalls);
        Assert.Equal("", _output.Only().Detail);
        AssertSamePath(Path.Combine(_library, "Album", "a.lrc"), _output.Only().WrittenLrcPath);
    }

    // ---- the romaji that could not be made ----

    [Fact]
    public void WhenClaudeAnswersNothingTheLinesComeWithoutRomajiTextAndANoteSaysWhy()
    {
        _data.Claude = lines => new RomajiResult(new Dictionary<string, List<string>>(), 0, 1, "rate limit - run it again.");
        string lrc = LrcOnly("a", Mixed);

        Preview(true);

        Assert.Null(_output.Error);
        SongLyricsResult song = _output.Only();
        Assert.Equal(LyricsAction.RomajiAdded, song.Action);
        AssertChange(song.LineChanges[0], Kind.RomajiAdded, 1000, "안녕 세상", null, null);
        AssertChange(song.LineChanges[1], Kind.RomajiReplaced, 4000, "가 나다", "家 那达", null);
        Assert.Equal("romaji text is not available for 2 lines yet: rate limit - run it again", song.Detail);
        Assert.Equal(new List<int> { 2, 1, 1, 0 }, Counts(song)); // the counts stay the planned ones
        AssertSamePath(lrc, song.WrittenLrcPath);
    }

    [Fact]
    public void ARejectedKeyDoesNotStopAPreviewEither()
    {
        _data.Claude = lines => new RomajiResult(new Dictionary<string, List<string>>(), 0, 0, "The API key was rejected (check ANTHROPIC_API_KEY)");
        LrcOnly("a", StandardWithChineseRomaji);

        Preview(true);

        Assert.Null(_output.Error);
        Assert.Null(_output.Only().LineChanges[0].NewRomaji);
        Assert.Equal("romaji text is not available for 1 line yet: The API key was rejected (check ANTHROPIC_API_KEY)",
            _output.Only().Detail);
    }

    [Fact]
    public void WithoutAnApiKeyThePreviewIsNoErrorAndTheNoteSaysClaudeIsAskedByProcess()
    {
        _data.Key = null;
        _data.Claude = ScriptedData.NothingWithoutAKey;
        LrcOnly("a", Mixed);

        Preview(true);

        Assert.Null(_output.Error);
        Assert.True(_data.RomanizeCalls.Count == 1, "the data access is asked, and serves what is saved");
        Assert.Null(_data.KeysUsed[0]); // with no key
        SongLyricsResult song = _output.Only();
        Assert.Equal(2, song.LineChanges.Count);
        Assert.Null(song.LineChanges[0].NewRomaji);
        Assert.Equal("romaji text is not available for 2 lines yet: " + NoKey, song.Detail);
        Assert.Equal(new List<int> { 2, 1, 1, 0 }, Counts(song));
    }

    [Fact]
    public void ATypedKeyIsUsedForThePreview()
    {
        _data.Key = null;
        LrcOnly("a", Mixed);

        new StandardizeLyricsInteractor(_data, _output).Execute(
            new StandardizeLyricsInputData(_library, true, false, true, " sk-typed ", "m", null, true));

        Assert.Equal(new List<string> { "sk-typed" }, _data.KeysUsed);
        Assert.Equal("annyeong sesang", _output.Only().LineChanges[0].NewRomaji);
    }

    [Fact]
    public void TheEnvironmentKeyIsUsedForThePreviewWhenNoneIsTyped()
    {
        _data.Key = "sk-from-environment";
        LrcOnly("a", Mixed);

        Preview(true);

        Assert.Equal(new List<string> { "sk-from-environment" }, _data.KeysUsed);
    }

    // the real data access that serves the saved answers; its api key and its lyric writing are replaced
    private sealed class KeylessData : RealDataAccess
    {
        private readonly List<string> _writes;

        public KeylessData(List<string> writes)
        {
            _writes = writes;
        }

        public override string ReadApiKeyFromEnvironment()
        {
            return null;
        }

        public override void WriteLyricFile(string path, string content)
        {
            _writes.Add(path);
        }
    }

    [Fact]
    public void SavedAnswersAreServedWithoutAKeyAndOnlyTheRestComeWithoutText()
    {
        // the real data access with a saved answer for the first line only and no key: nothing is asked of Claude
        string key = RomajiLine.Of(new[] { new WordTimeStamp("안녕 ", 0, 0), new WordTimeStamp("세상", 0, 0) }).Key;
        Directory.CreateDirectory(Path.Combine(_home, ".musicmanager"));
        File.WriteAllBytes(Path.Combine(_home, ".musicmanager", "romaji-cache.json"), Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new Dictionary<string, List<string>> { [key] = new List<string> { "annyeong", "sesang" } },
                new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })));
        List<string> writes = new List<string>();
        string lrc = LrcOnly("a", Mixed);
        string before = Read(lrc);

        UserHome.Path = _home;
        try
        {
            IStandardizeLyricsDataAccess keyless = new KeylessData(writes);
            Preview(keyless, true, null);
        }
        finally
        {
            UserHome.Path = null;
        }

        Assert.Null(_output.Error);
        SongLyricsResult song = _output.Only();
        Assert.Equal(2, song.LineChanges.Count);
        AssertChange(song.LineChanges[0], Kind.RomajiAdded, 1000, "안녕 세상", null, "annyeong sesang");
        AssertChange(song.LineChanges[1], Kind.RomajiReplaced, 4000, "가 나다", "家 那达", null);
        Assert.Equal("romaji text is not available for 1 line yet: " + NoKey, song.Detail);
        Assert.Empty(writes);
        Assert.Equal(before, Read(lrc));
    }

    // ---- the counts ----

    [Fact]
    public void TheCountsAreThePlannedCountsOfAPlainDryRunWhetherOrNotTheTextCouldBeMade()
    {
        LrcOnly("a", Mixed);
        Audio("b");
        Sidecar("b", ".ttml", Ttml);
        LrcOnly("c", LddcLineLevel);
        LrcOnly("d", LineSyncedChineseRomaji);
        LrcOnly("e", StandardWithLatinRomaji);
        Audio("f");

        PlainDryRun();
        List<List<int>> plannedCounts = new List<List<int>>();
        List<LyricsAction> plannedActions = new List<LyricsAction>();
        foreach (SongLyricsResult result in _output.Result.Results)
        {
            plannedCounts.Add(Counts(result));
            plannedActions.Add(result.Action);
            Assert.True(result.LineChanges.Count == 0, "a plain dry run lists no lines");
        }

        Preview(true);
        Assert.Equal(plannedActions, _output.Result.Results.Select(r => r.Action).ToList());
        Assert.Equal(plannedCounts, _output.Result.Results.Select(Counts).ToList());

        _data.Claude = ScriptedData.NothingWithoutAKey;
        _data.Key = null;
        Preview(true);
        Assert.Equal(plannedActions, _output.Result.Results.Select(r => r.Action).ToList());
        Assert.Equal(plannedCounts, _output.Result.Results.Select(Counts).ToList());
        Assert.Equal(new List<int> { 2, 1, 1, 0 }, plannedCounts[0]);
    }

    [Fact]
    public void TheSummaryOfAPreviewStillCountsTheEstimateAndNotWhatWasMade()
    {
        LrcOnly("a", Mixed);

        Preview(true);

        Assert.Equal(1, _data.EstimateCalls);
        Assert.True(_output.Result.Romaji.IsRequested);
        Assert.True(_output.Result.Romaji.UniqueLines == 2, "what the estimate says, not what was made");
        Assert.Equal(2, _output.Result.Romaji.LinesAdded);
        Assert.Equal(1, _output.Result.Romaji.LinesReplaced);
        Assert.Equal(0, _output.Result.Romaji.LinesMissing);
        Assert.Null(_output.Result.Romaji.StoppedBecause);
    }

    // ---- the file that would be written ----

    [Fact]
    public void TheWrittenFileIsSetWhereAnLrcIsWrittenAndNullWhereNoneIs()
    {
        Audio("converted");
        Sidecar("converted", ".ttml", Ttml);
        string standard = LrcOnly("standard", StandardWithLatinRomaji);
        string kept = LrcOnly("kept", "[00:01.00]안녕 세상\n[00:04.00]Hello world\n");
        Audio("nolyrics");
        string normalized = LrcOnly("normalized", "[00:01.000]안녕 [00:02.000]세상[00:03.000]\r\n");

        Preview(false);

        Dictionary<string, SongLyricsResult> byName = new Dictionary<string, SongLyricsResult>();
        foreach (SongLyricsResult result in _output.Result.Results)
        {
            byName[Path.GetFileName(result.AudioPath)] = result;
        }
        Assert.Equal(LyricsAction.ConvertedFromTtml, byName["converted.flac"].Action);
        AssertSamePath(Path.Combine(_library, "Album", "converted.lrc"), byName["converted.flac"].WrittenLrcPath);
        Assert.Equal(LyricsAction.NormalizedLrc, byName["normalized.flac"].Action);
        AssertSamePath(normalized, byName["normalized.flac"].WrittenLrcPath);
        Assert.Equal(LyricsAction.AlreadyStandard, byName["standard.flac"].Action);
        Assert.Null(byName["standard.flac"].WrittenLrcPath);
        Assert.Equal(LyricsAction.KeptLineSynced, byName["kept.flac"].Action);
        Assert.Null(byName["kept.flac"].WrittenLrcPath);
        Assert.Equal(LyricsAction.NoLyrics, byName["nolyrics.flac"].Action);
        Assert.Null(byName["nolyrics.flac"].WrittenLrcPath);
        Assert.True(File.Exists(standard) && File.Exists(kept));
    }

    [Fact]
    public void ARealRunAndAPlainDryRunAlsoSayWhichFileTheyWriteAndListNoLines()
    {
        Audio("a");
        Sidecar("a", ".ttml", Ttml);
        string expected = Path.Combine(_library, "Album", "a.lrc");

        PlainDryRun();
        AssertSamePath(expected, _output.Only().WrittenLrcPath);
        Assert.Empty(_output.Only().LineChanges);
        Assert.False(File.Exists(expected));

        RealRun();
        AssertSamePath(expected, _output.Only().WrittenLrcPath);
        Assert.Empty(_output.Only().LineChanges);
        Assert.Equal(new List<string> { Path.GetFullPath(expected) }, _data.Writes.Select(p => Path.GetFullPath(p)).ToList());
        Assert.True(File.Exists(expected));
    }

    [Fact]
    public void ARunThatCouldNotMakeAnyRomajiForAnExistingFileWritesNothingAndSaysNoFile()
    {
        _data.Claude = lines => new RomajiResult(new Dictionary<string, List<string>>(), 0, 1, "rate limit");
        LrcOnly("a", StandardWithChineseRomaji);

        RealRun();

        Assert.Equal(LyricsAction.AlreadyStandard, _output.Only().Action);
        Assert.Null(_output.Only().WrittenLrcPath);
        Assert.Empty(_data.Writes);
    }

    // ---- verification still guards the preview ----

    [Fact]
    public void ASongThatWouldFailVerificationShowsFailedInThePreviewWithoutLinesOrFile()
    {
        string lrc = LrcOnly("a", LineSynced);
        _data.Reread = text => text.Replace("]annyeong sesang", "]annyeon sesang");

        Preview(true);

        Assert.Equal(LyricsAction.Failed, _output.Only().Action);
        Assert.True(_output.Only().Detail.StartsWith("Verification failed, nothing was changed", StringComparison.Ordinal),
            _output.Only().Detail);
        Assert.Empty(_output.Only().LineChanges);
        Assert.Null(_output.Only().WrittenLrcPath);
        Assert.Equal(LineSynced, Read(lrc));
    }

    [Fact]
    public void ASongWhoseRomajiIsMissingStillGetsVerifiedWithoutIt()
    {
        _data.Claude = ScriptedData.NothingWithoutAKey;
        LrcOnly("a", StandardWithChineseRomaji);
        _data.Reread = text => text.Replace("<00:02.000>세상", "<00:02.500>세상");

        Preview(true);

        Assert.Equal(LyricsAction.Failed, _output.Only().Action);
    }

    // ---- the scope ----

    [Fact]
    public void TheScopeStillLimitsTheSongsOfADetailedPreview()
    {
        string inScope = LrcOnly("a", Mixed);
        string other = LrcOnly("b", StandardWithChineseRomaji);
        Audio("c");
        Sidecar("c", ".ttml", Ttml);

        Preview(_data, true, new HashSet<string> { Path.Combine(_library, "Album", "a.flac") });

        Assert.Equal(1, _output.Result.Results.Count);
        Assert.Equal(2, _output.Only().LineChanges.Count);
        Assert.True(new List<string> { Path.GetFullPath(inScope) }.SequenceEqual(_data.Reads.Select(p => Path.GetFullPath(p))),
            "the other songs are not even read");
        Assert.Equal(1, _data.RomanizeCalls.Count);
        Assert.True(_data.RomanizeCalls[0].Count == 2, "only the lines of the song in scope are asked for");
        Assert.True(File.Exists(other));
    }

    // ---- a real run and a plain dry run are unchanged ----

    [Fact]
    public void APlainDryRunHasNoSentenceAboutTheRomajiLinesAndAsksClaudeNothing()
    {
        LrcOnly("a", Mixed);

        PlainDryRun();

        Assert.Equal("", _output.Only().Detail);
        Assert.Empty(_data.RomanizeCalls);
        Assert.Equal(1, _data.EstimateCalls);
    }

    [Fact]
    public void APlainDryRunNeedsNoKeyAndStillWorksWithoutOne()
    {
        _data.Key = null;
        LrcOnly("a", Mixed);

        PlainDryRun();

        Assert.Null(_output.Error);
        Assert.Equal(LyricsAction.RomajiAdded, _output.Only().Action);
    }

    [Fact]
    public void ARealRunWithoutAKeyStillStopsBeforeChangingAnything()
    {
        _data.Key = null;
        string lrc = LrcOnly("a", Mixed);

        RealRun();

        Assert.True(_output.Error.Contains("API key"), _output.Error);
        Assert.Null(_output.Result);
        Assert.Equal(Mixed, Read(lrc));
    }

    [Fact]
    public void ARealRunWhereClaudeCannotBeUsedAtAllStillStopsBeforeChangingAnything()
    {
        _data.Claude = lines => new RomajiResult(new Dictionary<string, List<string>>(), 0, 0, "The API key was rejected (check ANTHROPIC_API_KEY)");
        string lrc = LrcOnly("a", Mixed);

        RealRun();

        Assert.True(_output.Error.Contains("rejected") && _output.Error.Contains("Nothing was changed"), _output.Error);
        Assert.Equal(Mixed, Read(lrc));
    }

    [Fact]
    public void ARealRunStillWritesTheRomajiAndCountsWhatItDid()
    {
        string lrc = LrcOnly("a", Mixed);

        RealRun();

        Assert.Equal(new List<int> { 2, 1, 1, 0 }, Counts(_output.Only()));
        Assert.Equal("romaji added to 1 line, 1 old Chinese-character line replaced",
            _output.Only().Detail);
        Assert.Empty(_output.Only().LineChanges);
        Assert.True(Read(lrc).Contains("<00:04.000>ga <00:05.000>nada"), Read(lrc));
    }

    [Fact]
    public void ThePreviewFlagMeansNothingWithoutADryRun()
    {
        string lrc = LrcOnly("a", Mixed);

        new StandardizeLyricsInteractor(_data, _output).Execute(
            new StandardizeLyricsInputData(_library, false, false, true, null, "m", null, true));

        Assert.True(_output.Only().LineChanges.Count == 0, "a real run lists no lines");
        Assert.Equal(1, _data.Writes.Count);
        Assert.True(Read(lrc).Contains("annyeong"), Read(lrc));
    }
}
