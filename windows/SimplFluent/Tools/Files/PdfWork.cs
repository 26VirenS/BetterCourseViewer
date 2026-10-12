using System.Globalization;
using System.IO;
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace Simpl.Tools;

/// <summary>
/// A page's box as the file tools draw it: the part of the page that shows (its crop box, in the PDF's own points from
/// the bottom left) and the quarter turns it is shown at — and the way between the PDF's points and the page as it is
/// seen (points from its top left, turned as it shows).
/// </summary>
internal sealed class PdfBox
{
    public double X0, Y0, W, H;
    public int Rotate;

    /// <summary>The page's width as it is seen, in points.</summary>
    public double ShownW => Rotate % 180 == 0 ? W : H;
    public double ShownH => Rotate % 180 == 0 ? H : W;

    /// <summary>A point of the PDF's, where it shows on the page as seen.</summary>
    public (double U, double V) ToShown(double x, double y)
    {
        double u = x - X0, v = Y0 + H - y;
        return Rotate switch { 90 => (H - v, u), 180 => (W - u, H - v), 270 => (v, W - u), _ => (u, v) };
    }

    /// <summary>A point on the page as seen, in the PDF's own points.</summary>
    public (double X, double Y) ToPdf(double su, double sv)
    {
        var (u, v) = Rotate switch { 90 => (sv, H - su), 180 => (W - su, H - sv), 270 => (W - sv, su), _ => (su, sv) };
        return (X0 + u, Y0 + H - v);
    }
}

/// <summary>
/// A mark on a PDF's page as the annotator keeps it (the Mac's PDFAnnotation, as MarkModel holds them): its kind (the
/// PDF's own word: Highlight, Underline, StrikeOut, Text for a note, FreeText for a box of text, and Ink, Square and
/// Circle as other apps make them), where it is in the PDF's points, its colour and its note.
/// </summary>
internal sealed class PdfMark
{
    public string Kind = "Highlight";
    public int Page;
    /// <summary>Its rectangle in the PDF's points (left, bottom, right, top).</summary>
    public double X1, Y1, X2, Y2;
    /// <summary>The lines of words it marks: eight numbers each, the corners top left, top right, bottom left, bottom right.</summary>
    public List<double[]> Quads = new();
    public double R, G, B;
    public double Opacity = 1;
    public string Contents = "";
    public double FontSize = 14;
    public List<List<(double X, double Y)>> Ink = new();
    public double Border = 1;
    /// <summary>The words it marks, as read off the page (for its row and Copy Notes).</summary>
    public string Words = "";
    /// <summary>Its place among the page's annotations in the file it was read from (-1: made here, not saved yet).</summary>
    public int Origin = -1;
    /// <summary>Its note changed since the file was read.</summary>
    public bool Edited;

    public bool IsMarkup => Kind is "Highlight" or "Underline" or "StrikeOut";
}

/// <summary>
/// The PDFs the file tools write, with PDFsharp: pages copied as they are into a new document (merged, trimmed, split,
/// turned), pictures as pages, and an annotator's marks read from a file and written into it as real annotations, each
/// with its own appearance so every viewer shows it.
/// </summary>
internal static class PdfWork
{
    /// <summary>The annotations the annotator shows and keeps (the Mac's MarkModel.rebuild kinds).</summary>
    public static readonly string[] Kinds = { "Highlight", "Underline", "StrikeOut", "Text", "FreeText", "Ink", "Square", "Circle" };

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static PdfDocument OpenForImport(byte[] bytes) => PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);

    public static PdfDocument OpenForChange(byte[] bytes) => PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);

    /// <summary>A page to copy: which of the sources, which page of it, and the quarter turns added to it.</summary>
    public readonly record struct Pick(int Source, int Page, int Turn);

    /// <summary>Pages copied as they are (text, pictures and links stay what they were) into a new PDF; its page count.</summary>
    public static int Assemble(IReadOnlyList<byte[]> sources, IEnumerable<Pick> picks, string path)
    {
        var open = new Dictionary<int, PdfDocument>();
        try
        {
            using var output = new PdfDocument();
            foreach (var p in picks)
            {
                if (p.Source < 0 || p.Source >= sources.Count) continue;
                if (!open.TryGetValue(p.Source, out var src)) open[p.Source] = src = OpenForImport(sources[p.Source]);
                if (p.Page < 0 || p.Page >= src.PageCount) continue;
                var page = output.AddPage(src.Pages[p.Page]);
                if (p.Turn % 360 != 0) page.Rotate = Turn(page.Rotate + p.Turn);
            }
            var count = output.PageCount;
            if (count == 0) return 0;
            output.Save(path);
            return count;
        }
        finally
        {
            foreach (var d in open.Values) d.Dispose();
        }
    }

    /// <summary>Quarter turns, 0 to 270.</summary>
    public static int Turn(int degrees) => ((degrees / 90 * 90) % 360 + 360) % 360;

    /// <summary>A picture to put on a page: a PNG or JPEG file's bytes and its size in points.</summary>
    public readonly record struct Picture(byte[] Data, double Width, double Height);

    /// <summary>Pictures as the pages of one PDF, each page the size of its picture (the Mac's PDFPage(image:)).</summary>
    public static void Pictures(IEnumerable<Picture> pictures, string path)
    {
        var images = new List<XImage>();
        try
        {
            using var doc = new PdfDocument();
            foreach (var p in pictures)
            {
                var image = XImage.FromStream(new MemoryStream(p.Data));
                images.Add(image);
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(Math.Max(1, p.Width));
                page.Height = XUnit.FromPoint(Math.Max(1, p.Height));
                using var gfx = XGraphics.FromPdfPage(page);
                gfx.DrawImage(image, 0, 0, p.Width, p.Height);
            }
            if (doc.PageCount == 0) throw new InvalidDataException("None of the pictures could be read.");
            doc.Save(path);
        }
        finally
        {
            foreach (var i in images) i.Dispose();
        }
    }

    // ---- the annotator's marks ----

    /// <summary>What a PDF is to the annotator: its pages' boxes, the marks it came with, and a copy without them to draw
    /// the pages from (null: it had none, the file itself is drawn).</summary>
    public sealed record Marked(List<PdfBox> Pages, List<PdfMark> Marks, byte[]? Plain);

    public static Marked Read(byte[] bytes)
    {
        using var doc = OpenForChange(bytes);
        var pages = new List<PdfBox>();
        var marks = new List<PdfMark>();
        var stripped = false;
        for (var i = 0; i < doc.PageCount; i++)
        {
            var page = doc.Pages[i];
            var box = Box(page);
            pages.Add(box);
            var annots = page.Elements.GetArray("/Annots");
            if (annots == null) continue;
            var drop = new List<PdfItem>();
            for (var k = 0; k < annots.Elements.Count; k++)
            {
                var item = annots.Elements[k];
                if (Dict(item) is not { } d) continue;
                var sub = Sub(d);
                if (sub == "Popup") { drop.Add(item); continue; }
                if (!Kinds.Contains(sub)) continue;
                if (ReadMark(d, sub, i, k) is { } m) marks.Add(m);
                drop.Add(item);
            }
            foreach (var it in drop) annots.Elements.Remove(it);
            stripped |= drop.Count > 0;
        }
        byte[]? plain = null;
        if (stripped)
        {
            var ms = new MemoryStream();
            doc.Save(ms, false);
            plain = ms.ToArray();
        }
        return new Marked(pages, marks, plain);
    }

    /// <summary>A page's box: its crop box inside its media box, and its turn.</summary>
    public static PdfBox Box(PdfPage page)
    {
        var media = page.MediaBoxReadOnly;
        var crop = page.EffectiveCropBoxReadOnly;
        double mx1 = Math.Min(media.X1, media.X2), my1 = Math.Min(media.Y1, media.Y2), mx2 = Math.Max(media.X1, media.X2), my2 = Math.Max(media.Y1, media.Y2);
        double cx1 = Math.Min(crop.X1, crop.X2), cy1 = Math.Min(crop.Y1, crop.Y2), cx2 = Math.Max(crop.X1, crop.X2), cy2 = Math.Max(crop.Y1, crop.Y2);
        if (cx2 - cx1 <= 0 || cy2 - cy1 <= 0) { cx1 = mx1; cy1 = my1; cx2 = mx2; cy2 = my2; }
        else if (mx2 - mx1 > 0 && my2 - my1 > 0)
        {
            cx1 = Math.Max(cx1, mx1); cy1 = Math.Max(cy1, my1); cx2 = Math.Min(cx2, mx2); cy2 = Math.Min(cy2, my2);
            if (cx2 <= cx1 || cy2 <= cy1) { cx1 = mx1; cy1 = my1; cx2 = mx2; cy2 = my2; }
        }
        if (cx2 - cx1 <= 0 || cy2 - cy1 <= 0) { cx1 = 0; cy1 = 0; cx2 = 612; cy2 = 792; }
        return new PdfBox { X0 = cx1, Y0 = cy1, W = cx2 - cx1, H = cy2 - cy1, Rotate = Turn(page.Rotate) };
    }

    private static PdfMark? ReadMark(PdfDictionary d, string kind, int page, int origin)
    {
        var rect = d.Elements.GetRectangle("/Rect");
        var m = new PdfMark
        {
            Kind = kind, Page = page, Origin = origin,
            X1 = Math.Min(rect.X1, rect.X2), Y1 = Math.Min(rect.Y1, rect.Y2), X2 = Math.Max(rect.X1, rect.X2), Y2 = Math.Max(rect.Y1, rect.Y2),
            Contents = SafeString(d, "/Contents"),
        };
        if (Rgb(d.Elements.GetValue("/C")) is { } c) (m.R, m.G, m.B) = c;
        else if (kind != "FreeText") (m.R, m.G, m.B) = (1, 0.84, 0.04);
        if (d.Elements.ContainsKey("/CA")) m.Opacity = Math.Clamp(Real(d.Elements.GetValue("/CA")) ?? 1, 0, 1);
        if (m.IsMarkup)
        {
            if (Deref(d.Elements.GetValue("/QuadPoints")) is PdfArray q)
            {
                var nums = q.Elements.Select(e => Real(e) ?? 0).ToArray();
                for (var i = 0; i + 8 <= nums.Length; i += 8) m.Quads.Add(nums[i..(i + 8)]);
            }
            if (m.Quads.Count == 0) m.Quads.Add(new[] { m.X1, m.Y2, m.X2, m.Y2, m.X1, m.Y1, m.X2, m.Y1 });
        }
        else if (kind == "FreeText")
        {
            var (color, size) = Appearance(SafeString(d, "/DA"));
            (m.R, m.G, m.B) = color;
            m.FontSize = size;
        }
        else if (kind == "Ink" && Deref(d.Elements.GetValue("/InkList")) is PdfArray list)
        {
            foreach (var stroke in list.Elements)
            {
                if (Deref(stroke) is not PdfArray s) continue;
                var nums = s.Elements.Select(e => Real(e) ?? 0).ToArray();
                var path = new List<(double, double)>();
                for (var i = 0; i + 2 <= nums.Length; i += 2) path.Add((nums[i], nums[i + 1]));
                if (path.Count > 0) m.Ink.Add(path);
            }
        }
        if (kind is "Ink" or "Square" or "Circle")
        {
            m.Border = 1;
            if (Deref(d.Elements.GetValue("/BS")) is PdfDictionary bs && bs.Elements.ContainsKey("/W")) m.Border = Real(bs.Elements.GetValue("/W")) ?? 1;
        }
        return m;
    }

    /// <summary>A box of text's colour and size, from its default appearance ("/Helv 14 Tf 1 0 0 rg").</summary>
    private static ((double, double, double) Color, double Size) Appearance(string da)
    {
        var t = da.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        (double, double, double) color = (0, 0, 0);
        double size = 14;
        double Num(int i) => i >= 0 && i < t.Length && double.TryParse(t[i], NumberStyles.Float, Inv, out var v) ? v : 0;
        for (var i = 0; i < t.Length; i++)
        {
            if (t[i] == "rg" && i >= 3) color = (Num(i - 3), Num(i - 2), Num(i - 1));
            else if (t[i] == "g" && i >= 1) color = (Num(i - 1), Num(i - 1), Num(i - 1));
            else if (t[i] == "Tf" && i >= 1 && Num(i - 1) > 0) size = Num(i - 1);
        }
        return (color, size);
    }

    /// <summary>
    /// The marks written into a copy of the file (its bytes as read) and saved at <paramref name="path"/>: the ones taken
    /// off removed (and their pop-ups), notes changed written, the new ones added. Each mark's place among its page's
    /// annotations in the file saved, for the next save.
    /// </summary>
    public static Dictionary<PdfMark, int> Write(byte[] original, IReadOnlyList<PdfMark> marks, string path)
    {
        using var doc = OpenForChange(original);
        var placed = new Dictionary<PdfMark, int>();
        for (var i = 0; i < doc.PageCount; i++)
        {
            var page = doc.Pages[i];
            var rotate = Turn(page.Rotate);
            var mine = marks.Where(m => m.Page == i).ToList();
            var kept = mine.Where(m => m.Origin >= 0).GroupBy(m => m.Origin).ToDictionary(g => g.Key, g => g.First());
            var dicts = new Dictionary<PdfMark, PdfDictionary>();
            var annots = page.Elements.GetArray("/Annots");
            if (annots != null)
            {
                var drop = new List<PdfDictionary>();
                for (var k = 0; k < annots.Elements.Count; k++)
                {
                    if (Dict(annots.Elements[k]) is not { } d || !Kinds.Contains(Sub(d))) continue;
                    if (kept.TryGetValue(k, out var m))
                    {
                        dicts[m] = d;
                        if (m.Edited) Update(doc, d, m, rotate);
                    }
                    else
                    {
                        drop.Add(d);
                        if (Dict(d.Elements.GetValue("/Popup")) is { } pop) drop.Add(pop);
                    }
                }
                if (drop.Count > 0)
                    for (var k = annots.Elements.Count - 1; k >= 0; k--)
                        if (Dict(annots.Elements[k]) is { } d && drop.Any(x => ReferenceEquals(x, d))) annots.Elements.RemoveAt(k);
            }
            foreach (var m in mine.Where(m => m.Origin < 0))
            {
                if (annots == null)
                {
                    annots = new PdfArray(doc);
                    page.Elements["/Annots"] = annots;
                }
                var d = Make(doc, page, m, rotate);
                annots.Elements.Add(d.Reference!);
                dicts[m] = d;
            }
            if (annots == null) continue;
            for (var k = 0; k < annots.Elements.Count; k++)
            {
                var d = Dict(annots.Elements[k]);
                foreach (var (m, md) in dicts) if (ReferenceEquals(md, d)) placed[m] = k;
            }
        }
        doc.Save(path);
        return placed;
    }

    /// <summary>A mark made here, as an annotation of the PDF's: its kind, place, colour, note and appearance.</summary>
    private static PdfDictionary Make(PdfDocument doc, PdfPage page, PdfMark m, int rotate)
    {
        var d = new PdfDictionary(doc);
        d.Elements.SetName("/Type", "/Annot");
        d.Elements.SetName("/Subtype", "/" + m.Kind);
        d.Elements["/Rect"] = Arr(doc, m.X1, m.Y1, m.X2, m.Y2);
        d.Elements.SetInteger("/F", 4);
        d.Elements["/Contents"] = new PdfString(m.Contents ?? "", PdfStringEncoding.Unicode);
        d.Elements["/M"] = new PdfString(PdfDate(DateTime.Now));
        d.Elements["/NM"] = new PdfString("simpl-" + Guid.NewGuid().ToString("N"));
        d.Elements.SetReference("/P", page);
        if (m.Kind == "FreeText")
        {
            d.Elements["/C"] = new PdfArray(doc);
            d.Elements["/DA"] = new PdfString(Da(m));
            d.Elements.SetInteger("/Q", 0);
            var bs = new PdfDictionary(doc);
            bs.Elements.SetInteger("/W", 0);
            d.Elements["/BS"] = bs;
        }
        else d.Elements["/C"] = Arr(doc, m.R, m.G, m.B);
        if (m.IsMarkup) d.Elements["/QuadPoints"] = Arr(doc, m.Quads.SelectMany(q => q).ToArray());
        if (m.Kind == "Text")
        {
            d.Elements.SetName("/Name", "/Note");
            d.Elements.SetBoolean("/Open", false);
        }
        if (m.Opacity < 1) d.Elements["/CA"] = new PdfReal(m.Opacity);
        SetAppearance(doc, d, m, rotate);
        doc.Internals.AddObject(d);
        return d;
    }

    /// <summary>A mark the file came with, its note (and a box of text's words on the page) changed.</summary>
    private static void Update(PdfDocument doc, PdfDictionary d, PdfMark m, int rotate)
    {
        d.Elements["/Contents"] = new PdfString(m.Contents ?? "", PdfStringEncoding.Unicode);
        d.Elements["/M"] = new PdfString(PdfDate(DateTime.Now));
        if (m.Kind == "FreeText")
        {
            d.Elements["/DA"] = new PdfString(Da(m));
            SetAppearance(doc, d, m, rotate);
        }
    }

    private static string Da(PdfMark m) => $"/Helv {N(m.FontSize)} Tf {N(m.R)} {N(m.G)} {N(m.B)} rg";

    private static string PdfDate(DateTime t)
    {
        var off = TimeZoneInfo.Local.GetUtcOffset(t);
        var sign = off < TimeSpan.Zero ? "-" : "+";
        return $"D:{t:yyyyMMddHHmmss}{sign}{Math.Abs(off.Hours):00}'{Math.Abs(off.Minutes):00}'";
    }

    // ---- appearances: each mark drawn as viewers show it ----

    private static void SetAppearance(PdfDocument doc, PdfDictionary d, PdfMark m, int rotate)
    {
        var form = m.Kind switch
        {
            "Highlight" => Highlight(doc, m),
            "Underline" => Line(doc, m, 0.08),
            "StrikeOut" => Line(doc, m, 0.5),
            "Text" => NoteIcon(doc, m),
            "FreeText" => TextBox(doc, m, rotate),
            _ => null,
        };
        if (form == null) return;
        var ap = new PdfDictionary(doc);
        ap.Elements.SetReference("/N", form);
        d.Elements["/AP"] = ap;
    }

    private static PdfDictionary Highlight(PdfDocument doc, PdfMark m)
    {
        var s = new StringBuilder("/GS0 gs ").Append(Rgb(m, "rg"));
        foreach (var q in m.Quads)
            s.Append($" {N(q[0])} {N(q[1])} m {N(q[2])} {N(q[3])} l {N(q[6])} {N(q[7])} l {N(q[4])} {N(q[5])} l h f");
        var gs = new PdfDictionary(doc);
        gs.Elements.SetName("/Type", "/ExtGState");
        gs.Elements.SetName("/BM", "/Multiply");
        var states = new PdfDictionary(doc);
        states.Elements["/GS0"] = gs;
        var res = new PdfDictionary(doc);
        res.Elements["/ExtGState"] = states;
        return Form(doc, new[] { m.X1, m.Y1, m.X2, m.Y2 }, null, res, s.ToString());
    }

    /// <summary>An underline (low on each line of words) or a strike-through (across its middle).</summary>
    private static PdfDictionary Line(PdfDocument doc, PdfMark m, double at)
    {
        var s = new StringBuilder(Rgb(m, "RG"));
        foreach (var q in m.Quads)
        {
            // from the bottom edge (bottom left to bottom right) up towards the top edge
            double h = Math.Sqrt(Math.Pow(q[0] - q[4], 2) + Math.Pow(q[1] - q[5], 2));
            var w = Math.Max(0.8, h / 14);
            double ax = q[4] + (q[0] - q[4]) * at, ay = q[5] + (q[1] - q[5]) * at, bx = q[6] + (q[2] - q[6]) * at, by = q[7] + (q[3] - q[7]) * at;
            s.Append($" {N(w)} w {N(ax)} {N(ay)} m {N(bx)} {N(by)} l S");
        }
        return Form(doc, new[] { m.X1 - 2, m.Y1 - 2, m.X2 + 2, m.Y2 + 2 }, null, null, s.ToString());
    }

    /// <summary>A note's icon (viewers keep a note upright on a turned page themselves, as the PDF's rules have it).</summary>
    private static PdfDictionary NoteIcon(PdfDocument doc, PdfMark m)
    {
        var s = $"{Rgb(m, "rg")} 0.25 0.25 0.25 RG 0.8 w 1.5 1.5 19 19 re B 1 w 5 15.5 m 17 15.5 l S 5 11 m 17 11 l S 5 6.5 m 13 6.5 l S";
        return Form(doc, new double[] { 0, 0, 22, 22 }, null, null, s);
    }

    private static PdfDictionary TextBox(PdfDocument doc, PdfMark m, int rotate)
    {
        double w = m.X2 - m.X1, h = m.Y2 - m.Y1;
        if (rotate % 180 != 0) (w, h) = (h, w);
        var size = m.FontSize > 0 ? m.FontSize : 14;
        var s = new StringBuilder($"/Tx BMC q 0 0 {N(w)} {N(h)} re W n BT /Helv {N(size)} Tf {Rgb(m, "rg")} 2 {N(h - size - 1)} Td");
        var first = true;
        foreach (var line in (m.Contents ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            if (!first) s.Append($" 0 {N(-size * 1.2)} Td");
            s.Append(" (").Append(Literal(line)).Append(") Tj");
            first = false;
        }
        s.Append(" ET Q EMC");
        var font = new PdfDictionary(doc);
        font.Elements.SetName("/Type", "/Font");
        font.Elements.SetName("/Subtype", "/Type1");
        font.Elements.SetName("/BaseFont", "/Helvetica");
        font.Elements.SetName("/Encoding", "/WinAnsiEncoding");
        var fonts = new PdfDictionary(doc);
        fonts.Elements["/Helv"] = font;
        var res = new PdfDictionary(doc);
        res.Elements["/Font"] = fonts;
        return Form(doc, new[] { 0, 0, w, h }, Rotation(rotate), res, s.ToString());
    }

    /// <summary>What turns an appearance drawn upright to stand upright on a page shown turned.</summary>
    private static double[]? Rotation(int rotate) => rotate switch
    {
        90 => new double[] { 0, 1, -1, 0, 0, 0 },
        180 => new double[] { -1, 0, 0, -1, 0, 0 },
        270 => new double[] { 0, -1, 1, 0, 0, 0 },
        _ => null,
    };

    private static PdfDictionary Form(PdfDocument doc, double[] bbox, double[]? matrix, PdfDictionary? resources, string content)
    {
        var f = new PdfDictionary(doc);
        f.Elements.SetName("/Type", "/XObject");
        f.Elements.SetName("/Subtype", "/Form");
        f.Elements.SetInteger("/FormType", 1);
        f.Elements["/BBox"] = Arr(doc, bbox);
        if (matrix != null) f.Elements["/Matrix"] = Arr(doc, matrix);
        f.Elements["/Resources"] = resources ?? new PdfDictionary(doc);
        f.CreateStream(Encoding.Latin1.GetBytes(content));
        doc.Internals.AddObject(f);
        return f;
    }

    private static string Rgb(PdfMark m, string op) => $"{N(m.R)} {N(m.G)} {N(m.B)} {op}";

    /// <summary>Words as a PDF string in Windows' Western letters (what the standard Helvetica can show; others as "?").</summary>
    private static string Literal(string s)
    {
        var b = new StringBuilder();
        foreach (var ch in s)
        {
            var c = WinAnsi(ch);
            if (c is '(' or ')' or '\\') b.Append('\\');
            b.Append(c);
        }
        return b.ToString();
    }

    private static char WinAnsi(char c)
    {
        if (c is >= ' ' and < (char)0x7f) return c;
        if (c is >= (char)0xa0 and <= (char)0xff) return c;
        if (c == '\t') return ' ';
        return c switch
        {
            '€' => (char)0x80, '‚' => (char)0x82, 'ƒ' => (char)0x83, '„' => (char)0x84, '…' => (char)0x85, '†' => (char)0x86,
            '‡' => (char)0x87, 'ˆ' => (char)0x88, '‰' => (char)0x89, 'Š' => (char)0x8a, '‹' => (char)0x8b, 'Œ' => (char)0x8c,
            'Ž' => (char)0x8e, '‘' => (char)0x91, '’' => (char)0x92, '“' => (char)0x93, '”' => (char)0x94, '•' => (char)0x95,
            '–' => (char)0x96, '—' => (char)0x97, '˜' => (char)0x98, '™' => (char)0x99, 'š' => (char)0x9a, '›' => (char)0x9b,
            'œ' => (char)0x9c, 'ž' => (char)0x9e, 'Ÿ' => (char)0x9f,
            _ => '?',
        };
    }

    // ---- reading the PDF's objects ----

    private static string Sub(PdfDictionary d) => (d.Elements.GetName("/Subtype") ?? "").TrimStart('/');

    private static PdfItem? Deref(PdfItem? item) => item is PdfReference r ? r.Value : item;

    private static PdfDictionary? Dict(PdfItem? item) => Deref(item) as PdfDictionary;

    private static double? Real(PdfItem? item) => Deref(item) switch
    {
        PdfReal r => r.Value,
        PdfInteger i => i.Value,
        PdfNumber n when double.TryParse(n.ToString(), NumberStyles.Float, Inv, out var v) => v,
        _ => null,
    };

    private static string SafeString(PdfDictionary d, string key)
    {
        try { return d.Elements.ContainsKey(key) ? d.Elements.GetString(key) ?? "" : ""; }
        catch { return ""; }
    }

    /// <summary>A colour array (grey, RGB or CMYK) as red, green and blue; none for an empty one.</summary>
    private static (double, double, double)? Rgb(PdfItem? item)
    {
        if (Deref(item) is not PdfArray a) return null;
        var v = a.Elements.Select(e => Math.Clamp(Real(e) ?? 0, 0, 1)).ToArray();
        return v.Length switch
        {
            1 => (v[0], v[0], v[0]),
            3 => (v[0], v[1], v[2]),
            4 => ((1 - v[0]) * (1 - v[3]), (1 - v[1]) * (1 - v[3]), (1 - v[2]) * (1 - v[3])),
            _ => null,
        };
    }

    private static PdfArray Arr(PdfDocument doc, params double[] values) => new(doc, values.Select(v => (PdfItem)new PdfReal(Math.Round(v, 4))).ToArray());

    private static string N(double v) => Math.Round(v, 3).ToString("0.###", Inv);
}
