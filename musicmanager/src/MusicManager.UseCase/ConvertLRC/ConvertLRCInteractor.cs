using System.Globalization;
using System.Text;
using MusicManager.Entity;

namespace MusicManager.UseCase.ConvertLRC;

public class ConvertLRCInteractor : IConvertLRCInputBoundary
{
    private IConvertLRCDataAccess _dataAccess;
    private IConvertLRCOutputBoundary _outputBoundary;

    public void ConvertLRC(IConvertLRCDataAccess dataAccess, IConvertLRCOutputBoundary outputBoundary)
    {
        _dataAccess = dataAccess;
        _outputBoundary = outputBoundary;
    }

    public void Execute(ConvertLRCInputData inputData)
    {
        List<Music> musics = inputData.Musics;
        List<string> targetFormats = inputData.TargetFormats;

        IDictionary<Music, Lyric> lyricsByMusic = _dataAccess.GetLRCs(new Queue<Music>(musics));

        int successCount = 0;
        int failCount = 0;
        int totalCount = musics.Count;
        List<string> failedPaths = new List<string>();

        foreach (Music music in musics)
        {
            lyricsByMusic.TryGetValue(music, out Lyric currLyric);

            // dataAccess.GetLRCs silently skips musics with no lyric file; report those as
            // failed too instead of leaving the caller waiting on a song that was never
            // actually attempted
            if (currLyric == null)
            {
                failCount++;
                failedPaths.Add(music.Path);
                _outputBoundary.PresentProgress(new ConvertLRCProgressData(music.Path, false));
                continue;
            }

            try
            {
                foreach (string format in targetFormats)
                {
                    switch (format)
                    {
                        case "TTML":
                            ConvertToTTML(currLyric);
                            break;
                        case "HMRC":
                            ConvertToHMRC(currLyric);
                            break;
                        case "LRC":
                            ConvertToLRC(currLyric);
                            break;
                    }
                }
                successCount++;
                _outputBoundary.PresentProgress(new ConvertLRCProgressData(music.Path, true));
            }
            catch (Exception)
            {
                failCount++;
                failedPaths.Add(currLyric.Path);
                _outputBoundary.PresentProgress(new ConvertLRCProgressData(music.Path, false));
            }
        }

        ConvertLRCOutputData outputData = new ConvertLRCOutputData();
        outputData.FailCount = failCount;
        outputData.SuccessCount = successCount;
        outputData.TotalCount = totalCount;
        outputData.FailedPaths = failedPaths;
        _outputBoundary.Present(outputData);
    }

    /// <summary>
    /// converts a Lyric to TTML format, mirroring LyricsManager#toTTML
    /// </summary>
    public void ConvertToTTML(Lyric lyric)
    {
        List<LyricLine> lines = lyric.Lines;

        StringBuilder ttml = new StringBuilder();
        ttml.Append(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><tt xmlns=\"http://www.w3.org/ns/ttml\" xmlns:itunes=\"http://music.apple.com/lyric-ttml-internal\" xmlns:ttm=\"http://www.w3.org/ns/ttml#metadata\" itunes:timing=\"Word\">");
        ttml.Append("\n    <head>");
        ttml.Append("\n        <metadata>");
        ttml.Append("\n            <ttm:agent type=\"person\" xml:id=\"v1\"/>");
        ttml.Append("\n        </metadata>");
        ttml.Append("\n    </head>");

        string duration = lines.Count == 0 ? FormatTTMLTime(0)
                : FormatTTMLTime(lines[lines.Count - 1].EndTimeMs);

        ttml.Append("\n    <body dur=\"").Append(duration).Append("\">");
        ttml.Append("\n        <div begin=\"00:00.000\" end=\"").Append(duration).Append("\">");

        int i = 0;
        foreach (LyricLine lyricLine in lines)
        {
            string begTime = FormatTTMLTime(lyricLine.StartTimeMs);
            string endTime = FormatTTMLTime(lyricLine.EndTimeMs);

            ttml.Append("\n            <p begin=\"").Append(begTime).Append("\" end=\"").Append(endTime)
                    .Append("\" ttm:agent=\"v1\" itunes:key=\"L").Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append("\">");

            WordTimeStamp[] wordTimeStamps = lyricLine.WordTimeStamps;
            if (wordTimeStamps != null && wordTimeStamps.Length > 0)
            {
                // word synced: emit a <span> per word
                foreach (WordTimeStamp word in wordTimeStamps)
                {
                    string wordBeg = FormatTTMLTime(word.StartTimeMs);
                    string wordEnd = FormatTTMLTime(word.EndTimeMs);
                    string text = word.Word.Replace("&", "&amp;");

                    ttml.Append("\n                <span begin=\"").Append(wordBeg).Append("\" end=\"")
                            .Append(wordEnd).Append("\">").Append(text).Append("</span>");
                }
            }
            else
            {
                // line synced: no per-word timing available, emit the line text directly
                ttml.Append(lyricLine.Line.Replace("&", "&amp;"));
            }

            ttml.Append("\n            </p>");
            i++;
        }

        ttml.Append("\n        </div>");
        ttml.Append("\n    </body>");
        ttml.Append("\n</tt>");

        _dataAccess.SaveTTML(lyric, ttml.ToString());
    }

    /// <summary>
    /// converts a Lyric to HMRC format, mirroring LyricsManager#toHMRC
    /// </summary>
    public void ConvertToHMRC(Lyric lyric)
    {
        int offset = 0;

        StringBuilder hmrc = new StringBuilder();
        hmrc.Append("[offset:").Append(Num(offset)).Append("]\n");

        foreach (LyricLine lyricLine in lyric.Lines)
        {
            long lineBeg = lyricLine.StartTimeMs + offset;
            long lineDur = lyricLine.EndTimeMs - lyricLine.StartTimeMs;

            hmrc.Append("[").Append(Num(lineBeg)).Append(",").Append(Num(lineDur)).Append("]");

            WordTimeStamp[] wordTimeStamps = lyricLine.WordTimeStamps;
            if (wordTimeStamps != null)
            {
                foreach (WordTimeStamp word in wordTimeStamps)
                {
                    long wordBeg = word.StartTimeMs + offset;
                    long wordDur = word.EndTimeMs - word.StartTimeMs;
                    long relativeBeg = wordBeg - lineBeg;

                    hmrc.Append("<").Append(Num(relativeBeg)).Append(",").Append(Num(wordDur)).Append(">")
                            .Append(word.Word);
                }
            }

            hmrc.Append("\n");
        }

        _dataAccess.SaveHMRC(lyric, hmrc.ToString());
    }

    /// <summary>
    /// converts a Lyric down to a line synced (none word synced) LRC, mirroring LyricsManager#convertTranslation.
    /// WILL REPLACE FILE
    /// </summary>
    public void ConvertToLRC(Lyric lyric)
    {
        int offset = 700; // default offset, matches LyricsManager#convertTranslation

        StringBuilder lrc = new StringBuilder();
        lrc.Append("[offset:").Append(Num(offset)).Append("]\n");

        foreach (LyricLine lyricLine in lyric.Lines)
        {
            string begTime = FormatLRCTime(lyricLine.StartTimeMs);
            lrc.Append("[").Append(begTime).Append("]").Append(lyricLine.Line).Append("\n");
        }

        _dataAccess.SaveLRC(lyric, lrc.ToString());
    }

    /// <summary>
    /// formats a millisecond timestamp as mm:ss.mmm, used by TTML begin/end/dur attributes
    /// </summary>
    private static string FormatTTMLTime(long ms)
    {
        long minutes = ms / 60000;
        long seconds = (ms % 60000) / 1000;
        long millis = ms % 1000;
        return PadZero(minutes, 2) + ":" + PadZero(seconds, 2) + "." + PadZero(millis, 3);
    }

    /// <summary>
    /// formats a millisecond timestamp as mm:ss.xx, the standard LRC timestamp format
    /// </summary>
    private static string FormatLRCTime(long ms)
    {
        long minutes = ms / 60000;
        long seconds = (ms % 60000) / 1000;
        long centiseconds = (ms % 1000) / 10;
        return PadZero(minutes, 2) + ":" + PadZero(seconds, 2) + "." + PadZero(centiseconds, 2);
    }

    /// <summary>
    /// Java's %0Nd: the number is zero padded to a total width of N characters, a minus sign counting as one of them
    /// (so -5 with width 2 stays "-5", where .NET's "D2" would give "-05")
    /// </summary>
    private static string PadZero(long value, int width)
    {
        string digits = value.ToString(CultureInfo.InvariantCulture);
        if (value >= 0)
        {
            return digits.PadLeft(width, '0');
        }
        return "-" + digits.Substring(1).PadLeft(width - 1, '0');
    }

    private static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);
}
