using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace MusicManager.App.View.Library;

/// <summary>
/// The decoded cover thumbnails of the song cards and the song window (Java: ThumbnailCache). The library keeps every
/// song's cover as small PNG bytes; they are decoded only when a card is shown, and the most recently used ones are kept
/// (a bounded LRU), so scrolling back and forth is cheap and 2,000 songs never hold 2,000 decoded images. UI thread only.
/// </summary>
internal static class CoverImageCache
{
    /// <summary>the library's thumbnails are at most 96 px: decoding at that size keeps them sharp up to 150 percent scale</summary>
    public const int Pixels = 96;

    public const int Capacity = 256;

    private sealed class Entry
    {
        public byte[] Source; // the bytes the image was made from: a refreshed song has new bytes
        public BitmapImage Image; // null while loading, and for bytes that are not an image
        public bool Done;
        public List<Action> Waiting = new();
        public LinkedListNode<string> Node;
    }

    private static readonly Dictionary<string, Entry> Entries = new();
    private static readonly LinkedList<string> Order = new(); // most recently used first

    /// <summary>
    /// The cover of the song with this path as a decoded image, or null: the song has no cover, the image is still being
    /// decoded (<paramref name="onReady"/> is called on the UI thread when it is there) or the bytes cannot be read, in
    /// which case the card keeps its placeholder.
    /// </summary>
    public static BitmapImage Get(string path, byte[] cover, Action onReady)
    {
        if (cover == null || cover.Length == 0)
        {
            return null;
        }
        string key = path ?? "";
        if (Entries.TryGetValue(key, out Entry entry) && ReferenceEquals(entry.Source, cover))
        {
            Order.Remove(entry.Node);
            Order.AddFirst(entry.Node);
            if (!entry.Done && onReady != null)
            {
                entry.Waiting.Add(onReady);
            }
            return entry.Image;
        }
        if (entry != null)
        {
            Order.Remove(entry.Node);
        }
        entry = new Entry { Source = cover, Node = new LinkedListNode<string>(key) };
        if (onReady != null)
        {
            entry.Waiting.Add(onReady);
        }
        Entries[key] = entry;
        Order.AddFirst(entry.Node);
        while (Entries.Count > Capacity)
        {
            string eldest = Order.Last.Value;
            Order.RemoveLast();
            Entries.Remove(eldest);
        }
        _ = Decode(entry);
        return null;
    }

    private static async Task Decode(Entry entry)
    {
        BitmapImage image = null;
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(entry.Source);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            var decoded = new BitmapImage { DecodePixelWidth = Pixels };
            await decoded.SetSourceAsync(stream);
            image = decoded;
        }
        catch (Exception)
        {
            image = null; // not an image: the placeholder stays, and this is remembered so it is not decoded again
        }
        entry.Image = image;
        entry.Done = true;
        List<Action> waiting = entry.Waiting;
        entry.Waiting = new List<Action>();
        if (image == null)
        {
            return;
        }
        foreach (Action onReady in waiting)
        {
            try
            {
                onReady();
            }
            catch (Exception)
            {
                // a card that is gone must not stop the others from getting their cover
            }
        }
    }
}
