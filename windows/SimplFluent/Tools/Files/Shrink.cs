using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace Simpl.Tools;

// Compress files (0.3.11): the sums behind the Compress files tool, free of any window — a picture read with Windows'
// own codecs (WPF's imaging, which is WIC underneath: JPEG, PNG, BMP, TIFF, GIF, and HEIC or WebP where Windows has the
// codec), turned the way the camera held it, made smaller on its long edge where the preset asks, and written as a JPEG
// of the preset's quality (or a PNG again, for see-through parts); and a PDF's embedded pictures found in its pages'
// resources and written again the same way with PDFsharp, the pages themselves left as they are — never drawn again as
// pictures. "Fit under" searches the quality, then the size, for the largest that lands under the limit.

/// <summary>How small to go: the four presets the tool offers (the Mac's the same).</summary>
internal enum ShrinkPreset { Max, Balanced, Smallest, Fit }

/// <summary>The presets' words, keys and settings.</summary>
internal static class ShrinkPresets
{
    public static readonly ShrinkPreset[] All = { ShrinkPreset.Max, ShrinkPreset.Balanced, ShrinkPreset.Smallest, ShrinkPreset.Fit };

    public static string Key(ShrinkPreset p) => p switch { ShrinkPreset.Max => "max", ShrinkPreset.Balanced => "balanced", ShrinkPreset.Smallest => "smallest", _ => "fit" };

    public static string Label(ShrinkPreset p) => p switch { ShrinkPreset.Max => "Max quality", ShrinkPreset.Balanced => "Balanced", ShrinkPreset.Smallest => "Smallest", _ => "Fit under" };

    /// <summary>A preset by its key or its label ("balanced", "Max quality"); null for anything else.</summary>
    public static ShrinkPreset? Parse(string? s)
    {
        var t = (s ?? "").Trim().ToLowerInvariant();
        foreach (var p in All) if (t == Key(p) || t == Label(p).ToLowerInvariant()) return p;
        return t switch { "max quality" or "quality" => ShrinkPreset.Max, "small" or "smaller" => ShrinkPreset.Smallest, "fit under" => ShrinkPreset.Fit, _ => null };
    }

    /// <summary>The JPEG quality a preset writes at (1–100); Fit under starts here and searches down.</summary>
    public static int Quality(ShrinkPreset p) => p switch { ShrinkPreset.Max => 92, ShrinkPreset.Balanced => 80, ShrinkPreset.Smallest => 62, _ => 82 };

    /// <summary>The most pixels a picture keeps on its long edge (0: never made smaller).</summary>
    public static int LongEdge(ShrinkPreset p) => p switch { ShrinkPreset.Max => 0, ShrinkPreset.Balanced => 2048, ShrinkPreset.Smallest => 1280, _ => 2048 };

    /// <summary>A line for the preset's row.</summary>
    public static string Line(ShrinkPreset p) => p switch
    {
        ShrinkPreset.Max => "Same size, a little smaller on disk.",
        ShrinkPreset.Balanced => "Up to 2048 px, good quality.",
        ShrinkPreset.Smallest => "Up to 1280 px, see-through parts flattened.",
        _ => "The largest that lands under the limit.",
    };
}

/// <summary>
/// What came out: the bytes, the kind written (jpg, png or pdf), its size in pixels (0 for a PDF), whether it landed
/// under the limit asked for (always true but for Fit under that could not), and a word about what was done.
/// </summary>
internal sealed record ShrinkResult(byte[] Bytes, string Ext, int Width, int Height, bool Reached, string? Note);

/// <summary>The compressor's own sums and codecs, free of any window (tested in SimplFluent.Tests/ShrinkTests.cs).</summary>
internal static class Shrink
{
    public const string Filter = "Pictures and PDFs|*.png;*.jpg;*.jpeg;*.heic;*.heif;*.webp;*.bmp;*.tif;*.tiff;*.gif;*.pdf";

    private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".heic", ".heif", ".webp", ".bmp", ".tif", ".tiff", ".gif" };

    public static bool IsImage(string path) => ImageExts.Contains(Path.GetExtension(path).ToLowerInvariant());
    public static bool IsPdf(string path) => Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    public static bool Takes(string path) => IsImage(path) || IsPdf(path);

    /// <summary>"1.2 MB", "410 KB", "900 bytes" — thousands, as File Explorer's figures read.</summary>
    public static string Size(long bytes)
    {
        double size = Math.Max(0, bytes);
        string[] units = { "bytes", "KB", "MB", "GB" };
        var u = 0;
        while (size >= 1000 && u < units.Length - 1) { size /= 1000; u++; }
        return u == 0 ? $"{size:0} {units[u]}" : $"{size.ToString(size < 10 ? "0.##" : size < 100 ? "0.#" : "0", CultureInfo.InvariantCulture)} {units[u]}";
    }

    /// <summary>What a smaller file saved, as a whole percentage (0 when it is no smaller).</summary>
    public static int Saved(long before, long after) => before <= 0 || after >= before ? 0 : (int)Math.Round(100.0 * (before - after) / before, MidpointRounding.AwayFromZero);

    /// <summary>"0.2" MB as bytes (null when it is not a size).</summary>
    public static double? Megabytes(string s)
    {
        var t = s.Replace(",", ".").Trim();
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 && double.IsFinite(v) ? v * 1_000_000 : null;
    }

    // ---- pictures ----

    /// <summary>A picture as read: its pixels the way the camera held it, and whether any part of it is see-through.</summary>
    internal sealed record Picture(BitmapSource Source, bool Alpha);

    /// <summary>
    /// A picture made smaller: a JPEG of the preset's quality, no wider on its long edge than the preset allows — a PNG
    /// with see-through parts stays a PNG (flattened onto white only for Smallest, or to fit). The bytes come back as they
    /// are when nothing smaller could be made (<see cref="ShrinkResult.Note"/> says so), never larger.
    /// </summary>
    public static ShrinkResult Image(byte[] data, string ext, ShrinkPreset preset, double? fitBytes = null)
    {
        var p = Read(data, preset == ShrinkPreset.Max ? 0 : ShrinkPresets.LongEdge(preset));
        var png = p.Alpha && preset != ShrinkPreset.Smallest;
        ShrinkResult r;
        if (preset == ShrinkPreset.Fit)
        {
            var limit = fitBytes ?? 1_000_000;
            r = Fit(data, p, png, limit);
            if (!r.Reached && png)
            {
                // (see-through parts dropped as a last resort: a JPEG on white goes far smaller than any PNG)
                var flat = Fit(data, p, false, limit);
                if (flat.Reached || flat.Bytes.Length < r.Bytes.Length) r = flat with { Note = flat.Reached ? "Flattened onto white to fit under the limit." : flat.Note };
            }
        }
        else r = Encode(p.Source, png, ShrinkPresets.Quality(preset), 0) with { Reached = true };
        if (r.Bytes.Length >= data.Length * 0.98)
            return new ShrinkResult(data, ext.TrimStart('.').ToLowerInvariant() is "jpeg" ? "jpg" : ext.TrimStart('.').ToLowerInvariant(), p.Source.PixelWidth, p.Source.PixelHeight, preset != ShrinkPreset.Fit || data.Length <= (fitBytes ?? 1_000_000), "Already as small as it gets: left as it was.");
        return r;
    }

    /// <summary>The largest picture that lands under the limit: the quality first (92 down to 30), then its size on the
    /// long edge (three quarters at a time, to 320 px), the quality searched again at each; the smallest it could reach
    /// when none lands under, said as such.</summary>
    private static ShrinkResult Fit(byte[] data, Picture p, bool png, double limit)
    {
        ShrinkResult? best = null, smallest = null;
        var edge = Math.Max(p.Source.PixelWidth, p.Source.PixelHeight);
        for (var size = edge; size >= 320; size = (int)Math.Round(size * 0.75))
        {
            var source = size == edge ? p.Source : Scale(p.Source, size);
            if (png)
            {
                var e = Encode(source, true, 100, 0);
                if (smallest == null || e.Bytes.Length < smallest.Bytes.Length) smallest = e;
                if (e.Bytes.Length <= limit) return e with { Reached = true, Note = size == edge ? null : $"Made smaller to {e.Width} × {e.Height} to fit." };
                continue;
            }
            int lo = 30, hi = 92;
            var top = Encode(source, false, hi, 0);
            if (smallest == null || top.Bytes.Length < smallest.Bytes.Length) smallest = top;
            if (top.Bytes.Length <= limit) best = top;
            else
            {
                var floor = Encode(source, false, lo, 0);
                if (floor.Bytes.Length < smallest.Bytes.Length) smallest = floor;
                if (floor.Bytes.Length <= limit)
                {
                    best = floor;
                    while (hi - lo > 3)
                    {
                        var mid = (lo + hi) / 2;
                        var e = Encode(source, false, mid, 0);
                        if (e.Bytes.Length <= limit) { best = e; lo = mid; } else hi = mid;
                    }
                }
            }
            if (best != null) return best with { Reached = true, Note = size == edge ? null : $"Made smaller to {best.Width} × {best.Height} to fit." };
        }
        var s = smallest ?? Encode(p.Source, png, 30, 320);
        return s with { Reached = false, Note = $"The smallest it could reach is {Size(s.Bytes.Length)}." };
    }

    /// <summary>A picture file read: the way the camera held it, no wider than <paramref name="longEdge"/> (0: as it is).</summary>
    public static Picture Read(byte[] data, int longEdge)
    {
        BitmapFrame frame;
        try
        {
            var decoder = BitmapDecoder.Create(new MemoryStream(data), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            frame = decoder.Frames[0];
        }
        catch (NotSupportedException) { throw new ShrinkError("Windows has no codec for this picture (HEIC and WebP need their extensions from the Microsoft Store)."); }
        catch (Exception e) when (e is not ShrinkError) { throw new ShrinkError("The picture could not be read."); }
        var orientation = Orientation(frame);
        BitmapSource source = frame;
        var alpha = HasAlpha(frame);
        var edge = Math.Max(frame.PixelWidth, frame.PixelHeight);
        if (longEdge > 0 && edge > longEdge) source = Scale(frame, longEdge);
        source = Turn(source, orientation);
        return new Picture(source, alpha);
    }

    /// <summary>A picture scaled so its long edge is so many pixels (WPF's scaler, which averages the pixels it drops).</summary>
    public static BitmapSource Scale(BitmapSource source, int longEdge)
    {
        var edge = Math.Max(source.PixelWidth, source.PixelHeight);
        if (edge <= longEdge) return source;
        var k = (double)longEdge / edge;
        var t = new TransformedBitmap(source, new ScaleTransform(k, k));
        t.Freeze();
        return t;
    }

    /// <summary>The camera's orientation tag (1–8), read off the picture's metadata; 1 when there is none.</summary>
    private static int Orientation(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata m && m.ContainsQuery("System.Photo.Orientation") && m.GetQuery("System.Photo.Orientation") is { } o)
                return Convert.ToInt32(o, CultureInfo.InvariantCulture);
        }
        catch (Exception) { /* (a picture whose metadata cannot be read is upright) */ }
        return 1;
    }

    /// <summary>A picture turned the way the camera held it (EXIF orientations 2–8).</summary>
    private static BitmapSource Turn(BitmapSource s, int orientation)
    {
        if (orientation is < 2 or > 8) return s;
        var g = new TransformGroup();
        if (orientation is 2 or 4 or 5 or 7) g.Children.Add(new ScaleTransform(-1, 1));
        var turn = orientation switch { 3 or 4 => 180, 5 or 6 => 90, 7 or 8 => 270, _ => 0 };
        if (turn != 0) g.Children.Add(new RotateTransform(turn));
        var t = new TransformedBitmap(s, g);
        t.Freeze();
        return t;
    }

    /// <summary>Whether any pixel is see-through.</summary>
    public static bool HasAlpha(BitmapSource s)
    {
        var f = s.Format;
        if (f != PixelFormats.Bgra32 && f != PixelFormats.Pbgra32 && f != PixelFormats.Rgba64 && f != PixelFormats.Prgba64 && f != PixelFormats.Rgba128Float && f != PixelFormats.Prgba128Float && f != PixelFormats.Indexed8 && f != PixelFormats.Indexed4 && f != PixelFormats.Indexed2 && f != PixelFormats.Indexed1)
            return false;
        var c = s.Format == PixelFormats.Bgra32 ? s : new FormatConvertedBitmap(s, PixelFormats.Bgra32, null, 0);
        var w = c.PixelWidth;
        var h = c.PixelHeight;
        var row = new byte[w * 4];
        // (every row of a small picture; every eighth of a large one — a see-through corner is rows deep)
        var step = h > 2000 ? 8 : h > 600 ? 3 : 1;
        for (var y = 0; y < h; y += step)
        {
            c.CopyPixels(new Int32Rect(0, y, w, 1), row, w * 4, 0);
            for (var i = 3; i < row.Length; i += 4) if (row[i] != 255) return true;
        }
        return false;
    }

    /// <summary>A picture written: a PNG as it is, or a JPEG of the quality — laid on white where it had see-through parts.</summary>
    public static ShrinkResult Encode(BitmapSource source, bool png, int quality, int longEdge)
    {
        if (longEdge > 0) source = Scale(source, longEdge);
        var ms = new MemoryStream();
        if (png)
        {
            var e = new PngBitmapEncoder();
            e.Frames.Add(BitmapFrame.Create(source));
            e.Save(ms);
            return new ShrinkResult(ms.ToArray(), "png", source.PixelWidth, source.PixelHeight, true, null);
        }
        var flat = OnWhite(source);
        var j = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
        j.Frames.Add(BitmapFrame.Create(flat));
        j.Save(ms);
        return new ShrinkResult(ms.ToArray(), "jpg", flat.PixelWidth, flat.PixelHeight, true, null);
    }

    /// <summary>A picture with no see-through parts: laid on white where it had any, in 24-bit colour (or grey as it was).</summary>
    public static BitmapSource OnWhite(BitmapSource s)
    {
        if (s.Format == PixelFormats.Gray8 || s.Format == PixelFormats.Bgr24) return s;
        if (!HasAlpha(s))
        {
            var plain = new FormatConvertedBitmap(s, PixelFormats.Bgr24, null, 0);
            plain.Freeze();
            return plain;
        }
        var c = s.Format == PixelFormats.Bgra32 ? s : new FormatConvertedBitmap(s, PixelFormats.Bgra32, null, 0);
        int w = c.PixelWidth, h = c.PixelHeight;
        var px = new byte[w * h * 4];
        c.CopyPixels(px, w * 4, 0);
        var outPx = new byte[w * h * 3];
        for (int i = 0, o = 0; i < px.Length; i += 4, o += 3)
        {
            int a = px[i + 3];
            for (var k = 0; k < 3; k++) outPx[o + k] = (byte)((px[i + k] * a + 255 * (255 - a)) / 255);
        }
        var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr24, null, outPx, w * 3);
        b.Freeze();
        return b;
    }

    // ---- PDFs ----

    /// <summary>What happened to a PDF's pictures: how many were written smaller, how many were left (a kind the tool
    /// does not touch), and how many pages the file has.</summary>
    internal sealed record PdfReport(int Changed, int Left, int Pages);

    /// <summary>
    /// A PDF's embedded pictures written again smaller — JPEGs (DCTDecode) and plain 8-bit RGB or grey pictures
    /// (FlateDecode) in the pages' and their forms' resources, written back as JPEGs of the preset's quality, no wider
    /// than the preset's long edge — the pages, their words and their links untouched. Pictures of other kinds (masks,
    /// indexed colours, CMYK, 16-bit) are left as they are. The bytes come back as they are when nothing got smaller.
    /// </summary>
    public static ShrinkResult Pdf(byte[] data, ShrinkPreset preset, double? fitBytes = null)
    {
        if (preset != ShrinkPreset.Fit)
        {
            var (bytes, report) = PdfAt(data, ShrinkPresets.Quality(preset), ShrinkPresets.LongEdge(preset));
            return Finish(data, bytes, report, true, true, null);
        }
        var limit = fitBytes ?? 1_000_000;
        (byte[] Bytes, PdfReport Report)? smallest = null;
        foreach (var (q, edge) in new[] { (82, 2048), (70, 2048), (60, 1600), (50, 1280), (40, 1000), (30, 800) })
        {
            var step = PdfAt(data, q, edge);
            if (smallest == null || step.Bytes.Length < smallest.Value.Bytes.Length) smallest = step;
            if (step.Bytes.Length <= limit) return Finish(data, step.Bytes, step.Report, true, data.Length <= limit, null);
        }
        var s = smallest!.Value;
        return Finish(data, s.Bytes, s.Report, false, data.Length <= limit, $"The smallest it could reach is {Size(s.Bytes.Length)}.");
    }

    /// <summary>The result, or the file as it was when nothing got smaller (<paramref name="reachedAsItWas"/>: whether the
    /// file as it was already lands under a limit asked for).</summary>
    private static ShrinkResult Finish(byte[] original, byte[] bytes, PdfReport r, bool reached, bool reachedAsItWas, string? note)
    {
        var words = r.Changed == 0
            ? r.Left > 0 ? "Its pictures are of a kind that is left as it is." : "It has no pictures to make smaller."
            : $"{ToolKit.Plural(r.Changed, "picture")} written smaller{(r.Left > 0 ? $", {r.Left} left as {(r.Left == 1 ? "it was" : "they were")}" : "")}.";
        if (bytes.Length >= original.Length * 0.98) return new ShrinkResult(original, "pdf", 0, 0, reachedAsItWas, "Already as small as it gets: left as it was. " + words);
        return new ShrinkResult(bytes, "pdf", 0, 0, reached, note == null ? words : $"{words} {note}");
    }

    /// <summary>The PDF with its pictures written again at a quality and a long edge.</summary>
    public static (byte[] Bytes, PdfReport Report) PdfAt(byte[] data, int quality, int longEdge)
    {
        PdfDocument doc;
        try { doc = PdfReader.Open(new MemoryStream(data), PdfDocumentOpenMode.Modify); }
        catch (Exception e)
        {
            if (e.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase))
                throw new ShrinkError("The PDF is locked with a password.");
            throw new ShrinkError("The PDF could not be opened.");
        }
        using (doc)
        {
            doc.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
            doc.Options.CompressContentStreams = true;
            doc.Options.UseFlateDecoderForJpegImages = PdfUseFlateDecoderForJpegImages.Never;
            var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
            int changed = 0, left = 0;
            // (everything the report says is read before the save: a saved PdfDocument may not be looked at again)
            var pages = doc.PageCount;
            for (var i = 0; i < pages; i++)
                WalkResources(doc.Pages[i].Elements.GetDictionary("/Resources"), seen, quality, longEdge, ref changed, ref left, 0);
            var ms = new MemoryStream();
            doc.Save(ms, false);
            return (ms.ToArray(), new PdfReport(changed, left, pages));
        }
    }

    /// <summary>The pictures among a resource dictionary's XObjects, and those of the forms among them (a few levels deep).</summary>
    private static void WalkResources(PdfDictionary? resources, HashSet<PdfDictionary> seen, int quality, int longEdge, ref int changed, ref int left, int depth)
    {
        if (resources == null || depth > 4) return;
        var xobjects = resources.Elements.GetDictionary("/XObject");
        if (xobjects == null) return;
        foreach (var key in xobjects.Elements.Keys.ToList())
        {
            var d = xobjects.Elements.GetDictionary(key);
            if (d == null || !seen.Add(d)) continue;
            var sub = d.Elements.GetName("/Subtype");
            if (sub == "/Form") WalkResources(d.Elements.GetDictionary("/Resources"), seen, quality, longEdge, ref changed, ref left, depth + 1);
            else if (sub == "/Image")
            {
                switch (ShrinkImageObject(d, quality, longEdge))
                {
                    case true: changed++; break;
                    case false: left++; break;
                }
            }
        }
    }

    /// <summary>One picture object written smaller: true when it was, false when it is of a kind left alone, null when it
    /// is small already or came out no smaller.</summary>
    private static bool? ShrinkImageObject(PdfDictionary d, int quality, int longEdge)
    {
        if (d.Stream is not { } stream) return null;
        var e = d.Elements;
        if (e.GetBoolean("/ImageMask") || e.ContainsKey("/Decode") || e.ContainsKey("/Mask")) return false;
        var bpc = e.GetInteger("/BitsPerComponent");
        int w = e.GetInteger("/Width"), h = e.GetInteger("/Height");
        if (w < 64 || h < 64 || (long)w * h < 40_000) return null; // (an icon, a rule: nothing to gain)
        if (bpc != 8) return false;
        var filter = FilterOf(d);
        var components = ColourComponents(d);
        if (components == null) return false;
        BitmapSource source;
        try
        {
            if (filter == "/DCTDecode")
            {
                var frame = BitmapDecoder.Create(new MemoryStream(stream.Value), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                if (frame.Format == PixelFormats.Cmyk32 || frame.PixelWidth != w || frame.PixelHeight != h) return false;
                source = frame;
            }
            else if (filter == "/FlateDecode" || filter == "")
            {
                if (HasPredictor(d)) return false;
                var raw = filter == "" ? stream.Value : stream.UnfilteredValue;
                var stride = w * components.Value;
                if (raw.Length < (long)stride * h) return false;
                source = BitmapSource.Create(w, h, 96, 96, components == 1 ? PixelFormats.Gray8 : PixelFormats.Rgb24, null, raw, stride);
            }
            else return false;
        }
        catch (Exception) { return false; }
        // (a soft mask of its own shape beside it: the picture's size kept, so the mask still lies over it)
        var masked = e.ContainsKey("/SMask");
        var r = Encode(source, false, quality, masked ? 0 : longEdge);
        if (r.Bytes.Length >= stream.Value.Length * 0.95) return null;
        stream.Value = r.Bytes;
        e.SetName("/Filter", "/DCTDecode");
        e.Remove("/DecodeParms");
        e.SetInteger("/Width", r.Width);
        e.SetInteger("/Height", r.Height);
        e.SetInteger("/BitsPerComponent", 8);
        // (a JPEG written here is grey or RGB, whatever profile the picture named before)
        e.SetName("/ColorSpace", components == 1 ? "/DeviceGray" : "/DeviceRGB");
        return true;
    }

    /// <summary>A picture's one filter as a name ("/DCTDecode"); "" for none; "?" for a chain of them.</summary>
    private static string FilterOf(PdfDictionary d)
    {
        var item = d.Elements.GetValue("/Filter");
        if (item is PdfReference r) item = r.Value;
        return item switch
        {
            null => "",
            PdfName n => n.Value,
            PdfArray a when a.Elements.Count == 0 => "",
            PdfArray a when a.Elements.Count == 1 && a.Elements[0] is PdfName n1 => n1.Value,
            _ => "?",
        };
    }

    private static bool HasPredictor(PdfDictionary d)
    {
        var item = d.Elements.GetValue("/DecodeParms");
        if (item is PdfReference r) item = r.Value;
        if (item is PdfArray a) item = a.Elements.Count > 0 ? (a.Elements[0] is PdfReference r2 ? r2.Value : a.Elements[0]) : null;
        return item is PdfDictionary p && p.Elements.GetInteger("/Predictor") > 1;
    }

    /// <summary>How many colour values a pixel has — 1 for grey, 3 for RGB (device or ICC-based); null for the kinds left
    /// as they are (CMYK, indexed, separations, Lab).</summary>
    private static int? ColourComponents(PdfDictionary d)
    {
        var item = d.Elements.GetValue("/ColorSpace");
        if (item is PdfReference r) item = r.Value;
        switch (item)
        {
            case PdfName n:
                return n.Value switch { "/DeviceRGB" or "/CalRGB" => 3, "/DeviceGray" or "/CalGray" => 1, _ => null };
            case PdfArray a when a.Elements.Count >= 2 && a.Elements[0] is PdfName kind:
                if (kind.Value is "/ICCBased")
                {
                    var profile = a.Elements[1] is PdfReference pr ? pr.Value : a.Elements[1];
                    var n2 = profile is PdfDictionary pd ? pd.Elements.GetInteger("/N") : 0;
                    return n2 switch { 1 => 1, 3 => 3, _ => null };
                }
                if (kind.Value is "/CalRGB") return 3;
                if (kind.Value is "/CalGray") return 1;
                return null;
            default:
                return null;
        }
    }

    /// <summary>The number of pictures in a PDF the tool would look at (for a row's words before it is compressed).</summary>
    public static int PictureCount(byte[] data)
    {
        try
        {
            using var doc = PdfReader.Open(new MemoryStream(data), PdfDocumentOpenMode.Import);
            var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
            var n = 0;
            for (var i = 0; i < doc.PageCount; i++) Count(doc.Pages[i].Elements.GetDictionary("/Resources"), seen, ref n, 0);
            return n;
        }
        catch (Exception) { return 0; }
    }

    private static void Count(PdfDictionary? resources, HashSet<PdfDictionary> seen, ref int n, int depth)
    {
        var xobjects = resources?.Elements.GetDictionary("/XObject");
        if (xobjects == null || depth > 4) return;
        foreach (var key in xobjects.Elements.Keys.ToList())
        {
            var d = xobjects.Elements.GetDictionary(key);
            if (d == null || !seen.Add(d)) continue;
            var sub = d.Elements.GetName("/Subtype");
            if (sub == "/Image") n++;
            else if (sub == "/Form") Count(d.Elements.GetDictionary("/Resources"), seen, ref n, depth + 1);
        }
    }

    /// <summary>"3 pictures" / "1 picture" as a PDF's row says it.</summary>
    public static string PicturesText(int n) => n == 0 ? "no pictures" : ToolKit.Plural(n, "picture");

    private static readonly Regex NameEnd = new(@"-smaller(?: \d+)?$", RegexOptions.Compiled);

    /// <summary>The name a smaller copy is saved under: "<name>-smaller.<ext>" (a copy of a copy stays "-smaller").</summary>
    public static string OutputName(string path) => NameEnd.Replace(Path.GetFileNameWithoutExtension(path), "") + "-smaller";
}

/// <summary>Something that could not be made smaller, said in a sentence.</summary>
internal sealed class ShrinkError(string message) : Exception(message);
