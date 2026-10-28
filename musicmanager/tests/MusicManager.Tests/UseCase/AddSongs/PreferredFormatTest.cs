using MusicManager.UseCase.AddSongs;
using Xunit;

namespace MusicManager.Tests.UseCase.AddSongs;

/// <summary>Not a port: which version of a song the Add button takes when it is there in several formats.</summary>
public class PreferredFormatTest
{
    private static int Choose(string preferred, params (string Extension, long Size)[] versions)
    {
        return PreferredFormat.Choose(versions, preferred);
    }

    [Fact]
    public void ThePreferredFormatIsTakenWhateverTheSizes()
    {
        Assert.Equal(1, Choose("flac", (".mp3", 9_000_000), (".flac", 1_000)));
        Assert.Equal(0, Choose("mp3", (".mp3", 10), (".flac", 99_000_000)));
        Assert.Equal(2, Choose("wav", (".flac", 30), (".mp3", 5), (".wav", 4)));
    }

    [Fact]
    public void OfSeveralCopiesInThePreferredFormatTheLargestIsTaken()
    {
        // a standard FLAC and a high-resolution master FLAC of one song, and a WAV bigger than both
        Assert.Equal(1, Choose("flac", (".flac", 52_000_000), (".flac", 171_000_000), (".wav", 300_000_000), (".mp3", 10_000_000)));
        Assert.Equal(0, Choose("flac", (".flac", 171_000_000), (".flac", 52_000_000)));
    }

    [Fact]
    public void FlacBeatsWavWhenFlacIsPreferred()
    {
        Assert.Equal(0, Choose("flac", (".flac", 25_000_000), (".wav", 60_000_000)));
    }

    [Fact]
    public void WithNoneInThePreferredFormatTheLargestFileIsTaken()
    {
        // wav is preferred, the folder has only flac and mp3
        Assert.Equal(0, Choose("wav", (".flac", 30_000_000), (".mp3", 8_000_000)));
        Assert.Equal(1, Choose("wav", (".mp3", 8_000_000), (".flac", 30_000_000)));
        Assert.Equal(1, Choose("wav", (".flac", 3_000_000), (".mp3", 8_000_000)));
    }

    [Fact]
    public void OnATieOfSizesTheBetterFormatIsTaken()
    {
        Assert.Equal(1, Choose("ogg", (".mp3", 100), (".flac", 100)));
        Assert.Equal(0, Choose("ogg", (".flac", 100), (".wav", 100)));
        Assert.Equal(1, Choose("ogg", (".xyz", 100), (".mp3", 100))); // an unknown format ranks last
    }

    [Fact]
    public void ACaseOrADotMakesNoDifference()
    {
        Assert.Equal(1, Choose(".FLAC", (".MP3", 9), (".Flac", 1)));
        Assert.Equal(0, Choose(" Mp3 ", (".mp3", 1), (".flac", 2)));
    }

    [Fact]
    public void ABlankOrUnknownPreferenceMeansFlac()
    {
        Assert.Equal(1, Choose(null, (".mp3", 9), (".flac", 1)));
        Assert.Equal(1, Choose("", (".mp3", 9), (".flac", 1)));
        Assert.Equal(1, Choose("opus", (".mp3", 9), (".flac", 1)));
    }

    [Fact]
    public void ASingleVersionIsItself()
    {
        Assert.Equal(0, Choose("wav", (".mp3", 5)));
    }

    [Theory]
    [InlineData("flac", "flac")]
    [InlineData("FLAC", "flac")]
    [InlineData(".wav", "wav")]
    [InlineData("  Mp3 ", "mp3")]
    [InlineData("m4a", "m4a")]
    [InlineData("opus", "flac")]
    [InlineData("", "flac")]
    [InlineData(null, "flac")]
    public void TheFormatIsSavedAsALowerCaseExtension(string format, string expected)
    {
        Assert.Equal(expected, PreferredFormat.Normalize(format));
    }

    [Fact]
    public void EveryFormatTheAppReadsCanBePreferred()
    {
        Assert.Equal(new[] { "flac", "wav", "mp3", "m4a", "ogg", "aac" }, PreferredFormat.All.ToArray());
        Assert.Equal("flac", PreferredFormat.Default);
    }
}
