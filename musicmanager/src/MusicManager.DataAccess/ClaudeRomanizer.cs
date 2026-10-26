using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeArtists;
using MusicManager.UseCase.StandardizeLyrics;

namespace MusicManager.DataAccess;

/// <summary>
/// asks Claude to romanize Korean and Japanese lyric lines into Latin letters, word by word. Built for a whole library:
/// lines are deduplicated (a chorus is asked once), looked up in the on-disk cache first, packed many to a request,
/// several requests are in flight at once, and every answer is cached as soon as it arrives. A failure ends the run early
/// without losing what was done. An answer is never trusted blindly: a line only counts when it has exactly one
/// piece per input word (empty for a later character of a Japanese word whose reading is written whole before it), in
/// Latin letters, with the Chinese characters of a Korean line kept as they were (only
/// Korean and Japanese are romanized) and none left in a Japanese line (see
/// <see cref="RomajiScripts.IsLatinRomanizationOf"/>).
///
/// What Claude is sent and writes back is kept as short as it can be, because the time of a request is the time it takes
/// to write the answer: only the words that need romanizing are listed (English words, numbers, punctuation and Chinese
/// characters of a Korean line are copied by this class), and the answer is one plain text, a line "n: piece|piece" for
/// each numbered line, not an object per line.
/// </summary>
public static class ClaudeRomanizer
{
    /// <summary>waits between two attempts at the same lines (Java's Thread.sleep); throws a ThreadInterruptedException when interrupted</summary>
    public delegate void Pause(long millis);

    public const string ToolName = "record_romaji";
    public const int MaxLinesPerRequest = 200;
    public const int MaxCharsPerRequest = 12_000;
    public const int MaxTokens = 16000;
    public const int MaxConsecutiveFailures = 3;
    public const long RetryPauseMillis = 20_000;

    /// <summary>the pause before a retry is made in slices of this length, so that a run that is over does not wait for it</summary>
    public const long PauseSliceMillis = 1_000;

    /// <summary>how many requests are in flight at once in a normal run (the account allows far more; see <see cref="MaxWorkers"/>)</summary>
    public const int DefaultWorkers = 12;

    /// <summary>never more at once than this, whatever is asked for</summary>
    public const int MaxWorkers = 24;

    /// <summary>why the lines that are not saved yet come back without romaji when there is no key to ask with</summary>
    public const string NoApiKey = "no Anthropic API key";

    private const string ToolDescription =
            "Record the Latin-letter romanization of each lyric line (the Chinese characters of Korean lines are kept).";

    private const string Schema =
            "{\"type\":\"object\",\"properties\":{\"romaji\":{\"type\":\"string\"}},\"required\":[\"romaji\"]}";

    public const string SystemPrompt =
            "You romanize song lyrics for a lyric display that shows the romanization above each original line. Each " +
            "request lists numbered lyric lines, one JSON object per line: {\"n\": the number, \"lang\": \"ko\" or " +
            "\"ja\", \"count\": how many words, \"words\": [...]}. The \"words\" are the pieces of the line that need " +
            "romanizing, as they are timed to the music, in order. In a Korean line the words without Hangul (English " +
            "words, numbers, punctuation, Chinese characters) are left out of the list: the program copies them. In a " +
            "Japanese line the words with kanji are in the list too. A | inside a word is shown to you as /. A line " +
            "that was answered wrongly before also has \"previous\" (the answer that was refused) and \"problem\" (why), " +
            "and its words are numbered (\"1=甘\", \"2=酸\", ...): write it again and correct that problem, giving every " +
            "piece with the number of its word, as described below. Report by calling the record_romaji tool exactly " +
            "once.\n" +
            "\n" +
            "FORMAT OF THE ANSWER\n" +
            "The tool takes one text, \"romaji\", with one text line for each numbered line: the number, a colon and a " +
            "space, then the romanization of each word joined with | (a bar, with no spaces around it), for example:\n" +
            "1: mundeuk|dari|tteooreul\n" +
            "2: ma|ji|me|so|u\n" +
            "The romanization of line n must have EXACTLY \"count\" pieces (count - 1 bars), piece i belonging to word " +
            "i. Never merge two words into one piece, never split one word into two, never skip a word. Count the " +
            "words of every line before you write it, and check the number of pieces after. Write EVERY numbered line, " +
            "in order, with its number copied from the request, and nothing else: no commentary and no blank lines.\n" +
            "When the words of a line are numbered, write each piece as number=piece with the number of its word, for " +
            "example (for the ten words 甘, 酸, っ, ぱ, い, キ, ャ, ン, デ, ィ, and for the three words 今, 日, も):\n" +
            "3: 1=ama|2=zu|3=p|4=pa|5=i|6=ki|7=ya|8=n|9=de|10=i\n" +
            "4: 1=kyou|2=|3=mo\n" +
            "Write every number from 1 to \"count\" exactly once, in order; the piece of a number is what that word is " +
            "read as (an empty piece is the number and = with nothing after it, as 2= above), so a reading " +
            "never slides onto the word next to it. Write only the reading after the =, never a number.\n" +
            "\n" +
            "The \"lang\" field is \"ko\" for a Korean line (it has Hangul and no hiragana or katakana) and \"ja\" for " +
            "a Japanese line (it has some hiragana or katakana). Trust that field; never guess a line's language from " +
            "its Chinese characters.\n" +
            "\n" +
            "KOREAN (Hangul): Revised Romanization of Korean as the words are sung, with the sound changes applied " +
            "(달이 => dari, 같이 => gachi, 좋아 => joa, 있어 => isseo, 합니다 => hamnida, 떠오를 => tteooreul). No " +
            "hyphens inside a word. Write it the way a fan would read it aloud, in lowercase, capitalizing only " +
            "proper names.\n" +
            "JAPANESE (kana and kanji): Hepburn romanization, lowercase, capitalizing only proper names. Particles は " +
            "=> wa, を => o, へ => e. Write long vowels with no macrons: おう/おお => ou/oo, ー repeats the vowel (a word " +
            "that is only ー is the vowel before it). っ doubles the next consonant, and a っ with no sound after it (at " +
            "the end of a line) is written as ', ん => n.\n" +
            "Japanese words are often timed one character at a time (真, 面, 目, そ, う, で, も). So first work out " +
            "the correct reading of the WHOLE line, the way it is sung, then split that reading across the words so " +
            "that each piece is the reading of its own word, and the pieces joined together spell the full reading: " +
            "真|面|目|そ|う => ma|ji|me|so|u, 隠|さ|れ|た => kaku|sa|re|ta, 私|に => watashi|ni, 周|り|を => mawa|ri|o. " +
            "A character that carries several syllables gets all of them in its piece; never drop a syllable. A " +
            "small kana (ゃ ゅ ょ) that is timed as a word of its own gets its own piece: じ|ゃ => ji|ya. A small っ " +
            "timed as a word of its own doubles the next consonant and takes that consonant as its piece: " +
            "わ|か|っ|て|る => wa|ka|t|te|ru.\n" +
            "Several characters often make up ONE word whose reading does not divide between them. Divide it in the " +
            "most natural way when there is one (勝|手 => ka|tte, 全|て => sube|te, 到|底 => tou|tei), and when there " +
            "is none, write the whole reading in the piece of the FIRST of those characters and leave the pieces of " +
            "the others EMPTY (nothing between the bars, or nothing after the last bar): 今|日|も => kyou||mo, " +
            "大|人|に => otona||ni, 明|日|は => ashita||wa. An empty piece is only for that: it must come after a " +
            "piece that holds the reading, any kana (also a small one) and every Korean syllable always have a piece of " +
            "their own, and the number of pieces never changes. Never leave out a piece instead.\n" +
            "Korean words timed one syllable at a time keep one piece for each syllable (왠|지 => waen|ji, " +
            "하|루|가 => ha|ru|ga); do not join them into one word.\n" +
            "\n" +
            "OTHER CHARACTERS\n" +
            "- A word that mixes scripts keeps its Latin letters, digits and punctuation and romanizes only its Hangul or " +
            "  kana (\"by：소연\" => \"by：soyeon\", \"@소연이네\" => \"@soyeonine\", \"(Yeah)어\" => \"(Yeah)eo\").\n" +
            "- CHINESE CHARACTERS IN A KOREAN LINE (\"lang\":\"ko\") STAY CHINESE CHARACTERS: only Korean and Japanese are " +
            "  romanized, Chinese is not. Copy every Chinese character exactly as it is given: the same characters, in " +
            "  the same order, in the same word, in the same form (never convert simplified to traditional or the " +
            "  reverse, never write them as Hanyu Pinyin or as a Korean reading, never translate, drop or move them " +
            "  into another word). This covers titles, credit labels, names and numerals: 以我之名, 田小娟, 词, 曲, " +
            "  作曲. Only the Hangul is romanized. Example: the words [\"作词：소연\", \"(아이들)\"] => " +
            "  \"作词：Soyeon|(aideul)\".\n" +
            "- CHINESE CHARACTERS IN A JAPANESE LINE (\"lang\":\"ja\") ARE JAPANESE KANJI: romanize every one of them " +
            "  with Hepburn as described above, so that none is left, and never use Hanyu Pinyin there, not even for a " +
            "  credit label that looks Chinese (作曲 => sakkyoku, 編曲 => henkyoku).\n" +
            "- Credit names written in Hangul or kana are romanized the usual way they are written in English " +
            "  (소연 => Soyeon).\n" +
            "\n" +
            "OUTPUT RULES\n" +
            "Each piece uses only the letters a-z and A-Z, digits and ordinary punctuation (never a | and never a line " +
            "break), plus, in a Korean line only, the Chinese characters copied from that same word. NEVER Hangul or " +
            "kana. NEVER Chinese characters in a Japanese line. NEVER Hanyu Pinyin or any other romanization of Chinese " +
            "characters. No accents or macrons.";

    // Java's \s is ASCII only
    private static readonly Regex PieceSeparator = new Regex("[ \\t\\n\\x0B\\f\\r]*\\|[ \\t\\n\\x0B\\f\\r]*", RegexOptions.CultureInvariant);
    private static readonly Regex CombiningMarks = new Regex("\\p{M}+", RegexOptions.CultureInvariant);

    // "12: ..." (the colon may be full width, "12. ..." and "12) ..." are read too; a zero-width space or byte order mark may come first)
    private static readonly Regex AnswerLine = new Regex("^[\\s\\u200B\\uFEFF]*([0-9]+)\\s*[:：.)）]\\s?(.*)$", RegexOptions.CultureInvariant);

    /// <summary>the cost of romanizing these lines, from the cache alone</summary>
    public static RomajiEstimate Estimate(RomajiCache cache, IEnumerable<RomajiLine> lines)
    {
        OrderedDictionary<string, RomajiLine> unique = Unique(lines);
        int cached = 0;
        List<RomajiLine> toAsk = new List<RomajiLine>();
        foreach (RomajiLine line in unique.Values)
        {
            if (Usable(line, cache.Get(line.Key)) != null)
            {
                cached++;
            }
            else
            {
                toAsk.Add(line);
            }
        }
        return new RomajiEstimate(unique.Count, cached, Chunk(toAsk).Count);
    }

    /// <summary>
    /// Romanizes the lines, one request at a time (a run with a single worker: the order of the requests is the order of
    /// the lines). See <see cref="Run(AnthropicClient, RomajiCache, IEnumerable{RomajiLine}, string, string, RomajiProgressListener, Pause, int)"/>.
    /// </summary>
    public static RomajiResult Run(AnthropicClient client, RomajiCache cache, IEnumerable<RomajiLine> lines, string apiKey,
            string model, RomajiProgressListener progress, Pause pause)
    {
        return Run(client, cache, lines, apiKey, model, progress, pause, 1);
    }

    // ------------------------------------------------------------------ a run: requests in flight at once

    /// <summary>
    /// the later asks of lines an answer got wrong: ask again with what was wrong (stage 1, many lines to a request), then
    /// once more in small requests with more thought (stage 2, which also takes the answer it gets as it is when it is only
    /// the pieces that do not match the words). Stage 0 is the first ask.
    /// </summary>
    public const int RepairStages = 2;

    /// <summary>a stage that got none of at least this many lines right is the last: the next one would not do better</summary>
    private const int GiveUpLines = 20;

    /// <summary>how many lines go in one request of each stage after the first (see <see cref="RepairStages"/>)</summary>
    private static readonly int[] RepairChunkLines = { MaxLinesPerRequest, 40, 8 };

    /// <summary>an answer for a line that was not accepted, with the reason, to tell Claude when the line is asked again</summary>
    private sealed record Feedback(RomajiLine Line, string Previous, string Problem);

    /// <summary>one request still to make: the lines it asks about, the stage it is in, and what was wrong with the last answers</summary>
    private sealed class Job
    {
        public List<RomajiLine> Lines;
        public int Stage;
        public Dictionary<string, Feedback> Hints; // by line key; null in the first ask
        public int Generation; // the slowdown the request was started in (see Failed)
        public int Attempts; // how often it failed for a reason that will not change (see Failed)

        public bool Reask => Stage > 0;
    }

    /// <summary>everything the workers of one run share; every field is touched only while holding <see cref="Gate"/></summary>
    private sealed class RunState
    {
        public readonly object Gate = new object();
        public readonly LinkedList<Job> Queue = new LinkedList<Job>();
        public readonly Dictionary<string, List<string>> Done;
        public List<Feedback> Rejected = new List<Feedback>(); // the lines the asks of the current stage did not get right
        public int Stage; // the stage the queue is in (see RepairStages)
        public int Answered; // lines the answers had a row for, accepted or not (an answer that could be read)
        public int StageLines; // the lines the current repair stage was started with
        public int StageAccepted; // of them, the lines it got right
        public int Unanswered; // lines that no stage got right
        public int LinesTotal;
        public int MaxLimit;
        public int Limit; // how many requests may be in flight right now (lowered after a failure, raised again after successes)
        public int Running; // jobs a worker holds: being asked, or waiting out the pause before their retry
        public int Generation;
        public int GoodRun;
        public int Streak; // failures in a row (at one request at a time)
        public int Requests; // every call made, including a re-ask of lines the first answer got wrong
        public int ChunksDone; // what progress counts: the batches, not the re-asks
        public int RequestsTotal;
        public int LinesHandled;
        public int Accepted;
        public int Unreadable; // requests of many lines of which not one line was accepted
        public int DroppedStreak; // requests given up on in a row (nothing succeeded in between)
        public int DroppedLines;
        public string DroppedReason;
        public long LastFlush;
        public bool Stop;
        public string Stopped;
        public Exception Fault;

        public RunState(Dictionary<string, List<string>> done)
        {
            Done = done;
        }
    }

    /// <summary>what becomes of a job whose request failed</summary>
    private enum Next
    {
        /// <summary>nothing more is done with it (it is given up on, or the run is over)</summary>
        Finished,

        /// <summary>it is back in the queue</summary>
        Requeued,

        /// <summary>the worker waits, holding the job, and then puts it back at the front</summary>
        PauseThenRetry
    }

    /// <summary>
    /// Romanizes the lines. The saved answers are used first; the rest is asked of Claude, up to <paramref name="workers"/>
    /// requests at a time. Without a key (null or blank) only the saved answers are served: nothing is asked, and the
    /// result says why the other lines have no romaji.
    ///
    /// The lines an answer got wrong are not asked again at once: they are collected and asked together, once, when every
    /// first ask is done. After a failure that more waiting may cure (a rate limit, an overloaded service, no network) the
    /// number of requests in flight is halved (a burst of requests is answered with a burst of failures, and one slower
    /// request at a time is what gets through), the failed request waits before it is tried again, and the number grows
    /// back after a run of successes; three failures in a row at one request at a time end the run. A request that fails
    /// for a reason that does not change (Claude declines to answer, or answers in no readable way) goes to the back of
    /// the queue and is given up on after three tries, without slowing the others down.
    /// </summary>
    public static RomajiResult Run(AnthropicClient client, RomajiCache cache, IEnumerable<RomajiLine> lines, string apiKey,
            string model, RomajiProgressListener progress, Pause pause, int workers)
    {
        OrderedDictionary<string, RomajiLine> unique = Unique(lines);
        Dictionary<string, List<string>> done = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        List<RomajiLine> toAsk = new List<RomajiLine>();
        foreach (RomajiLine line in unique.Values)
        {
            List<string> cached = Usable(line, cache.Get(line.Key));
            if (cached != null)
            {
                done[line.Key] = cached;
            }
            else
            {
                toAsk.Add(line);
            }
        }
        int cachedLines = done.Count;

        bool noKey = apiKey == null || JavaString.IsBlank(apiKey);
        RunState state = new RunState(done);
        state.LinesTotal = toAsk.Count;
        state.MaxLimit = Math.Max(1, Math.Min(workers, MaxWorkers));
        state.Limit = state.MaxLimit;
        state.Stopped = noKey && toAsk.Count > 0 ? NoApiKey : null;
        if (!noKey)
        {
            foreach (List<RomajiLine> chunk in Chunk(toAsk))
            {
                state.Queue.AddLast(new Job { Lines = chunk });
            }
        }
        state.RequestsTotal = state.Queue.Count;

        // the calling thread works too, so a single worker is the plain, one-after-the-other run
        // (as many as may run at once, also when there is a single request to begin with: the repair of its lines runs several
        // requests at once too)
        int threads = state.Queue.Count == 0 ? 1 : state.MaxLimit;
        List<Thread> helpers = new List<Thread>();
        for (int i = 1; i < threads; i++)
        {
            Thread helper = new Thread(() => Guarded(client, cache, state, apiKey, model, progress, pause))
            {
                IsBackground = true,
                Name = "romaji-" + i
            };
            try
            {
                helper.Start();
            }
            catch (Exception)
            {
                break; // no more threads to be had: the ones that started (and this one) do the work
            }
            helpers.Add(helper);
        }
        Guarded(client, cache, state, apiKey, model, progress, pause);

        bool interrupted = false;
        foreach (Thread helper in helpers)
        {
            while (true)
            {
                try
                {
                    helper.Join();
                    break;
                }
                catch (ThreadInterruptedException)
                {
                    // the caller was cancelled while it waited: stop handing out requests, wait for those in flight
                    interrupted = true;
                    lock (state.Gate)
                    {
                        state.Stopped ??= "Cancelled";
                        state.Stop = true;
                        Monitor.PulseAll(state.Gate);
                    }
                }
            }
        }
        cache.Flush();
        if (interrupted)
        {
            Thread.CurrentThread.Interrupt();
        }
        if (state.Fault != null)
        {
            ExceptionDispatchInfo.Capture(state.Fault).Throw();
        }
        string stopped = state.Stopped; // (a stop for another reason, such as the key or the network, says why the rest is missing)
        if (stopped == null)
        {
            List<string> missing = new List<string>();
            if (state.DroppedLines > 0)
            {
                missing.Add("Claude gave no usable answer for " + state.DroppedLines + (state.DroppedLines == 1 ? " line" : " lines")
                        + " (" + state.DroppedReason + ")");
            }
            if (state.Unanswered > 0)
            {
                missing.Add("Claude's answers could not be used for " + state.Unanswered + (state.Unanswered == 1 ? " line" : " lines")
                        + " after " + (state.Stage + 1) + (state.Stage == 0 ? " try" : " tries"));
            }
            stopped = missing.Count == 0 ? null : string.Join("; ", missing);
        }
        return new RomajiResult(done, cachedLines, state.Requests, stopped);
    }

    /// <summary>a worker: whatever goes wrong in it stops the run and is rethrown by the caller, never lost on a thread of its own</summary>
    private static void Guarded(AnthropicClient client, RomajiCache cache, RunState state, string apiKey, string model,
            RomajiProgressListener progress, Pause pause)
    {
        try
        {
            Work(client, cache, state, apiKey, model, progress, pause);
        }
        catch (Exception e)
        {
            lock (state.Gate)
            {
                state.Fault ??= e;
                state.Stop = true;
                Monitor.PulseAll(state.Gate);
            }
        }
    }

    private static void Work(AnthropicClient client, RomajiCache cache, RunState state, string apiKey, string model,
            RomajiProgressListener progress, Pause pause)
    {
        while (true)
        {
            Job job;
            lock (state.Gate)
            {
                while (true)
                {
                    if (state.Stop)
                    {
                        return;
                    }
                    if (state.Queue.Count > 0 && state.Running < state.Limit)
                    {
                        job = state.Queue.First.Value;
                        state.Queue.RemoveFirst();
                        job.Generation = state.Generation;
                        state.Running++;
                        break;
                    }
                    if (state.Queue.Count == 0 && state.Running == 0)
                    {
                        // every ask of this stage is over: the lines that were answered wrongly are asked again, together, with
                        // what was wrong. Not when the answers could not be read at all (asking the same again would only cost
                        // twice; a handful of lines is worth one more try), and not when the stage before got nothing right
                        // out of a good many lines
                        if (state.Stage < RepairStages && state.Rejected.Count > 0
                                && (state.Answered > 0 || state.Rejected.Count <= RepairChunkLines[1])
                                && (state.Stage == 0 || state.StageAccepted > 0 || state.StageLines < GiveUpLines))
                        {
                            state.Stage++;
                            List<Feedback> rejected = state.Rejected;
                            state.Rejected = new List<Feedback>();
                            state.StageLines = rejected.Count;
                            state.StageAccepted = 0;
                            for (int start = 0; start < rejected.Count; start += RepairChunkLines[state.Stage])
                            {
                                List<Feedback> group = rejected.GetRange(start, Math.Min(RepairChunkLines[state.Stage], rejected.Count - start));
                                state.Queue.AddLast(new Job
                                {
                                    Lines = group.Select(f => f.Line).ToList(),
                                    Stage = state.Stage,
                                    Hints = group.ToDictionary(f => f.Line.Key, StringComparer.Ordinal)
                                });
                                state.RequestsTotal++; // progress counts the repair requests too, so a run is not frozen at 100%
                            }
                            Monitor.PulseAll(state.Gate);
                            continue;
                        }
                        state.Unanswered += state.Rejected.Count;
                        state.Rejected.Clear();
                        Monitor.PulseAll(state.Gate); // let the others see it is over
                        return;
                    }
                    try
                    {
                        Monitor.Wait(state.Gate);
                    }
                    catch (ThreadInterruptedException)
                    {
                        state.Stopped ??= "Cancelled";
                        state.Stop = true;
                        Monitor.PulseAll(state.Gate);
                        Thread.CurrentThread.Interrupt();
                        return;
                    }
                }
            }

            OrderedDictionary<string, List<string>> answers = null;
            List<Feedback> refused = new List<Feedback>();
            bool called = true;
            ArtistAiException failure = null;
            try
            {
                answers = Ask(client, job, apiKey, model, refused, out called);
            }
            catch (ArtistAiException e)
            {
                failure = e;
            }
            catch
            {
                lock (state.Gate)
                {
                    state.Running--;
                    Monitor.PulseAll(state.Gate);
                }
                throw; // not something to retry: Guarded stops the run with it
            }

            bool flush = false;
            Next next = Next.Finished;
            lock (state.Gate)
            {
                if (failure == null)
                {
                    state.Running--;
                    flush = Succeeded(cache, state, job, answers, refused, called, progress);
                }
                else
                {
                    next = Failed(state, job, failure);
                    if (next != Next.PauseThenRetry)
                    {
                        state.Running--; // (a job that waits to be retried keeps its place: nobody else may start in it)
                    }
                }
                Monitor.PulseAll(state.Gate);
            }
            if (flush)
            {
                cache.Flush(); // outside the gate: writing the file does not hold the other workers up
            }
            if (next == Next.PauseThenRetry)
            {
                bool cancelled = false;
                try
                {
                    PauseUnlessStopped(state, pause);
                }
                catch (ThreadInterruptedException)
                {
                    cancelled = true;
                }
                lock (state.Gate)
                {
                    state.Running--;
                    if (cancelled)
                    {
                        state.Stopped ??= "Cancelled";
                        state.Stop = true;
                    }
                    if (!state.Stop)
                    {
                        state.Queue.AddFirst(job);
                    }
                    Monitor.PulseAll(state.Gate);
                }
                if (cancelled)
                {
                    Thread.CurrentThread.Interrupt(); // after the lock: a pending interrupt makes a contended lock throw
                    return;
                }
            }
        }
    }

    /// <summary>waits out the pause before a retry, in slices, and stops waiting as soon as the run is over</summary>
    private static void PauseUnlessStopped(RunState state, Pause pause)
    {
        long remaining = RetryPauseMillis;
        while (remaining > 0)
        {
            lock (state.Gate)
            {
                if (state.Stop)
                {
                    return;
                }
            }
            long slice = Math.Min(remaining, PauseSliceMillis);
            pause(slice);
            remaining -= slice;
        }
    }

    /// <summary>
    /// an answer came back (the gate is held): remember it, count it, tell the listener. Returns whether the cache file is
    /// due to be written (the caller does it, outside the gate).
    /// </summary>
    private static bool Succeeded(RomajiCache cache, RunState state, Job job, OrderedDictionary<string, List<string>> answers,
            List<Feedback> refused, bool called, RomajiProgressListener progress)
    {
        if (called)
        {
            state.Requests++;
        }
        foreach (KeyValuePair<string, List<string>> answer in answers)
        {
            state.Done[answer.Key] = answer.Value;
            cache.Put(answer.Key, answer.Value);
        }
        // one request at a time saves after every answer; with several in flight the file (it grows with the library) is
        // written at most every two seconds, and once more at the end
        long now = Environment.TickCount64;
        bool flush = state.MaxLimit == 1 || now - state.LastFlush >= 2000;
        if (flush)
        {
            state.LastFlush = now;
        }
        state.Streak = 0;
        state.DroppedStreak = 0;
        state.Accepted += answers.Count;
        state.Answered += answers.Count + refused.Count;
        if (job.Reask)
        {
            state.StageAccepted += answers.Count;
        }
        if (state.Limit < state.MaxLimit && ++state.GoodRun >= 2 * state.Limit)
        {
            state.Limit++; // the slowdown after a failure wears off
            state.GoodRun = 0;
        }
        // the lines that came back wrong are asked again, with what was wrong (a line with nothing in it to romanize cannot
        // be answered: asking again would only be asking for nothing)
        foreach (RomajiLine line in job.Lines)
        {
            if (answers.ContainsKey(line.Key) || WordsToAsk(line).Count == 0)
            {
                continue;
            }
            Feedback why = refused.FirstOrDefault(r => r.Line.Key == line.Key)
                    ?? new Feedback(line, null, "this line was missing from your answer");
            state.Rejected.Add(why);
        }
        if (!job.Reask)
        {
            state.LinesHandled += job.Lines.Count;
        }
        state.ChunksDone++;
        if (progress != null)
        {
            progress(state.ChunksDone, state.RequestsTotal, state.LinesHandled, state.LinesTotal);
        }
        if (job.Reask)
        {
            return flush; // (the repair requests are counted in the progress, but are not read for the "unreadable" rule below)
        }
        // answers nobody can read (a different format, an empty text) are paid for with every request: when two requests of
        // many lines have not brought one accepted line, the rest of the library is not sent
        if (called && answers.Count == 0 && refused.Count == 0 && job.Lines.Count >= 20)
        {
            state.Unreadable++;
            if (state.Accepted == 0 && state.Unreadable >= 2)
            {
                state.Stopped ??= "Claude's answers could not be read: not one line of " + state.Unreadable
                        + " requests was usable";
                state.Stop = true;
            }
        }
        return flush;
    }

    /// <summary>
    /// a request failed (the gate is held). The key or the model being wrong, or the request refused, ends the run; a
    /// cut-off answer is asked for in halves; a failure that waiting may cure slows the run down and is tried again after
    /// a pause; a failure that will come again for these very lines is tried twice more, at the back of the queue, and then
    /// given up on.
    /// </summary>
    private static Next Failed(RunState state, Job job, ArtistAiException failure)
    {
        string message = failure.Message;
        state.GoodRun = 0;
        if (IsFatal(message))
        {
            state.Stopped ??= message;
            state.Stop = true;
            return Next.Finished;
        }
        if (message.StartsWith("The request was cancelled", StringComparison.Ordinal))
        {
            state.Stopped ??= "Cancelled";
            state.Stop = true;
            return Next.Finished;
        }
        if (message.StartsWith("The answer was cut off", StringComparison.Ordinal) && job.Lines.Count > 1)
        {
            // too much for one answer: ask for each half instead
            int half = job.Lines.Count / 2;
            state.Queue.AddFirst(new Job { Lines = new List<RomajiLine>(job.Lines.GetRange(half, job.Lines.Count - half)), Stage = job.Stage, Hints = job.Hints });
            state.Queue.AddFirst(new Job { Lines = new List<RomajiLine>(job.Lines.GetRange(0, half)), Stage = job.Stage, Hints = job.Hints });
            state.RequestsTotal++;
            return Next.Finished;
        }
        if (IsHopeless(message))
        {
            // asking again will not help, and waiting or fewer requests at once will not either: the others carry on
            if (++job.Attempts < MaxConsecutiveFailures)
            {
                state.Queue.AddLast(job);
                return Next.Requeued;
            }
            if (job.Stage < RepairStages)
            {
                // these lines are not lost: asked in smaller requests the one that cannot be answered is told apart from the rest
                foreach (RomajiLine line in job.Lines)
                {
                    if (WordsToAsk(line).Count != 0)
                    {
                        state.Rejected.Add(new Feedback(line, null, "no usable answer came back for this line"));
                    }
                }
                return Next.Finished;
            }
            state.DroppedLines += job.Lines.Count;
            state.DroppedReason = message;
            if (++state.DroppedStreak >= MaxConsecutiveFailures)
            {
                state.Stopped ??= message + " - stopped after " + MaxConsecutiveFailures
                        + " requests in a row that could not be answered. What was done is saved: run it again to continue.";
                state.Stop = true;
            }
            return Next.Finished;
        }
        if (job.Generation != state.Generation)
        {
            // one of the requests that were in flight when the run slowed down: the same burst, counted once
            return Next.PauseThenRetry;
        }
        if (state.Limit > 1)
        {
            state.Limit = Math.Max(1, state.Limit / 2);
            state.Generation++;
            return Next.PauseThenRetry;
        }
        state.Streak++;
        if (state.Streak >= MaxConsecutiveFailures)
        {
            state.Stopped ??= message + " - stopped after " + MaxConsecutiveFailures
                    + " failed requests in a row. What was done is saved: run it again to continue.";
            state.Stop = true;
            return Next.Finished;
        }
        return Next.PauseThenRetry; // same lines again, after a pause
    }

    /// <summary>the key, a bad model name or a refused request will not get better by trying again</summary>
    private static bool IsFatal(string message)
    {
        return message.StartsWith("The API key", StringComparison.Ordinal)
                || message.StartsWith("No API key", StringComparison.Ordinal)
                || message.StartsWith("The model was not found", StringComparison.Ordinal)
                || message.StartsWith("The API rejected the request", StringComparison.Ordinal);
    }

    /// <summary>a failure that comes from these very lines, not from the service being busy</summary>
    private static bool IsHopeless(string message)
    {
        return message.StartsWith("The model declined", StringComparison.Ordinal)
                || message.StartsWith("Unexpected response from the API", StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ one request

    // an invisible Hangul filler: nothing to romanize
    private static bool IsHangulFiller(char c)
    {
        return c == 'ㅤ' || c == 'ﾠ' || c == 'ᅟ' || c == 'ᅠ';
    }

    // the prolonged sound mark and the iteration mark are "Common" script characters, not kana, but they are Japanese and need their romanization
    private static bool IsJapaneseMark(char c)
    {
        return c == 'ー' || c == 'ｰ' || c == '〆';
    }

    /// <summary>
    /// the positions of the words of a line that Claude is asked about: those with Hangul or kana, and in a Japanese line
    /// also those with Chinese characters or a prolonged sound mark (they are kanji and ー there, timed one by one). The other
    /// words are copied.
    /// </summary>
    private static List<int> WordsToAsk(RomajiLine line)
    {
        bool japanese = IsJapanese(line);
        List<int> asked = new List<int>();
        for (int i = 0; i < line.Words.Count; i++)
        {
            string word = line.Words[i];
            if (word.All(c => IsHangulFiller(c) || char.IsWhiteSpace(c)))
            {
                continue; // nothing to read in it
            }
            if (RomajiScripts.HasHangulOrKana(word)
                    || (japanese && (RomajiScripts.HanSequence(word).Length != 0 || word.Any(IsJapaneseMark))))
            {
                asked.Add(i);
            }
        }
        return asked;
    }

    /// <summary>
    /// one request: the answers that passed validation, by line key. <paramref name="refused"/> receives, for each line
    /// that was answered but not accepted, the answer and what is wrong with it (the lines the answer has nothing for are
    /// not listed). <paramref name="called"/> is false when none of the lines had a word to ask about, so nothing was sent.
    /// </summary>
    private static OrderedDictionary<string, List<string>> Ask(AnthropicClient client, Job job, string apiKey,
            string model, List<Feedback> refused, out bool called)
    {
        List<RomajiLine> chunk = job.Lines;
        List<List<int>> asked = new List<List<int>>(chunk.Count);
        StringBuilder lines = new StringBuilder();
        int sent = 0;
        for (int i = 0; i < chunk.Count; i++)
        {
            List<int> positions = WordsToAsk(chunk[i]);
            asked.Add(positions);
            if (positions.Count == 0)
            {
                continue; // nothing in it to romanize: it cannot get an answer, and is not asked about
            }
            JsonArray words = new JsonArray();
            for (int k = 0; k < positions.Count; k++)
            {
                // the bar is what the pieces of the answer are joined with: a bar inside a word is sent as a slash
                string word = JavaString.Strip(chunk[i].Words[positions[k]]).Replace('|', '/').Replace('｜', '/');
                words.Add(JsonValue.Create(job.Stage > 0 ? (k + 1) + "=" + word : word));
            }
            JsonObject entry = new JsonObject();
            entry["n"] = i + 1;
            entry["lang"] = IsJapanese(chunk[i]) ? "ja" : "ko"; // the rule Usable checks the answer with, so they cannot disagree
            entry["count"] = positions.Count;
            entry["words"] = words;
            if (job.Hints != null && job.Hints.TryGetValue(chunk[i].Key, out Feedback hint))
            {
                if (hint.Previous != null)
                {
                    entry["previous"] = hint.Previous;
                }
                entry["problem"] = hint.Problem;
            }
            lines.Append(ClaudeJson.Compact(entry)).Append('\n');
            sent++;
        }
        OrderedDictionary<string, List<string>> valid = new OrderedDictionary<string, List<string>>(StringComparer.Ordinal);
        called = sent != 0;
        if (sent == 0)
        {
            return valid;
        }
        string message = "Romanize these " + sent + " lines.\n" + lines;

        // medium effort: the readings of kanji take some thought, but not the maximum; the last try at lines that were
        // answered wrongly twice gets more
        JsonObject answer = client.CallTool(apiKey, model, SystemPrompt, message, ToolName,
                ToolDescription, ClaudeJson.Parse(Schema).AsObject(), MaxTokens,
                new AnthropicClient.Options(job.Stage > 0 ? "high" : "medium", 0));

        string text = AnswerText(answer["romaji"]);
        if (text == null)
        {
            return valid;
        }
        foreach (string row in text.Split('\n'))
        {
            Match match = AnswerLine.Match(row);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out int n) || n < 1 || n > chunk.Count)
            {
                continue;
            }
            RomajiLine line = chunk[n - 1];
            List<int> positions = asked[n - 1];
            if (positions.Count == 0 || valid.ContainsKey(line.Key))
            {
                continue;
            }
            string rest = JavaString.Strip(match.Groups[2].Value);
            // an answer with every piece numbered ("3=kya") is read by the numbers, whatever the request asked for
            List<string> pieces = SplitNumbered(rest, positions.Count);
            if (pieces == null)
            {
                pieces = Split(rest);
                if (pieces.Any(piece => NumberPrefix.IsMatch(piece)))
                {
                    // numbered, but not every number once and in range: the numbers are not part of the reading
                    refused.Add(new Feedback(line, Shorten(rest), "write every piece as number=piece, with each number from 1 to "
                            + positions.Count + " exactly once (or no numbers at all)"));
                    continue;
                }
            }
            if (pieces.Count != positions.Count)
            {
                if (job.Stage >= RepairStages)
                {
                    // the last try: a reading that cannot be matched word by word is kept all the same
                    List<string> aligned = LeftAligned(line, positions, pieces);
                    if (aligned != null)
                    {
                        valid[line.Key] = aligned;
                        continue;
                    }
                }
                refused.Add(new Feedback(line, Shorten(rest), pieces.Count + " pieces, but \"count\" is " + positions.Count
                        + ": one piece for each word, no more and no fewer (an empty piece is allowed for a later character "
                        + "of one kanji word)"));
                continue;
            }
            string why = null;
            for (int k = 0; k < positions.Count && why == null; k++)
            {
                if (pieces[k].Length > 0 && !LooksRomanized(line.Words[positions[k]], pieces[k]))
                {
                    why = "piece " + (k + 1) + " has no letters in it: every piece is the reading of its word in Latin letters";
                }
            }
            List<string> cleaned = why == null ? Check(line, Fill(line, positions, pieces), out why) : null;
            if (cleaned != null)
            {
                valid[line.Key] = cleaned;
            }
            else
            {
                refused.Add(new Feedback(line, Shorten(rest), why));
            }
        }
        return valid;
    }

    /// <summary>the pieces of the asked words put at their places among the words that are copied as they are</summary>
    private static List<string> Fill(RomajiLine line, List<int> positions, List<string> pieces)
    {
        List<string> full = new List<string>(line.Words.Count);
        int next = 0;
        for (int i = 0; i < line.Words.Count; i++)
        {
            if (next < positions.Count && positions[next] == i)
            {
                full.Add(pieces[next++]);
            }
            else
            {
                // copied as it is (a control character that came in with the text is dropped, not a reason to lose the line)
                full.Add(new string(JavaString.Strip(line.Words[i]).Where(c => !char.IsControl(c)).ToArray()));
            }
        }
        return full;
    }

    /// <summary>
    /// What is kept of an answer whose pieces cannot be matched to the words, when it is a reading in Latin letters all
    /// the same and not far off (at most two pieces too many or too few): the pieces as they were written, the missing
    /// ones empty at the end, and the extra ones joined onto the last word. The reading is right and reads as sung; only
    /// its timing is off. Null when it does not pass <see cref="Check"/> (a line can still be left unanswered).
    /// </summary>
    private static List<string> LeftAligned(RomajiLine line, List<int> positions, List<string> pieces)
    {
        int n = positions.Count;
        if (Math.Abs(pieces.Count - n) > 2)
        {
            return null;
        }
        List<string> aligned = new List<string>(pieces);
        if (aligned.Count > n)
        {
            string separator = IsJapanese(line) && n > 1 ? "" : " "; // Japanese pieces run together as they are sung
            string tail = string.Join(separator, aligned.Skip(n - 1).Where(piece => piece.Length > 0));
            aligned = aligned.Take(n - 1).Append(tail).ToList();
        }
        while (aligned.Count < n)
        {
            aligned.Add("");
        }
        for (int k = 0; k < n; k++)
        {
            if (aligned[k].Length > 0 && !LooksRomanized(line.Words[positions[k]], aligned[k]))
            {
                return null;
            }
        }
        return Check(line, Fill(line, positions, aligned), out _);
    }

    /// <summary>the start of an answer cut at a character boundary (a surrogate pair is never split)</summary>
    private static string Shorten(string text)
    {
        if (text.Length <= 600)
        {
            return text;
        }
        int cut = char.IsHighSurrogate(text[599]) ? 599 : 600;
        return text.Substring(0, cut);
    }

    /// <summary>
    /// a word whose piece may be empty because its reading is written in the piece of a word before it: in a Japanese line,
    /// a kanji (several characters make up one word whose reading does not divide). Never a Korean syllable and never a
    /// kana, whose reading is its own.
    /// </summary>
    private static bool CanShareReading(string word)
    {
        string text = JavaString.Strip(word);
        return text.Length > 0 && text.EnumerateRunes().All(rune => RomajiScripts.IsHan(rune.Value));
    }

    // a piece that begins with the number of its word ("3=kya"): the numbers of a numbered answer are not part of the reading
    private static readonly Regex NumberPrefix = new Regex("^[ \\t]*[0-9]+[ \\t]*=", RegexOptions.CultureInvariant);

    /// <summary>the text of the answer: the string Claude was asked for, or (a deviation worth reading) an array of its lines</summary>
    private static string AnswerText(JsonNode romaji)
    {
        if (romaji is JsonValue value && value.GetValueKind() == JsonValueKind.String)
        {
            return ClaudeJson.AsString(value);
        }
        if (romaji is JsonArray rows)
        {
            return string.Join("\n", rows.OfType<JsonValue>().Where(v => v.GetValueKind() == JsonValueKind.String).Select(ClaudeJson.AsString));
        }
        return null;
    }

    /// <summary>
    /// the piece Claude gave for a word is the writing of it in Latin letters, not a bare symbol (the line-wide letter test
    /// would let it through when a copied English word is in the line). A small っ/ッ with nothing after it is written as
    /// an apostrophe, which is no letter.
    /// </summary>
    private static bool LooksRomanized(string word, string piece)
    {
        if (piece.Any(c => char.IsAsciiLetterOrDigit(c) || (char.IsLetterOrDigit(c) && !RomajiScripts.IsHan(c))))
        {
            return true;
        }
        return JavaString.Strip(word).All(c => c == 'っ' || c == 'ッ');
    }

    // "3=kya": the number of the word the piece belongs to, then the piece (the request writes "3=word"; a colon is read too)
    private static readonly Regex NumberedPiece = new Regex("^([0-9]+)[ \\t]*[=:](.*)$", RegexOptions.CultureInvariant | RegexOptions.Singleline);

    /// <summary>
    /// The pieces of an answer to a line whose words were numbered: each is written with the number of its word, so a
    /// piece cannot slide onto its neighbour. A number the answer leaves out is an empty piece (whether that piece may be
    /// empty is for <see cref="Check"/> to say). Null when the answer is not written that way, every piece with a number
    /// from 1 to count, each once (then it is read by position).
    /// </summary>
    private static List<string> SplitNumbered(string romaji, int count)
    {
        string[] byNumber = new string[count];
        foreach (string part in Split(romaji))
        {
            Match match = NumberedPiece.Match(part);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out int number) || number < 1 || number > count
                    || byNumber[number - 1] != null)
            {
                return null;
            }
            byNumber[number - 1] = JavaString.Strip(match.Groups[2].Value);
        }
        return byNumber.Select(piece => piece ?? "").ToList();
    }

    private static List<string> Split(string romaji)
    {
        List<string> pieces = new List<string>();
        foreach (string piece in PieceSeparator.Split(JavaString.Strip(romaji)))
        {
            pieces.Add(JavaString.Strip(piece));
        }
        return pieces;
    }

    /// <summary>a Japanese line has some kana in it; a line that has none is Korean (it is only asked about when it has Hangul)</summary>
    private static bool IsJapanese(RomajiLine line)
    {
        return line.Words.Any(RomajiScripts.HasKana);
    }

    /// <summary>
    /// the pieces cleaned up if they are a valid romanization of the line, else null: one piece per word, Latin letters
    /// only (accents folded away), and no empty piece for a word that has letters or digits in it, unless it is a later
    /// character of a Japanese word whose reading is written whole in an earlier piece (a kanji: 今|日 =>
    /// kyou|; see <see cref="SystemPrompt"/>). Only Korean and
    /// Japanese are romanized: in a Korean line the Chinese characters of every word must come back unchanged (same
    /// characters, same order, so pinyin is refused), in a Japanese line none may be left (they are kanji, romanized, and
    /// so is a prolonged sound mark). The decision is the line's, not the word's: Japanese words are often timed one
    /// character at a time.
    /// </summary>
    public static List<string> Usable(RomajiLine line, IReadOnlyList<string> pieces)
    {
        return Check(line, pieces, out _);
    }

    /// <summary><see cref="Usable"/>, and when the pieces are refused, why (said to Claude when the line is asked again)</summary>
    private static List<string> Check(RomajiLine line, IReadOnlyList<string> pieces, out string reason)
    {
        reason = "the answer does not have one piece for each word";
        if (pieces == null || pieces.Count != line.Words.Count)
        {
            return null;
        }
        bool japanese = IsJapanese(line);
        HashSet<int> askedWords = new HashSet<int>(WordsToAsk(line));
        bool reading = false; // an earlier word of the line has its reading: an empty piece can be the rest of it
        int emptied = 0; // asked words whose reading is written in the piece of an earlier word
        List<string> cleaned = new List<string>(pieces.Count);
        for (int i = 0; i < pieces.Count; i++)
        {
            if (pieces[i] == null)
            {
                return null; // Java would fail on it; a damaged cache entry is simply not usable
            }
            string piece = CombiningMarks.Replace(Normalize(pieces[i], NormalizationForm.FormD), "");
            piece = JavaString.Strip(Normalize(piece, NormalizationForm.FormC));
            string word = JavaString.Strip(line.Words[i]);
            if (word.All(IsHangulFiller))
            {
                cleaned.Add(""); // an invisible filler (or nothing at all) has nothing to write
                continue;
            }
            bool wordHasContent = word.EnumerateRunes().Any(rune => Rune.IsLetterOrDigit(rune) || RomajiScripts.IsHan(rune.Value));
            if (piece.Length == 0)
            {
                if (word.All(c => c == 'っ' || c == 'ッ' || c == 'ｯ'))
                {
                    cleaned.Add(""); // a small tsu with no sound after it: nothing to write (an apostrophe is fine too)
                    continue;
                }
                if (wordHasContent)
                {
                    // the reading of several characters that make up one word is written whole in the piece of the first
                    // one, and the pieces of the others are empty: that is a word of the line with no piece of its own
                    if (!japanese || !askedWords.Contains(i) || !CanShareReading(word))
                    {
                        reason = "the piece of word " + (askedWords.ToList().IndexOf(i) + 1) + " (" + word + ") is empty, but only a "
                                + "later kanji of one word may be: every other word needs its own reading";
                        return null;
                    }
                    if (!reading)
                    {
                        reason = "the first piece of a line cannot be empty: the reading of the first word comes first";
                        return null;
                    }
                    emptied++;
                    cleaned.Add("");
                    continue;
                }
                piece = word; // a word that is only punctuation is copied
            }
            if (RomajiScripts.HasHangulOrKana(piece))
            {
                reason = "a piece still has Hangul or kana in it: write everything in Latin letters";
                return null;
            }
            if (piece.Any(char.IsControl))
            {
                reason = "a piece contains a control character";
                return null;
            }
            if (NumberPrefix.IsMatch(piece))
            {
                reason = "a piece begins with a number and =: the numbers are not part of the reading, write only the reading";
                return null;
            }
            if (japanese && piece.Any(IsJapaneseMark))
            {
                reason = "ー is left in the line: write the vowel it stands for";
                return null;
            }
            if (RomajiScripts.HanSequence(piece) != (japanese ? "" : RomajiScripts.HanSequence(word)))
            {
                reason = japanese
                        ? "a Chinese character is left in a Japanese line: romanize every kanji with Hepburn"
                        : "the Chinese characters of a Korean line must stay exactly as they are, in the word they came in";
                return null;
            }
            reading |= askedWords.Contains(i);
            cleaned.Add(piece);
        }

        // more than half of the words without a reading of their own is a reading that stopped short, not a compound
        if (emptied * 2 > askedWords.Count)
        {
            reason = "too many empty pieces: divide the reading between the words";
            return null;
        }

        // a romaji line of a word-synced song needs two timed words to be read back as the romaji of its line (see
        // RomajiGrouper), and the empty pieces are words it does not have
        if (cleaned.Count(piece => piece.Length > 0) < Math.Min(RomajiScripts.MinWords, cleaned.Count))
        {
            reason = "a line needs at least two pieces that are not empty: divide the reading between the words, even if the "
                    + "division is not a natural one";
            return null;
        }

        // the same rule that is applied when the written file is read back, so what is accepted here is never redone there
        WordTimeStamp[] stamps = new WordTimeStamp[cleaned.Count];
        for (int i = 0; i < stamps.Length; i++)
        {
            stamps[i] = new WordTimeStamp(cleaned[i], 0, 0);
        }
        if (!RomajiScripts.IsLatinRomanizationOf(string.Concat(line.Words), stamps))
        {
            reason = "the line has no Latin letters in it";
            return null;
        }
        reason = null;
        return cleaned;
    }

    /// <summary>Normalizer.normalize leaves unpaired surrogates as they are; string.Normalize throws on them</summary>
    private static string Normalize(string text, NormalizationForm form)
    {
        try
        {
            return text.Normalize(form);
        }
        catch (ArgumentException)
        {
            return text;
        }
    }

    private static OrderedDictionary<string, RomajiLine> Unique(IEnumerable<RomajiLine> lines)
    {
        OrderedDictionary<string, RomajiLine> unique = new OrderedDictionary<string, RomajiLine>(StringComparer.Ordinal);
        foreach (RomajiLine line in lines)
        {
            unique.TryAdd(line.Key, line);
        }
        return unique;
    }

    private static List<List<RomajiLine>> Chunk(List<RomajiLine> lines)
    {
        List<List<RomajiLine>> chunks = new List<List<RomajiLine>>();
        List<RomajiLine> current = new List<RomajiLine>();
        int chars = 0;
        foreach (RomajiLine line in lines)
        {
            int size = line.Key.Length + 24;
            if (current.Count > 0 && (current.Count >= MaxLinesPerRequest || chars + size > MaxCharsPerRequest))
            {
                chunks.Add(current);
                current = new List<RomajiLine>();
                chars = 0;
            }
            current.Add(line);
            chars += size;
        }
        if (current.Count > 0)
        {
            chunks.Add(current);
        }
        return chunks;
    }
}
