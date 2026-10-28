using System.Globalization;
using System.Text;
using System.Text.Json;
using MusicManager.Common;
using MusicManager.Entity;

namespace MusicManager.DataAccess;

/// <summary>
/// the app's persisted preferences as a small JSON file (~/.musicmanager/settings.json): the last folder, the default
/// Convert LRC formats and the choices of the Options window, never the API key.
/// <para>
/// Reading never fails: a missing, empty or damaged file gives the defaults, and a file written by an older version
/// (no "standardizeOptions") keeps the keys it has. Each key is read on its own, so one damaged value does not take the
/// others down with it. Unknown keys are ignored. Writing goes through a temp file and a move, so a crash while saving
/// cannot leave half a file behind.
/// </para>
/// <para>
/// The file is the one the Java version wrote (Gson, pretty printed, two-space indent, "\n" line ends): the same keys,
/// the option names being the fields of StandardizeOptions and the mode written as its Java constant name.
/// </para>
/// </summary>
public sealed class SettingsStore
{
    // String.getBytes(UTF_8) writes an unpaired surrogate as '?', .NET's default would write U+FFFD
    private static readonly Encoding Utf8Writer = Encoding.GetEncoding("utf-8", new EncoderReplacementFallback("?"),
        DecoderFallback.ReplacementFallback);

    private readonly string _file;
    private readonly object _lock = new object();

    public SettingsStore(string file)
    {
        _file = file;
    }

    /// <summary>~/.musicmanager/settings.json</summary>
    public static SettingsStore AtDefaultLocation()
    {
        return new SettingsStore(Path.Combine(UserHome.Path, ".musicmanager", "settings.json"));
    }

    /// <summary>the saved settings; the defaults (empty path, no formats, default options) for whatever cannot be read</summary>
    public AppSettings Load()
    {
        lock (_lock)
        {
            string lastPath = "";
            List<string> formats = new List<string>();
            StandardizeOptions options = new StandardizeOptions();

            using (JsonDocument document = ReadObject())
            {
                if (document != null)
                {
                    JsonElement root = document.RootElement;
                    JsonElement? path = Find(root, "lastPath");
                    if (path != null && IsPrimitive(path.Value))
                    {
                        lastPath = PrimitiveText(path.Value);
                    }

                    JsonElement? array = Find(root, "defaultTargetFormats");
                    if (array != null && array.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement element in array.Value.EnumerateArray())
                        {
                            if (IsPrimitive(element))
                            {
                                formats.Add(PrimitiveText(element));
                            }
                        }
                    }

                    try
                    {
                        JsonElement? stored = Find(root, "standardizeOptions");
                        if (stored != null && stored.Value.ValueKind == JsonValueKind.Object)
                        {
                            options = ReadOptions(stored.Value);
                        }
                    }
                    catch (Exception)
                    {
                        options = new StandardizeOptions();
                    }
                }
            }
            return new AppSettings(lastPath, formats, options);
        }
    }

    /// <returns>false if the file could not be written (settings are a convenience, so this is not an error)</returns>
    public bool Save(AppSettings settings)
    {
        lock (_lock)
        {
            StringBuilder json = new StringBuilder();
            json.Append("{\n");
            json.Append("  \"lastPath\": ").Append(Quote(settings.LastPath ?? "")).Append(",\n");
            json.Append("  \"defaultTargetFormats\": ");
            List<string> formats = new List<string>();
            if (settings.DefaultTargetFormats != null)
            {
                foreach (string format in settings.DefaultTargetFormats)
                {
                    if (format != null)
                    {
                        formats.Add(format);
                    }
                }
            }
            if (formats.Count == 0)
            {
                json.Append("[]");
            }
            else
            {
                json.Append("[\n");
                for (int i = 0; i < formats.Count; i++)
                {
                    json.Append("    ").Append(Quote(formats[i])).Append(i < formats.Count - 1 ? ",\n" : "\n");
                }
                json.Append("  ]");
            }
            json.Append(",\n");
            json.Append("  \"standardizeOptions\": ");
            AppendOptions(json, settings.StandardizeOptions);
            json.Append("\n}");
            return WriteAtomically(_file, json + "\n");
        }
    }

    /// <summary>the fields of StandardizeOptions in declaration order; the API key is never written (it is transient in Java)</summary>
    private static void AppendOptions(StringBuilder json, StandardizeOptions options)
    {
        json.Append("{\n");
        json.Append("    \"mode\": ").Append(Quote(JavaName(options.GetMode()))).Append(",\n");
        json.Append("    \"lyrics\": ").Append(Flag(options.IsLyrics)).Append(",\n");
        json.Append("    \"romaji\": ").Append(Flag(options.IsRomaji)).Append(",\n");
        json.Append("    \"recycleBin\": ").Append(Flag(options.IsRecycleBin)).Append(",\n");
        json.Append("    \"artists\": ").Append(Flag(options.IsArtists)).Append(",\n");
        json.Append("    \"useClaudeForArtists\": ").Append(Flag(options.IsUseClaudeForArtists)).Append(",\n");
        json.Append("    \"albums\": ").Append(Flag(options.IsAlbums)).Append(",\n");
        json.Append("    \"useClaudeForAlbums\": ").Append(Flag(options.IsUseClaudeForAlbums)).Append(",\n");
        json.Append("    \"preferredFormat\": ").Append(Quote(options.PreferredFormat)).Append(",\n");
        json.Append("    \"separator\": ").Append(Quote(options.Separator)).Append(",\n");
        json.Append("    \"model\": ").Append(Quote(options.Model)).Append("\n");
        json.Append("  }");
    }

    private static string Flag(bool value)
    {
        return value ? "true" : "false";
    }

    // ---- reading the options the way Gson fills a StandardizeOptions ----

    /// <summary>
    /// a fresh StandardizeOptions with the keys of the object applied (keys that are missing keep their default, unknown
    /// and null keys are skipped), or an exception if one of the values has the wrong type, as Gson's does
    /// </summary>
    private static StandardizeOptions ReadOptions(JsonElement stored)
    {
        StandardizeOptions read = new StandardizeOptions();
        foreach (JsonProperty property in stored.EnumerateObject())
        {
            JsonElement value = property.Value;
            switch (property.Name)
            {
                case "mode":
                    read.SetMode(ReadMode(value));
                    break;
                case "lyrics":
                    read.IsLyrics = ReadFlag(value, read.IsLyrics);
                    break;
                case "romaji":
                    read.IsRomaji = ReadFlag(value, read.IsRomaji);
                    break;
                case "recycleBin":
                    read.IsRecycleBin = ReadFlag(value, read.IsRecycleBin);
                    break;
                case "artists":
                    read.IsArtists = ReadFlag(value, read.IsArtists);
                    break;
                case "useClaudeForArtists":
                    read.IsUseClaudeForArtists = ReadFlag(value, read.IsUseClaudeForArtists);
                    break;
                case "albums":
                    read.IsAlbums = ReadFlag(value, read.IsAlbums);
                    break;
                case "useClaudeForAlbums":
                    read.IsUseClaudeForAlbums = ReadFlag(value, read.IsUseClaudeForAlbums);
                    break;
                case "preferredFormat":
                    read.PreferredFormat = ReadText(value);
                    break;
                case "separator":
                    read.Separator = ReadText(value);
                    break;
                case "model":
                    read.Model = ReadText(value);
                    break;
                // "apiKey" is transient: never read, like any other unknown key
            }
        }
        return read;
    }

    /// <summary>a boolean, or the text "true" (any case) for true and any other text for false; null keeps the current value</summary>
    private static bool ReadFlag(JsonElement value, bool current)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
                return current;
            case JsonValueKind.String:
                return string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase);
            default:
                throw new JsonException("Expected a boolean but was " + value.ValueKind);
        }
    }

    /// <summary>a string; a number or boolean is read as its text, null as null</summary>
    private static string ReadText(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (IsPrimitive(value))
        {
            return PrimitiveText(value);
        }
        throw new JsonException("Expected a string but was " + value.ValueKind);
    }

    /// <summary>the mode of that name; an unknown name (or null) is the standardize mode</summary>
    private static StandardizeOptions.Mode? ReadMode(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
                return null;
            case JsonValueKind.String:
            case JsonValueKind.Number:
                string name = PrimitiveText(value);
                foreach (StandardizeOptions.Mode mode in Enum.GetValues<StandardizeOptions.Mode>())
                {
                    if (JavaName(mode) == name)
                    {
                        return mode;
                    }
                }
                return null;
            default:
                throw new JsonException("Expected a string but was " + value.ValueKind);
        }
    }

    /// <summary>the constant name Java prints for an enum value: Standardize is STANDARDIZE, ConvertedFromTtml is CONVERTED_FROM_TTML</summary>
    private static string JavaName(StandardizeOptions.Mode mode)
    {
        StringBuilder name = new StringBuilder();
        foreach (char c in mode.ToString())
        {
            if (char.IsUpper(c) && name.Length > 0)
            {
                name.Append('_');
            }
            name.Append(char.ToUpperInvariant(c));
        }
        return name.ToString();
    }

    private static JsonElement? Find(JsonElement root, string name)
    {
        JsonElement? found = null;
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (property.Name == name)
            {
                found = property.Value; // a repeated key: the last one wins
            }
        }
        return found;
    }

    private static bool IsPrimitive(JsonElement element)
    {
        JsonValueKind kind = element.ValueKind;
        return kind == JsonValueKind.String || kind == JsonValueKind.Number || kind == JsonValueKind.True
                || kind == JsonValueKind.False;
    }

    /// <summary>JsonPrimitive#getAsString: a number as written in the file, a boolean as true or false</summary>
    private static string PrimitiveText(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.True:
                return "true";
            case JsonValueKind.False:
                return "false";
            default:
                return element.GetRawText();
        }
    }

    /// <summary>the file's top-level object, or null if there is no usable one</summary>
    private JsonDocument ReadObject()
    {
        try
        {
            if (!File.Exists(_file))
            {
                return null;
            }
            // decoded by hand: bytes that are not UTF-8 become replacement characters instead of an exception
            string text = Encoding.UTF8.GetString(File.ReadAllBytes(_file));
            if (text.StartsWith('\uFEFF'))
            {
                text = text.Substring(1); // an editor's byte order mark
            }
            JsonDocument parsed = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            if (parsed.RootElement.ValueKind == JsonValueKind.Object)
            {
                return parsed;
            }
            parsed.Dispose();
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---- writing ----

    /// <summary>a JSON string literal the way Gson writes it without HTML escaping: only quotes, backslashes, control characters and U+2028/2029 are escaped</summary>
    private static string Quote(string text)
    {
        StringBuilder quoted = new StringBuilder(text.Length + 2);
        quoted.Append('"');
        foreach (char c in text)
        {
            switch (c)
            {
                case '"':
                    quoted.Append("\\\"");
                    break;
                case '\\':
                    quoted.Append("\\\\");
                    break;
                case '\t':
                    quoted.Append("\\t");
                    break;
                case '\b':
                    quoted.Append("\\b");
                    break;
                case '\n':
                    quoted.Append("\\n");
                    break;
                case '\r':
                    quoted.Append("\\r");
                    break;
                case '\f':
                    quoted.Append("\\f");
                    break;
                case '\u2028':
                    quoted.Append("\\u2028");
                    break;
                case '\u2029':
                    quoted.Append("\\u2029");
                    break;
                default:
                    if (c < ' ')
                    {
                        quoted.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        quoted.Append(c);
                    }
                    break;
            }
        }
        return quoted.Append('"').ToString();
    }

    /// <summary>
    /// writes text to file through a temp file in the same folder and a move, creating the folder if needed. Shared with
    /// the caches in this package, which need the same guarantee.
    /// </summary>
    /// <returns>false if anything went wrong (the old file is then left as it was)</returns>
    public static bool WriteAtomically(string file, string text)
    {
        string temp = null;
        try
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(file));
            Directory.CreateDirectory(directory);
            temp = Path.Combine(directory, Path.GetFileName(file) + Guid.NewGuid().ToString("N") + ".tmp");
            // GetBytes rather than a text writer: a stray unpaired surrogate becomes '?' instead of failing the whole write
            File.WriteAllBytes(temp, Utf8Writer.GetBytes(text));
            File.Move(temp, file, true);
            return true;
        }
        catch (Exception)
        {
            if (temp != null)
            {
                try
                {
                    File.Delete(temp);
                }
                catch (Exception)
                {
                    // nothing more to do
                }
            }
            return false;
        }
    }
}
