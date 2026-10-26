using System.Globalization;
using MusicManager.Entity;

namespace MusicManager.UseCase.StandardizeLyrics;

/// <summary>
/// decides whether a freshly written lyric file really preserves its source: the Lyric read back from
/// the new file is compared with the Lyric it was written from. Both are flattened to rows first
/// (a line, then one row per translation) because a translation may come back as a line of its own.
/// </summary>
public static class LyricVerifier
{
    private const long ToleranceMs = 1; // the file has millisecond precision, so allow for rounding
    private const int MaxDifferences = 10;
    private const int PreviewLength = 30;

    /// <summary>
    /// short human-readable differences, at most 10; empty when the two are equivalent. Metadata,
    /// offset and line end times are not compared.
    /// </summary>
    public static List<string> Compare(Lyric expected, Lyric actual)
    {
        List<string> differences = new List<string>();
        if (expected == null || actual == null)
        {
            if (expected != actual)
            {
                differences.Add((expected == null ? "expected" : "actual") + " lyric is missing");
            }
            return differences;
        }

        List<Row> want = Flatten(expected);
        List<Row> got = Flatten(actual);
        if (want.Count != got.Count)
        {
            differences.Add("expected " + Number(want.Count) + " lines (translations included) but found " + Number(got.Count));
        }
        for (int i = 0; i < Math.Min(want.Count, got.Count) && differences.Count < MaxDifferences; i++)
        {
            string difference = CompareRows(want[i], got[i]);
            if (difference != null)
            {
                differences.Add("line " + Number(i + 1) + " \"" + Preview(want[i].Text) + "\": " + difference);
            }
        }
        return differences;
    }

    /// <summary>null when the rows are equivalent, otherwise the first thing that differs</summary>
    private static string CompareRows(Row want, Row got)
    {
        if (!Close(want.Start, got.Start))
        {
            return "starts at " + Number(got.Start) + " ms instead of " + Number(want.Start) + " ms";
        }
        if (AsRead(want.Text) != AsRead(got.Text))
        {
            return "text is \"" + Preview(got.Text) + "\"";
        }
        // a line without words is written as a single word, so only rows that both have words compare them
        if (want.Words.Count == 0 || got.Words.Count == 0)
        {
            return null;
        }
        if (want.Words.Count != got.Words.Count)
        {
            return "has " + Number(got.Words.Count) + " words instead of " + Number(want.Words.Count);
        }
        for (int i = 0; i < want.Words.Count; i++)
        {
            WordTimeStamp a = want.Words[i];
            WordTimeStamp b = got.Words[i];
            if (a.Word != b.Word)
            {
                return "word " + Number(i + 1) + " is \"" + Preview(b.Word) + "\" instead of \"" + Preview(a.Word) + "\"";
            }
            if (!Close(a.StartTimeMs, b.StartTimeMs))
            {
                return "word " + Number(i + 1) + " \"" + Preview(a.Word) + "\" starts at " + Number(b.StartTimeMs)
                        + " ms instead of " + Number(a.StartTimeMs) + " ms";
            }
            if (!Close(a.EndTimeMs, b.EndTimeMs))
            {
                return "word " + Number(i + 1) + " \"" + Preview(a.Word) + "\" ends at " + Number(b.EndTimeMs)
                        + " ms instead of " + Number(a.EndTimeMs) + " ms";
            }
        }
        return null;
    }

    /// <summary>
    /// A line's text the way a reader sees it: zero-width spaces are nothing, a run of whitespace is one space and the ends
    /// are trimmed. A line's text is only a summary of its words (a .ttml says "How  could" with two spaces where its words
    /// are "How " and "could", and a zero-width space can end a line), so two texts that differ only in this kind of
    /// whitespace are the same lyric. The words themselves are still compared exactly below, so no character of a lyric
    /// can be lost without the verification noticing.
    /// </summary>
    private static string AsRead(string text)
    {
        System.Text.StringBuilder read = new System.Text.StringBuilder(text.Length);
        bool pendingSpace = false;
        foreach (char c in text)
        {
            if (c == '​' || c == '⁠' || c == '﻿')
            {
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = read.Length > 0;
                continue;
            }
            if (pendingSpace)
            {
                read.Append(' ');
                pendingSpace = false;
            }
            read.Append(c);
        }
        return read.ToString();
    }

    private static bool Close(long a, long b)
    {
        long difference = a - b;
        if (difference < 0)
        {
            difference = -difference; // wraps at long.MinValue like Java's Math.abs, where Math.Abs would throw
        }
        return difference <= ToleranceMs;
    }

    private static List<Row> Flatten(Lyric lyric)
    {
        List<Row> rows = new List<Row>();
        if (lyric.Lines == null)
        {
            return rows;
        }
        foreach (LyricLine line in lyric.Lines)
        {
            if (line == null)
            {
                continue;
            }
            if (line.HasRomanization())
            {
                // a line-synced line has no word timing of its own, and the end of its romaji line is not written, so
                // only the romaji text and start are comparable then
                List<WordTimeStamp> romanized = line.IsWordSynced ? WordsOf(line.RomanizedWords)
                        : new List<WordTimeStamp>();
                rows.Add(new Row(line.StartTimeMs, RomajiScripts.Join(line.RomanizedWords), romanized));
            }
            rows.Add(new Row(line.StartTimeMs, line.Line, WordsOf(line)));
            if (line.Translations == null)
            {
                continue;
            }
            foreach (string translation in line.Translations)
            {
                if (translation != null && !JavaText.IsBlank(translation))
                {
                    rows.Add(new Row(line.StartTimeMs, translation, new List<WordTimeStamp>()));
                }
            }
        }
        return rows;
    }

    /// <summary>the words that were written (null and empty ones are skipped by the writer)</summary>
    private static List<WordTimeStamp> WordsOf(LyricLine line)
    {
        return WordsOf(line.WordTimeStamps);
    }

    private static List<WordTimeStamp> WordsOf(WordTimeStamp[] stamps)
    {
        List<WordTimeStamp> words = new List<WordTimeStamp>();
        if (stamps != null)
        {
            foreach (WordTimeStamp word in stamps)
            {
                if (word != null && word.Word != null && word.Word.Length > 0)
                {
                    words.Add(word);
                }
            }
        }
        return words;
    }

    private static string Preview(string s)
    {
        return s.Length <= PreviewLength ? s : s.Substring(0, PreviewLength) + "...";
    }

    private static string Number(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private sealed class Row
    {
        public long Start { get; }
        public string Text { get; }
        public List<WordTimeStamp> Words { get; }

        public Row(long start, string text, List<WordTimeStamp> words)
        {
            Start = start;
            Text = text == null ? "" : text;
            Words = words;
        }
    }
}
