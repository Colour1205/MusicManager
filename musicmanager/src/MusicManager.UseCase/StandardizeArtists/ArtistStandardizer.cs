using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MusicManager.Common;
using MusicManager.Entity;

namespace MusicManager.UseCase.StandardizeArtists;

/// <summary>
/// The pure logic of "Standardize artists" (no I/O, no AI, no UI): groups the
/// spellings of every artist found in the tags, picks one name per artist, and
/// works out which tags of which song change. The AI only supplies the "parse"
/// (which artists are inside each raw tag value and what their English name is);
/// everything decided here is deterministic, so the user can review it.
/// </summary>
public static class ArtistStandardizer
{
    public const string DefaultSeparator = "; ";

    private const string JoinForPreview = " | ";

    // what Java's \s matches (ASCII white space only; .NET's \s is Unicode-wide)
    private const string WsClass = @"[ \t\n\x0B\f\r]";

    // names that contain a separator character and must never be split
    private static readonly List<Regex> ProtectedNames = CompileProtected("K/DA", "HUNTR/X", "AC/DC",
            "Tyler, The Creator", "Earth, Wind & Fire", "Simon & Garfunkel", "Mumford & Sons",
            "Florence + the Machine", "Years & Years", "Hall & Oates",
            // "X" would otherwise look like the " x " collaboration separator
            "Lil Nas X");

    // "[重复]", "(duplicate)", ...: markers an old tool left behind in artist names (also in full-width brackets)
    private static readonly Regex JunkMarker = new Regex(
            @"[\[(\uFF08\uFF3B\u3010]" + WsClass + @"*(?:\u91CD\u590D|\u91CD\u8907|duplicates?|dupe?|repeated?)" + WsClass
                    + @"*[\])\uFF09\uFF3D\u3011]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // the " & " case is covered by the bare "&"
    private static readonly string[] SpacedSeparators = { " feat. ", " ft. ", " / ", " x " };

    // a comma left at the edge of a piece ("IU, Suga,") separates nothing
    private static readonly Regex DanglingCommas = new Regex(@"^[, \t\n\x0B\f\r]+|[, \t\n\x0B\f\r]+$");

    private static readonly Regex EmptyBrackets = new Regex(
            @"[\[(\uFF08\uFF3B]" + WsClass + @"*[\])\uFF09\uFF3D]");

    // ------------------------------------------------------------------ text helpers

    /// <summary>
    /// Unicode NFC, trimmed, every whitespace run (NBSP and full-width space included)
    /// collapsed to one space. Invisible characters (zero-width space, BOM) are dropped,
    /// because they would make two identical-looking names differ.
    /// </summary>
    public static string NormalizeRaw(string s)
    {
        if (s == null)
        {
            return "";
        }
        StringBuilder sb = new StringBuilder(s.Length);
        bool pendingSpace = false;
        for (int i = 0; i < s.Length;)
        {
            int cp = CodePointAt(s, i);
            i += CharCount(cp);
            if (cp == 0x200B || cp == 0x2060 || cp == 0xFEFF)
            {
                continue;
            }
            // NUL separates the values of one ID3v2.4 frame and pads some tags, so it is a space, never glue
            if (IsWhitespaceOrSpaceChar(cp) || cp == 0x0085 || cp == 0x0000)
            {
                pendingSpace = sb.Length > 0; // leading whitespace is dropped, trailing never flushed
                continue;
            }
            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }
            AppendCodePoint(sb, cp);
        }
        // composed last, so dropping an invisible character between a letter and its accent cannot leave it decomposed
        return Normalize(sb.ToString(), NormalizationForm.FormC);
    }

    /// <summary>
    /// true iff the name has at least one letter and every letter is Latin script
    /// (accents are fine; digits, punctuation and symbols are ignored).
    /// </summary>
    public static bool IsEnglishOnly(string name)
    {
        if (name == null)
        {
            return false;
        }
        bool sawLetter = false;
        for (int i = 0; i < name.Length;)
        {
            int cp = CodePointAt(name, i);
            i += CharCount(cp);
            if (!IsLetter(cp))
            {
                continue;
            }
            if (!IsLatin(cp))
            {
                return false;
            }
            sawLetter = true;
        }
        return sawLetter;
    }

    /// <summary>
    /// The identity of an artist name: accents, case, punctuation and spacing are
    /// ignored ("ROSÉ" and "Rose" give "rose", "K/DA" and "K-DA" give "kda"). Letters and
    /// digits of every script are kept.
    /// </summary>
    public static string LooseKey(string name)
    {
        if (name == null)
        {
            return "";
        }
        string decomposed = ToLowerRoot(Normalize(name, NormalizationForm.FormKD));
        StringBuilder sb = new StringBuilder(decomposed.Length);
        for (int i = 0; i < decomposed.Length;)
        {
            int cp = CodePointAt(decomposed, i);
            i += CharCount(cp);
            if (IsAccentMark(cp))
            {
                continue;
            }
            // marks outside the accent blocks (kana dakuten, Indic vowel signs) are part of the
            // letter they sit on, so dropping them would merge different names
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(cp);
            if (IsLetter(category) || category == UnicodeCategory.DecimalDigitNumber
                    || category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.SpacingCombiningMark)
            {
                AppendCodePoint(sb, cp);
            }
        }
        return sb.ToString();
    }

    private static bool IsAccentMark(int cp)
    {
        return (cp >= 0x0300 && cp <= 0x036F) || (cp >= 0x1AB0 && cp <= 0x1AFF) || (cp >= 0x1DC0 && cp <= 0x1DFF)
                || (cp >= 0x20D0 && cp <= 0x20FF) || (cp >= 0xFE20 && cp <= 0xFE2F);
    }

    // ------------------------------------------------------------------ counting

    /// <summary>
    /// NormalizeRaw(value) -> number of tag values using it, over every value of
    /// ARTIST, ALBUMARTIST and ARTISTS of every song; blanks skipped; most used first.
    /// </summary>
    public static OrderedDictionary<string, int> CountRawValues(IEnumerable<SongArtistTags> songs)
    {
        Dictionary<string, int> counts = new Dictionary<string, int>();
        if (songs != null)
        {
            foreach (SongArtistTags song in songs)
            {
                if (song == null)
                {
                    continue;
                }
                CountValues(song.Artist, counts);
                CountValues(song.AlbumArtist, counts);
                CountValues(song.Artists, counts);
            }
        }
        List<KeyValuePair<string, int>> entries = new List<KeyValuePair<string, int>>(counts);
        entries.Sort((a, b) => a.Value == b.Value ? string.CompareOrdinal(a.Key, b.Key) : b.Value.CompareTo(a.Value));
        OrderedDictionary<string, int> sorted = new OrderedDictionary<string, int>(entries.Count);
        foreach (KeyValuePair<string, int> entry in entries)
        {
            sorted.Add(entry.Key, entry.Value);
        }
        return sorted;
    }

    private static void CountValues(IEnumerable<string> values, Dictionary<string, int> counts)
    {
        if (values == null)
        {
            return;
        }
        foreach (string value in values)
        {
            string raw = NormalizeRaw(value);
            if (raw.Length != 0)
            {
                counts[raw] = counts.TryGetValue(raw, out int current) ? current + 1 : 1;
            }
        }
    }

    // ------------------------------------------------------------------ local parsing (no AI)

    /// <summary>
    /// The no-AI fallback: splits each raw value into artists with conservative rules
    /// and strips junk and translations from the English name. It cannot translate
    /// names, so anything that is not plain Latin text is flagged low confidence.
    /// </summary>
    public static OrderedDictionary<string, List<ParsedArtist>> ParseLocally(IEnumerable<string> raws)
    {
        OrderedDictionary<string, List<ParsedArtist>> result = new OrderedDictionary<string, List<ParsedArtist>>();
        if (raws == null)
        {
            return result;
        }
        foreach (string rawValue in raws)
        {
            string raw = NormalizeRaw(rawValue);
            if (raw.Length == 0 || result.ContainsKey(raw))
            {
                continue;
            }
            List<ParsedArtist> artists = new List<ParsedArtist>();
            foreach (string piece in SplitPieces(raw))
            {
                artists.Add(new ParsedArtist(piece, EnglishName(piece), !IsEnglishOnly(piece)));
            }
            result.Add(raw, artists);
        }
        return result;
    }

    private static List<Regex> CompileProtected(params string[] names)
    {
        List<Regex> patterns = new List<Regex>();
        foreach (string name in names)
        {
            patterns.Add(new Regex(Regex.Escape(name), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        }
        return patterns;
    }

    /// <summary>the artist pieces of one (already normalized) raw value, trimmed and never empty</summary>
    private static List<string> SplitPieces(string raw)
    {
        bool[] locked = new bool[raw.Length]; // characters inside a protected name
        foreach (Regex pattern in ProtectedNames)
        {
            for (int from = 0; from < raw.Length;)
            {
                Match match = pattern.Match(raw, from);
                if (!match.Success)
                {
                    break;
                }
                int end = match.Index + match.Length;
                // whole-word match so "BAC/DC" or "Hall & Oatesy" are not protected by accident. The Java regex asks for no
                // letter or digit around the name with look-arounds: the look-ahead sees a whole code point, but the
                // look-behind only the char before the name, so a letter outside the BMP in front does not count
                if ((match.Index == 0 || !IsLetterOrNumber(raw[match.Index - 1]))
                        && (end >= raw.Length || !IsLetterOrNumber(CodePointAt(raw, end))))
                {
                    Array.Fill(locked, true, match.Index, match.Length);
                    from = end;
                }
                else
                {
                    from = match.Index + 1;
                }
            }
        }

        // pieces are [start, end) ranges of raw; each pass refines the ranges of the previous one
        List<int[]> spans = new List<int[]>();
        int start = 0;
        for (int i = 0; i < raw.Length;)
        {
            int length = StrongSeparatorLength(raw, i);
            if (length > 0 && !AnyLocked(locked, i, i + length))
            {
                spans.Add(new int[] { start, i });
                i += length;
                start = i;
            }
            else
            {
                i++;
            }
        }
        spans.Add(new int[] { start, raw.Length });

        List<int[]> afterCommas = new List<int[]>();
        foreach (int[] span in spans)
        {
            SplitOnCommas(raw, locked, span, afterCommas);
        }
        List<int[]> afterSlashes = new List<int[]>();
        foreach (int[] span in afterCommas)
        {
            SplitOnSlashes(raw, locked, span, afterSlashes);
        }

        List<string> pieces = new List<string>();
        foreach (int[] span in afterSlashes)
        {
            string piece = DanglingCommas.Replace(JavaString.Substring(raw, span[0], span[1]), "");
            if (piece.Length != 0)
            {
                pieces.Add(piece);
            }
        }
        if (pieces.Count == 0)
        {
            pieces.Add(raw); // nothing but separators: keep the value rather than lose it
        }
        return pieces;
    }

    /// <summary>length of a separator that always splits and starts at index i, or 0</summary>
    private static int StrongSeparatorLength(string s, int i)
    {
        char c = s[i];
        // ';' and '&', plus the full-width semicolon, the ideographic comma and the full-width comma
        if (c == ';' || c == '\uFF1B' || c == '\u3001' || c == '\uFF0C' || c == '&')
        {
            return 1;
        }
        if (c == ' ')
        {
            foreach (string separator in SpacedSeparators)
            {
                if (RegionMatchesIgnoreCase(s, i, separator))
                {
                    return separator.Length;
                }
            }
        }
        return 0;
    }

    private static bool AnyLocked(bool[] locked, int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            if (locked[i])
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>a bare comma separates artists only when both neighbours look like real names ("1,000 Maniacs" does not)</summary>
    private static void SplitOnCommas(string raw, bool[] locked, int[] span, List<int[]> output)
    {
        List<int> commas = new List<int>();
        for (int i = span[0]; i < span[1]; i++)
        {
            if (raw[i] == ',' && !locked[i])
            {
                commas.Add(i);
            }
        }
        int pieceStart = span[0];
        int segmentStart = span[0];
        for (int k = 0; k < commas.Count; k++)
        {
            int comma = commas[k];
            int nextEnd = k + 1 < commas.Count ? commas[k + 1] : span[1];
            if (HasWordOfTwo(JavaString.Substring(raw, segmentStart, comma))
                    && HasWordOfTwo(JavaString.Substring(raw, comma + 1, nextEnd)))
            {
                output.Add(new int[] { pieceStart, comma });
                pieceStart = comma + 1;
            }
            segmentStart = comma + 1;
        }
        output.Add(new int[] { pieceStart, span[1] });
    }

    private static bool HasWordOfTwo(string text)
    {
        foreach (string word in JavaString.Split(JavaString.Trim(text), WsClass + "+"))
        {
            if (CodePointCount(word) >= 2)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>a slash without spaces separates artists only if none of the pieces is shorter than 2 characters</summary>
    private static void SplitOnSlashes(string raw, bool[] locked, int[] span, List<int[]> output)
    {
        List<int[]> pieces = new List<int[]>();
        int pieceStart = span[0];
        for (int i = span[0]; i < span[1]; i++)
        {
            if (raw[i] == '/' && !locked[i])
            {
                pieces.Add(new int[] { pieceStart, i });
                pieceStart = i + 1;
            }
        }
        pieces.Add(new int[] { pieceStart, span[1] });
        foreach (int[] piece in pieces)
        {
            string text = JavaString.Trim(JavaString.Substring(raw, piece[0], piece[1]));
            if (CodePointCount(text) < 2)
            {
                output.Add(span);
                return;
            }
        }
        output.AddRange(pieces);
    }

    /// <summary>
    /// The English name inside one piece: junk markers and translations are removed
    /// ("Billlie (빌리)" gives "Billlie", "리사 (LISA)" gives "LISA", "G.E.M.邓紫棋"
    /// gives "G.E.M."). Pieces without any Latin letter cannot be translated and stay.
    /// </summary>
    private static string EnglishName(string piece)
    {
        string cleaned = NormalizeRaw(JunkMarker.Replace(piece, " "));
        if (cleaned.Length == 0)
        {
            return piece;
        }
        if (IsEnglishOnly(cleaned) || !HasLatinLetter(cleaned))
        {
            return cleaned;
        }

        StringBuilder latin = new StringBuilder(cleaned.Length);
        for (int i = 0; i < cleaned.Length;)
        {
            int cp = CodePointAt(cleaned, i);
            i += CharCount(cp);
            if (!IsCjk(cp))
            {
                AppendCodePoint(latin, cp);
            }
        }
        string name = latin.ToString();
        string previous;
        do
        {
            previous = name;
            name = EmptyBrackets.Replace(name, "");
        } while (!name.Equals(previous));
        name = UnwrapParentheses(TrimEdges(NormalizeRaw(name)));
        name = TrimEdges(NormalizeRaw(name));
        return name.Length == 0 ? cleaned : name;
    }

    private static bool HasLatinLetter(string s)
    {
        for (int i = 0; i < s.Length;)
        {
            int cp = CodePointAt(s, i);
            i += CharCount(cp);
            if (IsLetter(cp) && IsLatin(cp))
            {
                return true;
            }
        }
        return false;
    }

    // Han, Hangul and kana (plus the kana middle dot / long vowel mark, which are not letters of any script)
    private static bool IsCjk(int cp)
    {
        if (cp == 0x30FB || cp == 0x30FC || cp == 0xFF65 || cp == 0xFF70)
        {
            return true;
        }
        return InRanges(cp, HanRanges) || InRanges(cp, HangulRanges) || InRanges(cp, HiraganaRanges)
                || InRanges(cp, KatakanaRanges) || InRanges(cp, BopomofoRanges);
    }

    // space, hyphen, en dash, em dash, middle dot, colon and slash: what is left dangling when a translation is cut out
    private static string TrimEdges(string s)
    {
        int from = 0;
        int to = s.Length;
        while (from < to && " -\u2013\u2014\u00B7:/".IndexOf(s[from]) >= 0)
        {
            from++;
        }
        while (to > from && " -\u2013\u2014\u00B7:/".IndexOf(s[to - 1]) >= 0)
        {
            to--;
        }
        return JavaString.Substring(s, from, to);
    }

    /// <summary>"(LISA)" -> "LISA" (round brackets, ASCII or full-width), but "(G)I-DLE" stays because its first bracket closes early</summary>
    private static string UnwrapParentheses(string s)
    {
        if (s.Length < 2 || "(\uFF08".IndexOf(s[0]) < 0 || ")\uFF09".IndexOf(s[s.Length - 1]) < 0)
        {
            return s;
        }
        int depth = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '(' || c == '\uFF08')
            {
                depth++;
            }
            else if (c == ')' || c == '\uFF09')
            {
                depth--;
                if (depth == 0 && i < s.Length - 1)
                {
                    return s;
                }
            }
        }
        return JavaString.Substring(s, 1, s.Length - 1);
    }

    // ------------------------------------------------------------------ plan

    /// <summary>one artist found in a raw value, after blanks and fallbacks were sorted out</summary>
    private sealed record Mention(string Surface, string English, bool LowConfidence, bool KeepNative)
    {
        public Mention(string surface, string english, bool lowConfidence) : this(surface, english, lowConfidence, false)
        {
        }
    }

    /// <summary>everything collected about one artist (all ParsedArtists with the same LooseKey)</summary>
    private sealed class GroupBuilder
    {
        public readonly string Key;
        public readonly OrderedDictionary<string, int> Surfaces = new OrderedDictionary<string, int>(); // spelling -> tag values using it
        public readonly OrderedDictionary<string, int> Proposals = new OrderedDictionary<string, int>(); // proposed English name -> times proposed
        public bool LowConfidence;
        public bool KeepNative; // the artist's official name is Chinese, so the plain-English rule does not apply
        public int SongCount;

        public GroupBuilder(string key)
        {
            Key = key;
        }
    }

    /// <summary>
    /// Groups every spelling found in the songs' tags into artists and picks one
    /// name for each. Two ParsedArtists are the same artist when their English names
    /// have the same LooseKey; the name chosen is the most common English-only
    /// spelling in the library, or else the name the parse proposed (suggestedByAi).
    /// </summary>
    /// <param name="parsed">
    /// NormalizeRaw(raw tag value) -> the artists inside it; values it lacks are
    /// split with ParseLocally and reported in the plan's warnings
    /// </param>
    /// <param name="separator">joins several artists into one ARTIST/ALBUMARTIST value; null or empty = DefaultSeparator</param>
    /// <param name="usedAi">whether the parse came from the AI (only recorded in the plan)</param>
    public static ArtistPlan BuildPlan(IEnumerable<SongArtistTags> songs, IDictionary<string, List<ParsedArtist>> parsed,
            string separator, bool usedAi)
    {
        return BuildPlan(songs, parsed, separator, usedAi, null);
    }

    /// <summary>
    /// The same, but only the songs in scope may have their tags changed: the groups (and so the name chosen for each
    /// artist) are still worked out from every song, so a few songs processed on their own end up with the same names
    /// as the rest of the library.
    /// </summary>
    /// <param name="scope">audio paths (SongArtistTags.Path) of the songs that may change; null = every song</param>
    public static ArtistPlan BuildPlan(IEnumerable<SongArtistTags> songs, IDictionary<string, List<ParsedArtist>> parsed,
            string separator, bool usedAi, IEnumerable<string> scope)
    {
        List<SongArtistTags> songList = new List<SongArtistTags>();
        if (songs != null)
        {
            foreach (SongArtistTags song in songs)
            {
                if (song != null)
                {
                    songList.Add(song);
                }
            }
        }
        string sep = string.IsNullOrEmpty(separator) ? DefaultSeparator : separator;
        OrderedDictionary<string, int> counts = CountRawValues(songList);

        // a partial answer from the AI degrades gracefully: what it skipped is split locally
        Dictionary<string, List<Mention>> mentionsByRaw = UsableAnswers(parsed);
        List<string> missing = new List<string>();
        foreach (string raw in counts.Keys)
        {
            if (!mentionsByRaw.ContainsKey(raw))
            {
                missing.Add(raw);
            }
        }
        List<string> warnings = new List<string>();
        if (missing.Count != 0)
        {
            foreach (KeyValuePair<string, List<ParsedArtist>> entry in ParseLocally(missing))
            {
                mentionsByRaw[entry.Key] = ToMentions(entry.Value);
            }
            foreach (string raw in missing)
            {
                if (!mentionsByRaw.ContainsKey(raw))
                {
                    mentionsByRaw[raw] = new List<Mention> { new Mention(raw, raw, true) }; // never leave a value without an artist
                }
            }
            warnings.Add(missing.Count + " artist value(s) had no parse result and were split with the built-in "
                    + "local rules, please review: " + Preview(missing, 8));
        }

        OrderedDictionary<string, GroupBuilder> builders = new OrderedDictionary<string, GroupBuilder>();
        OrderedDictionary<string, List<string>> keysByRaw = new OrderedDictionary<string, List<string>>();
        foreach (KeyValuePair<string, int> entry in counts)
        {
            string raw = entry.Key;
            int weight = entry.Value;
            List<string> keys = new List<string>();
            HashSet<string> keySet = new HashSet<string>();
            HashSet<string> counted = new HashSet<string>(); // an artist named twice in one value still counts once
            foreach (Mention mention in mentionsByRaw[raw])
            {
                string key = IdentityKey(mention.English);
                if (!builders.TryGetValue(key, out GroupBuilder builder))
                {
                    builder = new GroupBuilder(key);
                    builders.Add(key, builder);
                }
                if (keySet.Add(key))
                {
                    keys.Add(key);
                }
                if (counted.Add("s\n" + key + "\n" + mention.Surface))
                {
                    AddTo(builder.Surfaces, mention.Surface, weight);
                }
                if (counted.Add("p\n" + key + "\n" + mention.English))
                {
                    AddTo(builder.Proposals, mention.English, 1);
                }
                if (mention.LowConfidence)
                {
                    builder.LowConfidence = true;
                }
                if (mention.KeepNative)
                {
                    builder.KeepNative = true;
                }
            }
            keysByRaw.Add(raw, keys);
        }

        foreach (SongArtistTags song in songList)
        {
            HashSet<string> keys = new HashSet<string>();
            AddKeys(song.Artist, keysByRaw, keys);
            AddKeys(song.AlbumArtist, keysByRaw, keys);
            AddKeys(song.Artists, keysByRaw, keys);
            foreach (string key in keys)
            {
                builders[key].SongCount++;
            }
        }

        List<ArtistGroup> groups = new List<ArtistGroup>();
        foreach (GroupBuilder builder in builders.Values)
        {
            groups.Add(ToGroup(builder));
        }
        // List.Sort is not stable, LINQ's OrderBy is (as is Java's List.sort)
        groups = groups.OrderByDescending(g => g.SongCount)
                .ThenBy(g => g.CanonicalName, CaseInsensitiveOrder)
                .ThenBy(g => g.CanonicalName, StringComparer.Ordinal)
                .ToList();

        return new ArtistPlan(sep, usedAi, groups, keysByRaw, songList, warnings, scope);
    }

    private static void AddKeys(IEnumerable<string> values, IDictionary<string, List<string>> keysByRaw, HashSet<string> keys)
    {
        if (values == null)
        {
            return;
        }
        foreach (string value in values)
        {
            if (keysByRaw.TryGetValue(NormalizeRaw(value), out List<string> found))
            {
                keys.UnionWith(found);
            }
        }
    }

    /// <summary>the answers worth keeping, keyed by normalized raw value; empty or unusable answers count as missing</summary>
    private static Dictionary<string, List<Mention>> UsableAnswers(IDictionary<string, List<ParsedArtist>> parsed)
    {
        Dictionary<string, List<Mention>> usable = new Dictionary<string, List<Mention>>();
        if (parsed == null)
        {
            return usable;
        }
        foreach (KeyValuePair<string, List<ParsedArtist>> entry in parsed)
        {
            string raw = NormalizeRaw(entry.Key);
            if (raw.Length == 0 || usable.ContainsKey(raw) || entry.Value == null)
            {
                continue;
            }
            List<Mention> mentions = ToMentions(entry.Value);
            if (mentions.Count != 0)
            {
                usable[raw] = mentions;
            }
        }
        return usable;
    }

    private static List<Mention> ToMentions(List<ParsedArtist> artists)
    {
        List<Mention> mentions = new List<Mention>();
        foreach (ParsedArtist artist in artists)
        {
            if (artist == null)
            {
                continue;
            }
            string text = NormalizeRaw(artist.Text);
            string english = NormalizeRaw(artist.English);
            if (text.Length == 0 && english.Length == 0)
            {
                continue;
            }
            mentions.Add(new Mention(text.Length == 0 ? english : text, english.Length == 0 ? text : english,
                    artist.IsLowConfidence, artist.IsKeepNative));
        }
        return mentions;
    }

    private static string IdentityKey(string english)
    {
        string key = LooseKey(english);
        // a name made only of symbols has no loose key, so it is told apart by its own text
        return key.Length == 0 ? ToLowerRoot(english) : key;
    }

    private static string Preview(List<string> values, int max)
    {
        List<string> shown = values.GetRange(0, Math.Min(max, values.Count));
        string text = string.Join(" | ", shown);
        return values.Count > max ? text + " | ... (" + (values.Count - max) + " more)" : text;
    }

    private static ArtistGroup ToGroup(GroupBuilder builder)
    {
        if (builder.KeepNative)
        {
            return ToNativeGroup(builder);
        }
        string canonical = null;
        bool suggestedByAi = false;

        // the most common spelling in the library, as long as it is plain English and really is the official name
        // written another way (a typo such as "Celing Dion" is not: the corrected official name replaces it)
        foreach (KeyValuePair<string, int> surface in builder.Surfaces)
        {
            if (IsEnglishOnly(surface.Key) && IdentityKey(surface.Key).Equals(builder.Key)
                    && (canonical == null || CompareSpellings(surface, canonical, builder) < 0))
            {
                canonical = surface.Key;
            }
        }
        if (canonical == null)
        {
            // no English spelling in the library: fall back to what the parse proposed
            suggestedByAi = true;
            foreach (KeyValuePair<string, int> proposal in builder.Proposals)
            {
                if (IsEnglishOnly(proposal.Key) && (canonical == null
                        || CompareProposals(proposal, canonical, builder) < 0))
                {
                    canonical = proposal.Key;
                }
            }
            if (canonical == null)
            {
                canonical = builder.Proposals.GetAt(0).Key;
                // the library's own (non-English) spelling is not a suggestion
                suggestedByAi = !builder.Surfaces.ContainsKey(canonical);
            }
        }
        canonical = NormalizeRaw(canonical);
        if (canonical.Length == 0)
        {
            canonical = builder.Key;
        }

        OrderedDictionary<string, int> counts = new OrderedDictionary<string, int>(builder.Surfaces);
        foreach (string proposal in builder.Proposals.Keys)
        {
            counts.TryAdd(proposal, 0); // a proposed name nobody wrote yet is still a choice for the user
        }
        List<ArtistVariant> variants = new List<ArtistVariant>();
        foreach (KeyValuePair<string, int> entry in counts)
        {
            variants.Add(new ArtistVariant(entry.Key, entry.Value));
        }
        variants.Sort(CompareVariants);

        return new ArtistGroup(builder.Key, canonical, suggestedByAi, builder.LowConfidence || suggestedByAi, variants,
                builder.SongCount);
    }

    /// <summary>
    /// A Chinese-language artist keeps the official Chinese credit, so the English-only rule does not apply: the name
    /// is the official one the parse proposed (most often, if it proposed several), even if the library spells it a
    /// little differently.
    /// </summary>
    private static ArtistGroup ToNativeGroup(GroupBuilder builder)
    {
        string canonical = null;
        foreach (KeyValuePair<string, int> proposal in builder.Proposals)
        {
            if (canonical == null || CompareProposals(proposal, canonical, builder) < 0)
            {
                canonical = proposal.Key;
            }
        }
        canonical = NormalizeRaw(canonical ?? "");
        if (canonical.Length == 0)
        {
            canonical = builder.Key;
        }
        bool inLibrary = builder.Surfaces.ContainsKey(canonical);

        OrderedDictionary<string, int> counts = new OrderedDictionary<string, int>(builder.Surfaces);
        foreach (string proposal in builder.Proposals.Keys)
        {
            counts.TryAdd(proposal, 0);
        }
        List<ArtistVariant> variants = new List<ArtistVariant>();
        foreach (KeyValuePair<string, int> entry in counts)
        {
            variants.Add(new ArtistVariant(entry.Key, entry.Value));
        }
        variants.Sort(CompareVariants);

        // a name the library never wrote is a suggestion; it is only low confidence if the source said so
        return new ArtistGroup(builder.Key, canonical, !inLibrary, builder.LowConfidence, variants, builder.SongCount);
    }

    // most used first, then by text (ordinal)
    private static int CompareVariants(ArtistVariant a, ArtistVariant b)
    {
        int byCount = b.Count.CompareTo(a.Count);
        return byCount != 0 ? byCount : string.CompareOrdinal(a.Text, b.Text);
    }

    /// <summary>negative when the entry's spelling is the better canonical name than the current best</summary>
    private static int CompareSpellings(KeyValuePair<string, int> entry, string best, GroupBuilder builder)
    {
        int byWeight = builder.Surfaces[best].CompareTo(entry.Value);
        if (byWeight != 0)
        {
            return byWeight;
        }
        return TieBreak(entry.Key, best, builder);
    }

    private static int CompareProposals(KeyValuePair<string, int> entry, string best, GroupBuilder builder)
    {
        int byCount = builder.Proposals[best].CompareTo(entry.Value);
        if (byCount != 0)
        {
            return byCount;
        }
        return TieBreak(entry.Key, best, builder);
    }

    // equal to a proposed English name first, then shorter, then plain alphabetical: the result never depends on map order
    private static int TieBreak(string candidate, string best, GroupBuilder builder)
    {
        bool candidateProposed = builder.Proposals.ContainsKey(candidate);
        bool bestProposed = builder.Proposals.ContainsKey(best);
        if (candidateProposed != bestProposed)
        {
            return candidateProposed ? -1 : 1;
        }
        if (candidate.Length != best.Length)
        {
            return candidate.Length.CompareTo(best.Length);
        }
        return string.CompareOrdinal(candidate, best);
    }

    private static void AddTo(OrderedDictionary<string, int> map, string key, int amount)
    {
        map[key] = map.TryGetValue(key, out int current) ? current + amount : amount;
    }

    // ------------------------------------------------------------------ updates

    /// <summary>
    /// The tag values to write for every song whose tags would change. Reads the
    /// canonical names from the plan's groups at call time, so edits the user made
    /// after BuildPlan are honoured. Only songs in the plan's scope are considered
    /// (every song when the scope is null).
    /// </summary>
    public static List<SongArtistUpdate> ComputeUpdates(ArtistPlan plan)
    {
        List<SongArtistUpdate> updates = new List<SongArtistUpdate>();
        if (plan == null || plan.Songs == null)
        {
            return updates;
        }
        Dictionary<string, string> nameByKey = new Dictionary<string, string>();
        if (plan.Groups != null)
        {
            foreach (ArtistGroup group in plan.Groups)
            {
                nameByKey[group.Key] = NameOf(group);
            }
        }
        IDictionary<string, List<string>> keysByRaw = plan.GroupKeysByRaw == null
                ? new Dictionary<string, List<string>>() : plan.GroupKeysByRaw;
        string sep = string.IsNullOrEmpty(plan.Separator) ? DefaultSeparator : plan.Separator;

        foreach (SongArtistTags song in plan.Songs)
        {
            if (song == null || !plan.IsInScope(song))
            {
                continue;
            }
            SongArtistUpdate update = UpdateFor(song, keysByRaw, nameByKey, sep);
            if (update != null)
            {
                updates.Add(update);
            }
        }
        return updates;
    }

    /// <summary>
    /// The songs whose files are not named "Artist - Title" after the tags they will have (the artists as the plan names
    /// them now, the title they have): for those the files get renamed, whether or not a tag changes. A song without an
    /// artist or without a title has nothing to name it after. Only songs in the plan's scope are considered.
    /// </summary>
    public static List<SongRename> ComputeRenames(ArtistPlan plan)
    {
        List<SongRename> renames = new List<SongRename>();
        if (plan == null || plan.Songs == null)
        {
            return renames;
        }
        Dictionary<string, string> nameByKey = new Dictionary<string, string>();
        if (plan.Groups != null)
        {
            foreach (ArtistGroup group in plan.Groups)
            {
                nameByKey[group.Key] = NameOf(group);
            }
        }
        IDictionary<string, List<string>> keysByRaw = plan.GroupKeysByRaw == null
                ? new Dictionary<string, List<string>>() : plan.GroupKeysByRaw;
        string sep = string.IsNullOrEmpty(plan.Separator) ? DefaultSeparator : plan.Separator;

        foreach (SongArtistTags song in plan.Songs)
        {
            if (song == null || !plan.IsInScope(song))
            {
                continue;
            }
            List<string> artistNames = NamesFor(song.Artist, keysByRaw, nameByKey);
            if (artistNames.Count == 0)
            {
                artistNames = NamesFor(song.AlbumArtist, keysByRaw, nameByKey);
            }
            string wanted = StandardFileName.Build(artistNames, song.Title, sep);
            if (wanted != null && !string.Equals(wanted, Path.GetFileNameWithoutExtension(song.Path), StringComparison.Ordinal))
            {
                renames.Add(new SongRename(song.Path, wanted));
            }
        }
        return renames;
    }

    private static string NameOf(ArtistGroup group)
    {
        string name = NormalizeRaw(group.CanonicalName);
        if (name.Length == 0 && group.Variants != null && group.Variants.Count != 0)
        {
            name = NormalizeRaw(group.Variants[0].Text); // a cleared name must not erase the artist
        }
        return name.Length == 0 ? group.Key : name;
    }

    private static SongArtistUpdate UpdateFor(SongArtistTags song, IDictionary<string, List<string>> keysByRaw,
            Dictionary<string, string> nameByKey, string sep)
    {
        List<string> artistNames = NamesFor(song.Artist, keysByRaw, nameByKey);
        List<string> albumArtistNames = NamesFor(song.AlbumArtist, keysByRaw, nameByKey);
        List<FieldChange> changes = new List<FieldChange>();

        string artistJoined = null;
        if (artistNames.Count != 0)
        {
            string joined = string.Join(sep, artistNames);
            if (!IsSingleValue(song.Artist, joined))
            {
                artistJoined = joined;
                changes.Add(new FieldChange("ARTIST", JoinForPreviewText(song.Artist), joined));
            }
        }

        // ARTISTS lists every individual artist; it is only worth having for collaborations
        // or when the song already carries it (then it must not go stale)
        List<string> target = artistNames.Count != 0 ? artistNames
                : albumArtistNames.Count != 0 ? albumArtistNames : NamesFor(song.Artists, keysByRaw, nameByKey);
        List<string> artistsList = null;
        if (target.Count != 0 && (target.Count >= 2 || HasText(song.Artists))
                && !song.Artists.SequenceEqual(target))
        {
            artistsList = target;
            changes.Add(new FieldChange("ARTISTS", JoinForPreviewText(song.Artists),
                    string.Join(JoinForPreview, target)));
        }

        string albumArtistJoined = null;
        if (albumArtistNames.Count != 0)
        {
            string joined = string.Join(sep, albumArtistNames);
            if (!IsSingleValue(song.AlbumArtist, joined))
            {
                albumArtistJoined = joined;
                changes.Add(new FieldChange("ALBUMARTIST", JoinForPreviewText(song.AlbumArtist),
                        joined));
            }
        }

        if (changes.Count == 0)
        {
            return null;
        }
        return new SongArtistUpdate(song.Path, artistJoined, artistsList, albumArtistJoined, changes);
    }

    /// <summary>canonical names of the artists in these raw tag values, in order, without duplicates</summary>
    private static List<string> NamesFor(IEnumerable<string> rawValues, IDictionary<string, List<string>> keysByRaw,
            Dictionary<string, string> nameByKey)
    {
        List<string> names = new List<string>();
        if (rawValues == null)
        {
            return names;
        }
        HashSet<string> seen = new HashSet<string>();
        foreach (string rawValue in rawValues)
        {
            string raw = NormalizeRaw(rawValue);
            if (raw.Length == 0)
            {
                continue;
            }
            List<string> found = new List<string>();
            if (keysByRaw.TryGetValue(raw, out List<string> keys) && keys != null)
            {
                foreach (string key in keys)
                {
                    if (nameByKey.TryGetValue(key, out string name))
                    {
                        found.Add(name);
                    }
                }
            }
            if (found.Count == 0)
            {
                found.Add(raw); // unknown data is kept, never lost
            }
            foreach (string name in found)
            {
                if (seen.Add(ToLowerRoot(name)))
                {
                    names.Add(name);
                }
            }
        }
        return names;
    }

    private static bool IsSingleValue(List<string> values, string text)
    {
        return values.Count == 1 && text.Equals(values[0]);
    }

    private static string JoinForPreviewText(List<string> values)
    {
        List<string> shown = new List<string>();
        foreach (string value in values)
        {
            shown.Add(value ?? "");
        }
        return string.Join(JoinForPreview, shown);
    }

    private static bool HasText(List<string> values)
    {
        foreach (string value in values)
        {
            if (value != null && JavaString.Trim(value).Length != 0)
            {
                return true;
            }
        }
        return false;
    }

    // ------------------------------------------------------------------ Java character semantics
    // The Java code works on code points with java.lang.Character; .NET has no direct equivalent for some of it
    // (scripts, lone surrogates, Java's case rules), so these helpers reproduce what Java answers.

    private static int CodePointAt(string s, int index)
    {
        char c = s[index];
        if (char.IsHighSurrogate(c) && index + 1 < s.Length && char.IsLowSurrogate(s[index + 1]))
        {
            return char.ConvertToUtf32(c, s[index + 1]);
        }
        return c; // a lone surrogate is its own code point, as in Java
    }

    private static int CodePointBefore(string s, int index)
    {
        char c = s[index - 1];
        if (char.IsLowSurrogate(c) && index >= 2 && char.IsHighSurrogate(s[index - 2]))
        {
            return char.ConvertToUtf32(s[index - 2], c);
        }
        return c;
    }

    private static int CharCount(int cp)
    {
        return cp >= 0x10000 ? 2 : 1;
    }

    private static void AppendCodePoint(StringBuilder sb, int cp)
    {
        if (cp >= 0x10000)
        {
            sb.Append(char.ConvertFromUtf32(cp));
        }
        else
        {
            sb.Append((char)cp);
        }
    }

    /// <summary>String.codePointCount: a surrogate pair counts once, a lone surrogate counts as one</summary>
    private static int CodePointCount(string s)
    {
        int count = 0;
        for (int i = 0; i < s.Length;)
        {
            i += CharCount(CodePointAt(s, i));
            count++;
        }
        return count;
    }

    /// <summary>Character.isWhitespace(cp) || Character.isSpaceChar(cp): Zs, Zl, Zp, and the ASCII/C0 white space</summary>
    private static bool IsWhitespaceOrSpaceChar(int cp)
    {
        if ((cp >= 0x09 && cp <= 0x0D) || (cp >= 0x1C && cp <= 0x1F))
        {
            return true;
        }
        UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(cp);
        return category == UnicodeCategory.SpaceSeparator || category == UnicodeCategory.LineSeparator
                || category == UnicodeCategory.ParagraphSeparator;
    }

    private static bool IsLetter(UnicodeCategory category)
    {
        return category == UnicodeCategory.UppercaseLetter || category == UnicodeCategory.LowercaseLetter
                || category == UnicodeCategory.TitlecaseLetter || category == UnicodeCategory.ModifierLetter
                || category == UnicodeCategory.OtherLetter;
    }

    /// <summary>[\p{L}\p{N}] of a Java regex: a letter or a number of any kind, by code point</summary>
    private static bool IsLetterOrNumber(int cp)
    {
        UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(cp);
        return IsLetter(category) || category == UnicodeCategory.DecimalDigitNumber
                || category == UnicodeCategory.LetterNumber || category == UnicodeCategory.OtherNumber;
    }

    /// <summary>Character.isLetter(cp)</summary>
    private static bool IsLetter(int cp)
    {
        return IsLetter(CharUnicodeInfo.GetUnicodeCategory(cp));
    }

    // Unicode script tables (Scripts.txt), as pairs of first and last code point in ascending order. Only what the
    // artist rules ask for: Latin (is this name English?) and the East Asian scripts (what is a translation?).
    private static readonly int[] LatinRanges =
    {
        0x0041, 0x005A, 0x0061, 0x007A, 0x00AA, 0x00AA, 0x00BA, 0x00BA, 0x00C0, 0x00D6, 0x00D8, 0x00F6, 0x00F8, 0x02B8,
        0x02E0, 0x02E4, 0x1D00, 0x1D25, 0x1D2C, 0x1D5C, 0x1D62, 0x1D65, 0x1D6B, 0x1D77, 0x1D79, 0x1DBE, 0x1E00, 0x1EFF,
        0x2071, 0x2071, 0x207F, 0x207F, 0x2090, 0x209C, 0x212A, 0x212B, 0x2132, 0x2132, 0x214E, 0x214E, 0x2160, 0x2188,
        0x2C60, 0x2C7F, 0xA722, 0xA787, 0xA78B, 0xA7DC, 0xA7F1, 0xA7FF, 0xAB30, 0xAB5A, 0xAB5C, 0xAB64, 0xAB66, 0xAB69, 0xFB00, 0xFB06,
        0xFF21, 0xFF3A, 0xFF41, 0xFF5A, 0x10780, 0x10785, 0x10787, 0x107B0, 0x107B2, 0x107BA, 0x1DF00, 0x1DF1E,
        0x1DF25, 0x1DF2A,
    };

    private static readonly int[] HanRanges =
    {
        0x2E80, 0x2E99, 0x2E9B, 0x2EF3, 0x2F00, 0x2FD5, 0x3005, 0x3005, 0x3007, 0x3007, 0x3021, 0x3029, 0x3038, 0x303B,
        0x3400, 0x4DBF, 0x4E00, 0x9FFF, 0xF900, 0xFA6D, 0xFA70, 0xFAD9, 0x16FE2, 0x16FE3, 0x16FF0, 0x16FF6,
        0x20000, 0x2A6DF, 0x2A700, 0x2B73F, 0x2B740, 0x2B81D, 0x2B820, 0x2CEAD, 0x2CEB0, 0x2EBE0, 0x2EBF0, 0x2EE5D,
        0x2F800, 0x2FA1D, 0x30000, 0x3134A, 0x31350, 0x33479,
    };

    private static readonly int[] HangulRanges =
    {
        0x1100, 0x11FF, 0x302E, 0x302F, 0x3131, 0x318E, 0x3200, 0x321E, 0x3260, 0x327E, 0xA960, 0xA97C, 0xAC00, 0xD7A3,
        0xD7B0, 0xD7C6, 0xD7CB, 0xD7FB, 0xFFA0, 0xFFBE, 0xFFC2, 0xFFC7, 0xFFCA, 0xFFCF, 0xFFD2, 0xFFD7, 0xFFDA, 0xFFDC,
    };

    private static readonly int[] HiraganaRanges =
    {
        0x3041, 0x3096, 0x309D, 0x309F, 0x1B001, 0x1B11F, 0x1B132, 0x1B132, 0x1B150, 0x1B152, 0x1F200, 0x1F200,
    };

    private static readonly int[] KatakanaRanges =
    {
        0x30A1, 0x30FA, 0x30FD, 0x30FF, 0x31F0, 0x31FF, 0x32D0, 0x32FE, 0x3300, 0x3357, 0xFF66, 0xFF6F, 0xFF71, 0xFF9D,
        0x1AFF0, 0x1AFF3, 0x1AFF5, 0x1AFFB, 0x1AFFD, 0x1AFFE, 0x1B000, 0x1B000, 0x1B120, 0x1B122, 0x1B155, 0x1B155,
        0x1B164, 0x1B167,
    };

    private static readonly int[] BopomofoRanges =
    {
        0x02EA, 0x02EB, 0x3105, 0x312F, 0x31A0, 0x31BF,
    };

    /// <summary>Character.UnicodeScript.of(cp) == LATIN</summary>
    private static bool IsLatin(int cp)
    {
        return InRanges(cp, LatinRanges);
    }

    private static bool InRanges(int cp, int[] ranges)
    {
        int low = 0;
        int high = ranges.Length / 2 - 1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (cp < ranges[2 * mid])
            {
                high = mid - 1;
            }
            else if (cp > ranges[2 * mid + 1])
            {
                low = mid + 1;
            }
            else
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Normalizer.normalize: like string.Normalize, but a lone surrogate and U+FFFE (which .NET rejects) pass through as they
    /// do in Java; they are starters, so the text on both sides of them is normalized on its own.
    /// </summary>
    private static string Normalize(string s, NormalizationForm form)
    {
        if (Ascii.IsValid(s))
        {
            return s;
        }
        StringBuilder sb = null;
        int runStart = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(c) || c == (char)0xFFFE)
            {
                sb ??= new StringBuilder(s.Length);
                sb.Append(s.Substring(runStart, i - runStart).Normalize(form)).Append(c);
                runStart = i + 1;
            }
        }
        if (sb == null)
        {
            return s.Normalize(form);
        }
        return sb.Append(s.Substring(runStart).Normalize(form)).ToString();
    }

    // Character.toUpperCase / toLowerCase (char): the simple case mappings, where .NET's invariant culture deliberately
    // leaves the Turkish dotted and dotless I alone
    private static char JavaToUpper(char c)
    {
        return c == '\u0131' ? 'I' : char.ToUpperInvariant(c);
    }

    private static char JavaToLower(char c)
    {
        return c == '\u0130' ? 'i' : char.ToLowerInvariant(c);
    }

    /// <summary>String.regionMatches(true, offset, other, 0, other.length())</summary>
    private static bool RegionMatchesIgnoreCase(string s, int offset, string other)
    {
        if (offset + other.Length > s.Length)
        {
            return false;
        }
        for (int k = 0; k < other.Length; k++)
        {
            char c1 = s[offset + k];
            char c2 = other[k];
            if (c1 == c2)
            {
                continue;
            }
            char u1 = JavaToUpper(c1);
            char u2 = JavaToUpper(c2);
            if (u1 == u2 || JavaToLower(u1) == JavaToLower(u2))
            {
                continue;
            }
            return false;
        }
        return true;
    }

    /// <summary>
    /// String.CASE_INSENSITIVE_ORDER: compares code points (a supplementary letter is one, a lone surrogate is itself), and
    /// orders by the lower case of the upper case of each, not by the upper case
    /// </summary>
    private static readonly IComparer<string> CaseInsensitiveOrder = Comparer<string>.Create((s1, s2) =>
    {
        int i1 = 0;
        int i2 = 0;
        while (i1 < s1.Length && i2 < s2.Length)
        {
            int c1 = CodePointAt(s1, i1);
            int c2 = CodePointAt(s2, i2);
            i1 += CharCount(c1);
            i2 += CharCount(c2);
            if (c1 != c2)
            {
                c1 = ToUpperCodePoint(c1);
                c2 = ToUpperCodePoint(c2);
                if (c1 != c2)
                {
                    c1 = ToLowerCodePoint(c1);
                    c2 = ToLowerCodePoint(c2);
                    if (c1 != c2)
                    {
                        return c1 - c2;
                    }
                }
            }
        }
        return (s1.Length - i1) - (s2.Length - i2);
    });

    // Character.toUpperCase / toLowerCase (int): the simple case mappings of a code point
    private static int ToUpperCodePoint(int cp)
    {
        if (cp == 0x0131)
        {
            return 'I';
        }
        return Rune.IsValid(cp) ? Rune.ToUpperInvariant(new Rune(cp)).Value : cp;
    }

    private static int ToLowerCodePoint(int cp)
    {
        if (cp == 0x0130)
        {
            return 'i';
        }
        return Rune.IsValid(cp) ? Rune.ToLowerInvariant(new Rune(cp)).Value : cp;
    }

    private const char CapitalSigma = (char)0x03A3;
    private const char SmallSigma = (char)0x03C3;
    private const char FinalSmallSigma = (char)0x03C2;
    private const char CapitalDottedI = (char)0x0130;
    private static readonly string DotAbove = "i" + (char)0x0307;

    /// <summary>
    /// String.toLowerCase(Locale.ROOT): the simple mapping plus the full mappings Java applies there, "İ" to "i" and a
    /// combining dot, and a capital sigma at the end of a word to the final sigma.
    /// </summary>
    private static string ToLowerRoot(string s)
    {
        if (s.IndexOf(CapitalSigma) < 0 && s.IndexOf(CapitalDottedI) < 0)
        {
            return s.ToLowerInvariant();
        }
        StringBuilder sb = new StringBuilder(s.Length + 2);
        int runStart = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c != CapitalSigma && c != CapitalDottedI)
            {
                continue;
            }
            sb.Append(s.Substring(runStart, i - runStart).ToLowerInvariant());
            if (c == CapitalDottedI)
            {
                sb.Append(DotAbove);
            }
            else
            {
                sb.Append(IsFinalSigma(s, i) ? FinalSmallSigma : SmallSigma);
            }
            runStart = i + 1;
        }
        return sb.Append(s.Substring(runStart).ToLowerInvariant()).ToString();
    }

    // Java's Final_Cased condition: inside the word around the sigma there is a cased letter before it and none after it
    private static bool IsFinalSigma(string s, int index)
    {
        int wordStart = index;
        while (wordStart > 0)
        {
            int cp = CodePointBefore(s, wordStart);
            int size = CharCount(cp);
            if (IsWordLetter(cp) || IsWordDigit(cp) || IsWordIgnorable(cp)
                    || IsWordJoiner(cp, PreviousSignificant(s, wordStart - size), NextSignificant(s, wordStart)))
            {
                wordStart -= size;
            }
            else
            {
                break;
            }
        }
        int wordEnd = index + 1;
        while (wordEnd < s.Length)
        {
            int cp = CodePointAt(s, wordEnd);
            int size = CharCount(cp);
            if (IsWordLetter(cp) || IsWordDigit(cp) || IsWordIgnorable(cp)
                    || IsWordJoiner(cp, PreviousSignificant(s, wordEnd), NextSignificant(s, wordEnd + size)))
            {
                wordEnd += size;
            }
            else
            {
                break;
            }
        }
        bool casedBefore = false;
        for (int i = wordStart; i < index && !casedBefore;)
        {
            int cp = CodePointAt(s, i);
            i += CharCount(cp);
            casedBefore = IsCased(cp);
        }
        if (!casedBefore)
        {
            return false;
        }
        for (int i = index + 1; i < wordEnd;)
        {
            int cp = CodePointAt(s, i);
            i += CharCount(cp);
            if (IsCased(cp))
            {
                return false;
            }
        }
        return true;
    }

    // the code point before / from the index, skipping combining marks (-1 at the edge of the text)
    private static int PreviousSignificant(string s, int index)
    {
        while (index > 0)
        {
            int cp = CodePointBefore(s, index);
            if (!IsWordIgnorable(cp))
            {
                return cp;
            }
            index -= CharCount(cp);
        }
        return -1;
    }

    private static int NextSignificant(string s, int index)
    {
        while (index < s.Length)
        {
            int cp = CodePointAt(s, index);
            if (!IsWordIgnorable(cp))
            {
                return cp;
            }
            index += CharCount(cp);
        }
        return -1;
    }

    // The word rules of Java's BreakIterator, loosely: a word is letters (every script but Han and kana, which are words of
    // their own), digits and combining marks, and a hyphen, quote or full stop stays inside it between two letters
    // (a comma or full stop between two digits as well).
    private static bool IsWordLetter(int cp)
    {
        UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(cp);
        if (!IsLetter(category) && category != UnicodeCategory.SpacingCombiningMark)
        {
            return false;
        }
        bool kanjiOrKana = cp == 0x3005 || (cp >= 0x4E00 && cp <= 0x9FA5) || (cp >= 0xF900 && cp <= 0xFA2D)
                || (cp >= 0x30A1 && cp <= 0x30FA) || (cp >= 0x3041 && cp <= 0x309D) || cp == 0x30FC;
        return !kanjiOrKana;
    }

    private static bool IsWordDigit(int cp)
    {
        UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(cp);
        return category == UnicodeCategory.DecimalDigitNumber || category == UnicodeCategory.LetterNumber
                || category == UnicodeCategory.OtherNumber;
    }

    private static bool IsWordIgnorable(int cp)
    {
        UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(cp);
        return category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.EnclosingMark
                || category == UnicodeCategory.Format;
    }

    private static bool IsWordJoiner(int cp, int before, int after)
    {
        if (before < 0 || after < 0)
        {
            return false;
        }
        bool midWord = CharUnicodeInfo.GetUnicodeCategory(cp) == UnicodeCategory.DashPunctuation || cp == 0x00AD
                || cp == 0x2027 || cp == '"' || cp == '\'' || cp == '.';
        if (midWord && IsWordLetter(before) && IsWordLetter(after))
        {
            return true;
        }
        bool midNumber = cp == '"' || cp == '\'' || cp == ',' || cp == 0x066B || cp == '.';
        return midNumber && IsWordDigit(before) && IsWordDigit(after);
    }

    // Java's "cased" test of the Final_Cased condition: lower, upper and title case letters, plus the
    // letters Unicode lists as Other_Lowercase / Other_Uppercase
    private static bool IsCased(int cp)
    {
        UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(cp);
        if (category == UnicodeCategory.UppercaseLetter || category == UnicodeCategory.LowercaseLetter
                || category == UnicodeCategory.TitlecaseLetter)
        {
            return true;
        }
        return (cp >= 0x02B0 && cp <= 0x02B8) || (cp >= 0x02C0 && cp <= 0x02C1) || (cp >= 0x02E0 && cp <= 0x02E4)
                || cp == 0x0345 || cp == 0x037A || (cp >= 0x1D2C && cp <= 0x1D61) || (cp >= 0x2160 && cp <= 0x217F)
                || (cp >= 0x24B6 && cp <= 0x24E9);
    }
}
