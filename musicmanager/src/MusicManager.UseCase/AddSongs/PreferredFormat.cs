namespace MusicManager.UseCase.AddSongs;

/// <summary>
/// Which copy of a song to add when the picked files have the same song in several formats (a folder with both
/// "Song.flac" and "Song.mp3"): the one in the format the user prefers; when there is none in that format, the largest file,
/// which is the one with the most of the sound in it.
/// </summary>
public static class PreferredFormat
{
    /// <summary>the format that is preferred until the user chooses another (the extension without the dot)</summary>
    public const string Default = "flac";

    /// <summary>the formats the user can prefer, as extensions without the dot, in the order the Options list them</summary>
    public static readonly IReadOnlyList<string> All = new List<string> { "flac", "wav", "mp3", "m4a", "ogg", "aac" }.AsReadOnly();

    /// <summary>on a tie of sizes the lossless format wins, then the better lossy one</summary>
    private static readonly string[] QualityOrder = { "flac", "wav", "m4a", "ogg", "aac", "mp3" };

    /// <summary>the extension of a format the way it is saved: lower case, no dot; anything that is not one of <see cref="All"/> is <see cref="Default"/></summary>
    public static string Normalize(string format)
    {
        string extension = Extension(format);
        return All.Contains(extension) ? extension : Default;
    }

    /// <summary>
    /// The index of the version to add among versions (extension, size in bytes) of one song: of those in the preferred
    /// format (a folder can hold two FLACs of one song, a standard one and a high-resolution master) the largest, and with
    /// none in that format the largest of all.
    /// </summary>
    public static int Choose(IReadOnlyList<(string Extension, long Size)> versions, string preferred)
    {
        string wanted = Normalize(preferred);
        int inFormat = -1;
        for (int i = 0; i < versions.Count; i++)
        {
            if (Extension(versions[i].Extension) == wanted && (inFormat < 0 || versions[i].Size > versions[inFormat].Size))
            {
                inFormat = i;
            }
        }
        if (inFormat >= 0)
        {
            return inFormat;
        }
        int best = 0;
        for (int i = 1; i < versions.Count; i++)
        {
            if (versions[i].Size > versions[best].Size
                    || (versions[i].Size == versions[best].Size && Rank(versions[i].Extension) < Rank(versions[best].Extension)))
            {
                best = i;
            }
        }
        return best;
    }

    private static string Extension(string format)
    {
        return (format ?? "").Trim().TrimStart('.').ToLowerInvariant();
    }

    private static int Rank(string extension)
    {
        return QualityRank(extension);
    }

    /// <summary>how good a format is for listening, 0 being the best (FLAC); a format nobody knows ranks last</summary>
    public static int QualityRank(string format)
    {
        int rank = Array.IndexOf(QualityOrder, Extension(format));
        return rank < 0 ? QualityOrder.Length : rank;
    }
}
