using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Simpl.Tools;

// The converter's documents, as the Mac's NSAttributedString carries them between kinds: paragraphs (headings, list
// items, preformatted lines) of runs of words with their bold, italic, underline, strike-through, size, colour and link,
// and pictures among them. Read from plain text, Word (.docx), OpenDocument (.odt) — and rich text and web pages by
// Windows' own readers (FileConverter.cs) — and written as Word, a web page or text; rich text and PDF are made from them
// there too. Word is read and written as the open format it is (a zip of XML), with no Word needed.

/// <summary>Words that look one way, or a picture.</summary>
internal sealed class DocRun
{
    public string Text = "";
    public bool Bold, Italic, Underline, Strike;
    /// <summary>Its size in points (none: the document's own).</summary>
    public double? Size;
    /// <summary>Its colour as "rrggbb" (none: black on the page).</summary>
    public string? Color;
    public string? Link;
    public byte[]? Image;
    /// <summary>"image/png", "image/jpeg" …</summary>
    public string? ImageType;
    /// <summary>The picture's size in points.</summary>
    public double ImageW, ImageH;

    public DocRun Like(string text) => new()
    {
        Text = text, Bold = Bold, Italic = Italic, Underline = Underline, Strike = Strike, Size = Size, Color = Color, Link = Link,
    };
}

/// <summary>A paragraph: a heading (1–6), a list item (bulleted, or numbered from 1), preformatted, or plain.</summary>
internal sealed class DocBlock
{
    public int Heading;
    /// <summary>0: not in a list; -1: a bullet; 1, 2 …: its number.</summary>
    public int List;
    public bool Pre;
    public List<DocRun> Runs = new();

    public string Plain => string.Concat(Runs.Select(r => r.Text));
}

internal sealed class DocModel
{
    public string Title = "";
    public List<DocBlock> Blocks = new();
    /// <summary>Read from plain text: its lines kept as they were.</summary>
    public bool PlainText;
}

/// <summary>Plain text: read (UTF-8, else Western), and written.</summary>
internal static class DocText
{
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }

    public static DocModel Read(string path)
    {
        var text = Decode(File.ReadAllBytes(path)).Replace("\r\n", "\n").Replace('\r', '\n');
        var m = new DocModel { Title = Path.GetFileNameWithoutExtension(path), PlainText = true };
        foreach (var line in text.Split('\n')) m.Blocks.Add(new DocBlock { Runs = { new DocRun { Text = line } } });
        if (m.Blocks.Count > 1 && m.Blocks[^1].Plain.Length == 0) m.Blocks.RemoveAt(m.Blocks.Count - 1);
        return m;
    }

    public static string Write(DocModel m)
    {
        var b = new StringBuilder();
        foreach (var block in m.Blocks)
        {
            if (block.List == -1) b.Append("• ");
            else if (block.List > 0) b.Append(block.List).Append(". ");
            b.Append(string.Concat(block.Runs.Where(r => r.Image == null).Select(r => r.Text))).Append('\n');
        }
        return b.ToString();
    }
}

/// <summary>A document as a web page: what the PDF is printed from, and the HTML target.</summary>
internal static class DocHtml
{
    public static string Write(DocModel m)
    {
        var b = new StringBuilder();
        b.Append("<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n<title>").Append(WebUtility.HtmlEncode(m.Title)).Append("</title>\n");
        b.Append("<style>\nbody { font-family: 'Segoe UI', Arial, sans-serif; font-size: 12pt; line-height: 1.4; color: #000; background: #fff; margin: 0; }\n");
        b.Append("p { margin: 0 0 0.6em 0; } h1, h2, h3, h4, h5, h6 { margin: 0.8em 0 0.4em 0; line-height: 1.2; }\n");
        b.Append("pre, .text { font-family: 'Segoe UI', Arial, sans-serif; white-space: pre-wrap; word-wrap: break-word; margin: 0; }\n");
        b.Append("img { max-width: 100%; height: auto; } ul, ol { margin: 0 0 0.6em 0; }\n</style>\n</head>\n<body>\n");
        if (m.PlainText)
        {
            b.Append("<div class=\"text\">");
            foreach (var block in m.Blocks) b.Append(WebUtility.HtmlEncode(block.Plain)).Append('\n');
            b.Append("</div>\n");
        }
        else
        {
            string? open = null;
            foreach (var block in m.Blocks)
            {
                var want = block.List == -1 ? "ul" : block.List > 0 ? "ol" : null;
                if (open != want)
                {
                    if (open != null) b.Append("</").Append(open).Append(">\n");
                    if (want != null) b.Append('<').Append(want).Append(want == "ol" && block.List > 1 ? $" start=\"{block.List}\"" : "").Append(">\n");
                    open = want;
                }
                var tag = want != null ? "li" : block.Heading is >= 1 and <= 6 ? $"h{block.Heading}" : block.Pre ? "pre" : "p";
                b.Append('<').Append(tag).Append('>');
                if (block.Runs.Count == 0 || block.Plain.Length == 0 && block.Runs.All(r => r.Image == null)) b.Append("<br>");
                foreach (var r in block.Runs) Run(b, r);
                b.Append("</").Append(tag).Append(">\n");
            }
            if (open != null) b.Append("</").Append(open).Append(">\n");
        }
        b.Append("</body>\n</html>\n");
        return b.ToString();
    }

    private static void Run(StringBuilder b, DocRun r)
    {
        if (r.Image != null)
        {
            var size = r.ImageW > 0 && r.ImageH > 0 ? $" style=\"width:{N(r.ImageW)}pt;height:{N(r.ImageH)}pt\"" : "";
            b.Append($"<img src=\"data:{r.ImageType ?? "image/png"};base64,{Convert.ToBase64String(r.Image)}\"{size} alt=\"\">");
            return;
        }
        if (r.Text.Length == 0) return;
        var text = WebUtility.HtmlEncode(r.Text).Replace("\n", "<br>").Replace("\t", "<span style=\"white-space:pre\">\t</span>");
        var style = new List<string>();
        if (r.Size is { } s) style.Add($"font-size:{N(s)}pt");
        if (r.Color is { } c) style.Add($"color:#{c}");
        if (r.Link != null) b.Append("<a href=\"").Append(WebUtility.HtmlEncode(r.Link)).Append("\">");
        if (style.Count > 0) b.Append("<span style=\"").Append(string.Join(";", style)).Append("\">");
        if (r.Bold) b.Append("<b>");
        if (r.Italic) b.Append("<i>");
        if (r.Underline) b.Append("<u>");
        if (r.Strike) b.Append("<s>");
        b.Append(text);
        if (r.Strike) b.Append("</s>");
        if (r.Underline) b.Append("</u>");
        if (r.Italic) b.Append("</i>");
        if (r.Bold) b.Append("</b>");
        if (style.Count > 0) b.Append("</span>");
        if (r.Link != null) b.Append("</a>");
    }

    internal static string N(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>Word documents (.docx), read and written as the open format they are.</summary>
internal static class DocWord
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace PR = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace PIC = "http://schemas.openxmlformats.org/drawingml/2006/picture";

    // ---- reading ----

    private sealed class Reading
    {
        public required ZipArchive Zip;
        public Dictionary<string, string> Rels = new();
        public Dictionary<string, string> Styles = new();
        public Dictionary<string, Dictionary<int, bool>> Bullets = new();
        public Dictionary<(string, int), int> Counters = new();
        public DocModel Model = new();
    }

    public static DocModel Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var doc = Xml(zip, "word/document.xml") ?? throw new InvalidDataException("The document could not be read.");
        var r = new Reading { Zip = zip };
        r.Model.Title = Path.GetFileNameWithoutExtension(path);
        if (Xml(zip, "word/_rels/document.xml.rels") is { } rels)
            foreach (var rel in rels.Root!.Elements(PR + "Relationship"))
                if ((string?)rel.Attribute("Id") is { } id) r.Rels[id] = (string?)rel.Attribute("Target") ?? "";
        if (Xml(zip, "word/styles.xml") is { } styles)
            foreach (var st in styles.Root!.Elements(W + "style"))
                if ((string?)st.Attribute(W + "styleId") is { } id) r.Styles[id] = ((string?)st.Element(W + "name")?.Attribute(W + "val") ?? id).ToLowerInvariant();
        if (Xml(zip, "word/numbering.xml") is { } numbering)
        {
            var abstracts = new Dictionary<string, Dictionary<int, bool>>();
            foreach (var an in numbering.Root!.Elements(W + "abstractNum"))
            {
                var levels = new Dictionary<int, bool>();
                foreach (var lvl in an.Elements(W + "lvl"))
                    levels[(int?)lvl.Attribute(W + "ilvl") ?? 0] = (string?)lvl.Element(W + "numFmt")?.Attribute(W + "val") is "bullet" or "none" or null;
                abstracts[(string?)an.Attribute(W + "abstractNumId") ?? ""] = levels;
            }
            foreach (var num in numbering.Root!.Elements(W + "num"))
                if ((string?)num.Attribute(W + "numId") is { } id && (string?)num.Element(W + "abstractNumId")?.Attribute(W + "val") is { } a && abstracts.TryGetValue(a, out var l))
                    r.Bullets[id] = l;
        }
        var body = doc.Root!.Element(W + "body");
        if (body != null) Blocks(r, body);
        return r.Model;
    }

    private static void Blocks(Reading r, XElement parent)
    {
        foreach (var e in parent.Elements())
        {
            if (e.Name == W + "p") r.Model.Blocks.Add(Paragraph(r, e));
            else if (e.Name == W + "tbl")
            {
                foreach (var tr in e.Elements(W + "tr"))
                {
                    var row = new DocBlock();
                    var first = true;
                    foreach (var tc in tr.Elements(W + "tc"))
                    {
                        if (!first) row.Runs.Add(new DocRun { Text = "\t" });
                        first = false;
                        var cell = new DocModel();
                        var inner = new Reading { Zip = r.Zip, Rels = r.Rels, Styles = r.Styles, Bullets = r.Bullets, Counters = r.Counters, Model = cell };
                        Blocks(inner, tc);
                        for (var i = 0; i < cell.Blocks.Count; i++)
                        {
                            if (i > 0) row.Runs.Add(new DocRun { Text = " " });
                            row.Runs.AddRange(cell.Blocks[i].Runs);
                        }
                    }
                    r.Model.Blocks.Add(row);
                }
            }
            else if (e.Name == W + "sdt") { if (e.Element(W + "sdtContent") is { } c) Blocks(r, c); }
            else if (e.Name == W + "customXml" || e.Name == W + "ins") Blocks(r, e);
        }
    }

    private static DocBlock Paragraph(Reading r, XElement p)
    {
        var block = new DocBlock();
        var ppr = p.Element(W + "pPr");
        if ((string?)ppr?.Element(W + "pStyle")?.Attribute(W + "val") is { } sid)
        {
            var name = r.Styles.TryGetValue(sid, out var n) ? n : sid.ToLowerInvariant();
            if (name == "title") block.Heading = 1;
            else if (name == "subtitle") block.Heading = 2;
            else if ((name.StartsWith("heading ") || name.StartsWith("heading")) && int.TryParse(new string(name.Where(char.IsDigit).ToArray()), out var level)) block.Heading = Math.Clamp(level, 1, 6);
        }
        if (ppr?.Element(W + "numPr") is { } num && (string?)num.Element(W + "numId")?.Attribute(W + "val") is { } numId && numId != "0")
        {
            var ilvl = (int?)num.Element(W + "ilvl")?.Attribute(W + "val") ?? 0;
            var bullet = !r.Bullets.TryGetValue(numId, out var levels) || !levels.TryGetValue(ilvl, out var b) || b;
            if (bullet) block.List = -1;
            else
            {
                var key = (numId, ilvl);
                r.Counters[key] = r.Counters.GetValueOrDefault(key) + 1;
                block.List = r.Counters[key];
            }
        }
        Inline(r, block, p, null);
        return block;
    }

    private static void Inline(Reading r, DocBlock block, XElement parent, string? link)
    {
        foreach (var e in parent.Elements())
        {
            var n = e.Name;
            if (n == W + "r") RunOf(r, block, e, link);
            else if (n == W + "hyperlink")
            {
                var target = link;
                if ((string?)e.Attribute(R + "id") is { } id && r.Rels.TryGetValue(id, out var t)) target = t;
                Inline(r, block, e, target);
            }
            else if (n == W + "ins" || n == W + "smartTag" || n == W + "customXml" || n == W + "fldSimple" || n == W + "sdt" || n == W + "sdtContent" || n == W + "bdo" || n == W + "dir")
                Inline(r, block, e, link);
        }
    }

    private static bool On(XElement? rpr, string name)
    {
        var e = rpr?.Element(W + name);
        if (e == null) return false;
        var v = (string?)e.Attribute(W + "val");
        return v is null or "1" or "true" or "on";
    }

    private static void RunOf(Reading r, DocBlock block, XElement run, string? link)
    {
        var rpr = run.Element(W + "rPr");
        var style = new DocRun
        {
            Bold = On(rpr, "b"), Italic = On(rpr, "i"), Strike = On(rpr, "strike") || On(rpr, "dstrike"), Link = link,
            Underline = rpr?.Element(W + "u") is { } u && (string?)u.Attribute(W + "val") != "none",
        };
        if ((string?)rpr?.Element(W + "sz")?.Attribute(W + "val") is { } sz && double.TryParse(sz, NumberStyles.Float, CultureInfo.InvariantCulture, out var half)) style.Size = half / 2;
        if ((string?)rpr?.Element(W + "color")?.Attribute(W + "val") is { Length: 6 } color && color != "000000" && color != "auto") style.Color = color.ToLowerInvariant();
        foreach (var e in run.Elements())
        {
            var n = e.Name;
            if (n == W + "t") Add(block, style.Like(e.Value));
            else if (n == W + "tab" || n == W + "ptab") Add(block, style.Like("\t"));
            else if (n == W + "br" || n == W + "cr") Add(block, style.Like("\n"));
            else if (n == W + "noBreakHyphen") Add(block, style.Like("-"));
            else if (n == W + "softHyphen") { }
            else if (n == W + "drawing" && Picture(r, e) is { } pic) block.Runs.Add(pic);
        }
    }

    private static void Add(DocBlock block, DocRun run)
    {
        // (runs that look the same run on as one)
        if (block.Runs.Count > 0 && block.Runs[^1] is { Image: null } last && last.Bold == run.Bold && last.Italic == run.Italic && last.Underline == run.Underline
            && last.Strike == run.Strike && last.Size == run.Size && last.Color == run.Color && last.Link == run.Link)
        {
            last.Text += run.Text;
            return;
        }
        block.Runs.Add(run);
    }

    private static DocRun? Picture(Reading r, XElement drawing)
    {
        var blip = drawing.Descendants(A + "blip").FirstOrDefault();
        if ((string?)blip?.Attribute(R + "embed") is not { } id || !r.Rels.TryGetValue(id, out var target)) return null;
        var entry = r.Zip.GetEntry("word/" + target.TrimStart('/').Replace("word/", "")) ?? r.Zip.GetEntry(target.TrimStart('/'));
        if (entry == null) return null;
        using var s = entry.Open();
        var ms = new MemoryStream();
        s.CopyTo(ms);
        var ext = Path.GetExtension(target).ToLowerInvariant();
        var type = ext switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".bmp" => "image/bmp", _ => null };
        if (type == null) return null;
        var extent = drawing.Descendants(WP + "extent").FirstOrDefault();
        double w = ((double?)extent?.Attribute("cx") ?? 0) / 12700, h = ((double?)extent?.Attribute("cy") ?? 0) / 12700;
        return new DocRun { Image = ms.ToArray(), ImageType = type, ImageW = w, ImageH = h };
    }

    private static XDocument? Xml(ZipArchive zip, string name)
    {
        var e = zip.GetEntry(name);
        if (e == null) return null;
        using var s = e.Open();
        return XDocument.Load(s);
    }

    // ---- writing ----

    /// <summary>A Word document of the paragraphs: headings in Word's own heading styles, lists, links and pictures.</summary>
    public static void Write(DocModel m, string path, bool letter)
    {
        var rels = new List<(string Id, string Type, string Target, bool External)>
        {
            ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml", false),
            ("rId2", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering", "numbering.xml", false),
        };
        var media = new List<(string Name, byte[] Data)>();
        var nums = new List<int>(); // a numbered list each: its numId's abstract (0 bullets, 1 numbers)
        var body = new XElement(W + "body");
        var picture = 0;
        int? numbered = null;
        foreach (var block in m.Blocks)
        {
            var p = new XElement(W + "p");
            var ppr = new XElement(W + "pPr");
            if (block.Heading is >= 1 and <= 6) ppr.Add(new XElement(W + "pStyle", new XAttribute(W + "val", $"Heading{block.Heading}")));
            else if (block.List != 0) ppr.Add(new XElement(W + "pStyle", new XAttribute(W + "val", "ListParagraph")));
            if (block.List != 0)
            {
                int id;
                if (block.List == -1) { id = 1; numbered = null; }
                else
                {
                    if (numbered == null || block.List == 1) { nums.Add(1); numbered = nums.Count + 2; }
                    id = numbered.Value;
                }
                ppr.Add(new XElement(W + "numPr", new XElement(W + "ilvl", new XAttribute(W + "val", 0)), new XElement(W + "numId", new XAttribute(W + "val", id))));
            }
            else numbered = null;
            if (block.Pre) ppr.Add(new XElement(W + "spacing", new XAttribute(W + "after", 0)));
            if (ppr.HasElements) p.Add(ppr);
            foreach (var r in block.Runs)
            {
                if (r.Image != null)
                {
                    var ext = r.ImageType switch { "image/jpeg" => "jpeg", "image/gif" => "gif", "image/bmp" => "bmp", _ => "png" };
                    picture++;
                    var name = $"image{picture}.{ext}";
                    media.Add((name, r.Image));
                    var rid = $"rId{rels.Count + 1}";
                    rels.Add((rid, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image", "media/" + name, false));
                    p.Add(new XElement(W + "r", Drawing(rid, picture, r.ImageW > 0 ? r.ImageW : 200, r.ImageH > 0 ? r.ImageH : 150)));
                    continue;
                }
                if (r.Text.Length == 0) continue;
                var run = RunXml(r);
                if (r.Link != null && Uri.TryCreate(r.Link, UriKind.Absolute, out _))
                {
                    var rid = $"rId{rels.Count + 1}";
                    rels.Add((rid, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink", r.Link, true));
                    p.Add(new XElement(W + "hyperlink", new XAttribute(R + "id", rid), run));
                }
                else p.Add(run);
            }
            body.Add(p);
        }
        var (pw, ph) = letter ? (12240, 15840) : (11906, 16838);
        body.Add(new XElement(W + "sectPr",
            new XElement(W + "pgSz", new XAttribute(W + "w", pw), new XAttribute(W + "h", ph)),
            new XElement(W + "pgMar", new XAttribute(W + "top", 1080), new XAttribute(W + "right", 1080), new XAttribute(W + "bottom", 1080), new XAttribute(W + "left", 1080),
                new XAttribute(W + "header", 720), new XAttribute(W + "footer", 720), new XAttribute(W + "gutter", 0))));
        var document = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(W + "document", new XAttribute(XNamespace.Xmlns + "w", W), new XAttribute(XNamespace.Xmlns + "r", R),
                new XAttribute(XNamespace.Xmlns + "wp", WP), new XAttribute(XNamespace.Xmlns + "a", A), new XAttribute(XNamespace.Xmlns + "pic", PIC), body));

        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Put(zip, "[Content_Types].xml", ContentTypes());
        Put(zip, "_rels/.rels", new XDocument(new XElement(PR + "Relationships",
            new XElement(PR + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"), new XAttribute("Target", "word/document.xml")))));
        Put(zip, "word/document.xml", document);
        Put(zip, "word/styles.xml", Styles());
        Put(zip, "word/numbering.xml", Numbering(nums.Count));
        Put(zip, "word/_rels/document.xml.rels", new XDocument(new XElement(PR + "Relationships", rels.Select(r =>
        {
            var e = new XElement(PR + "Relationship", new XAttribute("Id", r.Id), new XAttribute("Type", r.Type), new XAttribute("Target", r.Target));
            if (r.External) e.Add(new XAttribute("TargetMode", "External"));
            return e;
        }))));
        foreach (var (name, data) in media)
        {
            using var s = zip.CreateEntry("word/media/" + name).Open();
            s.Write(data);
        }
    }

    private static XElement RunXml(DocRun r)
    {
        var rpr = new XElement(W + "rPr");
        if (r.Bold) rpr.Add(new XElement(W + "b"));
        if (r.Italic) rpr.Add(new XElement(W + "i"));
        if (r.Strike) rpr.Add(new XElement(W + "strike"));
        if (r.Color != null) rpr.Add(new XElement(W + "color", new XAttribute(W + "val", r.Color)));
        else if (r.Link != null) rpr.Add(new XElement(W + "color", new XAttribute(W + "val", "0563C1")));
        if (r.Size is { } s) rpr.Add(new XElement(W + "sz", new XAttribute(W + "val", (int)Math.Round(s * 2))));
        if (r.Underline || r.Link != null) rpr.Add(new XElement(W + "u", new XAttribute(W + "val", "single")));
        var run = new XElement(W + "r");
        if (rpr.HasElements) run.Add(rpr);
        var parts = r.Text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0) run.Add(new XElement(W + "br"));
            var tabs = parts[i].Split('\t');
            for (var j = 0; j < tabs.Length; j++)
            {
                if (j > 0) run.Add(new XElement(W + "tab"));
                if (tabs[j].Length > 0) run.Add(new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), Clean(tabs[j])));
            }
        }
        return run;
    }

    /// <summary>Text with no characters XML cannot carry.</summary>
    private static string Clean(string s) => new(s.Where(ch => ch >= ' ' || ch is '\t' or '\n' or '\r').ToArray());

    private static XElement Drawing(string rid, int n, double wPt, double hPt)
    {
        long cx = (long)(wPt * 12700), cy = (long)(hPt * 12700);
        return new XElement(W + "drawing",
            new XElement(WP + "inline", new XAttribute("distT", 0), new XAttribute("distB", 0), new XAttribute("distL", 0), new XAttribute("distR", 0),
                new XElement(WP + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                new XElement(WP + "docPr", new XAttribute("id", n), new XAttribute("name", $"Picture {n}")),
                new XElement(A + "graphic",
                    new XElement(A + "graphicData", new XAttribute("uri", PIC.NamespaceName),
                        new XElement(PIC + "pic",
                            new XElement(PIC + "nvPicPr", new XElement(PIC + "cNvPr", new XAttribute("id", n), new XAttribute("name", $"Picture {n}")), new XElement(PIC + "cNvPicPr")),
                            new XElement(PIC + "blipFill", new XElement(A + "blip", new XAttribute(R + "embed", rid)), new XElement(A + "stretch", new XElement(A + "fillRect"))),
                            new XElement(PIC + "spPr",
                                new XElement(A + "xfrm", new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)), new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
                                new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst"))))))));
    }

    private static XDocument ContentTypes()
    {
        XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
        return new XDocument(new XElement(ct + "Types",
            new XElement(ct + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(ct + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement(ct + "Default", new XAttribute("Extension", "png"), new XAttribute("ContentType", "image/png")),
            new XElement(ct + "Default", new XAttribute("Extension", "jpeg"), new XAttribute("ContentType", "image/jpeg")),
            new XElement(ct + "Default", new XAttribute("Extension", "gif"), new XAttribute("ContentType", "image/gif")),
            new XElement(ct + "Default", new XAttribute("Extension", "bmp"), new XAttribute("ContentType", "image/bmp")),
            new XElement(ct + "Override", new XAttribute("PartName", "/word/document.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")),
            new XElement(ct + "Override", new XAttribute("PartName", "/word/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml")),
            new XElement(ct + "Override", new XAttribute("PartName", "/word/numbering.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"))));
    }

    private static XDocument Styles()
    {
        XElement Style(string id, string name, double? size, bool bold, int? outline, string? basedOn = "Normal")
        {
            var rpr = new XElement(W + "rPr");
            if (bold) rpr.Add(new XElement(W + "b"));
            if (size is { } s) rpr.Add(new XElement(W + "sz", new XAttribute(W + "val", (int)(s * 2))));
            var ppr = new XElement(W + "pPr", new XElement(W + "keepNext"), new XElement(W + "spacing", new XAttribute(W + "before", 240), new XAttribute(W + "after", 80)));
            if (outline is { } o) ppr.Add(new XElement(W + "outlineLvl", new XAttribute(W + "val", o)));
            var e = new XElement(W + "style", new XAttribute(W + "type", "paragraph"), new XAttribute(W + "styleId", id), new XElement(W + "name", new XAttribute(W + "val", name)));
            if (basedOn != null) e.Add(new XElement(W + "basedOn", new XAttribute(W + "val", basedOn)));
            e.Add(new XElement(W + "qFormat"), ppr, rpr);
            return e;
        }
        var normal = new XElement(W + "style", new XAttribute(W + "type", "paragraph"), new XAttribute(W + "default", 1), new XAttribute(W + "styleId", "Normal"),
            new XElement(W + "name", new XAttribute(W + "val", "Normal")), new XElement(W + "qFormat"),
            new XElement(W + "pPr", new XElement(W + "spacing", new XAttribute(W + "after", 120), new XAttribute(W + "line", 276), new XAttribute(W + "lineRule", "auto"))),
            new XElement(W + "rPr", new XElement(W + "rFonts", new XAttribute(W + "ascii", "Calibri"), new XAttribute(W + "hAnsi", "Calibri"), new XAttribute(W + "cs", "Calibri")),
                new XElement(W + "sz", new XAttribute(W + "val", 22))));
        var list = new XElement(W + "style", new XAttribute(W + "type", "paragraph"), new XAttribute(W + "styleId", "ListParagraph"),
            new XElement(W + "name", new XAttribute(W + "val", "List Paragraph")), new XElement(W + "basedOn", new XAttribute(W + "val", "Normal")), new XElement(W + "qFormat"),
            new XElement(W + "pPr", new XElement(W + "spacing", new XAttribute(W + "after", 40)), new XElement(W + "ind", new XAttribute(W + "left", 720))));
        var sizes = new[] { 20.0, 16, 14, 13, 12, 11 };
        return new XDocument(new XElement(W + "styles", new XAttribute(XNamespace.Xmlns + "w", W), normal, list,
            Enumerable.Range(1, 6).Select(i => Style($"Heading{i}", $"heading {i}", sizes[i - 1], true, i - 1))));
    }

    private static XDocument Numbering(int numberedLists)
    {
        XElement Abstract(int id, string fmt, string text) => new(W + "abstractNum", new XAttribute(W + "abstractNumId", id),
            new XElement(W + "multiLevelType", new XAttribute(W + "val", "singleLevel")),
            new XElement(W + "lvl", new XAttribute(W + "ilvl", 0),
                new XElement(W + "start", new XAttribute(W + "val", 1)),
                new XElement(W + "numFmt", new XAttribute(W + "val", fmt)),
                new XElement(W + "lvlText", new XAttribute(W + "val", text)),
                new XElement(W + "lvlJc", new XAttribute(W + "val", "left")),
                new XElement(W + "pPr", new XElement(W + "ind", new XAttribute(W + "left", 720), new XAttribute(W + "hanging", 360)))));
        var root = new XElement(W + "numbering", new XAttribute(XNamespace.Xmlns + "w", W), Abstract(0, "bullet", "•"), Abstract(1, "decimal", "%1."),
            new XElement(W + "num", new XAttribute(W + "numId", 1), new XElement(W + "abstractNumId", new XAttribute(W + "val", 0))),
            new XElement(W + "num", new XAttribute(W + "numId", 2), new XElement(W + "abstractNumId", new XAttribute(W + "val", 1))));
        for (var i = 0; i < numberedLists; i++)
            root.Add(new XElement(W + "num", new XAttribute(W + "numId", i + 3), new XElement(W + "abstractNumId", new XAttribute(W + "val", 1)),
                new XElement(W + "lvlOverride", new XAttribute(W + "ilvl", 0), new XElement(W + "startOverride", new XAttribute(W + "val", 1)))));
        return new XDocument(root);
    }

    private static void Put(ZipArchive zip, string name, XDocument x)
    {
        using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        using var w = System.Xml.XmlWriter.Create(s, new System.Xml.XmlWriterSettings { Encoding = new UTF8Encoding(false) });
        x.Save(w);
    }
}

/// <summary>OpenDocument text (.odt): its paragraphs, headings, lists and the look of its words.</summary>
internal static class DocOpen
{
    private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace T = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private static readonly XNamespace S = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    private static readonly XNamespace Fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
    private static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";

    private sealed record Look(bool Bold, bool Italic, bool Underline, bool Strike, double? Size, string? Color);

    public static DocModel Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("content.xml") ?? throw new InvalidDataException("The document could not be read.");
        XDocument content;
        using (var s = entry.Open()) content = XDocument.Load(s);
        var looks = new Dictionary<string, Look>();
        void Styles(XElement? root)
        {
            if (root == null) return;
            foreach (var st in root.Descendants(S + "style"))
            {
                if ((string?)st.Attribute(S + "name") is not { } name || st.Element(S + "text-properties") is not { } tp) continue;
                double? size = null;
                if ((string?)tp.Attribute(Fo + "font-size") is { } fs && fs.EndsWith("pt") && double.TryParse(fs[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var pt)) size = pt;
                var color = (string?)tp.Attribute(Fo + "color") is { Length: 7 } c && c != "#000000" ? c[1..].ToLowerInvariant() : null;
                looks[name] = new Look(
                    (string?)tp.Attribute(Fo + "font-weight") is "bold" or "600" or "700" or "800" or "900",
                    (string?)tp.Attribute(Fo + "font-style") is "italic" or "oblique",
                    (string?)tp.Attribute(S + "text-underline-style") is { } u && u != "none",
                    (string?)tp.Attribute(S + "text-line-through-style") is { } l && l != "none",
                    size, color);
            }
        }
        var stylesEntry = zip.GetEntry("styles.xml");
        if (stylesEntry != null) { using var s = stylesEntry.Open(); Styles(XDocument.Load(s).Root); }
        Styles(content.Root?.Element(Office + "automatic-styles"));
        var m = new DocModel { Title = Path.GetFileNameWithoutExtension(path) };
        var text = content.Root?.Element(Office + "body")?.Element(Office + "text");
        if (text != null) Blocks(m, text, looks, 0);
        return m;
    }

    private static void Blocks(DocModel m, XElement parent, Dictionary<string, Look> looks, int list)
    {
        foreach (var e in parent.Elements())
        {
            if (e.Name == T + "p" || e.Name == T + "h")
            {
                var b = new DocBlock { List = list };
                if (e.Name == T + "h") b.Heading = Math.Clamp((int?)e.Attribute(T + "outline-level") ?? 1, 1, 6);
                var look = (string?)e.Attribute(T + "style-name") is { } sn && looks.TryGetValue(sn, out var l) ? l : null;
                Inline(b, e, looks, look, null);
                m.Blocks.Add(b);
            }
            else if (e.Name == T + "list")
            {
                var n = 0;
                var numbered = ((string?)e.Attribute(T + "style-name") ?? "").Contains("Numbering", StringComparison.OrdinalIgnoreCase);
                foreach (var item in e.Elements(T + "list-item"))
                    Blocks(m, item, looks, numbered ? ++n : -1);
            }
            else if (e.Name == Table + "table")
            {
                foreach (var row in e.Descendants(Table + "table-row"))
                {
                    var b = new DocBlock();
                    var first = true;
                    foreach (var cell in row.Elements(Table + "table-cell"))
                    {
                        if (!first) b.Runs.Add(new DocRun { Text = "\t" });
                        first = false;
                        var inner = new DocModel();
                        Blocks(inner, cell, looks, 0);
                        for (var i = 0; i < inner.Blocks.Count; i++)
                        {
                            if (i > 0) b.Runs.Add(new DocRun { Text = " " });
                            b.Runs.AddRange(inner.Blocks[i].Runs);
                        }
                    }
                    m.Blocks.Add(b);
                }
            }
            else if (e.Name == T + "section" || e.Name == T + "list-header") Blocks(m, e, looks, list);
        }
    }

    private static void Inline(DocBlock b, XElement parent, Dictionary<string, Look> looks, Look? look, string? link)
    {
        DocRun Styled(string text) => new()
        {
            Text = text, Bold = look?.Bold ?? false, Italic = look?.Italic ?? false, Underline = look?.Underline ?? false, Strike = look?.Strike ?? false,
            Size = look?.Size, Color = look?.Color, Link = link,
        };
        foreach (var node in parent.Nodes())
        {
            if (node is XText t) { b.Runs.Add(Styled(t.Value)); continue; }
            if (node is not XElement e) continue;
            if (e.Name == T + "span")
            {
                var inner = (string?)e.Attribute(T + "style-name") is { } sn && looks.TryGetValue(sn, out var l) ? l : look;
                Inline(b, e, looks, inner, link);
            }
            else if (e.Name == T + "a") Inline(b, e, looks, look, (string?)e.Attribute(XLink + "href") ?? link);
            else if (e.Name == T + "s") b.Runs.Add(Styled(new string(' ', (int?)e.Attribute(T + "c") ?? 1)));
            else if (e.Name == T + "tab") b.Runs.Add(Styled("\t"));
            else if (e.Name == T + "line-break") b.Runs.Add(Styled("\n"));
            else if (e.Name != T + "note" && e.Name != T + "annotation") Inline(b, e, looks, look, link);
        }
    }
}

/// <summary>CSV and JSON, one into the other (the Mac's ConvEngine.csvToJSON and jsonToCSV, with FCText's CSV).</summary>
internal static class ConvData
{
    /// <summary>CSV's rows: commas between cells, quotes round a cell with commas, quotes or lines in it.</summary>
    public static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var chars = text.Replace("\r\n", "\n").Replace('\r', '\n');
        for (var i = 0; i < chars.Length; i++)
        {
            var ch = chars[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < chars.Length && chars[i + 1] == '"') { cell.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else cell.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { row.Add(cell.ToString()); cell.Clear(); }
            else if (ch == '\n') { row.Add(cell.ToString()); rows.Add(row); row = new List<string>(); cell.Clear(); }
            else cell.Append(ch);
        }
        row.Add(cell.ToString());
        if (row.Any(c => c.Length > 0)) rows.Add(row);
        return rows;
    }

    public static string Cell(string s) => s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string J(string s) => JsonSerializer.Serialize(s, Json);

    public static string CsvToJson(string text)
    {
        var rows = ParseCsv(text);
        if (rows.Count == 0 || rows[0].Count == 0) throw new InvalidDataException("The CSV has no rows.");
        var keys = rows[0].Select((k, i) => k.Trim().Length == 0 ? $"column{i + 1}" : k).ToList();
        var objects = rows.Skip(1).Select(r => "  {" + string.Join(", ", keys.Select((k, i) => J(k) + ": " + J(i < r.Count ? r[i] : ""))) + "}");
        return "[\n" + string.Join(",\n", objects) + "\n]\n";
    }

    public static string JsonToCsv(byte[] data)
    {
        JsonNode? any;
        try { any = JsonNode.Parse(DocText.Decode(data)); }
        catch { throw new InvalidDataException("The JSON could not be read."); }
        var list = new List<JsonObject>();
        if (any is JsonArray arr && arr.All(x => x is JsonObject)) list = arr.Cast<JsonObject>().ToList();
        else if (any is JsonObject obj && obj.Select(kv => kv.Value).FirstOrDefault(v => v is JsonArray a && a.Count > 0 && a.All(x => x is JsonObject)) is JsonArray inner)
            list = inner.Cast<JsonObject>().ToList();
        else if (any is JsonObject single) list = new List<JsonObject> { single };
        if (list.Count == 0) throw new InvalidDataException("The JSON holds no list of records.");
        var keys = new List<string>();
        foreach (var o in list) foreach (var k in o.Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal)) if (!keys.Contains(k)) keys.Add(k);
        string Value(JsonNode? v) => v switch
        {
            null => "",
            JsonValue jv when jv.TryGetValue<string>(out var s) => Cell(s),
            JsonValue jv => jv.ToJsonString(),
            _ => Cell(v.ToJsonString(Json)),
        };
        var lines = new List<string> { string.Join(",", keys.Select(Cell)) };
        lines.AddRange(list.Select(o => string.Join(",", keys.Select(k => Value(o[k])))));
        return string.Join("\n", lines) + "\n";
    }
}
