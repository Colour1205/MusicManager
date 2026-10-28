using System.Text;

namespace MusicManager.Tests;

/// <summary>an MP3 file with an ID3v2.4 tag of just the frames wanted (title, artist, album, album artist), then valid MPEG frames</summary>
internal static class TestMp3
{
    private static byte[] SyncSafe(int n)
    {
        return new[] { (byte)((n >> 21) & 0x7F), (byte)((n >> 14) & 0x7F), (byte)((n >> 7) & 0x7F), (byte)(n & 0x7F) };
    }

    private static byte[] TextFrame(string id, string value)
    {
        List<byte> data = new List<byte> { 3 }; // UTF-8
        data.AddRange(Encoding.UTF8.GetBytes(value));
        List<byte> frame = new List<byte>(Encoding.ASCII.GetBytes(id));
        frame.AddRange(SyncSafe(data.Count));
        frame.AddRange(new byte[] { 0, 0 });
        frame.AddRange(data);
        return frame.ToArray();
    }

    /// <summary>writes the file (and its folder) and returns its path; a null value leaves that frame out</summary>
    public static string Create(string folder, string fileName, string artist, string title, string album, string albumArtist = null,
            int extraFrames = 0)
    {
        List<byte[]> frames = new List<byte[]>();
        if (artist != null)
        {
            frames.Add(TextFrame("TPE1", artist));
        }
        if (title != null)
        {
            frames.Add(TextFrame("TIT2", title));
        }
        if (album != null)
        {
            frames.Add(TextFrame("TALB", album));
        }
        if (albumArtist != null)
        {
            frames.Add(TextFrame("TPE2", albumArtist));
        }
        List<byte> file = new List<byte>();
        if (frames.Count > 0)
        {
            file.AddRange(new byte[] { (byte)'I', (byte)'D', (byte)'3', 4, 0, 0 });
            file.AddRange(SyncSafe(frames.Sum(f => f.Length)));
            foreach (byte[] frame in frames)
            {
                file.AddRange(frame);
            }
        }
        for (int i = 0; i < 30 + extraFrames; i++)
        {
            file.AddRange(new byte[] { 0xFF, 0xFB, 0x90, 0x00 });
            file.AddRange(new byte[413]);
        }
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, fileName);
        File.WriteAllBytes(path, file.ToArray());
        return path;
    }
}
