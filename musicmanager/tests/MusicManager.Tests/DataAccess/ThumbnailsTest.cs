using System.Drawing;
using System.Drawing.Imaging;
using MusicManager.DataAccess;
using Xunit;

namespace MusicManager.Tests.DataAccess;

public class ThumbnailsTest
{
    // java.awt.Color constants (System.Drawing's Green and Orange are other colors)
    private static readonly Color Red = Color.FromArgb(255, 0, 0);
    private static readonly Color Green = Color.FromArgb(0, 255, 0);
    private static readonly Color Blue = Color.FromArgb(0, 0, 255);
    private static readonly Color White = Color.FromArgb(255, 255, 255);
    private static readonly Color Orange = Color.FromArgb(255, 200, 0);
    private static readonly Color Pink = Color.FromArgb(255, 175, 175);
    private static readonly Color Magenta = Color.FromArgb(255, 0, 255);
    private static readonly Color Cyan = Color.FromArgb(0, 255, 255);

    // BufferedImage.TYPE_INT_RGB and TYPE_INT_ARGB
    private const PixelFormat TypeIntRgb = PixelFormat.Format24bppRgb;
    private const PixelFormat TypeIntArgb = PixelFormat.Format32bppArgb;

    private static byte[] Encode(Bitmap image, ImageFormat format)
    {
        using MemoryStream output = new MemoryStream();
        image.Save(output, format);
        return output.ToArray();
    }

    /// <summary>an image of the given size filled with vertical bands of the given colors, each as wide as given</summary>
    private static Bitmap Bands(PixelFormat type, int height, params (int Width, Color Color)[] widthAndColor)
    {
        int width = 0;
        foreach ((int bandWidth, Color _) in widthAndColor)
        {
            width += bandWidth;
        }
        Bitmap image = new Bitmap(width, height, type);
        using (Graphics g = Graphics.FromImage(image))
        {
            int x = 0;
            foreach ((int bandWidth, Color color) in widthAndColor)
            {
                using (SolidBrush brush = new SolidBrush(color))
                {
                    g.FillRectangle(brush, x, 0, bandWidth, height);
                }
                x += bandWidth;
            }
        }
        return image;
    }

    /// <summary>the picture of a PNG (the stream stays open for the picture, as GDI+ wants it)</summary>
    private static Bitmap Decode(byte[] png)
    {
        return new Bitmap(new MemoryStream(png));
    }

    private static bool Near(Color actual, Color expected, int tolerance)
    {
        return Math.Abs(actual.R - expected.R) <= tolerance
                && Math.Abs(actual.G - expected.G) <= tolerance
                && Math.Abs(actual.B - expected.B) <= tolerance;
    }

    [Fact]
    public void ABigSquareCoverBecomesA96PixelPng()
    {
        byte[] jpeg = Encode(Bands(TypeIntRgb, 1000, (1000, Red)), ImageFormat.Jpeg);

        byte[] png = Thumbnails.PngFrom(jpeg);

        Assert.NotNull(png);
        Assert.Equal(0x89, png[0]); // a PNG signature
        Assert.Equal((byte)'P', png[1]);
        Bitmap thumbnail = Decode(png);
        Assert.Equal(96, thumbnail.Width);
        Assert.Equal(96, thumbnail.Height);
        Assert.True(Near(thumbnail.GetPixel(48, 48), Red, 12), "still the same picture");
        Assert.True(png.Length < 20_000, "small enough for thousands of songs: " + png.Length);
    }

    [Fact]
    public void AHugeCoverIsReadSubsampledButStillComesOutRight()
    {
        // 3000 x 3000: the decoder takes every 7th pixel, the result must not notice
        Bitmap cover = Bands(TypeIntRgb, 3000, (1500, Blue), (1500, White));

        Bitmap thumbnail = Decode(Thumbnails.PngFrom(Encode(cover, ImageFormat.Png)));

        Assert.Equal(96, thumbnail.Width);
        Assert.True(Near(thumbnail.GetPixel(10, 48), Blue, 12));
        Assert.True(Near(thumbnail.GetPixel(85, 48), White, 12));
    }

    [Fact]
    public void AWideCoverIsCroppedToItsMiddleSquareNotSquashed()
    {
        // red | green | blue, the green middle is a square of 200 and the red and blue strips are cut off
        Bitmap cover = Bands(TypeIntRgb, 200, (50, Red), (200, Green), (50, Blue));

        Bitmap thumbnail = Decode(Thumbnails.PngFrom(Encode(cover, ImageFormat.Png)));

        Assert.Equal(96, thumbnail.Width);
        Assert.Equal(96, thumbnail.Height);
        foreach (int x in new[] { 1, 48, 94 })
        {
            Assert.True(Near(thumbnail.GetPixel(x, 48), Green, 12), "x=" + x);
        }
    }

    [Fact]
    public void ATallCoverIsCroppedToItsMiddleSquareToo()
    {
        Bitmap tall = new Bitmap(200, 400, TypeIntRgb);
        using (Graphics g = Graphics.FromImage(tall))
        {
            using (SolidBrush red = new SolidBrush(Red))
            {
                g.FillRectangle(red, 0, 0, 200, 400);
            }
            using (SolidBrush green = new SolidBrush(Green))
            {
                g.FillRectangle(green, 0, 100, 200, 200);
            }
        }

        Bitmap thumbnail = Decode(Thumbnails.PngFrom(Encode(tall, ImageFormat.Png)));

        Assert.Equal(96, thumbnail.Height);
        Assert.True(Near(thumbnail.GetPixel(48, 2), Green, 12));
        Assert.True(Near(thumbnail.GetPixel(48, 93), Green, 12));
    }

    [Fact]
    public void ASmallCoverIsNotEnlarged()
    {
        byte[] png = Thumbnails.PngFrom(Encode(Bands(TypeIntRgb, 40, (40, Orange)), ImageFormat.Png));

        Bitmap thumbnail = Decode(png);

        Assert.Equal(40, thumbnail.Width);
        Assert.Equal(40, thumbnail.Height);
    }

    [Fact]
    public void ACoverOfExactlyTheThumbnailSizeKeepsItsPixels()
    {
        Bitmap cover = Bands(TypeIntRgb, 96, (48, Red), (48, Blue));

        Bitmap thumbnail = Decode(Thumbnails.PngFrom(Encode(cover, ImageFormat.Png)));

        Assert.Equal(96, thumbnail.Width);
        Assert.True(Near(thumbnail.GetPixel(10, 10), Red, 2));
        Assert.True(Near(thumbnail.GetPixel(90, 10), Blue, 2));
    }

    [Fact]
    public void TransparencyIsKept()
    {
        Bitmap cover = new Bitmap(300, 300, TypeIntArgb); // fully transparent

        Bitmap thumbnail = Decode(Thumbnails.PngFrom(Encode(cover, ImageFormat.Png)));

        Assert.True(Image.IsAlphaPixelFormat(thumbnail.PixelFormat));
        Assert.Equal(0, thumbnail.GetPixel(48, 48).A);
    }

    [Fact]
    public void ACoverWithoutTransparencyComesBackFullyOpaqueAndSmall()
    {
        byte[] png = Thumbnails.PngFrom(Encode(Bands(TypeIntRgb, 300, (300, Pink)), ImageFormat.Png));
        Bitmap thumbnail = Decode(png);

        Assert.Equal(255, thumbnail.GetPixel(10, 10).A);
        Assert.Equal(255, thumbnail.GetPixel(95, 95).A);
        Assert.True(png.Length < 3000, "a flat picture is a few hundred bytes, not " + png.Length);
    }

    [Fact]
    public void ManyThreadsMakeExactlyTheThumbnailsOneThreadMakes()
    {
        List<byte[]> covers = new List<byte[]>();
        for (int i = 0; i < 40; i++)
        {
            covers.Add(Encode(Bands(TypeIntRgb, 200 + i * 7, (100 + i, Red), (100, Green)), i % 2 == 0 ? ImageFormat.Jpeg : ImageFormat.Png));
        }
        List<byte[]> alone = covers.Select(Thumbnails.PngFrom).ToList();
        byte[][] together = new byte[covers.Count][];

        Parallel.For(0, covers.Count, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i => together[i] = Thumbnails.PngFrom(covers[i]));

        for (int i = 0; i < covers.Count; i++)
        {
            Assert.NotNull(together[i]);
            Assert.Equal(alone[i], together[i]);
        }
    }

    [Fact]
    public void NothingToShrinkGivesNull()
    {
        Assert.Null(Thumbnails.PngFrom(null));
        Assert.Null(Thumbnails.PngFrom(new byte[0]));
    }

    [Fact]
    public void BytesThatAreNoImageGiveNull()
    {
        Assert.Null(Thumbnails.PngFrom(System.Text.Encoding.UTF8.GetBytes("this is not a picture")));
        byte[] brokenJpeg = Encode(Bands(TypeIntRgb, 300, (300, Red)), ImageFormat.Jpeg);
        byte[] cut = brokenJpeg.Take(40).ToArray(); // the header only, the picture itself is missing
        Assert.Null(Thumbnails.PngFrom(cut));
    }

    // ---- the cache of one scan ----

    [Fact]
    public void TheSameCoverIsShrunkOnceAndSharedByEverySongOfTheAlbum()
    {
        Thumbnails.Cache cache = new Thumbnails.Cache();
        byte[] cover = Encode(Bands(TypeIntRgb, 400, (400, Magenta)), ImageFormat.Png);

        byte[] first = cache.PngFrom(cover);
        byte[] again = cache.PngFrom((byte[])cover.Clone()); // another song, its own copy of the same cover

        Assert.NotNull(first);
        Assert.Same(first, again);
    }

    [Fact]
    public void DifferentCoversGetTheirOwnThumbnails()
    {
        Thumbnails.Cache cache = new Thumbnails.Cache();

        byte[] red = cache.PngFrom(Encode(Bands(TypeIntRgb, 300, (300, Red)), ImageFormat.Png));
        byte[] blue = cache.PngFrom(Encode(Bands(TypeIntRgb, 300, (300, Blue)), ImageFormat.Png));

        Assert.True(Near(Decode(red).GetPixel(48, 48), Red, 12));
        Assert.True(Near(Decode(blue).GetPixel(48, 48), Blue, 12));
    }

    [Fact]
    public void TheCacheAnswersLikeTheDirectCallForCoversThatAreNoPicture()
    {
        Thumbnails.Cache cache = new Thumbnails.Cache();

        Assert.Null(cache.PngFrom(null));
        Assert.Null(cache.PngFrom(new byte[0]));
        Assert.Null(cache.PngFrom(System.Text.Encoding.UTF8.GetBytes("no picture")));
        Assert.Null(cache.PngFrom(System.Text.Encoding.UTF8.GetBytes("no picture"))); // also the second time, when it is remembered
    }

    [Fact]
    public void SeveralThreadsAskingForTheSameCoverGetTheSameThumbnail()
    {
        Thumbnails.Cache cache = new Thumbnails.Cache();
        byte[] cover = Encode(Bands(TypeIntRgb, 1200, (1200, Cyan)), ImageFormat.Jpeg);
        byte[][] answers = new byte[8][];
        List<Thread> threads = new List<Thread>();
        for (int i = 0; i < answers.Length; i++)
        {
            int index = i;
            Thread thread = new Thread(() => answers[index] = cache.PngFrom((byte[])cover.Clone()));
            threads.Add(thread);
            thread.Start();
        }
        foreach (Thread thread in threads)
        {
            thread.Join();
        }
        byte[] first = answers[0];
        Assert.NotNull(first);
        foreach (byte[] answer in answers)
        {
            Assert.Same(first, answer);
        }
    }
}
