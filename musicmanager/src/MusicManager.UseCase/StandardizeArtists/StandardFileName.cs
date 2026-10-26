using System.Text;
using MusicManager.Common;

namespace MusicManager.UseCase.StandardizeArtists;

/// <summary>
/// The name a song's files have in a standardized library: "Artist - Title", made of the ARTIST and TITLE tags and nothing
/// else. Several artists are joined with the separator of the Options (" & "). What Windows does not allow in a file name
/// is dealt with the way the library's own names do: a slash becomes a dash ("K/DA" is "K-DA", "JP Ver. / Live" is "JP Ver.
/// - Live"), the other forbidden characters are left out, and the spaces that leaves are collapsed.
/// </summary>
public static class StandardFileName
{
    /// <summary>between the artists and the title</summary>
    public const string ArtistTitleSeparator = " - ";

    /// <summary>
    /// "Artist - Title" for these tag values, without the extension; null when there is no artist or no title to make a name of.
    /// </summary>
    public static string Build(IEnumerable<string> artists, string title, string separator)
    {
        if (artists == null)
        {
            return null;
        }
        List<string> names = new List<string>();
        foreach (string artist in artists)
        {
            if (artist != null && JavaString.Trim(artist).Length != 0)
            {
                names.Add(artist);
            }
        }
        string artistText = Clean(string.Join(string.IsNullOrEmpty(separator) ? ArtistStandardizer.DefaultSeparator : separator, names));
        string titleText = Clean(title);
        return artistText.Length == 0 || titleText.Length == 0 ? null : artistText + ArtistTitleSeparator + titleText;
    }

    /// <summary>one part of a name made fit for a file name (see the class summary)</summary>
    public static string Clean(string text)
    {
        if (text == null)
        {
            return "";
        }
        StringBuilder clean = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c == '/' || c == '\\')
            {
                clean.Append('-');
            }
            else if (char.IsControl(c))
            {
                clean.Append(' '); // a tab or a line break between two words is a space
            }
            else if (c != ':' && c != '*' && c != '?' && c != '"' && c != '<' && c != '>' && c != '|')
            {
                clean.Append(c);
            }
        }
        // the places the forbidden characters were left a gap: one space, and none at either end
        StringBuilder spaced = new StringBuilder(clean.Length);
        bool lastWasSpace = true;
        foreach (char c in clean.ToString())
        {
            bool space = char.IsWhiteSpace(c);
            if (space && lastWasSpace)
            {
                continue;
            }
            spaced.Append(space ? ' ' : c);
            lastWasSpace = space;
        }
        return JavaString.Trim(spaced.ToString());
    }
}
