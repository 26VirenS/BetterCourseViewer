using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Simpl.Support;

namespace Simpl.Kit;

/// <summary>
/// A theme's pictures in their places (the Mac's SlotPhotoView and PhotoLayer, the web's Personalize): the navigation
/// pane and each of the Dashboard's six counters show a drawn scene of the theme worn, or a photo of the student's own,
/// inked tone on tone — a mask coloured a shade off the ground it sits on (a scene in the accent, a photo in the ground
/// lifted a shade) — pinned at its place's foot (a counter's at its bottom right) and frosted a little under the words.
/// Nothing where a place has neither.
/// </summary>
public static class Scenes
{
    private static readonly Dictionary<string, ImageSource?> Images = new();

    /// <summary>Each mask inked in each colour it is drawn in (Inked), until the look changes.</summary>
    private static readonly Dictionary<(ImageSource Mask, Color Ink), BitmapSource> Inks = new();

    static Scenes() => Appearance.Changed += () => Inks.Clear();

    /// <summary>The counters in the Dashboard's order: which of a scene's six drawings each wears, so no two look alike.</summary>
    private static readonly string[] Cards = { "today", "tomorrow", "next", "overdue", "unread", "graded" };

    private static Color Page => Appearance.Dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xf3, 0xf3, 0xf3);

    /// <summary>The paper a picture is inked on: the ground cast with the accent.</summary>
    private static Color Paper => K.Mix(Page, Appearance.Accent, Appearance.Dark ? 0.1 : 0.12);

    /// <summary>A drawn scene's ink: the paper a third of the way to the accent (lib/theme.js SCENE_INK).</summary>
    public static Color SceneInk => K.Mix(Paper, Appearance.Accent, Appearance.Dark ? 0.34 : 0.3);

    /// <summary>A photo's ink: the paper lifted a shade (INK_LIFT).</summary>
    public static Color PhotoInk => K.Mix(Paper, Appearance.Dark ? Colors.White : Colors.Black, Appearance.Dark ? 0.1 : 0.07);

    /// <summary>The picture a place shows now and how it is inked, or null: its photo; else the drawing picked for it, or
    /// the worn theme's (Appearance.SceneFor).</summary>
    public static (ImageSource Mask, bool Scene)? Of(string slot)
    {
        if (Appearance.PhotoKind(slot) == "photo" && Appearance.Photo(slot) is { } file && InkOf(file) is { } ink) return (ink, false);
        if (Appearance.SceneFor(slot) is not { } scene || Appearance.PhotoKind(slot) == "none") return null;
        var i = Array.IndexOf(Cards, slot);
        var name = slot == "side" ? $"Theme{scene}Side" : $"Theme{scene}Card{Math.Max(0, i)}";
        return Load(name) is { } img ? (img, true) : null;
    }

    private static ImageSource? Load(string name)
    {
        if (Images.TryGetValue(name, out var have)) return have;
        ImageSource? img = null;
        try
        {
            var b = new BitmapImage();
            b.BeginInit();
            b.UriSource = new Uri($"pack://application:,,,/Assets/Themes/{name}.png");
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.EndInit();
            b.Freeze();
            img = b;
        }
        catch (Exception e) { Log.Write($"scene {name}: {e.Message}"); }
        Images[name] = img;
        return img;
    }

    /// <summary>
    /// The picture a place shows, filling it (put it first in a Grid, under what is written there): its ink pinned at
    /// the place's foot — a counter's at its bottom right, the pane's at its bottom middle — zoomed and moved as set (or
    /// as <paramref name="pan"/> says, while it is being dragged in Settings' preview), and frosted.
    /// </summary>
    public static FrameworkElement? Backdrop(string slot, bool frost = true, (double X, double Y)? pan = null)
    {
        var side = slot == "side";
        var zoom = Math.Clamp(Appearance.Zoom(slot), 0.5, 3);
        // (0.3.59) a photo with its Ink switch off: the photo itself, whole, nothing over it
        if (Appearance.Plain(slot) && Appearance.Photo(slot) is { } file && Original(file) is { } photo)
        {
            var o = new Grid { IsHitTestVisible = false, ClipToBounds = true };
            o.Children.Add(new Rectangle { Fill = Placed(photo, side ? AlignmentX.Center : AlignmentX.Right, zoom, side ? 0.5 : 1, pan ?? Appearance.Pan(slot)), IsHitTestVisible = false });
            return o;
        }
        if (Of(slot) is not { } pic) return null;
        var g = new Grid { IsHitTestVisible = false, ClipToBounds = true };
        g.Children.Add(Drawn(pic.Mask, pic.Scene ? SceneInk : PhotoInk, side ? AlignmentX.Center : AlignmentX.Right, zoom, side ? 0.5 : 1, pan ?? Appearance.Pan(slot)));
        if (frost && pic.Scene) g.Children.Add(new Rectangle { Fill = K.Brush(Color.FromArgb(56, Page.R, Page.G, Page.B)) });
        return g;
    }

    /// <summary>
    /// (0.3.59) The words on a photo with its Ink switch off (the web's .bcv-side--plain, the Mac's PlainPhotoWords):
    /// dark in either look, with a gentle white border round them — a soft white edge, close in — so they read on any
    /// photo. The theme's text brushes are given again for this part alone; off, it is as it was.
    /// One shadow round a thin letter is too faint over a dark photo, so the white is built up: a close shadow on each
    /// of its parts and on each of theirs, under the soft one round the whole — near solid at the letters, soft beyond
    /// (the web's ring of white and its glow). A part's own effect is left as it is.
    /// </summary>
    public static void PlainWords(FrameworkElement words, bool on)
    {
        foreach (var (key, color) in PlainInks)
        {
            if (on) words.Resources[key] = K.Brush(color);
            else words.Resources.Remove(key);
        }
        words.Effect = on ? HaloSoft : null;
        foreach (var part in Parts(words))
        {
            if (!Close(part, on)) continue;
            foreach (var inner in Parts(part)) Close(inner, on);
        }
    }

    private static readonly System.Windows.Media.Effects.DropShadowEffect HaloClose = Halo(2), HaloSoft = Halo(5);
    private static System.Windows.Media.Effects.DropShadowEffect Halo(double blur)
    {
        var e = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.White, ShadowDepth = 0, BlurRadius = blur, Opacity = 1 };
        e.Freeze();
        return e;
    }
    /// <summary>The close white shadow put on (or taken off) one part, unless it has an effect of its own.</summary>
    private static bool Close(FrameworkElement part, bool on)
    {
        if (part.Effect != null && part.Effect != HaloClose) return false;
        part.Effect = on ? HaloClose : null;
        return true;
    }
    private static IEnumerable<FrameworkElement> Parts(FrameworkElement e) => e switch
    {
        Panel p => p.Children.OfType<FrameworkElement>().ToList(),
        Decorator { Child: FrameworkElement c } => new[] { c },
        ContentControl { Content: FrameworkElement c } => new[] { c },
        _ => Array.Empty<FrameworkElement>(),
    };

    private static readonly (string Key, Color Color)[] PlainInks =
    {
        (B.Text, Color.FromRgb(0x1c, 0x1c, 0x1e)), (B.Text2, Color.FromRgb(0x3a, 0x3a, 0x3c)), (B.Text3, Color.FromRgb(0x48, 0x48, 0x4a)),
    };

    /// <summary>(0.3.59) A photo of the student's own as it is (for its Ink switch off), read once.</summary>
    private static ImageSource? Original(string file)
    {
        if (Images.TryGetValue(file, out var have)) return have;
        ImageSource? img = null;
        try
        {
            var b = new BitmapImage();
            b.BeginInit();
            b.UriSource = new Uri(file);
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            b.DecodePixelWidth = 1600; // (no larger than a place shows it)
            b.EndInit();
            b.Freeze();
            img = b;
        }
        catch (Exception e) { Log.Write("photo: " + e.Message); }
        Images[file] = img;
        return img;
    }

    /// <summary>
    /// The page's colour laid over a small card's scene where its words are (a Dashboard counter: its name over its number
    /// at the left), about 70% there and thinning towards the card's right edge — so the scene's rings and skyline stay
    /// faint behind the words and show at the edge. A plain gradient fill: no opacity mask to draw again each frame.
    /// </summary>
    public static Rectangle WordsVeil() => new()
    {
        IsHitTestVisible = false,
        Fill = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(190, Page.R, Page.G, Page.B), 0),
            new(Color.FromArgb(176, Page.R, Page.G, Page.B), 0.68),
            new(Color.FromArgb(40, Page.R, Page.G, Page.B), 1),
        }, new Point(0, 0.5), new Point(1, 0.5)),
    };

    /// <summary>A theme's drawing for its tile in Settings: its scene inked in the theme's own colour on its paper.</summary>
    public static FrameworkElement? Swatch(string scene, string image, Color accent)
    {
        if (Load($"Theme{scene}{image}") is not { } img) return null;
        var paper = K.Mix(Page, accent, Appearance.Dark ? 0.1 : 0.12);
        var ink = K.Mix(paper, accent, Appearance.Dark ? 0.34 : 0.3);
        var g = new Grid { Background = K.Brush(paper), ClipToBounds = true };
        g.Children.Add(Drawn(img, ink, AlignmentX.Right));
        return g;
    }

    /// <summary>
    /// A mask drawn in its ink, filling its place: pinned at the foot (and the side asked), zoomed from there. The mask
    /// is inked once into a picture of its own, drawn as it is — not the ink through an opacity mask, a layer of its
    /// own drawn again on every frame (on the processor, where WPF draws without the graphics card).
    /// </summary>
    private static Rectangle Drawn(ImageSource mask, Color ink, AlignmentX x, double zoom = 1, double originX = 1, (double X, double Y) pan = default)
    {
        var inked = Inked(mask, ink);
        var brush = Placed(inked ?? mask, x, zoom, originX, pan);
        // (were the inking to fail: the ink through the mask, as before)
        return inked != null
            ? new Rectangle { Fill = brush, IsHitTestVisible = false }
            : new Rectangle { Fill = K.Brush(ink), OpacityMask = brush, IsHitTestVisible = false };
    }

    /// <summary>A picture filling its place, pinned at the foot (and the side asked), zoomed from there and moved by a share
    /// of the place (the brush's own relative units).</summary>
    private static ImageBrush Placed(ImageSource img, AlignmentX x, double zoom = 1, double originX = 1, (double X, double Y) pan = default)
    {
        var brush = new ImageBrush(img) { Stretch = Stretch.UniformToFill, AlignmentX = x, AlignmentY = AlignmentY.Bottom };
        var moved = Math.Abs(pan.X) > 0.001 || Math.Abs(pan.Y) > 0.001;
        if (Math.Abs(zoom - 1) > 0.001 && moved) brush.RelativeTransform = new TransformGroup { Children = { new ScaleTransform(zoom, zoom, originX, 1), new TranslateTransform(pan.X, pan.Y) } };
        else if (Math.Abs(zoom - 1) > 0.001) brush.RelativeTransform = new ScaleTransform(zoom, zoom, originX, 1);
        else if (moved) brush.RelativeTransform = new TranslateTransform(pan.X, pan.Y);
        return brush;
    }

    /// <summary>A mask in a colour: the ink where the mask is, as much as the mask is there (premultiplied), frozen.</summary>
    private static BitmapSource? Inked(ImageSource mask, Color ink)
    {
        if (Inks.TryGetValue((mask, ink), out var have)) return have;
        if (mask is not BitmapSource src) return null;
        try
        {
            var pbgra = new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);
            int w = pbgra.PixelWidth, h = pbgra.PixelHeight;
            var px = new byte[w * h * 4];
            pbgra.CopyPixels(px, w * 4, 0);
            for (var i = 0; i < px.Length; i += 4)
            {
                var a = (px[i + 3] * ink.A + 127) / 255;
                px[i] = (byte)((ink.B * a + 127) / 255);
                px[i + 1] = (byte)((ink.G * a + 127) / 255);
                px[i + 2] = (byte)((ink.R * a + 127) / 255);
                px[i + 3] = (byte)a;
            }
            var bmp = BitmapSource.Create(w, h, src.DpiX, src.DpiY, PixelFormats.Pbgra32, null, px, w * 4);
            bmp.Freeze();
            Inks[(mask, ink)] = bmp;
            return bmp;
        }
        catch (Exception e) { Log.Write("ink: " + e.Message); return null; }
    }

    // ---- a photo of the student's own, inked (lib/theme.js inkOf, the Mac's inkMask) ----

    private static ImageSource? InkOf(string file)
    {
        var inkFile = file + ".ink.png";
        if (Images.TryGetValue(inkFile, out var have)) return have;
        ImageSource? img = null;
        try
        {
            if (!File.Exists(inkFile) || File.GetLastWriteTimeUtc(inkFile) < File.GetLastWriteTimeUtc(file)) MakeInk(file, inkFile);
            var b = new BitmapImage();
            b.BeginInit();
            b.UriSource = new Uri(inkFile);
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            b.EndInit();
            b.Freeze();
            img = b;
        }
        catch (Exception e) { Log.Write("ink: " + e.Message); }
        Images[inkFile] = img;
        return img;
    }

    /// <summary>Forget what was inked before (a photo replaced or removed).</summary>
    public static void Forget()
    {
        Images.Clear();
        Inks.Clear();
    }

    /// <summary>
    /// A photo inked down to two tones, as the web inks it: the darkest part of it (below four fifths of Otsu's split)
    /// and its outlines (Sobel) are the ink, the rest the paper — a mask, white where the ink goes, clear elsewhere.
    /// Laid out by area (1,000 across for a 16:9 picture).
    /// </summary>
    private static void MakeInk(string file, string to)
    {
        var src = new BitmapImage();
        src.BeginInit();
        src.UriSource = new Uri(file);
        src.CacheOption = BitmapCacheOption.OnLoad;
        src.EndInit();
        var ratio = (double)src.PixelHeight / Math.Max(1, src.PixelWidth);
        var w = Math.Max(8, (int)Math.Round(Math.Sqrt(1000.0 * 1000 * 0.5625 / ratio)));
        var h = Math.Max(8, (int)Math.Round(w * ratio));
        var scaled = new TransformedBitmap(src, new ScaleTransform((double)w / src.PixelWidth, (double)h / src.PixelHeight));
        var bgra = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        w = bgra.PixelWidth;
        h = bgra.PixelHeight;
        var px = new byte[w * h * 4];
        bgra.CopyPixels(px, w * 4, 0);
        var n = w * h;
        var lum = new double[n];
        for (var i = 0; i < n; i++) lum[i] = 0.2126 * px[i * 4 + 2] + 0.7152 * px[i * 4 + 1] + 0.0722 * px[i * 4];
        // a light blur first: grain is not an outline
        var sm = new double[n];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                double a = 0;
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                        a += lum[Math.Clamp(y + dy, 0, h - 1) * w + Math.Clamp(x + dx, 0, w - 1)];
                sm[y * w + x] = a / 9;
            }
        // Otsu's split: the threshold that keeps the two tones most apart
        var hist = new double[256];
        foreach (var v in sm) hist[Math.Clamp((int)Math.Round(v), 0, 255)]++;
        double sum = 0;
        for (var t = 0; t < 256; t++) sum += t * hist[t];
        double sumB = 0, wB = 0, best = 0, split = 128;
        for (var t = 0; t < 256; t++)
        {
            wB += hist[t];
            if (wB == 0) continue;
            var wF = n - wB;
            if (wF == 0) break;
            sumB += t * hist[t];
            var mB = sumB / wB;
            var mF = (sum - sumB) / wF;
            var v = wB * wF * (mB - mF) * (mB - mF);
            if (v > best) { best = v; split = t; }
        }
        var deep = split * 0.8;
        var outPx = new byte[n * 4];
        for (var y = 0; y < h; y++)
        {
            int y0 = Math.Max(0, y - 1), y1 = Math.Min(h - 1, y + 1);
            for (var x = 0; x < w; x++)
            {
                int x0 = Math.Max(0, x - 1), x1 = Math.Min(w - 1, x + 1);
                var gx = -sm[y0 * w + x0] - 2 * sm[y * w + x0] - sm[y1 * w + x0] + sm[y0 * w + x1] + 2 * sm[y * w + x1] + sm[y1 * w + x1];
                var gy = -sm[y0 * w + x0] - 2 * sm[y0 * w + x] - sm[y0 * w + x1] + sm[y1 * w + x0] + 2 * sm[y1 * w + x] + sm[y1 * w + x1];
                var edge = Math.Clamp((Math.Sqrt(gx * gx + gy * gy) - 22) * 3.5, 0, 255);
                var lv = sm[y * w + x];
                var shape = lv < deep ? Math.Min(255, (deep - lv) * 24) : 0;
                var a = (byte)Math.Max(shape, edge);
                var o = (y * w + x) * 4;
                outPx[o] = a; outPx[o + 1] = a; outPx[o + 2] = a; outPx[o + 3] = a; // (white, premultiplied)
            }
        }
        var mask = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, outPx, w * 4);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(mask));
        using var fs = File.Create(to);
        enc.Save(fs);
    }
}
