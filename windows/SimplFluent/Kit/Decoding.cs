using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Simpl.Kit;

/// <summary>
/// A picture from a page or a file, decoded for the screen: never more than <see cref="MostPixels"/> across. A phone's
/// photo is 4,000 pixels across — 48 MB once decoded — and a page shows it a column wide at most; narrowed as it is
/// decoded (a JPEG's decoder reads it at a half or a quarter directly), it takes a fraction of that and of the time, and
/// it is drawn at the size it would have had, so nothing on the page moves. Frozen, so it can be decoded off the window's
/// thread.
/// </summary>
public static class Decoding
{
    /// <summary>The widest a picture is decoded: a wide reading column at 225 % scaling.</summary>
    public const int MostPixels = 2400;

    /// <summary>A decoded picture and the size it has as it is, in device-independent pixels (before any narrowing).</summary>
    public readonly record struct Decoded(BitmapSource Source, double Width, double Height);

    /// <summary>The picture in <paramref name="bytes"/>, decoded no wider than <see cref="MostPixels"/>; null if it is
    /// not one. Safe on any thread.</summary>
    public static Decoded? Picture(byte[] bytes) => Picture(() => new MemoryStream(bytes, writable: false));

    /// <summary>The picture in a file, as <see cref="Picture(byte[])"/>.</summary>
    public static Decoded? Picture(string path) => Picture(() => File.OpenRead(path));

    private static Decoded? Picture(Func<Stream> open)
    {
        try
        {
            int across;
            double width, height;
            using (var probe = open())
            {
                // (its size from its header, nothing decoded yet)
                var f = BitmapFrame.Create(probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                across = f.PixelWidth;
                width = f.Width;
                height = f.Height;
            }
            using var s = open();
            var b = new BitmapImage();
            b.BeginInit();
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.StreamSource = s;
            if (across > MostPixels) b.DecodePixelWidth = MostPixels;
            b.EndInit();
            b.Freeze();
            return new Decoded(b, width, height);
        }
        catch { return null; }
    }

    /// <summary>
    /// The picture in an Image that shows a picture whole, no larger than it is (Stretch Uniform, DownOnly): a narrowed one
    /// is stretched back up to the size it would have had and never past it — the same size on the page, at every width.
    /// </summary>
    public static void Show(Image img, Decoded p)
    {
        if (p.Source.Width < p.Width - 0.5)
        {
            img.StretchDirection = StretchDirection.Both;
            img.MaxWidth = Math.Min(img.MaxWidth, p.Width);
            img.MaxHeight = Math.Min(img.MaxHeight, p.Height);
        }
        img.Source = p.Source;
    }
}
