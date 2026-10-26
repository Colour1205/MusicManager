using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace MusicManager.DataAccess;

/// <summary>
/// shrinks the cover art embedded in a song to the small picture the library keeps in memory for every song, so
/// thousands of songs fit at once (a raw cover is often a megabyte or more). <see cref="LibraryScanner"/> uses it.
/// </summary>
public static class Thumbnails
{
    static Thumbnails()
    {
        // System.Drawing looks up its PNG encoder lazily and fails (ArgumentNullException "encoder") when several threads
        // do that for the first time at once, as the threads of a library scan do: the first PNG is made once, here,
        // while the other threads wait for this initializer
        using (Bitmap warmUp = new Bitmap(1, 1))
        using (MemoryStream png = new MemoryStream())
        {
            warmUp.Save(png, ImageFormat.Png);
        }
    }

    /// <summary>the longest side of a thumbnail in pixels; the library draws it at about half this size, sharp on HiDPI</summary>
    public const int Size = 96;

    // a cover far bigger than the thumbnail is first reduced in one step, which is much faster than many halvings;
    // it is still kept at four times the thumbnail so the smooth shrinking below has plenty to work with
    // (Java read only every n-th pixel of such a cover; GDI+ cannot, so here it is drawn smaller)
    private const int DecodeFactor = 4;

    /// <summary>
    /// thumbnails made during one scan, by the content of the cover. The songs of an album almost always carry the same
    /// cover, so it is decoded and shrunk once and all of them share the one small PNG (which is also a tenth of the
    /// memory). Safe to use from several threads; a second thread that needs a cover another one is still working on
    /// waits for it instead of doing it again.
    /// </summary>
    public sealed class Cache
    {
        // a cover that is no usable picture is remembered as well (its Lazy holds null)
        private readonly ConcurrentDictionary<string, Lazy<byte[]>> _made = new ConcurrentDictionary<string, Lazy<byte[]>>();

        /// <summary>the same as <see cref="Thumbnails.PngFrom"/>, and the very same array for the same cover</summary>
        public byte[] PngFrom(byte[] coverArt)
        {
            if (coverArt == null || coverArt.Length == 0)
            {
                return null;
            }
            Lazy<byte[]> made = _made.GetOrAdd(Digest(coverArt),
                    _ => new Lazy<byte[]>(() => Thumbnails.PngFrom(coverArt), LazyThreadSafetyMode.ExecutionAndPublication));
            return made.Value;
        }

        private static string Digest(byte[] data)
        {
            return Convert.ToHexString(SHA1.HashData(data));
        }
    }

    /// <summary>
    /// the cover as a square PNG of at most Size x Size pixels (a smaller cover is not enlarged). A cover that is not
    /// square is cropped to its middle, so the library can draw it into a square without squashing it.
    /// </summary>
    /// <returns>the PNG bytes, or null if there is no cover or it cannot be decoded as an image</returns>
    public static byte[] PngFrom(byte[] coverArt)
    {
        if (coverArt == null || coverArt.Length == 0)
        {
            return null;
        }
        try
        {
            byte[] fast = WicPng(coverArt, Size);
            if (fast != null)
            {
                return fast;
            }
        }
        catch (Exception)
        {
            // a picture Windows' own decoder will not take: GDI+ may still manage it
        }
        return GdiPlusPng(coverArt);
    }

    /// <summary>
    /// The thumbnail made by Windows' imaging component (WIC), which is what makes a library of thousands of covers load in
    /// a second: it decodes a JPEG straight to the small size instead of at full size first (GDI+ does that, and all its
    /// calls queue behind one lock, so more threads hardly help), and any number of threads can use it at once. The
    /// picture is the same as the GDI+ one to within about one per cent of a pixel's value.
    /// </summary>
    private static byte[] WicPng(byte[] coverArt, int size)
    {
        using InMemoryRandomAccessStream input = new InMemoryRandomAccessStream();
        using (DataWriter writer = new DataWriter(input))
        {
            writer.WriteBytes(coverArt);
            writer.StoreAsync().AsTask().GetAwaiter().GetResult();
            writer.DetachStream();
        }
        input.Seek(0);
        BitmapDecoder decoder = BitmapDecoder.CreateAsync(input).AsTask().GetAwaiter().GetResult();
        uint width = decoder.PixelWidth;
        uint height = decoder.PixelHeight;
        uint side = Math.Min(width, height);
        if (side == 0)
        {
            return null;
        }
        uint target = Math.Min((uint)size, side); // a smaller cover is not enlarged
        // the short side is shrunk to the target, then the middle square is taken (a cover that is not square is cropped)
        uint scaledWidth = (uint)Math.Round((double)width * target / side);
        uint scaledHeight = (uint)Math.Round((double)height * target / side);
        BitmapTransform transform = new BitmapTransform
        {
            ScaledWidth = scaledWidth,
            ScaledHeight = scaledHeight,
            InterpolationMode = BitmapInterpolationMode.Fant,
            Bounds = new BitmapBounds
            {
                X = (scaledWidth - target) / 2,
                Y = (scaledHeight - target) / 2,
                Width = target,
                Height = target
            }
        };
        // like GDI+: no colour profile, no rotation by the picture's EXIF tag
        PixelDataProvider pixels = decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform,
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)
                .AsTask().GetAwaiter().GetResult();
        using InMemoryRandomAccessStream output = new InMemoryRandomAccessStream();
        BitmapEncoder encoder = BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output).AsTask().GetAwaiter().GetResult();
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, target, target, 96, 96, pixels.DetachPixelData());
        encoder.FlushAsync().AsTask().GetAwaiter().GetResult();
        output.Seek(0);
        byte[] png = new byte[output.Size];
        using DataReader reader = new DataReader(output);
        reader.LoadAsync((uint)output.Size).AsTask().GetAwaiter().GetResult();
        reader.ReadBytes(png);
        return png;
    }

    /// <summary>the thumbnail made with GDI+: the way it was always done, kept for the pictures WIC will not decode</summary>
    private static byte[] GdiPlusPng(byte[] coverArt)
    {
        try
        {
            // the image reads from the stream whenever it is drawn, so the stream outlives the image
            using MemoryStream input = new MemoryStream(coverArt, false);
            using Image cover = Decode(input);
            if (cover == null)
            {
                return null;
            }
            using Bitmap thumbnail = Thumbnail(cover, Size);
            using MemoryStream png = new MemoryStream(16 * 1024);
            thumbnail.Save(png, ImageFormat.Png);
            return png.ToArray();
        }
        catch (Exception)
        {
            // a damaged or unsupported picture is just a song without a cover (GDI+ reports some of those as OutOfMemoryException)
            return null;
        }
    }

    /// <summary>the middle square of the image, shrunk to at most size x size</summary>
    public static Bitmap Thumbnail(Image source, int size)
    {
        int side = Math.Min(source.Width, source.Height);
        int left = (source.Width - side) / 2;
        int top = (source.Height - side) / 2;
        // a cover without transparency stays a 24-bit picture, which makes for a much smaller PNG
        bool hasAlpha = Image.IsAlphaPixelFormat(source.PixelFormat) || (source.Flags & (int)ImageFlags.HasAlpha) != 0;
        PixelFormat type = hasAlpha ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb;

        int step = side / (Size * DecodeFactor);
        Bitmap square;
        if (step > 1)
        {
            side /= step;
            square = ScaleStep(source, new Rectangle(left, top, side * step, side * step), side, side, type);
        }
        else
        {
            square = ScaleStep(source, new Rectangle(left, top, side, side), side, side, type);
        }
        using (square)
        {
            return HighQualityScale(square, Math.Min(size, side), type);
        }
    }

    private static Image Decode(Stream data)
    {
        return Image.FromStream(data, false, true);
    }

    /// <summary>
    /// scales an image toward the target size in repeated halving steps (each step at most a 2x change) using
    /// bilinear interpolation. A single large-ratio scale (a 1000 px cover shrunk straight to 96 px) shows aliasing;
    /// gentle halving avoids that. The same approach as the old SongCard's thumbnails.
    /// </summary>
    private static Bitmap HighQualityScale(Bitmap source, int target, PixelFormat type)
    {
        Bitmap current = source;
        int width = source.Width;
        int height = source.Height;
        try
        {
            while (width > target * 2 || height > target * 2)
            {
                width = Math.Max(target, width / 2);
                height = Math.Max(target, height / 2);
                Bitmap next = ScaleStep(current, new Rectangle(0, 0, current.Width, current.Height), width, height, type);
                if (!ReferenceEquals(current, source))
                {
                    current.Dispose();
                }
                current = next;
            }
            return ScaleStep(current, new Rectangle(0, 0, current.Width, current.Height), target, target, type);
        }
        finally
        {
            if (!ReferenceEquals(current, source))
            {
                current.Dispose();
            }
        }
    }

    /// <summary>draws that part of the source into a new bitmap of the given size and pixel type</summary>
    private static Bitmap ScaleStep(Image source, Rectangle part, int width, int height, PixelFormat type)
    {
        Bitmap scaled = new Bitmap(width, height, type);
        try
        {
            using Graphics g = Graphics.FromImage(scaled);
            // a step of at most 2x is what Java's bilinear drawImage averages exactly, and GDI+'s plain Bilinear does the same;
            // its "high quality" filters would blur the picture a little more at every halving (compared with Java's
            // thumbnails of 140 real covers the mean difference is 0.9 instead of 2.9 of 255). Only the one big reduction
            // of a huge cover needs the filter
            g.InterpolationMode = part.Width <= width * 2 + 1 && part.Height <= height * 2 + 1
                    ? InterpolationMode.Bilinear
                    : InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.CompositingMode = CompositingMode.SourceCopy; // keeps the alpha channel as it is
            using ImageAttributes edges = new ImageAttributes();
            edges.SetWrapMode(WrapMode.TileFlipXY); // no dark or transparent fringe at the borders
            g.DrawImage(source, new Rectangle(0, 0, width, height), part.X, part.Y, part.Width, part.Height,
                    GraphicsUnit.Pixel, edges);
            return scaled;
        }
        catch (Exception)
        {
            scaled.Dispose();
            throw;
        }
    }
}
