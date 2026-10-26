using MusicManager.UseCase.StandardizeArtists;
using Xunit;

namespace MusicManager.Tests.UseCase.StandardizeArtists;

/// <summary>The name a standardized song's files have: "Artist - Title", from the tags, fit for a Windows file name.</summary>
public class StandardFileNameTest
{
    private static List<string> L(params string[] values) => new List<string>(values);

    [Fact]
    public void ItIsTheArtistDashTheTitle()
    {
        Assert.Equal("IU - Good Day", StandardFileName.Build(L("IU"), "Good Day", " & "));
    }

    [Fact]
    public void SeveralArtistsAreJoinedWithTheSeparator()
    {
        Assert.Equal("ROSÉ & Bruno Mars - APT.", StandardFileName.Build(L("ROSÉ", "Bruno Mars"), "APT.", " & "));
        Assert.Equal("ROSÉ; Bruno Mars - APT.", StandardFileName.Build(L("ROSÉ", "Bruno Mars"), "APT.", "; "));
    }

    [Fact]
    public void WithoutASeparatorTheDefaultOneJoinsThem()
    {
        Assert.Equal("A; B - T", StandardFileName.Build(L("A", "B"), "T", null));
        Assert.Equal("A; B - T", StandardFileName.Build(L("A", "B"), "T", ""));
    }

    [Theory]
    [InlineData("K/DA", "POP/STARS", "K-DA - POP-STARS")]
    [InlineData("AC\\DC", "Back In Black", "AC-DC - Back In Black")]
    [InlineData("BLACKPINK", "STAY (Remix / JP Ver. / Live at Tokyo Dome, 2019/12/4)", "BLACKPINK - STAY (Remix - JP Ver. - Live at Tokyo Dome, 2019-12-4)")]
    public void ASlashBecomesADash(string artist, string title, string expected)
    {
        Assert.Equal(expected, StandardFileName.Build(L(artist), title, " & "));
    }

    [Fact]
    public void TheOtherCharactersWindowsForbidsAreLeftOutAndTheGapIsClosed()
    {
        Assert.Equal("ITZY - WANT IT", StandardFileName.Build(L("ITZY"), "WANT IT?", " & "));
        Assert.Equal("JENNIE & FKJ - Intro JANE (With FKJ)", StandardFileName.Build(L("JENNIE", "FKJ"), "Intro : JANE (With FKJ)", " & "));
        Assert.Equal("A - He said hello", StandardFileName.Build(L("A"), "He said \"hello\"", " & "));
        Assert.Equal("A - ab", StandardFileName.Build(L("A"), "a<b>|*", " & "));
        Assert.Equal("A - one two", StandardFileName.Build(L("A"), "one\ttwo", " & "));
    }

    [Fact]
    public void SpacesAtTheEndsAndRunsOfSpacesAreTidied()
    {
        Assert.Equal("A - B C", StandardFileName.Build(L("  A "), "  B    C  ", " & "));
    }

    [Fact]
    public void ADotAtTheEndOfTheTitleStays()
    {
        Assert.Equal("BLACKPINK - AS IF IT'S YOUR LAST -JP Ver.", StandardFileName.Build(L("BLACKPINK"), "AS IF IT'S YOUR LAST -JP Ver.", " & "));
    }

    [Fact]
    public void ChineseKoreanAndJapaneseAreKeptAsTheyAre()
    {
        Assert.Equal("G.E.M. 邓紫棋 - 自由的你", StandardFileName.Build(L("G.E.M. 邓紫棋"), "自由的你", " & "));
        Assert.Equal("SOYEON - 낭만 보내기", StandardFileName.Build(L("SOYEON"), "낭만 보내기", " & "));
    }

    [Fact]
    public void WithoutAnArtistOrATitleThereIsNoName()
    {
        Assert.Null(StandardFileName.Build(null, "T", " & "));
        Assert.Null(StandardFileName.Build(L(), "T", " & "));
        Assert.Null(StandardFileName.Build(L("  ", null), "T", " & "));
        Assert.Null(StandardFileName.Build(L("A"), null, " & "));
        Assert.Null(StandardFileName.Build(L("A"), "  ", " & "));
        Assert.Null(StandardFileName.Build(L("A"), "?*", " & ")); // nothing is left of it
    }

    [Fact]
    public void BlankArtistValuesAreSkipped()
    {
        Assert.Equal("A & B - T", StandardFileName.Build(L("A", " ", "B", null), "T", " & "));
    }
}
