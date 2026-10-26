using System.Text;
using System.Text.RegularExpressions;

namespace MusicManager.Common;

/// <summary>
/// The Java String methods whose .NET look-alikes behave differently. Use these (not string.Trim / Split / Substring)
/// wherever the Java code used trim(), strip(), isBlank(), split(), replaceFirst() or substring(begin, end).
/// </summary>
public static class JavaString
{
    /// <summary>Java trim(): removes every character up to and including U+0020 (control characters too), nothing else.</summary>
    public static string Trim(string s)
    {
        if (s == null) return null;
        int start = 0, end = s.Length;
        while (start < end && s[start] <= ' ') start++;
        while (end > start && s[end - 1] <= ' ') end--;
        return s.Substring(start, end - start);
    }

    /// <summary>Java strip(): removes Unicode white space (what Character.isWhitespace says) at both ends.</summary>
    public static string Strip(string s) => s == null ? null : s.Trim();

    public static string StripLeading(string s) => s == null ? null : s.TrimStart();

    public static string StripTrailing(string s) => s == null ? null : s.TrimEnd();

    /// <summary>Java isBlank(): empty or only white space.</summary>
    public static bool IsBlank(string s) => s == null || s.All(char.IsWhiteSpace);

    /// <summary>Java substring(beginIndex, endIndex): the END index, not a length.</summary>
    public static string Substring(string s, int beginIndex, int endIndex) => s.Substring(beginIndex, endIndex - beginIndex);

    /// <summary>
    /// Java String.split(regex): trailing empty strings are removed, and a leading empty string is dropped when the
    /// match at index 0 has zero width. A limit of 0 means "no limit, drop trailing empties" as in Java.
    /// </summary>
    public static string[] Split(string s, string regex, int limit = 0)
    {
        var parts = new List<string>();
        int index = 0;
        foreach (Match m in Regex.Matches(s, regex))
        {
            if (limit > 0 && parts.Count == limit - 1) break;
            if (m.Length == 0 && m.Index == 0) continue; // Java: no leading empty string for a zero-width match at 0
            if (m.Length == 0 && m.Index >= s.Length) continue;
            parts.Add(s.Substring(index, m.Index - index));
            index = m.Index + m.Length;
        }
        if (index == 0 && parts.Count == 0) return new[] { s };
        parts.Add(s.Substring(index));
        if (limit == 0)
        {
            while (parts.Count > 0 && parts[^1].Length == 0) parts.RemoveAt(parts.Count - 1);
        }
        return parts.ToArray();
    }

    /// <summary>Java replaceFirst(regex, replacement) (the replacement uses .NET $1 syntax, as Java's does).</summary>
    public static string ReplaceFirst(string s, string regex, string replacement) =>
        new Regex(regex).Replace(s, replacement, 1);

    /// <summary>Java String.join / repeat helpers that read naturally at call sites.</summary>
    public static string Repeat(string s, int count)
    {
        var sb = new StringBuilder(s.Length * Math.Max(count, 0));
        for (int i = 0; i < count; i++) sb.Append(s);
        return sb.ToString();
    }
}
