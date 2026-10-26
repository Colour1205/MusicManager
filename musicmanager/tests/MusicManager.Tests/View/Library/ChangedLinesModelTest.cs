using MusicManager.InterfaceAdapter.Library;
using MusicManager.View.Library;
using Xunit;

namespace MusicManager.Tests.View.Library;

/// <summary>
/// The data logic of the Java ChangedLinesView: which paragraphs a changed line has and in which order (the layout and
/// painting tests of the Java view are Swing-bound and not ported).
/// </summary>
public class ChangedLinesModelTest
{
    private static ChangedLinesModel.Block Block(SongLyricsPreview.LineLine line)
    {
        return ChangedLinesModel.Blocks(new List<SongLyricsPreview.LineLine> { line })[0];
    }

    [Fact]
    public void TheOldRomajiComesFirstThenTheNewOneThenTheOriginal()
    {
        ChangedLinesModel.Block block = Block(new SongLyricsPreview.LineLine("0:07", "오늘 밤은", "哦 呐", "oneul bameun"));

        Assert.Equal("0:07", block.Time);
        Assert.Equal(3, block.Paragraphs.Count);
        Assert.Equal("哦 呐", block.Paragraphs[0].Text);
        Assert.Equal(ChangedLinesModel.Kind.OldRomaji, block.Paragraphs[0].Kind);
        Assert.Equal("oneul bameun", block.Paragraphs[1].Text);
        Assert.Equal(ChangedLinesModel.Kind.NewRomaji, block.Paragraphs[1].Kind);
        Assert.Equal("오늘 밤은", block.Paragraphs[2].Text);
        Assert.Equal(ChangedLinesModel.Kind.Original, block.Paragraphs[2].Kind);
    }

    [Fact]
    public void ALineWithoutOldRomajiHasNothingStruckOut()
    {
        ChangedLinesModel.Block block = Block(new SongLyricsPreview.LineLine("0:07", "오늘 밤은", null, "oneul bameun"));

        Assert.Equal(2, block.Paragraphs.Count);
        Assert.Equal(ChangedLinesModel.Kind.NewRomaji, block.Paragraphs[0].Kind);
        Assert.Equal("oneul bameun", block.Paragraphs[0].Text);
    }

    [Fact]
    public void RomajiThatIsNotMadeYetIsSaidSo()
    {
        ChangedLinesModel.Block block = Block(new SongLyricsPreview.LineLine("0:07", "오늘 밤은", "哦 呐", null));

        Assert.Equal(3, block.Paragraphs.Count);
        Assert.Equal(ChangedLinesModel.Kind.OldRomaji, block.Paragraphs[0].Kind); // the old romaji still goes
        Assert.Equal(ChangedLinesModel.NotMadeYet, block.Paragraphs[1].Text);
        Assert.Equal(ChangedLinesModel.Kind.NotMade, block.Paragraphs[1].Kind);
    }

    [Fact]
    public void TextIsTrimmedAndBlankPartsAreLeftOut()
    {
        ChangedLinesModel.Block block = Block(new SongLyricsPreview.LineLine(" 1:02 ", "   ", "  ", "  x "));

        Assert.Equal("1:02", block.Time);
        Assert.Single(block.Paragraphs);
        Assert.Equal("x", block.Paragraphs[0].Text);
    }

    [Fact]
    public void TheBlocksKeepTheOrderOfTheLinesAndNoLinesGiveNoBlocks()
    {
        List<ChangedLinesModel.Block> blocks = ChangedLinesModel.Blocks(new List<SongLyricsPreview.LineLine>
        {
            new SongLyricsPreview.LineLine("0:07", "a", null, "b"),
            new SongLyricsPreview.LineLine("0:09", "c", "d", "e"),
            new SongLyricsPreview.LineLine("10:45", "f", null, null)
        });

        Assert.Equal(new List<string> { "0:07", "0:09", "10:45" }, blocks.Select(b => b.Time).ToList());
        Assert.Empty(ChangedLinesModel.Blocks(null));
        Assert.Empty(ChangedLinesModel.Blocks(new List<SongLyricsPreview.LineLine>()));
    }
}
