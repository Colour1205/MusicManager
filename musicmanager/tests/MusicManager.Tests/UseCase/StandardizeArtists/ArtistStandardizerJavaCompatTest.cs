using System.Text;
using MusicManager.Entity;
using MusicManager.UseCase.StandardizeArtists;
using Xunit;

namespace MusicManager.Tests.UseCase.StandardizeArtists;

/// <summary>
/// Not a port of a Java test: pins the places where .NET text handling differs from Java's and ArtistStandardizer
/// reproduces what the Java code answers (the expected values were taken from running the Java ArtistStandardizer).
/// </summary>
public class ArtistStandardizerJavaCompatTest
{
    /// <summary>a string from code points; a surrogate code point stays a single (possibly lone) char</summary>
    private static string S(params int[] codePoints)
    {
        StringBuilder sb = new StringBuilder();
        foreach (int cp in codePoints)
        {
            if (cp >= 0xD800 && cp <= 0xDFFF)
            {
                sb.Append((char)cp);
            }
            else
            {
                sb.Append(char.ConvertFromUtf32(cp));
            }
        }
        return sb.ToString();
    }

    private static List<string> TextsOf(string raw)
    {
        var result = ArtistStandardizer.ParseLocally(new List<string> { raw });
        return result.Values.First().Select(a => a.Text).ToList();
    }

    private static List<string> NamesOf(params string[] artists)
    {
        List<SongArtistTags> songs = new List<SongArtistTags>();
        for (int i = 0; i < artists.Length; i++)
        {
            songs.Add(new SongArtistTags("p" + i, new List<string> { artists[i] }, new List<string>(), new List<string>()));
        }
        ArtistPlan plan = ArtistStandardizer.BuildPlan(songs, new Dictionary<string, List<ParsedArtist>>(), null, false);
        return plan.Groups.Select(g => g.CanonicalName).ToList();
    }

    // Java lower-cases a capital sigma at the end of a word to the final sigma, deciding "word" with its BreakIterator
    [Fact]
    public void ACapitalSigmaBecomesTheFinalSigmaAtTheEndOfAWord()
    {
        Assert.Equal(S(0x3C3, 0x3B1, 0x3C2), ArtistStandardizer.LooseKey(S(0x3A3, 0x391, 0x3A3)));
        Assert.Equal(ArtistStandardizer.LooseKey(S(0x3C3, 0x3B1, 0x3C2)), ArtistStandardizer.LooseKey(S(0x3A3, 0x391, 0x3A3)));
        Assert.Equal(S(0x3C3), ArtistStandardizer.LooseKey(S(0x3A3)));
        // a space, a Han character or a comma ends the word, so the sigma is final
        Assert.Equal(S(0x3C3, 0x3B1, 0x3C2) + "au", ArtistStandardizer.LooseKey(S(0x3A3, 0x391, 0x3A3, 0x20, 0x41, 0x75)));
        Assert.Equal(S(0x3C3, 0x3B1, 0x3C2, 0x4E2D) + "au", ArtistStandardizer.LooseKey(S(0x3A3, 0x391, 0x3A3, 0x4E2D, 0x41, 0x75)));
        Assert.Equal(S(0x3C3, 0x3B1, 0x3C2) + "au", ArtistStandardizer.LooseKey(S(0x3A3, 0x391, 0x3A3, 0x2C, 0x41, 0x75)));
        // letters of any script, and a hyphen between letters, keep the word going: a cased letter after the sigma
        Assert.Equal(S(0x3C3, 0x3B1, 0x3C3) + "au", ArtistStandardizer.LooseKey(S(0x3A3, 0x391, 0x3A3, 0x41, 0x75)));
        Assert.Equal(S(0x3C3, 0x3B1, 0x3C3) + "au", ArtistStandardizer.LooseKey(S(0x3A3, 0x391, 0x3A3, 0x2D, 0x41, 0x75)));
        Assert.Equal(S(0x3C3, 0x3B1, 0x3C3, 0x1112, 0x1161, 0x11AB, 0x1100, 0x116E, 0x11A8) + "au",
                ArtistStandardizer.LooseKey(S(0x3A3, 0x391, 0x3A3, 0xD55C, 0xAD6D, 0x41, 0x75)));
    }

    [Fact]
    public void ACapitalDottedIGivesAnIAndASeparateDotThatTheKeyDrops()
    {
        Assert.Equal("is", ArtistStandardizer.LooseKey(S(0x130, 0x73)));
    }

    // the look-behind of the Java regex sees one char, the look-ahead a whole code point
    [Fact]
    public void AProtectedNameAfterALetterOutsideTheBmpIsStillProtected()
    {
        Assert.Equal(new List<string> { S(0x20BB7) + "Tyler, The Creator" }, TextsOf(S(0x20BB7) + "Tyler, The Creator"));
        Assert.Equal(new List<string> { S(0x1D400) + "K/DA" }, TextsOf(S(0x1D400) + "K/DA"));
        Assert.Equal(new List<string> { "xTyler", "The Creator" }, TextsOf("xTyler, The Creator"));
    }

    [Fact]
    public void AProtectedNameBeforeALetterOutsideTheBmpIsNot()
    {
        Assert.Equal(new List<string> { "Tyler", "The Creator" + S(0x20BB7) }, TextsOf("Tyler, The Creator" + S(0x20BB7)));
    }

    // String.CASE_INSENSITIVE_ORDER compares code points; the UTF-16 order would put the emoji (D83C) before U+FF08
    [Fact]
    public void GroupsAreSortedByCodePointsNotByUtf16Units()
    {
        Assert.Equal(new List<string> { S(0xFF08, 0x41), S(0x1F3B5) }, NamesOf(S(0x1F3B5), S(0xFF08, 0x41)));
    }

    // ... and by the lower case of the upper case of each character, so that "_" sorts before "b"
    [Fact]
    public void GroupsAreSortedByTheLowerCaseOfTheUpperCase()
    {
        Assert.Equal(new List<string> { "a_c", "ab" }, NamesOf("ab", "a_c"));
    }

    // string.Normalize throws on U+FFFE and on a lone surrogate, Java's Normalizer lets them through
    [Fact]
    public void NormalizationLetsUnnormalizableCharactersThrough()
    {
        Assert.Equal("a" + S(0xFFFE) + "b", ArtistStandardizer.NormalizeRaw("a" + S(0xFFFE) + "b"));
        Assert.Equal("ab", ArtistStandardizer.LooseKey("a" + S(0xFFFE) + "b"));
        Assert.Equal("e" + S(0xD83C, 0x301), ArtistStandardizer.NormalizeRaw("e" + S(0xD83C, 0x301)));
        Assert.Equal("e", ArtistStandardizer.LooseKey("E" + S(0xD83C, 0x301)));
    }
}
