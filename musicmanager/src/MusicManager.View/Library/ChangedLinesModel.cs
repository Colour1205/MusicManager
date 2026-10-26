using MusicManager.Common;
using MusicManager.InterfaceAdapter.Library;

namespace MusicManager.View.Library;

/// <summary>
/// The data logic of the Java ChangedLinesView (the lyric lines of the song window whose romaji changes), without any
/// painting: for each line, the time and the paragraphs to show, in order. A WinUI view lays these out and styles them:
/// the old romaji (red, struck out), the new Latin romaji (accent color, bold) or a muted italic note while it is not
/// made yet, and the original line.
/// </summary>
public static class ChangedLinesModel
{
    public const string NotMadeYet = "romaji not made yet";

    /// <summary>Which of the three texts of a line a paragraph holds.</summary>
    public enum Kind
    {
        /// <summary>The old non-Latin romaji that is replaced (red, struck out).</summary>
        OldRomaji,

        /// <summary>The new Latin romaji (accent color, bold).</summary>
        NewRomaji,

        /// <summary>The note that the new romaji is not made yet (secondary color, italic).</summary>
        NotMade,

        /// <summary>The original Korean/Japanese line.</summary>
        Original
    }

    /// <summary>One paragraph of a block: its text and which kind it is.</summary>
    public sealed class Paragraph
    {
        public string Text { get; }
        public Kind Kind { get; }

        public Paragraph(string text, Kind kind)
        {
            Text = text;
            Kind = kind;
        }
    }

    /// <summary>One changed line: the time in the left column and the paragraphs stacked next to it.</summary>
    public sealed class Block
    {
        public string Time { get; }
        public List<Paragraph> Paragraphs { get; }

        public Block(string time, List<Paragraph> paragraphs)
        {
            Time = time;
            Paragraphs = paragraphs;
        }
    }

    /// <summary>The blocks of these lines, in the same order (null = no lines).</summary>
    public static List<Block> Blocks(IEnumerable<SongLyricsPreview.LineLine> lines)
    {
        var blocks = new List<Block>();
        if (lines == null)
        {
            return blocks;
        }
        foreach (SongLyricsPreview.LineLine line in lines)
        {
            blocks.Add(BlockOf(line));
        }
        return blocks;
    }

    /// <summary>The block of one line.</summary>
    public static Block BlockOf(SongLyricsPreview.LineLine line)
    {
        var paragraphs = new List<Paragraph>();
        if (TextOf(line.OldRomaji).Length != 0)
        {
            paragraphs.Add(new Paragraph(TextOf(line.OldRomaji), Kind.OldRomaji));
        }
        if (TextOf(line.NewRomaji).Length != 0)
        {
            paragraphs.Add(new Paragraph(TextOf(line.NewRomaji), Kind.NewRomaji));
        }
        else
        {
            paragraphs.Add(new Paragraph(NotMadeYet, Kind.NotMade));
        }
        if (TextOf(line.Original).Length != 0)
        {
            paragraphs.Add(new Paragraph(TextOf(line.Original), Kind.Original));
        }
        return new Block(TextOf(line.Time), paragraphs);
    }

    /// <summary>The text of a field as shown: trimmed, and empty when there is none.</summary>
    public static string TextOf(string text)
    {
        return text == null ? "" : JavaString.Trim(text);
    }
}
