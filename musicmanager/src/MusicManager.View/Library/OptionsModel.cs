using MusicManager.Common;
using MusicManager.Entity;
using MusicManager.UseCase.ConvertLRC;
using AddSongsFormat = MusicManager.UseCase.AddSongs.PreferredFormat; // (OptionsModel.Values has a property of the same name)

namespace MusicManager.View.Library;

/// <summary>
/// What the Options window's controls mean, without any UI: the choices a user can make, which controls depend on
/// which, and how the controls turn into StandardizeOptions and into the Convert formats.
/// </summary>
public static class OptionsModel
{
    public static readonly string[] Models = { "claude-sonnet-5-5", "claude-opus-5-5", "claude-haiku-5-5" };

    /// <summary>One entry of the separator drop-down: a label with an example, and the text that joins the artists.</summary>
    public sealed class SeparatorChoice
    {
        private readonly string _label;
        private readonly string _value;

        public SeparatorChoice(string label, string value)
        {
            _label = label;
            _value = value;
        }

        public string Value()
        {
            return _value;
        }

        public override string ToString()
        {
            return _label;
        }
    }

    /// <summary>One entry of the "Prefer" drop-down of Add: the format's name, and the extension that is saved.</summary>
    public sealed class FormatChoice
    {
        public FormatChoice(string extension)
        {
            Extension = extension;
        }

        public string Extension { get; }

        public override string ToString()
        {
            return Extension.ToUpperInvariant();
        }
    }

    /// <summary>The formats Add can prefer, in the order of the drop-down.</summary>
    public static readonly IReadOnlyList<FormatChoice> PreferredFormats =
            AddSongsFormat.All.Select(extension => new FormatChoice(extension)).ToList().AsReadOnly();

    /// <summary>The drop-down entry for a saved format (an unknown one is the default, FLAC).</summary>
    public static FormatChoice FormatChoiceFor(string extension)
    {
        string wanted = AddSongsFormat.Normalize(extension);
        return PreferredFormats.First(choice => choice.Extension == wanted);
    }

    public static readonly IReadOnlyList<SeparatorChoice> Separators = new List<SeparatorChoice>
    {
        new SeparatorChoice("Semicolon     aespa; Becky G", "; "),
        new SeparatorChoice("Ampersand     aespa & Becky G", " & "),
        new SeparatorChoice("Comma     aespa, Becky G", ", "),
        new SeparatorChoice("Slash     aespa / Becky G", " / ")
    }.AsReadOnly();

    /// <summary>The drop-down entry for a saved separator; one that is not in the list (an edited settings file) is kept as it is.</summary>
    public static SeparatorChoice ChoiceFor(string separator)
    {
        foreach (SeparatorChoice choice in Separators)
        {
            if (choice.Value() == separator)
            {
                return choice;
            }
        }
        return new SeparatorChoice("Custom     aespa" + separator + "Becky G", separator);
    }

    /// <summary>The formats of the deprecated Convert mode, with the labels shown for them (same order as the use case's list).</summary>
    public static string FormatLabel(string format)
    {
        switch (format)
        {
            case "TTML":
                return "TTML (deprecated)";
            case "HMRC":
                return "HMRC (deprecated)";
            case "LRC":
                return "LRC (line-synced)";
            default:
                return format;
        }
    }

    /// <summary>The formats to tick when the settings say none yet: all of them, like the old Convert LRC window.</summary>
    public static List<string> FormatsToTick(List<string> saved)
    {
        return ConvertLRCInputData.OrDefaultFormats(saved);
    }

    /// <summary>The state of the Standardize controls.</summary>
    public sealed class Values
    {
        public bool Lyrics { get; set; }
        public bool Romaji { get; set; }
        public bool RecycleBin { get; set; }
        public bool Artists { get; set; }
        public bool UseClaudeForArtists { get; set; }
        public bool Albums { get; set; }
        public bool UseClaudeForAlbums { get; set; }
        public string PreferredFormat { get; set; }
        public string Separator { get; set; }
        public string Model { get; set; }
        public string ApiKey { get; set; }

        public static Values From(StandardizeOptions options)
        {
            var values = new Values();
            values.Lyrics = options.IsLyrics;
            values.Romaji = options.IsRomaji;
            values.RecycleBin = options.IsRecycleBin;
            values.Artists = options.IsArtists;
            values.UseClaudeForArtists = options.IsUseClaudeForArtists;
            values.Albums = options.IsAlbums;
            values.UseClaudeForAlbums = options.IsUseClaudeForAlbums;
            values.PreferredFormat = options.PreferredFormat;
            values.Separator = options.Separator;
            values.Model = options.Model;
            values.ApiKey = options.ApiKey;
            return values;
        }

        /// <summary>The options these controls make, on top of a copy of base (which keeps what the window does not show, the mode).</summary>
        public StandardizeOptions ApplyTo(StandardizeOptions baseOptions)
        {
            StandardizeOptions options = baseOptions.Copy();
            options.IsLyrics = Lyrics;
            options.IsRomaji = Romaji;
            options.IsRecycleBin = RecycleBin;
            options.IsArtists = Artists;
            options.IsUseClaudeForArtists = UseClaudeForArtists;
            options.IsAlbums = Albums;
            options.IsUseClaudeForAlbums = UseClaudeForAlbums;
            options.PreferredFormat = AddSongsFormat.Normalize(PreferredFormat);
            options.Separator =string.IsNullOrEmpty(Separator) ? StandardizeOptions.DefaultSeparator : Separator;
            options.Model = Model == null || JavaString.IsBlank(Model) ? StandardizeOptions.DefaultModel : JavaString.Trim(Model);
            options.ApiKey = ApiKey == null ? "" : JavaString.Trim(ApiKey);
            return options;
        }

        /// <summary>Which controls can be used, given the ticks: a control that has no effect is greyed out.</summary>
        public OptionsModel.Enabled Enabled()
        {
            bool claude = (Lyrics && Romaji) || (Artists && UseClaudeForArtists) || (Albums && UseClaudeForAlbums);
            return new OptionsModel.Enabled(Lyrics, Lyrics, Artists, Artists, claude, Albums);
        }
    }

    /// <summary>Which controls of the Standardize cards are enabled.</summary>
    public sealed class Enabled
    {
        public bool Romaji { get; }
        public bool RecycleBin { get; }
        public bool UseClaudeForArtists { get; }
        public bool Separator { get; }
        public bool ClaudeSettings { get; } // the API key and the model
        public bool UseClaudeForAlbums { get; }

        public Enabled(bool romaji, bool recycleBin, bool useClaudeForArtists, bool separator, bool claudeSettings)
            : this(romaji, recycleBin, useClaudeForArtists, separator, claudeSettings, false)
        {
        }

        public Enabled(bool romaji, bool recycleBin, bool useClaudeForArtists, bool separator, bool claudeSettings,
                bool useClaudeForAlbums)
        {
            Romaji = romaji;
            RecycleBin = recycleBin;
            UseClaudeForArtists = useClaudeForArtists;
            Separator = separator;
            ClaudeSettings = claudeSettings;
            UseClaudeForAlbums = useClaudeForAlbums;
        }
    }

    /// <summary>The ticked formats, in the use case's order.</summary>
    public static List<string> SelectedFormats(IEnumerable<string> ticked)
    {
        var tickedSet = new HashSet<string>(ticked);
        var formats = new List<string>();
        foreach (string format in ConvertLRCInputData.AvailableFormats)
        {
            if (tickedSet.Contains(format))
            {
                formats.Add(format);
            }
        }
        return formats;
    }
}
