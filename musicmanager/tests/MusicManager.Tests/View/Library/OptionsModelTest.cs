using MusicManager.Entity;
using MusicManager.View.Library;
using Xunit;

namespace MusicManager.Tests.View.Library;

/// <summary>How the Options window's controls map to StandardizeOptions, and which controls depend on which.</summary>
public class OptionsModelTest
{
    [Fact]
    public void DefaultOptionsRoundTripThroughTheControls()
    {
        StandardizeOptions options = new StandardizeOptions();
        options.IsRecycleBin = true;
        options.Separator = " & ";
        options.Model = "claude-opus-5-5";
        options.ApiKey = "typed-key";

        StandardizeOptions back = OptionsModel.Values.From(options).ApplyTo(new StandardizeOptions());

        Assert.True(back.IsLyrics);
        Assert.True(back.IsRomaji);
        Assert.True(back.IsRecycleBin);
        Assert.True(back.IsArtists);
        Assert.True(back.IsUseClaudeForArtists);
        Assert.Equal(" & ", back.Separator);
        Assert.Equal("claude-opus-5-5", back.Model);
        Assert.Equal("typed-key", back.ApiKey);
    }

    [Fact]
    public void EveryTickIsCarriedOver()
    {
        OptionsModel.Values values = OptionsModel.Values.From(new StandardizeOptions());
        values.Lyrics = false;
        values.Romaji = false;
        values.RecycleBin = true;
        values.Artists = false;
        values.UseClaudeForArtists = false;

        StandardizeOptions options = values.ApplyTo(new StandardizeOptions());

        Assert.False(options.IsLyrics);
        Assert.False(options.IsRomaji);
        Assert.True(options.IsRecycleBin);
        Assert.False(options.IsArtists);
        Assert.False(options.IsUseClaudeForArtists);
    }

    [Fact]
    public void WhatTheWindowDoesNotShowIsKeptAndTheBaseIsNotChanged()
    {
        StandardizeOptions baseOptions = new StandardizeOptions();
        baseOptions.SetMode(StandardizeOptions.Mode.Convert);
        OptionsModel.Values values = OptionsModel.Values.From(baseOptions);
        values.Lyrics = false;

        StandardizeOptions options = values.ApplyTo(baseOptions);

        Assert.Equal(StandardizeOptions.Mode.Convert, options.GetMode());
        Assert.True(baseOptions.IsLyrics, "the base options are edited on a copy");
    }

    [Fact]
    public void BlankModelAndSeparatorFallBackToTheDefaultsAndTheKeyIsTrimmed()
    {
        OptionsModel.Values values = OptionsModel.Values.From(new StandardizeOptions());
        values.Model = "   ";
        values.Separator = "";
        values.ApiKey = "  sk-123 \n";

        StandardizeOptions options = values.ApplyTo(new StandardizeOptions());

        Assert.Equal(StandardizeOptions.DefaultModel, options.Model);
        Assert.Equal(StandardizeOptions.DefaultSeparator, options.Separator);
        Assert.Equal("sk-123", options.ApiKey);
    }

    [Fact]
    public void ATypedModelIsTrimmed()
    {
        OptionsModel.Values values = OptionsModel.Values.From(new StandardizeOptions());
        values.Model = "  claude-haiku-5-5 ";
        Assert.Equal("claude-haiku-5-5", values.ApplyTo(new StandardizeOptions()).Model);
    }

    // ---- which controls depend on which

    [Fact]
    public void RomajiAndRecycleBinNeedLyrics()
    {
        OptionsModel.Values values = OptionsModel.Values.From(new StandardizeOptions());
        values.Lyrics = false;
        OptionsModel.Enabled enabled = values.Enabled();
        Assert.False(enabled.Romaji);
        Assert.False(enabled.RecycleBin);
        Assert.True(enabled.UseClaudeForArtists);
        Assert.True(enabled.Separator);
    }

    [Fact]
    public void TheClaudeOptionAndSeparatorNeedArtists()
    {
        OptionsModel.Values values = OptionsModel.Values.From(new StandardizeOptions());
        values.Artists = false;
        OptionsModel.Enabled enabled = values.Enabled();
        Assert.False(enabled.UseClaudeForArtists);
        Assert.False(enabled.Separator);
        Assert.True(enabled.Romaji);
        Assert.True(enabled.RecycleBin);
    }

    [Fact]
    public void TheKeyAndModelAreOnlyNeededWhenSomethingUsesClaude()
    {
        OptionsModel.Values values = OptionsModel.Values.From(new StandardizeOptions());
        Assert.True(values.Enabled().ClaudeSettings);

        values.Romaji = false; // artists still use Claude
        Assert.True(values.Enabled().ClaudeSettings);

        values.UseClaudeForArtists = false; // so do the albums
        Assert.True(values.Enabled().ClaudeSettings);

        values.UseClaudeForAlbums = false;
        Assert.False(values.Enabled().ClaudeSettings);

        values.Romaji = true; // romaji does
        Assert.True(values.Enabled().ClaudeSettings);

        values.Lyrics = false; // but only while lyrics are on
        Assert.False(values.Enabled().ClaudeSettings);
    }

    // ---- the separator list

    [Fact]
    public void AKnownSeparatorSelectsItsEntry()
    {
        Assert.Same(OptionsModel.Separators[0], OptionsModel.ChoiceFor("; "));
        Assert.Same(OptionsModel.Separators[3], OptionsModel.ChoiceFor(" / "));
    }

    [Fact]
    public void AnUnknownSeparatorIsKeptRatherThanReplaced()
    {
        OptionsModel.SeparatorChoice choice = OptionsModel.ChoiceFor(" | ");
        Assert.Equal(" | ", choice.Value());
        Assert.Contains("|", choice.ToString());
        Assert.DoesNotContain(choice, OptionsModel.Separators);
    }

    [Fact]
    public void EverySeparatorEntryShowsTheTextItUses()
    {
        foreach (OptionsModel.SeparatorChoice choice in OptionsModel.Separators)
        {
            Assert.True(choice.ToString().Contains("aespa" + choice.Value() + "Becky G"), choice + " should show an example");
        }
    }

    // ---- Convert formats (the deprecated mode)

    [Fact]
    public void FormatsAreListedInTheUseCasesOrderWhateverTheTickOrder()
    {
        Assert.Equal(new List<string> { "TTML", "LRC" }, OptionsModel.SelectedFormats(new HashSet<string> { "LRC", "TTML" }));
        Assert.Equal(new List<string>(), OptionsModel.SelectedFormats(new HashSet<string>()));
    }

    [Fact]
    public void NoSavedFormatsMeansEveryFormatIsTicked()
    {
        Assert.Equal(new List<string> { "TTML", "HMRC", "LRC" }, OptionsModel.FormatsToTick(null));
        Assert.Equal(new List<string> { "TTML", "HMRC", "LRC" }, OptionsModel.FormatsToTick(new List<string>()));
        Assert.Equal(new List<string> { "LRC" }, OptionsModel.FormatsToTick(new List<string> { "LRC" }));
    }

    [Fact]
    public void FormatLabelsSayWhichAreDeprecated()
    {
        Assert.Equal("TTML (deprecated)", OptionsModel.FormatLabel("TTML"));
        Assert.Equal("HMRC (deprecated)", OptionsModel.FormatLabel("HMRC"));
        Assert.Equal("LRC (line-synced)", OptionsModel.FormatLabel("LRC"));
        Assert.Equal("XYZ", OptionsModel.FormatLabel("XYZ"));
    }

    // ---- Add: the format to prefer

    [Fact]
    public void TheDropDownOffersEveryFormatFlacFirst()
    {
        Assert.Equal(new[] { "FLAC", "WAV", "MP3", "M4A", "OGG", "AAC" }, OptionsModel.PreferredFormats.Select(c => c.ToString()).ToArray());
        Assert.Equal(new[] { "flac", "wav", "mp3", "m4a", "ogg", "aac" }, OptionsModel.PreferredFormats.Select(c => c.Extension).ToArray());
    }

    [Fact]
    public void ASavedFormatSelectsItsEntryAndAnUnknownOneIsFlac()
    {
        Assert.Same(OptionsModel.PreferredFormats[1], OptionsModel.FormatChoiceFor("wav"));
        Assert.Same(OptionsModel.PreferredFormats[2], OptionsModel.FormatChoiceFor(".MP3"));
        Assert.Same(OptionsModel.PreferredFormats[0], OptionsModel.FormatChoiceFor("opus"));
        Assert.Same(OptionsModel.PreferredFormats[0], OptionsModel.FormatChoiceFor(null));
    }

    [Fact]
    public void ThePreferredFormatTravelsThroughTheControls()
    {
        StandardizeOptions options = new StandardizeOptions();
        options.PreferredFormat = "wav";

        OptionsModel.Values values = OptionsModel.Values.From(options);
        Assert.Equal("wav", values.PreferredFormat);

        values.PreferredFormat = "MP3";
        Assert.Equal("mp3", values.ApplyTo(options).PreferredFormat);

        values.PreferredFormat = null; // nothing selected in the box
        Assert.Equal("flac", values.ApplyTo(options).PreferredFormat);
    }

    [Fact]
    public void TheAlbumControlsTravelThroughTheValuesToo()
    {
        StandardizeOptions options = new StandardizeOptions();
        options.IsAlbums = false;
        options.IsUseClaudeForAlbums = false;

        OptionsModel.Values values = OptionsModel.Values.From(options);
        Assert.False(values.Albums);
        Assert.False(values.UseClaudeForAlbums);
        Assert.False(values.Enabled().UseClaudeForAlbums); // greyed out while albums are off

        values.Albums = true;
        values.UseClaudeForAlbums = true;
        StandardizeOptions applied = values.ApplyTo(options);
        Assert.True(applied.IsAlbums);
        Assert.True(applied.IsUseClaudeForAlbums);
        Assert.True(values.Enabled().UseClaudeForAlbums);
    }
}
