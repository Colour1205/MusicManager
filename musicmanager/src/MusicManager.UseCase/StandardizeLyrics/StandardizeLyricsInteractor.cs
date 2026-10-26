using System.Text.RegularExpressions;
using MusicManager.Common;
using MusicManager.Entity;
using SyncType = MusicManager.Entity.Lyric.SyncType;

namespace MusicManager.UseCase.StandardizeLyrics;

/// <summary>
/// Standardizes lyrics so each song has exactly one word-synced .lrc:
///
/// source priority  .ttml (word timed)  &gt;  existing word-synced .lrc  &gt;  .hmrc (word timed)
/// a song with no word-synced source anywhere keeps its line-synced .lrc (and only gets romaji, if asked for).
///
/// Optionally every Korean and Japanese line also gets a Latin-letter romaji line, made by Claude, in ANY .lrc: a
/// word-synced one (one romaji word per word), LDDC's line-level "[t]text[t]" output (a "[t]&lt;t&gt;romaji&lt;t&gt;" line
/// above each line) or a plain line-synced one (a "[t]romaji" line above). Romaji that an older tool wrote in Chinese
/// characters (what LDDC's romaji option makes) is replaced by Latin letters; romaji that is already in Latin letters is
/// kept. A run has three phases so that Claude is asked once for the whole library instead of song by song:
/// 1. decide what to do for every song (nothing is written yet),
/// 2. romanize every line that needs it, all songs together, many lines to a request,
/// 3. write each song.
///
/// Nothing on disk is touched until the rendered text has been re-parsed in memory and compared with the source;
/// deprecated .ttml/.hmrc files are only removed once the standard .lrc is in place (or already was), and are kept if
/// they could not be read. The run can be limited to some songs (<see cref="StandardizeLyricsInputData.AudioPaths"/>):
/// the others are neither read nor written nor deleted.
///
/// A dry run changes nothing. The detailed one made for a single song's window
/// (<see cref="StandardizeLyricsInputData.IsPreviewDetail"/>) also lists the lines whose romaji changes
/// (<see cref="SongLyricsResult.LineChanges"/>) and makes the romaji for them (the saved answers, and Claude for the
/// rest), so that the window can show the real new text; with no key or a failed request those lines just come without
/// it and the result says why.
/// </summary>
public class StandardizeLyricsInteractor : IStandardizeLyricsInputBoundary
{
    private const string ArtistJoiner = "; ";

    // Java's \s (without UNICODE_CHARACTER_CLASS) and \d are ASCII only; .NET's would also match Unicode white space and digits
    private const string JavaSpace = @"[ \t\n\x0B\f\r]";

    // the "line L12" / "line #12" part of a per-line note, so notes that differ only by line number are counted together
    private static readonly Regex LineReference = new Regex(JavaSpace + "+line" + JavaSpace + "+[L#]?[0-9]+(?=:)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Spaces = new Regex(JavaSpace + "+");

    // trailing dots and white space of the reason Claude stopped
    private static readonly Regex TrailingDotsAndSpaces = new Regex(@"[.\t\n\x0B\f\r ]+$");

    private readonly IStandardizeLyricsDataAccess _dataAccess;
    private readonly IStandardizeLyricsOutputBoundary _outputBoundary;
    private readonly int _workers;

    public StandardizeLyricsInteractor(IStandardizeLyricsDataAccess dataAccess,
        IStandardizeLyricsOutputBoundary outputBoundary) : this(dataAccess, outputBoundary, 1)
    {
    }

    /// <param name="workers">
    /// how many songs are looked at (phase 1) and written (phase 3) at the same time. The songs do not depend on each other,
    /// so a library of thousands is checked in a fraction of the time on a machine with many cores; the results, the order
    /// of the songs sent to the romaji phase and the progress numbers are the same as with one. 1 = one song after the
    /// other. The data access and the output boundary may be called from several threads (the progress calls are made one
    /// at a time).
    /// </param>
    public StandardizeLyricsInteractor(IStandardizeLyricsDataAccess dataAccess,
        IStandardizeLyricsOutputBoundary outputBoundary, int workers)
    {
        _dataAccess = dataAccess;
        _outputBoundary = outputBoundary;
        _workers = Math.Max(1, workers);
    }

    /// <summary>runs body for 0..count-1, on up to the interactor's workers at once (the calling thread is one of them)</summary>
    private void ForEachSong(int count, Action<int> body)
    {
        if (_workers <= 1 || count <= 1)
        {
            for (int i = 0; i < count; i++)
            {
                body(i);
            }
            return;
        }
        try
        {
            Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = _workers }, body);
        }
        catch (AggregateException e)
        {
            // what the body threw, not the wrapper (a presenter that fails is its own bug and reaches the caller as such)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerExceptions[0]).Throw();
            throw;
        }
    }

    /// <summary>what the romaji phase has to do for one lyric</summary>
    private sealed class RomajiPlan
    {
        public static readonly RomajiPlan None = new RomajiPlan(new List<LyricLine>(), 0, 0);

        public readonly List<LyricLine> Targets; // lines that get a (new) romaji line: none yet, or an old non-Latin one
        public readonly int Replaced; // of the targets, those whose old romaji is not in Latin letters
        public readonly int Present; // Korean/Japanese lines that already have Latin romaji and keep it

        public RomajiPlan(List<LyricLine> targets, int replaced, int present)
        {
            Targets = targets;
            Replaced = replaced;
            Present = present;
        }
    }

    /// <summary>a song that has a .lrc to write, waiting for the romaji phase</summary>
    private sealed class Pending
    {
        public readonly int Index;
        public readonly SongLyricFiles Song;
        public readonly Lyric Source;
        public readonly LyricsAction Action;
        public readonly LyricsAction ActionIfNothingWritten; // what an existing .lrc counts as when no romaji could be made for it
        public readonly List<string> Notes;
        public readonly List<string> Deprecated;
        public readonly RomajiPlan Romaji;

        public Pending(int index, SongLyricFiles song, Lyric source, LyricsAction action,
            LyricsAction actionIfNothingWritten, List<string> notes, List<string> deprecated, RomajiPlan romaji)
        {
            Index = index;
            Song = song;
            Source = source;
            Action = action;
            ActionIfNothingWritten = actionIfNothingWritten;
            Notes = notes;
            Deprecated = deprecated;
            Romaji = romaji;
        }
    }

    /// <summary>either a finished result (nothing to write) or a pending write</summary>
    private sealed class Decision
    {
        public readonly SongLyricsResult Result;
        public readonly Pending Pending;

        public Decision(SongLyricsResult result, Pending pending)
        {
            Result = result;
            Pending = pending;
        }
    }

    public void Execute(StandardizeLyricsInputData input)
    {
        // a run needs the key before it changes anything; the preview of one song makes the romaji it can show, and
        // shows the lines without their romaji text when there is no key
        string apiKey = null;
        if (input.IsRomanize && (!input.IsDryRun || input.IsPreviewDetail))
        {
            apiKey = input.ApiKey == null || JavaText.IsBlank(input.ApiKey)
                ? _dataAccess.ReadApiKeyFromEnvironment()
                : JavaString.Trim(input.ApiKey);
            if (apiKey == null || JavaText.IsBlank(apiKey))
            {
                if (!input.IsPreviewDetail)
                {
                    _outputBoundary.PresentError("Romaji needs an Anthropic API key. Set the ANTHROPIC_API_KEY "
                        + "environment variable, type a key into the window, or untick the romaji option.");
                    return;
                }
                apiKey = null;
            }
        }

        List<SongLyricFiles> songs = InScope(_dataAccess.FindSongs(input.Path), input.AudioPaths);
        SongLyricsResult[] results = new SongLyricsResult[songs.Count];
        List<Pending> pending = new List<Pending>();

        // phase 1: decide (the songs may be looked at several at once; what comes of each is put in its own place, so the
        // order of everything that follows is the order of the songs)
        Decision[] decisions = new Decision[songs.Count];
        object progressGate = new object();
        int decided = 0;
        ForEachSong(songs.Count, i =>
        {
            decisions[i] = DecideSafely(i, songs[i], input);
            lock (progressGate)
            {
                _outputBoundary.PresentProgress(++decided, songs.Count, SongName(songs[i]));
            }
        });
        for (int i = 0; i < songs.Count; i++)
        {
            if (decisions[i].Result != null)
            {
                results[i] = decisions[i].Result;
            }
            else
            {
                pending.Add(decisions[i].Pending);
            }
        }

        // phase 2: romaji for every pending song at once
        RomajiResult romaji = null;
        RomajiEstimate estimate = null;
        if (input.IsRomanize)
        {
            List<RomajiLine> needed = CollectRomajiNeeds(pending);
            if (input.IsDryRun)
            {
                estimate = _dataAccess.EstimateRomaji(needed);
            }
            // a plain preview asks Claude nothing; a run does, and so does the preview of one song (to show the romaji)
            if (needed.Count > 0 && (!input.IsDryRun || input.IsPreviewDetail))
            {
                romaji = _dataAccess.Romanize(needed, apiKey, input.Model, (requestsDone, requestsTotal, linesDone,
                    linesTotal) => _outputBoundary.PresentProgress(requestsDone, requestsTotal,
                        "Romaji, request " + requestsDone + " of " + requestsTotal + " (" + linesDone + " of "
                        + linesTotal + " lines)"));
            }
        }

        // Claude could not be used at all (rejected key, unknown model, no network): stop before changing anything,
        // instead of standardizing and deleting the whole library without the romaji that was asked for. A preview
        // changes nothing, so it carries on and shows the lines without their romaji text.
        if (!input.IsDryRun && romaji != null && romaji.Requests == 0 && romaji.StoppedBecause != null)
        {
            _outputBoundary.PresentError("Romaji could not be made: " + romaji.StoppedBecause
                + ". Nothing was changed.");
            return;
        }

        // phase 3: write
        string romajiGap = input.IsPreviewDetail && input.IsRomanize ? RomajiGapReason(apiKey, romaji) : null;
        int written = 0;
        ForEachSong(pending.Count, i =>
        {
            Pending p = pending[i];
            results[p.Index] = CommitSafely(p, input, romaji, romajiGap);
            lock (progressGate)
            {
                _outputBoundary.PresentProgress(++written, pending.Count, "Writing " + SongName(p.Song));
            }
        });

        List<SongLyricsResult> ordered = new List<SongLyricsResult>(results.Length);
        foreach (SongLyricsResult result in results)
        {
            ordered.Add(result);
        }
        RomajiSummary summary = input.IsRomanize ? Summarize(ordered, input, pending, romaji, estimate) : RomajiSummary.None;
        _outputBoundary.Present(new StandardizeLyricsOutputData(ordered, input.IsDryRun, summary));
    }

    /// <summary>the totals of the per-song counts, together with what Claude was (or, in a preview, would be) asked</summary>
    private static RomajiSummary Summarize(List<SongLyricsResult> results, StandardizeLyricsInputData input,
        List<Pending> pending, RomajiResult romaji, RomajiEstimate estimate)
    {
        int added = 0;
        int replaced = 0;
        int missing = 0;
        foreach (SongLyricsResult result in results)
        {
            added += result.RomajiAdded;
            replaced += result.RomajiReplaced;
            missing += result.RomajiMissing;
        }
        if (input.IsDryRun)
        {
            return new RomajiSummary(true, estimate.UniqueLines, estimate.CachedLines, estimate.Requests,
                added, replaced, 0, null);
        }
        return new RomajiSummary(true, romaji == null ? 0 : UniqueKeyCount(pending),
            romaji == null ? 0 : romaji.CachedLines, romaji == null ? 0 : romaji.Requests, added,
            replaced, missing, romaji == null ? null : romaji.StoppedBecause);
    }

    /// <summary>the songs a run is limited to; the scope is compared as written and, failing that, as normalized paths</summary>
    private static List<SongLyricFiles> InScope(List<SongLyricFiles> songs, HashSet<string> scope)
    {
        if (scope == null)
        {
            return songs;
        }
        // java.nio.file.Path.equals on Windows ignores case, so the normalized paths are compared that way
        HashSet<string> normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in scope)
        {
            string normal = Normalize(path);
            if (normal != null)
            {
                normalized.Add(normal);
            }
        }
        List<SongLyricFiles> kept = new List<SongLyricFiles>();
        foreach (SongLyricFiles song in songs)
        {
            string normalSong = Normalize(song.AudioPath);
            if (scope.Contains(song.AudioPath) || (normalSong != null && normalized.Contains(normalSong)))
            {
                kept.Add(song);
            }
        }
        return kept;
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ phase 1: deciding

    private Decision DecideSafely(int index, SongLyricFiles song, StandardizeLyricsInputData input)
    {
        try
        {
            return Decide(index, song, input);
        }
        catch (Exception e)
        {
            return new Decision(Result(song, LyricsAction.Failed, e.Message, new List<string>()), null);
        }
    }

    private Decision Decide(int index, SongLyricFiles song, StandardizeLyricsInputData input)
    {
        string lrcPath = song.LrcPath;
        string ttmlPath = song.TtmlPath;
        string hmrcPath = song.HmrcPath;

        if (lrcPath == null && ttmlPath == null && hmrcPath == null)
        {
            return Done(Result(song, LyricsAction.NoLyrics, "", new List<string>()));
        }

        List<string> notes = new List<string>();
        List<string> deprecated = new List<string>();
        if (ttmlPath != null)
        {
            deprecated.Add(ttmlPath);
        }
        if (hmrcPath != null)
        {
            deprecated.Add(hmrcPath);
        }

        // an .lrc already in the standard syntax only needs the deprecated files cleaned up, and the romaji if asked for
        if (lrcPath != null && _dataAccess.IsStandardWordSyncedFile(lrcPath))
        {
            RomajiPlan plan = RomajiPlan.None;
            if (input.IsRomanize)
            {
                Lyric existing = Prepare(TryRead(lrcPath, notes));
                plan = PlanRomaji(existing);
                if (plan.Targets.Count > 0)
                {
                    return new Decision(null, new Pending(index, song, existing, LyricsAction.RomajiAdded,
                        LyricsAction.AlreadyStandard, notes, deprecated, plan));
                }
            }
            List<string> deleted = RemoveFiles(deprecated, input, notes);
            return Done(Result(song, LyricsAction.AlreadyStandard, Join(notes), deleted, 0, 0, plan.Present, 0));
        }

        Lyric ttml = Prepare(TryRead(ttmlPath, notes));
        if (IsWordSynced(ttml))
        {
            return MakePending(index, song, ttml, LyricsAction.ConvertedFromTtml, notes, deprecated, input);
        }

        Lyric lrc = Prepare(TryRead(lrcPath, notes));
        if (IsWordSynced(lrc))
        {
            return MakePending(index, song, lrc, LyricsAction.NormalizedLrc, notes, deprecated, input);
        }

        Lyric hmrc = Prepare(TryRead(hmrcPath, notes));
        if (IsWordSynced(hmrc))
        {
            return MakePending(index, song, hmrc, LyricsAction.ConvertedFromHmrc, notes, deprecated, input);
        }

        // no word-synced source anywhere: the best we can do is a line-synced .lrc
        Lyric fallback = Rank(ttml) >= Rank(hmrc) ? ttml : hmrc;
        if (Rank(lrc) > 0 && Rank(lrc) >= Rank(fallback))
        {
            // keep the existing .lrc; only deprecated files that were readable (so add nothing) are removed
            List<string> removable = new List<string>();
            if (ttml != null)
            {
                removable.Add(ttmlPath);
            }
            if (hmrc != null)
            {
                removable.Add(hmrcPath);
            }
            // a line-synced .lrc still gets its romaji lines, written the line-synced way
            RomajiPlan plan = input.IsRomanize ? PlanRomaji(lrc) : RomajiPlan.None;
            if (plan.Targets.Count > 0)
            {
                return new Decision(null, new Pending(index, song, lrc, LyricsAction.RomajiAdded,
                    LyricsAction.KeptLineSynced, notes, removable, plan));
            }
            if (input.IsRomanize && lrc.GetSyncType() == SyncType.NoSync && HasKoreanOrJapanese(lrc))
            {
                notes.Add("the lyrics have no timing, so no romaji was added");
            }
            List<string> deleted = RemoveFiles(removable, input, notes);
            return Done(Result(song, LyricsAction.KeptLineSynced, Join(notes), deleted, 0, 0, plan.Present, 0));
        }
        if (Rank(fallback) > 0)
        {
            return MakePending(index, song, fallback, LyricsAction.LineSyncedFromDeprecated, notes, deprecated, input);
        }

        string reason = notes.Count == 0 ? "No readable lyric file" : Join(notes);
        return Done(Result(song, LyricsAction.Failed, reason, new List<string>()));
    }

    private static Decision Done(SongLyricsResult result)
    {
        return new Decision(result, null);
    }

    private static Decision MakePending(int index, SongLyricFiles song, Lyric source, LyricsAction action,
        List<string> notes, List<string> deprecated, StandardizeLyricsInputData input)
    {
        RomajiPlan plan = input.IsRomanize ? PlanRomaji(source) : RomajiPlan.None;
        return new Decision(null, new Pending(index, song, source, action, action, notes, deprecated, plan));
    }

    /// <summary>
    /// a freshly read lyric in the one shape the standard file can hold: no space-only words, and a romanization line
    /// folded into the line it belongs to
    /// </summary>
    private static Lyric Prepare(Lyric lyric)
    {
        if (lyric == null)
        {
            return null;
        }
        LyricNormalizer.Normalize(lyric);
        RomajiGrouper.Group(lyric);
        return lyric;
    }

    // ------------------------------------------------------------------ phase 2: romaji

    /// <summary>
    /// what the lyric's Korean and Japanese lines need: the ones with no romaji yet or with old romaji that is not in
    /// Latin letters become targets, the ones that already have Latin romaji are only counted. An untimed lyric has
    /// nowhere to put a romaji line, so it needs nothing.
    /// </summary>
    private static RomajiPlan PlanRomaji(Lyric lyric)
    {
        if (lyric == null || lyric.Lines == null || lyric.GetSyncType() == SyncType.NoSync)
        {
            return RomajiPlan.None;
        }
        bool lineLevel = RomajiScripts.IsLineLevel(lyric);
        List<LyricLine> targets = new List<LyricLine>();
        int replaced = 0;
        int present = 0;
        foreach (LyricLine line in lyric.Lines)
        {
            if (!RomajiScripts.NeedsRomaji(line, lineLevel))
            {
                continue;
            }
            if (RomajiScripts.HasLatinRomanization(line))
            {
                present++;
            }
            else
            {
                targets.Add(line);
                if (line.HasRomanization())
                {
                    replaced++;
                }
            }
        }
        return new RomajiPlan(targets, replaced, present);
    }

    private static bool HasKoreanOrJapanese(Lyric lyric)
    {
        foreach (LyricLine line in lyric.Lines)
        {
            if (line != null && RomajiScripts.HasHangulOrKana(line.Line))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// the words of a line as Claude is given them: its timed words, or for a line without word timing the whole line
    /// as one word
    /// </summary>
    private static WordTimeStamp[] RomajiSourceWords(LyricLine line)
    {
        WordTimeStamp[] words = line.WordTimeStamps;
        if (words != null && words.Length > 0)
        {
            return words;
        }
        long start = line.StartTimeMs;
        return new[] { new WordTimeStamp(line.Line, start, Math.Max(start, line.EndTimeMs)) };
    }

    private List<RomajiLine> CollectRomajiNeeds(List<Pending> pending)
    {
        OrderedDictionary<string, RomajiLine> unique = new OrderedDictionary<string, RomajiLine>();
        foreach (Pending p in pending)
        {
            foreach (LyricLine line in p.Romaji.Targets)
            {
                RomajiLine need = RomajiLine.Of(RomajiSourceWords(line));
                unique.TryAdd(need.Key, need);
            }
        }
        return new List<RomajiLine>(unique.Values);
    }

    private static int UniqueKeyCount(List<Pending> pending)
    {
        HashSet<string> keys = new HashSet<string>();
        foreach (Pending p in pending)
        {
            foreach (LyricLine line in p.Romaji.Targets)
            {
                keys.Add(RomajiLine.Of(RomajiSourceWords(line)).Key);
            }
        }
        return keys.Count;
    }

    /// <summary>
    /// gives each target line its romanized words, keeping the original words' timing and spacing (a line without word
    /// timing gets one word, the whole line, over the line's time); returns {lines that got one, of those the lines that
    /// had old romaji to replace}
    /// </summary>
    private static (int Added, int Replaced) ApplyRomaji(Pending p, RomajiResult romaji)
    {
        int added = 0;
        int replaced = 0;
        foreach (LyricLine line in p.Romaji.Targets)
        {
            WordTimeStamp[] romanized = RomanizedWords(line, romaji);
            if (romanized == null)
            {
                continue;
            }
            bool hadOldRomaji = line.HasRomanization();
            line.RomanizedWords = romanized;
            added++;
            if (hadOldRomaji)
            {
                replaced++;
            }
        }
        return (added, replaced);
    }

    /// <summary>
    /// the line's romanized words (the original words' timing and spacing, romaji for the text), or null when there is
    /// no usable answer for it
    /// </summary>
    private static WordTimeStamp[] RomanizedWords(LyricLine line, RomajiResult romaji)
    {
        WordTimeStamp[] words = RomajiSourceWords(line);
        List<string> pieces = romaji == null ? null : romaji.Get(RomajiLine.Of(words).Key);
        if (pieces == null || pieces.Count != words.Length)
        {
            return null;
        }
        List<WordTimeStamp> romanized = new List<WordTimeStamp>(words.Length);
        for (int i = 0; i < words.Length; i++)
        {
            string original = words[i].Word;
            int leadingLength = 0; // Java: original.length() - original.stripLeading().length()
            while (leadingLength < original.Length && JavaText.IsWhitespace(original[leadingLength]))
            {
                leadingLength++;
            }
            string leading = original.Substring(0, leadingLength);
            string trailing = original.Substring(JavaText.StripTrailing(original).Length);
            if (pieces[i].Length == 0)
            {
                // the rest of the reading of the word before (several characters that make up one word): that reading
                // lasts as long as all of them, and this word has no romaji of its own. With no word before it, it is
                // dropped (a romaji word with no text is not written, and could not be read back)
                if (romanized.Count == 0)
                {
                    continue;
                }
                WordTimeStamp before = romanized[romanized.Count - 1];
                bool spaced = before.Word.Length > 0 && JavaText.IsWhitespace(before.Word[before.Word.Length - 1]);
                romanized[romanized.Count - 1] = new WordTimeStamp(spaced ? before.Word : before.Word + trailing,
                    before.StartTimeMs, Math.Max(before.EndTimeMs, words[i].EndTimeMs));
                continue;
            }
            romanized.Add(new WordTimeStamp(leading + pieces[i] + trailing, words[i].StartTimeMs,
                words[i].EndTimeMs));
        }
        return romanized.ToArray();
    }

    /// <summary>
    /// the lines of a song whose romaji changes, in lyric order, as the preview of its window shows them. Called before the
    /// romaji is applied, while the old romaji is still on the lines; a line with no answer yet has no new romaji.
    /// </summary>
    private static List<LyricLineChange> DescribeChanges(Pending p, RomajiResult romaji)
    {
        List<LyricLineChange> changes = new List<LyricLineChange>();
        foreach (LyricLine line in p.Romaji.Targets)
        {
            bool hadOldRomaji = line.HasRomanization();
            WordTimeStamp[] romanized = RomanizedWords(line, romaji);
            changes.Add(new LyricLineChange(
                hadOldRomaji ? LyricLineChange.Kind.RomajiReplaced : LyricLineChange.Kind.RomajiAdded,
                line.StartTimeMs, line.Line == null ? "" : JavaText.Strip(line.Line),
                hadOldRomaji ? Text(line.RomanizedWords) : null, romanized == null ? null : Text(romanized)));
        }
        return changes;
    }

    /// <summary>the words as one line of text: joined as written (they carry their own spaces), trimmed, spaces collapsed</summary>
    private static string Text(WordTimeStamp[] words)
    {
        System.Text.StringBuilder text = new System.Text.StringBuilder();
        foreach (WordTimeStamp word in words)
        {
            text.Append(word.Word);
        }
        return Spaces.Replace(JavaText.Strip(text.ToString()), " ");
    }

    /// <summary>why a preview has lines without their romaji text: no key, the reason Claude stopped, or an unusable answer</summary>
    private static string RomajiGapReason(string apiKey, RomajiResult romaji)
    {
        if (apiKey == null)
        {
            return "no Anthropic API key (Process asks Claude for it)";
        }
        if (romaji != null && romaji.StoppedBecause != null)
        {
            return TrailingDotsAndSpaces.Replace(JavaText.Strip(romaji.StoppedBecause), "");
        }
        return "Claude gave no usable answer for them";
    }

    // ------------------------------------------------------------------ phase 3: writing

    private SongLyricsResult CommitSafely(Pending p, StandardizeLyricsInputData input, RomajiResult romaji,
        string romajiGap)
    {
        try
        {
            return Commit(p, input, romaji, romajiGap);
        }
        catch (Exception e)
        {
            return Result(p.Song, LyricsAction.Failed, e.Message, new List<string>());
        }
    }

    /// <summary>
    /// renders the source, proves the rendered text round-trips to the same lyric, and only then
    /// replaces the .lrc and removes the deprecated files. The preview of one song (<c>input.IsPreviewDetail</c>) does
    /// all of it but the writing and deleting, and also lists the lines whose romaji changes.
    /// </summary>
    /// <param name="romajiGap">why some lines of a preview have no romaji text (null outside a preview)</param>
    private SongLyricsResult Commit(Pending p, StandardizeLyricsInputData input, RomajiResult romaji, string romajiGap)
    {
        Lyric source = p.Source;
        List<string> notes = p.Notes;
        int wanted = p.Romaji.Targets.Count;

        // the lines are listed before their new romaji is applied, while the old romaji is still on them
        List<LyricLineChange> lineChanges = input.IsPreviewDetail ? DescribeChanges(p, romaji) : new List<LyricLineChange>();

        // a preview plans what a run would do; a run counts what it did
        int added = input.IsDryRun ? wanted : 0;
        int replaced = input.IsDryRun ? p.Romaji.Replaced : 0;
        if (wanted > 0 && (!input.IsDryRun || input.IsPreviewDetail))
        {
            (int Added, int Replaced) applied = ApplyRomaji(p, romaji); // a preview applies it in memory only, to verify the result
            if (!input.IsDryRun)
            {
                added = applied.Added;
                replaced = applied.Replaced;
            }
        }
        int missing = input.IsDryRun ? 0 : wanted - added;
        int present = p.Romaji.Present;

        if (wanted > 0)
        {
            // the two kinds of line are told apart, as the library's status line and the review do: "added" counts the
            // lines that had no romaji, "replaced" the lines whose old romaji was in Chinese characters
            if (input.IsPreviewDetail)
            {
                // the lines themselves are listed, so no sentence about them; only what could not be made is said
                int unavailable = 0;
                foreach (LyricLineChange change in lineChanges)
                {
                    if (change.NewRomaji == null)
                    {
                        unavailable++;
                    }
                }
                if (unavailable > 0)
                {
                    notes.Add("romaji text is not available for " + Lines(unavailable) + " yet: " + romajiGap);
                }
            }
            else if (!input.IsDryRun) // a plain preview has no sentence: the summary and the song window tell the counts
            {
                if (added > 0)
                {
                    notes.Add(RomajiSentence(added - replaced, replaced));
                }
                if (missing > 0)
                {
                    notes.Add("romaji missing for " + missing + " of " + wanted + " lines (run again to finish)");
                }
            }
        }

        // an existing file gets nothing from a run that could not make any romaji: leave it alone
        if (p.Action == LyricsAction.RomajiAdded && !input.IsDryRun && added == 0)
        {
            List<string> deleted = RemoveFiles(p.Deprecated, input, notes);
            return Result(p.Song, p.ActionIfNothingWritten, Join(notes), deleted, 0, 0, present, missing);
        }

        // the header tags are not part of what is verified, so a preview skips reading every audio file's tags
        if (!input.IsDryRun)
        {
            FillHeaders(source, p.Song);
        }
        string text = StandardLrcFormatter.Format(source);

        Lyric reread = Prepare(_dataAccess.ParseLrcText(text));
        List<string> differences = LyricVerifier.Compare(source, reread);
        if (differences.Count > 0)
        {
            return Result(p.Song, LyricsAction.Failed, "Verification failed, nothing was changed: " + differences[0],
                new List<string>());
        }

        foreach (string warning in source.Warnings)
        {
            notes.Add(warning);
        }

        if (!input.IsDryRun)
        {
            _dataAccess.WriteLyricFile(p.Song.TargetLrcPath, text);
        }
        List<string> removed = RemoveFiles(p.Deprecated, input, notes);
        return new SongLyricsResult(p.Song.AudioPath, p.Action, Join(notes), removed, added, replaced, present,
            missing, p.Song.TargetLrcPath, lineChanges);
    }

    /// <summary>"1 line" / "30 lines"</summary>
    private static string Lines(int count)
    {
        return count + (count == 1 ? " line" : " lines");
    }

    /// <summary>"1 old Chinese-character line" / "12 old Chinese-character lines"</summary>
    private static string OldLines(int count)
    {
        return count + " old Chinese-character " + (count == 1 ? "line" : "lines");
    }

    /// <summary>
    /// The note about what romaji did to a song: lines that had none got a new one ("added"), lines whose old romaji was in
    /// Chinese characters got Latin letters instead ("replaced"). The two counts never overlap. Only a real run has it.
    /// </summary>
    internal static string RomajiSentence(int added, int replaced)
    {
        if (replaced == 0)
        {
            return "romaji added to " + Lines(added);
        }
        if (added == 0)
        {
            return OldLines(replaced) + " replaced with Latin romaji";
        }
        return "romaji added to " + Lines(added) + ", " + OldLines(replaced) + " replaced";
    }

    /// <summary>a Lyric built from .ttml/.hmrc has no header tags: take title/artist/album from the audio file's tag</summary>
    private void FillHeaders(Lyric lyric, SongLyricFiles song)
    {
        Music tags = _dataAccess.ReadMusicTags(song.AudioPath);
        if (tags == null)
        {
            return;
        }
        PutIfMissing(lyric, "ti", tags.Title);
        PutIfMissing(lyric, "ar", tags.Artists == null ? "" : string.Join(ArtistJoiner, tags.Artists));
        PutIfMissing(lyric, "al", tags.Album);
    }

    private void PutIfMissing(Lyric lyric, string key, string value)
    {
        lyric.Metadata.TryGetValue(key, out string existing);
        if ((existing == null || JavaString.Trim(existing).Length == 0) && value != null
            && JavaString.Trim(value).Length != 0)
        {
            lyric.Metadata[key] = JavaString.Trim(value);
        }
    }

    /// <summary>deletes (or, in a dry run, only lists) the files; returns the ones that are/would be gone</summary>
    private List<string> RemoveFiles(List<string> paths, StandardizeLyricsInputData input, List<string> notes)
    {
        List<string> removed = new List<string>();
        foreach (string path in paths)
        {
            if (input.IsDryRun || _dataAccess.DeleteFile(path, input.IsMoveToTrash))
            {
                removed.Add(path);
            }
            else
            {
                notes.Add("could not " + (input.IsMoveToTrash ? "recycle " : "delete ") + FileName(path));
            }
        }
        return removed;
    }

    private Lyric TryRead(string path, List<string> notes)
    {
        if (path == null)
        {
            return null;
        }
        try
        {
            return _dataAccess.ReadLyric(path);
        }
        catch (Exception e)
        {
            notes.Add(FileName(path) + ": " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// true when some line really has per-word timing. A file whose every line is one timed "word" (LDDC's
    /// "[00:06.990]text[00:19.590]" line-level output) is line-synced however it is written, and rewriting it as
    /// word-synced would only pretend to add timing.
    /// </summary>
    private static bool IsWordSynced(Lyric lyric)
    {
        if (lyric == null || lyric.GetSyncType() != SyncType.WordSync)
        {
            return false;
        }
        foreach (LyricLine line in lyric.Lines)
        {
            WordTimeStamp[] words = line.WordTimeStamps;
            if (words != null && words.Length >= 2)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>how much timing a lyric carries; used to avoid replacing a better file with a worse one</summary>
    private static int Rank(Lyric lyric)
    {
        if (lyric == null || lyric.Lines.Count == 0)
        {
            return 0;
        }
        if (IsWordSynced(lyric))
        {
            return 3;
        }
        return lyric.GetSyncType() == SyncType.NoSync ? 1 : 2;
    }

    private static SongLyricsResult Result(SongLyricFiles song, LyricsAction action, string detail,
        List<string> deleted)
    {
        return new SongLyricsResult(song.AudioPath, action, detail, deleted);
    }

    private static SongLyricsResult Result(SongLyricFiles song, LyricsAction action, string detail,
        List<string> deleted, int romajiAdded, int romajiReplaced, int romajiPresent, int romajiMissing)
    {
        return new SongLyricsResult(song.AudioPath, action, detail, deleted, romajiAdded, romajiReplaced,
            romajiPresent, romajiMissing);
    }

    /// <summary>
    /// joins the notes for display; a parser that complains about the same thing on dozens of lines
    /// ("TTML line L2: removed corrupted characters", "... L3: ...") becomes one "... (x22)" entry
    /// </summary>
    private static string Join(List<string> notes)
    {
        OrderedDictionary<string, int> counts = new OrderedDictionary<string, int>();
        OrderedDictionary<string, string> firstSeen = new OrderedDictionary<string, string>();
        foreach (string note in notes)
        {
            string key = LineReference.Replace(note, "", 1);
            counts[key] = counts.TryGetValue(key, out int seen) ? seen + 1 : 1;
            firstSeen.TryAdd(key, note);
        }

        List<string> parts = new List<string>();
        foreach (KeyValuePair<string, int> entry in counts)
        {
            int count = entry.Value;
            parts.Add(count == 1 ? firstSeen[entry.Key] : entry.Key + " (x" + count + ")");
        }
        return string.Join("; ", parts);
    }

    private static string FileName(string path)
    {
        return Path.GetFileName(path);
    }

    private static string SongName(SongLyricFiles song)
    {
        string name = FileName(song.AudioPath);
        int dot = name.LastIndexOf('.');
        return dot > 0 ? name.Substring(0, dot) : name;
    }
}
