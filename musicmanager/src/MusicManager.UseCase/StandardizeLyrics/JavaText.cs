using System.Globalization;

namespace MusicManager.UseCase.StandardizeLyrics;

/// <summary>
/// The Java text rules the lyric helpers depend on, where .NET differs: Character.isWhitespace (U+00A0, U+2007 and U+202F
/// are not white space, U+001C..U+001F are), String.isBlank / strip / stripTrailing built on it, and code point iteration
/// with Character.isLetter. Only for the helpers of this folder.
/// </summary>
internal static class JavaText
{
    /// <summary>Java Character.isWhitespace(char).</summary>
    public static bool IsWhitespace(char c)
    {
        // numbers, not escapes or literals, so that the invisible characters cannot get lost from the source
        return (c >= 0x09 && c <= 0x20 && (c <= 0x0D || c >= 0x1C))
                || c == 0x1680
                || (c >= 0x2000 && c <= 0x200A && c != 0x2007)
                || c == 0x2028 || c == 0x2029 || c == 0x205F || c == 0x3000;
    }

    /// <summary>Java String.isBlank(): empty or only white space.</summary>
    public static bool IsBlank(string s)
    {
        foreach (char c in s)
        {
            if (!IsWhitespace(c))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Java String.strip().</summary>
    public static string Strip(string s)
    {
        int start = 0;
        int end = s.Length;
        while (start < end && IsWhitespace(s[start]))
        {
            start++;
        }
        while (end > start && IsWhitespace(s[end - 1]))
        {
            end--;
        }
        return s.Substring(start, end - start);
    }

    /// <summary>Java String.stripTrailing().</summary>
    public static string StripTrailing(string s)
    {
        int end = s.Length;
        while (end > 0 && IsWhitespace(s[end - 1]))
        {
            end--;
        }
        return s.Substring(0, end);
    }

    /// <summary>Java Character.isLetter(int): a Unicode letter (Lu, Ll, Lt, Lm, Lo).</summary>
    public static bool IsLetter(int codePoint)
    {
        switch (CharUnicodeInfo.GetUnicodeCategory(codePoint))
        {
            case UnicodeCategory.UppercaseLetter:
            case UnicodeCategory.LowercaseLetter:
            case UnicodeCategory.TitlecaseLetter:
            case UnicodeCategory.ModifierLetter:
            case UnicodeCategory.OtherLetter:
                return true;
            default:
                return false;
        }
    }

    /// <summary>Java text.codePoints().anyMatch(predicate): an unpaired surrogate is passed on as it is.</summary>
    public static bool AnyCodePoint(string text, Func<int, bool> predicate)
    {
        for (int i = 0; i < text.Length; i++)
        {
            int codePoint = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codePoint = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }
            if (predicate(codePoint))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Java text.codePoints().anyMatch(Character::isLetter)</summary>
    public static bool HasLetter(string text)
    {
        return AnyCodePoint(text, IsLetter);
    }
}
