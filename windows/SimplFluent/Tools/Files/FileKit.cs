using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Simpl.Kit;
using Simpl.Support;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using Wpf.Ui.Controls;
using BitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;
using BitmapEncoder = Windows.Graphics.Imaging.BitmapEncoder;
using BitmapFrame = System.Windows.Media.Imaging.BitmapFrame;
using WinPdfDocument = Windows.Data.Pdf.PdfDocument;
using WinPdfPage = Windows.Data.Pdf.PdfPage;

namespace Simpl.Tools;

/// <summary>
/// What the file tools share on Windows (the Mac's PageSection, RowDivider and ToolFiles as these tools use them): a
/// section's heading with its controls, a list in a card, links, a line under a step, the clipboard, and the files a
/// launch hands a tool for the screenshot suite.
/// </summary>
internal static class FileKit
{
    public static readonly Color Green = Color.FromRgb(0x2f, 0xa8, 0x4f);
    public static readonly Color Orange = Color.FromRgb(0xf7, 0x63, 0x0c);
    public static readonly Color Red = Color.FromRgb(0xe8, 0x3b, 0x30);

    /// <summary>A tool's own colour (its tile's).</summary>
    public static Color Tint(string key) => ToolsCenter.KindOf(key)?.Color ?? Color.FromRgb(0x8a, 0x8a, 0x8a);

    /// <summary>The Mac's PageSection: its title, a word beside it (a count), its controls at the right, and its content under.</summary>
    public static FrameworkElement Section(string title, string? trailing, UIElement? accessory, UIElement content) =>
        K.V(0, Heading.Section(title, trailing, accessory), content);

    /// <summary>Rows in a card with a hairline between them, set in from the left.</summary>
    public static Border List(IEnumerable<UIElement> rows, double inset)
    {
        var v = K.V(0);
        foreach (var r in rows)
        {
            if (v.Children.Count > 0) v.Children.Add(K.Divider(inset));
            v.Children.Add(r);
        }
        return K.Card(v, new Thickness(0, 4, 0, 4));
    }

    /// <summary>A button of the tools: its words, its symbol, and the accent for the step it is for.</summary>
    public static Wpf.Ui.Controls.Button Button(string text, SymbolRegular? icon, Action click, bool accent = false, string? tip = null)
    {
        var b = K.Button(text, click, accent, icon);
        b.MinWidth = 0;
        if (tip != null) b.ToolTip = tip;
        return b;
    }

    /// <summary>A link-like button (the Mac's .buttonStyle(.link)), with a symbol before its words if it has one.</summary>
    public static Wpf.Ui.Controls.Button Link(string text, Action click, SymbolRegular? icon = null, string? tip = null)
    {
        var b = K.Link(text, click);
        if (icon is { } i && b.Content is System.Windows.Controls.TextBlock t)
        {
            b.Content = null;
            b.Content = K.H(6, K.Icon(i, 14, B.AccentText), t);
        }
        if (tip != null) b.ToolTip = tip;
        return b;
    }

    /// <summary>A small line under a step (the Mac's footnote).</summary>
    public static System.Windows.Controls.TextBlock Foot(string text) => K.Wrapped(text, Ty.Caption, B.Text2);

    /// <summary>A quiet line with its symbol (the Mac's ToolNote in the secondary colour).</summary>
    public static FrameworkElement Info(string text) => ToolKit.Note(text, SymbolRegular.Info24, K.Tone("gray"));

    /// <summary>A line that says something went well.</summary>
    public static FrameworkElement Done(string text) => ToolKit.Note(text, SymbolRegular.CheckmarkCircle24, Green);

    /// <summary>The words put on the clipboard (another app holding it a moment: let go).</summary>
    public static void Copy(string text)
    {
        try { Clipboard.SetText(text); } catch (Exception e) { Log.Write("copy: " + e.Message); }
    }

    /// <summary>The window a tool's dialogs belong to.</summary>
    public static Window? Owner(FrameworkElement view, ToolContext c) => Window.GetWindow(view) ?? c.Owner ?? Application.Current?.MainWindow;

    /// <summary>An element let go of by the parent it was in (to be put somewhere else).</summary>
    public static void Detach(FrameworkElement e)
    {
        switch (e.Parent)
        {
            case Panel p: p.Children.Remove(e); break;
            case Decorator d: d.Child = null; break;
            case ContentControl c: c.Content = null; break;
        }
    }

    /// <summary>
    /// A tool's scrolling page (ToolKit.Page) with new content: the same page kept when there is one, so it stays
    /// scrolled where it was while the tool is built again.
    /// </summary>
    public static ScrollViewer Page(ScrollViewer? page, UIElement content, double maxWidth = 1480)
    {
        if (page == null) return ToolKit.Page(content, maxWidth);
        if (content is FrameworkElement fe) { fe.MaxWidth = maxWidth; fe.HorizontalAlignment = HorizontalAlignment.Left; }
        page.Content = content;
        return page;
    }

    /// <summary>
    /// The files a launch hands the tool open first (the screenshot suite: --tool-file C:\a.pdf, several joined by "|",
    /// or --tool-file sample for a PDF of a page of notes made on the spot).
    /// </summary>
    public static IReadOnlyList<string> Launched()
    {
        if (Launch.Take("", "tool-file") is not { Length: > 0 } v) return Array.Empty<string>();
        if (v == "sample") return Sample() is { } s ? new[] { s } : Array.Empty<string>();
        return v.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(File.Exists).ToList();
    }

    /// <summary>A PDF of two pages of a student's notes (for the screenshot suite).</summary>
    private static string? Sample()
    {
        try
        {
            if (!PdfSharp.Fonts.GlobalFontSettings.UseWindowsFontsUnderWindows) PdfSharp.Fonts.GlobalFontSettings.UseWindowsFontsUnderWindows = true;
            var path = Path.Combine(AppPaths.Root, "Biology notes.pdf");
            using var doc = new PdfSharp.Pdf.PdfDocument();
            var title = new PdfSharp.Drawing.XFont("Arial", 22, PdfSharp.Drawing.XFontStyleEx.Bold);
            var body = new PdfSharp.Drawing.XFont("Arial", 12);
            string[][] pages =
            {
                new[]
                {
                    "Photosynthesis",
                    "Plants make their own food from light, water and carbon dioxide. The light is caught by chlorophyll in the chloroplasts of the leaf's cells.",
                    "The light-dependent reactions take place in the thylakoid membranes. They split water, give off oxygen and make ATP and NADPH.",
                    "The Calvin cycle takes place in the stroma. It uses the ATP and NADPH to fix carbon dioxide into glucose.",
                    "Factors that limit the rate: the intensity of the light, the concentration of carbon dioxide, and the temperature.",
                },
                new[]
                {
                    "Cellular respiration",
                    "Glucose is broken down to release energy as ATP. Aerobic respiration needs oxygen and happens mostly in the mitochondria.",
                    "Glycolysis, the Krebs cycle and the electron transport chain follow one another; most of the ATP is made in the last.",
                },
            };
            foreach (var lines in pages)
            {
                var page = doc.AddPage();
                using var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
                var tf = new PdfSharp.Drawing.Layout.XTextFormatter(gfx);
                gfx.DrawString(lines[0], title, PdfSharp.Drawing.XBrushes.Black, new PdfSharp.Drawing.XPoint(72, 96));
                var y = 130.0;
                foreach (var para in lines.Skip(1))
                {
                    var rect = new PdfSharp.Drawing.XRect(72, y, page.Width.Point - 144, 60);
                    tf.DrawString(para, body, PdfSharp.Drawing.XBrushes.Black, rect);
                    y += 62;
                }
            }
            doc.Save(path);
            return path;
        }
        catch (Exception e)
        {
            Log.Write("sample pdf: " + e.Message);
            return null;
        }
    }
}

/// <summary>Windows' own PDF reader (Windows.Data.Pdf): a PDF opened, and its pages drawn as pictures.</summary>
internal static class WinPdf
{
    /// <summary>Two pages drawn at a time at most, so a long PDF's thumbnails do not crowd the reader.</summary>
    private static readonly SemaphoreSlim Gate = new(2);

    public static async Task<WinPdfDocument> Open(byte[] bytes)
    {
        var ras = new InMemoryRandomAccessStream();
        await ras.WriteAsync(bytes.AsBuffer());
        ras.Seek(0);
        return await WinPdfDocument.LoadFromStreamAsync(ras);
    }

    /// <summary>A page drawn as a PNG file's bytes, so many pixels across (and as many down as it takes).</summary>
    public static async Task<byte[]> Png(WinPdfPage page, double width, double? height = null)
    {
        var size = page.Size;
        var w = (uint)Math.Clamp(Math.Round(width), 1, 8000);
        var h = (uint)Math.Clamp(Math.Round(height ?? width * size.Height / Math.Max(1, size.Width)), 1, 8000);
        await Gate.WaitAsync();
        try
        {
            using var ras = new InMemoryRandomAccessStream();
            var o = new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = w, DestinationHeight = h, BitmapEncoderId = BitmapEncoder.PngEncoderId };
            await page.RenderToStreamAsync(ras, o);
            return await WinImage.Bytes(ras);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>A page drawn as a picture to show.</summary>
    public static async Task<BitmapSource> Picture(WinPdfPage page, double width, double? height = null) => WinImage.Show(await Png(page, width, height));
}

/// <summary>Windows' own pictures (Windows.Graphics.Imaging): read, turned as the camera held them, and written.</summary>
internal static class WinImage
{
    public static async Task<byte[]> Bytes(IRandomAccessStream s)
    {
        s.Seek(0);
        using var input = s.GetInputStreamAt(0).AsStreamForRead();
        var ms = new MemoryStream();
        await input.CopyToAsync(ms);
        return ms.ToArray();
    }

    /// <summary>A picture's bytes (PNG, JPEG …) as a picture to show.</summary>
    public static BitmapSource Show(byte[] data)
    {
        var b = new BitmapImage();
        b.BeginInit();
        b.CacheOption = BitmapCacheOption.OnLoad;
        b.StreamSource = new MemoryStream(data);
        b.EndInit();
        b.Freeze();
        return b;
    }

    /// <summary>A picture as Windows decodes it, and its resolution.</summary>
    public sealed record Decoded(SoftwareBitmap Bitmap, double DpiX, double DpiY);

    /// <summary>A picture file read (any kind Windows has a codec for), turned the way it was held, at most so many pixels across.</summary>
    public static async Task<Decoded> Read(string path, uint maxSide = 0, BitmapAlphaMode alpha = BitmapAlphaMode.Straight)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        return await Read(bytes, maxSide, alpha);
    }

    public static async Task<Decoded> Read(byte[] bytes, uint maxSide = 0, BitmapAlphaMode alpha = BitmapAlphaMode.Straight)
    {
        using var ras = new InMemoryRandomAccessStream();
        await ras.WriteAsync(bytes.AsBuffer());
        ras.Seek(0);
        var d = await BitmapDecoder.CreateAsync(ras);
        var t = new BitmapTransform { InterpolationMode = BitmapInterpolationMode.Fant };
        var big = Math.Max(d.PixelWidth, d.PixelHeight);
        if (maxSide > 0 && big > maxSide)
        {
            var k = (double)maxSide / big;
            t.ScaledWidth = (uint)Math.Max(1, Math.Round(d.PixelWidth * k));
            t.ScaledHeight = (uint)Math.Max(1, Math.Round(d.PixelHeight * k));
        }
        SoftwareBitmap sb;
        try { sb = await d.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, alpha, t, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb); }
        catch { sb = await d.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, alpha, t, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage); }
        return new Decoded(sb, d.DpiX, d.DpiY);
    }

    /// <summary>A picture written in a kind of Windows' (PNG, JPEG, TIFF, HEIF …), with a quality for the lossy ones.</summary>
    public static async Task<byte[]> Write(SoftwareBitmap sb, Guid encoder, double? quality = null)
    {
        using var ras = new InMemoryRandomAccessStream();
        BitmapEncoder e;
        if (quality is { } q)
        {
            var props = new BitmapPropertySet { { "ImageQuality", new BitmapTypedValue((float)Math.Clamp(q, 0, 1), Windows.Foundation.PropertyType.Single) } };
            e = await BitmapEncoder.CreateAsync(encoder, ras, props);
        }
        else e = await BitmapEncoder.CreateAsync(encoder, ras);
        e.SetSoftwareBitmap(sb);
        await e.FlushAsync();
        return await Bytes(ras);
    }

    /// <summary>A picture's pixels (blue, green, red, alpha).</summary>
    public static byte[] Pixels(SoftwareBitmap sb)
    {
        var n = (uint)(sb.PixelWidth * sb.PixelHeight * 4);
        var buf = new Windows.Storage.Streams.Buffer(n);
        sb.CopyToBuffer(buf);
        if (buf.Length < n) buf.Length = n;
        return buf.ToArray(0, (int)n);
    }

    /// <summary>A picture laid on white (for a kind that keeps no see-through parts: JPEG, HEIC).</summary>
    public static SoftwareBitmap OnWhite(SoftwareBitmap sb)
    {
        var px = Pixels(sb);
        var premultiplied = sb.BitmapAlphaMode == BitmapAlphaMode.Premultiplied;
        for (var i = 0; i < px.Length; i += 4)
        {
            int a = px[i + 3];
            if (a == 255) continue;
            for (var c = 0; c < 3; c++)
            {
                var v = premultiplied ? px[i + c] : px[i + c] * a / 255;
                px[i + c] = (byte)Math.Min(255, v + (255 - a));
            }
            px[i + 3] = 255;
        }
        return SoftwareBitmap.CreateCopyFromBuffer(px.AsBuffer(), BitmapPixelFormat.Bgra8, sb.PixelWidth, sb.PixelHeight, BitmapAlphaMode.Premultiplied);
    }

    /// <summary>Whether a picture has see-through parts.</summary>
    public static bool HasAlpha(SoftwareBitmap sb)
    {
        var px = Pixels(sb);
        for (var i = 3; i < px.Length; i += 4) if (px[i] != 255) return true;
        return false;
    }

    /// <summary>A decoded picture to show.</summary>
    public static BitmapSource Show(SoftwareBitmap sb)
    {
        var px = Pixels(sb);
        var format = sb.BitmapAlphaMode == BitmapAlphaMode.Straight ? PixelFormats.Bgra32 : PixelFormats.Pbgra32;
        var b = BitmapSource.Create(sb.PixelWidth, sb.PixelHeight, 96, 96, format, null, px, sb.PixelWidth * 4);
        b.Freeze();
        return b;
    }

    /// <summary>A picture WPF holds (the clipboard's) as Windows' own, opaque.</summary>
    public static async Task<SoftwareBitmap> From(BitmapSource source)
    {
        var opaque = new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(opaque));
        var ms = new MemoryStream();
        enc.Save(ms);
        return (await Read(ms.ToArray(), 0, BitmapAlphaMode.Premultiplied)).Bitmap;
    }
}

/// <summary>Windows' own reading of words off a picture (Windows.Media.Ocr), in the languages of the student's profile.</summary>
internal static class WinOcr
{
    private static OcrEngine? engine;
    private static bool tried;

    public const string Missing = "Windows can’t read words on this PC yet: add a language with optical character recognition in Settings ▸ Time & language ▸ Language & region.";

    public static OcrEngine? Engine
    {
        get
        {
            if (tried) return engine;
            tried = true;
            try { engine = OcrEngine.TryCreateFromUserProfileLanguages(); } catch (Exception e) { Log.Write("ocr: " + e.Message); }
            return engine;
        }
    }

    /// <summary>The largest side Windows reads a picture at.</summary>
    public static uint MaxSide
    {
        get { try { return Math.Max(1000u, OcrEngine.MaxImageDimension); } catch { return 2600; } }
    }

    public static async Task<OcrResult> Read(SoftwareBitmap sb)
    {
        var e = Engine ?? throw new InvalidOperationException(Missing);
        if (sb.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || sb.BitmapAlphaMode == BitmapAlphaMode.Straight)
            sb = SoftwareBitmap.Convert(sb, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        return await e.RecognizeAsync(sb);
    }

    /// <summary>The words on a picture, a line at a time, in reading order.</summary>
    public static async Task<string> Text(SoftwareBitmap sb)
    {
        var r = await Read(sb);
        return string.Join("\n", r.Lines.Select(l => l.Text));
    }

    /// <summary>
    /// A PDF's page read: drawn large enough for its words to be clear (two and a half pixels a point, within what
    /// Windows reads), then read. Its words' places are in pixels: <c>PerPoint</c> of them to a point of the page as seen.
    /// </summary>
    public static async Task<(OcrResult Result, double PerPoint)> Page(WinPdfPage page, double shownW, double shownH, double scale = 2.5)
    {
        var k = Math.Min(scale, Math.Min(MaxSide, 4000) / Math.Max(1, Math.Max(shownW, shownH)));
        var png = await WinPdf.Png(page, shownW * k, shownH * k);
        var sb = (await WinImage.Read(png, 0, BitmapAlphaMode.Premultiplied)).Bitmap;
        return (await Read(sb), k);
    }

    /// <summary>A PDF's page read, its size taken from Windows' own measure of it (device-independent pixels).</summary>
    public static Task<(OcrResult Result, double PerPoint)> Page(WinPdfPage page) =>
        Page(page, Math.Max(1, page.Size.Width * 72 / 96), Math.Max(1, page.Size.Height * 72 / 96));

    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
}
