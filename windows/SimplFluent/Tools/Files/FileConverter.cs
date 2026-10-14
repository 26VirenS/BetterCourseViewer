using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Simpl.Kit;
using Simpl.Screens;
using Simpl.Support;
using Windows.Graphics.Imaging;
using Wpf.Ui.Controls;
using BitmapEncoder = Windows.Graphics.Imaging.BitmapEncoder;
using BitmapFrame = System.Windows.Media.Imaging.BitmapFrame;
using Image = System.Windows.Controls.Image;
using List = System.Windows.Documents.List;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

// The file converter, as the web's and the Mac's (FileConverter.swift): drop files and the tool works out what they
// are, then offers only the conversions that exist for them — all on this PC, with Windows' own means (no service, no
// key): pictures to PNG, JPEG, HEIC, TIFF or a PDF (one each, or all in one) by Windows' imaging; Word, OpenDocument,
// rich text, web pages and plain text to PDF, Word, rich text, text or HTML (Word read and written as its open format,
// rich text by WPF, web pages laid out and printed to PDF by Edge's WebView2); PDFs to text, Word or rich text (their
// words read off the pages by Windows' OCR — Windows has no reader of a PDF's own text) and to PNG and JPEG pages (drawn
// by Windows.Data.Pdf); CSV to JSON and back. One kind of file a batch: files of another kind dropped in alongside are
// named as left out, never silently dropped; one bad file never stops the rest.

#pragma warning disable CA2255
internal static class FileConverterRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.Register("conv", c => new FileConverterTool(c));
}
#pragma warning restore CA2255

internal enum ConvKind { Image, Document, Pdf, Csv, Json, Unsupported }

/// <summary>What a file is to the converter, and what it can become (the Mac's ConvKind).</summary>
internal static class ConvKinds
{
    public static string Label(ConvKind k) => k switch
    {
        ConvKind.Image => "Pictures", ConvKind.Document => "Documents", ConvKind.Pdf => "PDFs", ConvKind.Csv => "CSV", ConvKind.Json => "JSON", _ => "Other files",
    };

    public static SymbolRegular Symbol(ConvKind k) => k switch
    {
        ConvKind.Image => SymbolRegular.Image24, ConvKind.Document => SymbolRegular.DocumentText24, ConvKind.Pdf => SymbolRegular.DocumentPdf24,
        ConvKind.Csv or ConvKind.Json => SymbolRegular.Table24, _ => SymbolRegular.DocumentQuestionMark24,
    };

    public static ConvKind Of(string path) => Path.GetExtension(path).TrimStart('.').ToLowerInvariant() switch
    {
        "png" or "jpg" or "jpeg" or "heic" or "heif" or "tif" or "tiff" or "gif" or "bmp" or "webp" => ConvKind.Image,
        "docx" or "doc" or "rtf" or "rtfd" or "odt" or "html" or "htm" or "txt" or "md" or "markdown" or "text" => ConvKind.Document,
        "pdf" => ConvKind.Pdf,
        "csv" => ConvKind.Csv,
        "json" => ConvKind.Json,
        _ => ConvKind.Unsupported,
    };

    public static (string Key, string Label)[] Targets(ConvKind k) => k switch
    {
        ConvKind.Image => new[] { ("png", "PNG"), ("jpeg", "JPEG"), ("heic", "HEIC"), ("tiff", "TIFF"), ("pdf", "PDF") },
        ConvKind.Document => new[] { ("pdf", "PDF"), ("docx", "Word"), ("rtf", "Rich text"), ("txt", "Text"), ("html", "HTML") },
        ConvKind.Pdf => new[] { ("txt", "Text"), ("docx", "Word"), ("rtf", "Rich text"), ("png", "PNG pages"), ("jpeg", "JPEG pages") },
        ConvKind.Csv => new[] { ("json", "JSON") },
        ConvKind.Json => new[] { ("csv", "CSV") },
        _ => Array.Empty<(string, string)>(),
    };

    public static string Ext(string target) => target == "jpeg" ? "jpg" : target;

    /// <summary>The save dialog's filter for a kind of file made.</summary>
    public static string Filter(string target) => target switch
    {
        "png" => "PNG picture|*.png", "jpeg" => "JPEG picture|*.jpg;*.jpeg", "heic" => "HEIC picture|*.heic", "tiff" => "TIFF picture|*.tiff;*.tif",
        "pdf" => "PDF document|*.pdf", "docx" => "Word document|*.docx", "rtf" => "Rich text|*.rtf", "txt" => "Text|*.txt", "html" => "Web page|*.html;*.htm",
        "json" => "JSON|*.json", "csv" => "CSV|*.csv", _ => "All files|*.*",
    };
}

/// <summary>Something that could not be converted, said in a word.</summary>
internal sealed class ConvError(string message) : Exception(message);

/// <summary>The conversions themselves (the Mac's ConvEngine), with Windows' own means.</summary>
internal static class ConvEngine
{
    /// <summary>US Letter where the region uses it, A4 elsewhere (the Mac's printer settings' paper).</summary>
    public static bool Letter => RegionInfo.CurrentRegion.TwoLetterISORegionName is "US" or "CA" or "MX" or "PH" or "CL" or "CO" or "VE" or "PR" or "GT" or "CR";

    // ---- pictures ----

    public static async Task Picture(string src, string target, double quality, string dest)
    {
        WinImage.Decoded d;
        try { d = await WinImage.Read(src); }
        catch { throw new ConvError("The picture could not be read."); }
        var sb = d.Bitmap;
        Guid encoder;
        double? q = null;
        switch (target)
        {
            case "png": encoder = BitmapEncoder.PngEncoderId; break;
            case "jpeg": encoder = BitmapEncoder.JpegEncoderId; q = quality; sb = WinImage.OnWhite(sb); break;
            case "heic": encoder = BitmapEncoder.HeifEncoderId; q = quality; sb = WinImage.OnWhite(sb); break;
            default: encoder = BitmapEncoder.TiffEncoderId; break;
        }
        byte[] bytes;
        try { bytes = await WinImage.Write(sb, encoder, q); }
        catch (Exception first)
        {
            Log.Write($"convert {target}: {first.Message}");
            try
            {
                // (an encoder that takes no quality setting: its own)
                if (q == null) throw;
                bytes = await WinImage.Write(sb, encoder);
            }
            catch
            {
                if (target == "heic") throw new ConvError("This PC cannot write HEIC pictures: Windows needs the HEIF Image Extensions from the Microsoft Store.");
                throw new ConvError("The picture could not be written.");
            }
        }
        try { await File.WriteAllBytesAsync(dest, bytes); }
        catch { throw new ConvError("The picture could not be written."); }
    }

    /// <summary>Pictures as the pages of one PDF, each page the size of its picture.</summary>
    public static async Task PdfFromPictures(IReadOnlyList<string> sources, string dest)
    {
        var pictures = new List<PdfWork.Picture>();
        foreach (var src in sources)
        {
            try
            {
                var d = await WinImage.Read(src);
                var sb = d.Bitmap;
                byte[] data;
                if (WinImage.HasAlpha(sb)) data = await WinImage.Write(sb, BitmapEncoder.PngEncoderId);
                else if (Path.GetExtension(src).ToLowerInvariant() is ".jpg" or ".jpeg" or ".heic" or ".heif" or ".webp")
                    data = await WinImage.Write(WinImage.OnWhite(sb), BitmapEncoder.JpegEncoderId, 0.92);
                else data = await WinImage.Write(WinImage.OnWhite(sb), BitmapEncoder.PngEncoderId);
                double dx = d.DpiX > 1 ? d.DpiX : 72, dy = d.DpiY > 1 ? d.DpiY : 72;
                pictures.Add(new PdfWork.Picture(data, sb.PixelWidth * 72 / dx, sb.PixelHeight * 72 / dy));
            }
            catch (Exception e) { Log.Write($"picture to pdf {Path.GetFileName(src)}: {e.Message}"); }
        }
        if (pictures.Count == 0) throw new ConvError("None of the pictures could be read.");
        try { await Task.Run(() => PdfWork.Pictures(pictures, dest)); }
        catch (Exception e)
        {
            Log.Write("pictures pdf: " + e);
            throw new ConvError("The PDF could not be written.");
        }
    }

    // ---- documents ----

    public static async Task<DocModel> Document(string path, Window? owner)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            switch (ext)
            {
                case ".txt" or ".md" or ".markdown" or ".text": return DocText.Read(path);
                case ".docx": return DocWord.Read(path);
                case ".odt": return DocOpen.Read(path);
                case ".rtf": return Rtf(path);
                case ".html" or ".htm": return await ConvWeb.Read(path, owner);
                case ".doc": throw new ConvError("Word 97–2003 documents (.doc) can’t be read without Word: open it in Word and save it as a Word document (.docx) first.");
                case ".rtfd": throw new ConvError("This is a Mac’s rich text package; it can’t be read on Windows.");
            }
        }
        catch (ConvError) { throw; }
        catch (Exception e)
        {
            Log.Write($"read {Path.GetFileName(path)}: {e}");
            throw new ConvError("The document could not be read.");
        }
        throw new ConvError("The document could not be read.");
    }

    public static async Task Write(DocModel m, string target, string dest, Window? owner, string? htmlSource = null)
    {
        try
        {
            switch (target)
            {
                case "txt": await File.WriteAllTextAsync(dest, DocText.Write(m), new UTF8Encoding(false)); break;
                case "html": await File.WriteAllTextAsync(dest, DocHtml.Write(m), new UTF8Encoding(false)); break;
                case "docx": DocWord.Write(m, dest, Letter); break;
                case "rtf": WriteRtf(m, dest); break;
                case "pdf": await ConvWeb.Print(htmlSource, htmlSource == null ? DocHtml.Write(m) : null, dest, owner); break;
                default: throw new ConvError("Nothing to convert this to.");
            }
        }
        catch (ConvError) { throw; }
        catch (Exception e)
        {
            Log.Write($"write {target}: {e}");
            throw new ConvError(target == "pdf" ? "The PDF could not be written." : "The document could not be written.");
        }
    }

    /// <summary>Rich text, read by WPF's own reader into its paragraphs and runs.</summary>
    private static DocModel Rtf(string path)
    {
        var doc = new FlowDocument { FontSize = 16, Foreground = Brushes.Black, FontFamily = new FontFamily("Calibri") };
        var range = new TextRange(doc.ContentStart, doc.ContentEnd);
        using (var fs = File.OpenRead(path)) range.Load(fs, DataFormats.Rtf);
        var m = new DocModel { Title = Path.GetFileNameWithoutExtension(path) };
        Blocks(doc.Blocks, m, 0);
        return m;
    }

    private static void Blocks(BlockCollection blocks, DocModel m, int list)
    {
        foreach (var b in blocks)
        {
            switch (b)
            {
                case Paragraph p:
                    var block = new DocBlock { List = list };
                    foreach (var i in p.Inlines) Inline(i, block, null);
                    m.Blocks.Add(block);
                    list = 0;
                    break;
                case List l:
                    var n = l.StartIndex;
                    var bullets = l.MarkerStyle is TextMarkerStyle.Disc or TextMarkerStyle.Circle or TextMarkerStyle.Square or TextMarkerStyle.Box or TextMarkerStyle.None;
                    foreach (var item in l.ListItems) Blocks(item.Blocks, m, bullets ? -1 : n++);
                    break;
                case Section s:
                    Blocks(s.Blocks, m, list);
                    break;
                case Table t:
                    foreach (var group in t.RowGroups)
                        foreach (var row in group.Rows)
                        {
                            var line = new DocBlock();
                            var first = true;
                            foreach (var cell in row.Cells)
                            {
                                if (!first) line.Runs.Add(new DocRun { Text = "\t" });
                                first = false;
                                var inner = new DocModel();
                                Blocks(cell.Blocks, inner, 0);
                                for (var k = 0; k < inner.Blocks.Count; k++)
                                {
                                    if (k > 0) line.Runs.Add(new DocRun { Text = " " });
                                    line.Runs.AddRange(inner.Blocks[k].Runs);
                                }
                            }
                            m.Blocks.Add(line);
                        }
                    break;
            }
        }
    }

    private static void Inline(System.Windows.Documents.Inline i, DocBlock b, string? link)
    {
        switch (i)
        {
            case Run r:
                var color = r.Foreground is SolidColorBrush sc && (sc.Color.R > 24 || sc.Color.G > 24 || sc.Color.B > 24) ? $"{sc.Color.R:x2}{sc.Color.G:x2}{sc.Color.B:x2}" : null;
                b.Runs.Add(new DocRun
                {
                    Text = r.Text, Bold = r.FontWeight.ToOpenTypeWeight() >= 600, Italic = r.FontStyle != FontStyles.Normal,
                    Underline = Decorated(r, TextDecorationLocation.Underline), Strike = Decorated(r, TextDecorationLocation.Strikethrough),
                    Size = Math.Round(r.FontSize * 72 / 96 * 2) / 2, Color = color, Link = link,
                });
                break;
            case LineBreak:
                b.Runs.Add(new DocRun { Text = "\n" });
                break;
            case Hyperlink h:
                foreach (var c in h.Inlines) Inline(c, b, h.NavigateUri?.ToString() ?? link);
                break;
            case Span s:
                foreach (var c in s.Inlines) Inline(c, b, link);
                break;
            case InlineUIContainer { Child: Image { Source: BitmapSource bs } img }:
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bs));
                var ms = new MemoryStream();
                enc.Save(ms);
                var w = double.IsNaN(img.Width) ? bs.Width : img.Width;
                var hgt = double.IsNaN(img.Height) ? bs.Height : img.Height;
                b.Runs.Add(new DocRun { Image = ms.ToArray(), ImageType = "image/png", ImageW = w * 72 / 96, ImageH = hgt * 72 / 96 });
                break;
        }
    }

    /// <summary>Whether a run is underlined or struck through, by itself or by what it is in.</summary>
    private static bool Decorated(System.Windows.Documents.Inline i, TextDecorationLocation where)
    {
        for (DependencyObject? d = i; d is TextElement t; d = t.Parent)
        {
            var decorations = t switch { System.Windows.Documents.Inline x => x.TextDecorations, Paragraph p => p.TextDecorations, _ => null };
            if (decorations?.Any(x => x.Location == where) == true) return true;
        }
        return false;
    }

    /// <summary>Rich text written by WPF's own writer, from the paragraphs.</summary>
    private static void WriteRtf(DocModel m, string dest)
    {
        var doc = new FlowDocument { FontFamily = new FontFamily("Calibri"), FontSize = 11 * 96 / 72.0, Foreground = Brushes.Black };
        var headings = new[] { 20.0, 16, 14, 13, 12, 11 };
        List? list = null;
        foreach (var block in m.Blocks)
        {
            var p = new Paragraph { Margin = new Thickness(0, 0, 0, block.Pre || m.PlainText ? 0 : 6) };
            if (block.Heading is >= 1 and <= 6)
            {
                p.FontSize = headings[block.Heading - 1] * 96 / 72;
                p.FontWeight = FontWeights.Bold;
                p.Margin = new Thickness(0, 10, 0, 4);
            }
            foreach (var r in block.Runs)
            {
                if (r.Image != null)
                {
                    try
                    {
                        var img = new Image { Source = WinImage.Show(r.Image), Width = r.ImageW * 96 / 72, Height = r.ImageH * 96 / 72 };
                        p.Inlines.Add(new InlineUIContainer(img));
                    }
                    catch { }
                    continue;
                }
                var parts = r.Text.Replace("\r\n", "\n").Split('\n');
                for (var k = 0; k < parts.Length; k++)
                {
                    if (k > 0) p.Inlines.Add(new LineBreak());
                    if (parts[k].Length == 0) continue;
                    var run = new Run(parts[k]);
                    if (r.Bold) run.FontWeight = FontWeights.Bold;
                    if (r.Italic) run.FontStyle = FontStyles.Italic;
                    if (r.Size is { } s) run.FontSize = s * 96 / 72;
                    if (r.Color is { Length: 6 } c) run.Foreground = new SolidColorBrush(K.Hex("#" + c, Colors.Black));
                    var decorations = new TextDecorationCollection();
                    if (r.Underline || r.Link != null) decorations.Add(TextDecorations.Underline[0]);
                    if (r.Strike) decorations.Add(TextDecorations.Strikethrough[0]);
                    if (decorations.Count > 0) run.TextDecorations = decorations;
                    if (r.Link != null && Uri.TryCreate(r.Link, UriKind.Absolute, out var u))
                    {
                        if (r.Color == null) run.Foreground = new SolidColorBrush(Color.FromRgb(0x05, 0x63, 0xc1));
                        p.Inlines.Add(new Hyperlink(run) { NavigateUri = u });
                    }
                    else p.Inlines.Add(run);
                }
            }
            if (block.List != 0)
            {
                var marker = block.List == -1 ? TextMarkerStyle.Disc : TextMarkerStyle.Decimal;
                if (list == null || list.MarkerStyle != marker || block.List == 1)
                {
                    list = new List { MarkerStyle = marker, Margin = new Thickness(0, 0, 0, 6) };
                    if (block.List > 1) list.StartIndex = block.List;
                    doc.Blocks.Add(list);
                }
                p.Margin = new Thickness(0, 0, 0, 2);
                list.ListItems.Add(new ListItem(p));
            }
            else
            {
                list = null;
                doc.Blocks.Add(p);
            }
        }
        var range = new TextRange(doc.ContentStart, doc.ContentEnd);
        using var fs = File.Create(dest);
        range.Save(fs, DataFormats.Rtf);
    }

    // ---- PDFs ----

    /// <summary>Each page as a picture, twice its size; the files written.</summary>
    public static async Task<List<string>> Pages(string src, string target, double quality, string name, string folder)
    {
        var doc = await OpenPdf(src);
        var outs = new List<string>();
        for (uint i = 0; i < doc.PageCount; i++)
        {
            try
            {
                var page = doc.GetPage(i);
                var png = await WinPdf.Png(page, page.Size.Width * 72 / 96 * 2);
                var bytes = png;
                if (target == "jpeg")
                {
                    var sb = (await WinImage.Read(png, 0, BitmapAlphaMode.Premultiplied)).Bitmap;
                    bytes = await WinImage.Write(WinImage.OnWhite(sb), BitmapEncoder.JpegEncoderId, quality);
                }
                var path = Files.Free($"{name} - page {i + 1}", ConvKinds.Ext(target), folder);
                await File.WriteAllBytesAsync(path, bytes);
                outs.Add(path);
            }
            catch (Exception e) { Log.Write($"page {i}: {e.Message}"); }
        }
        if (outs.Count == 0) throw new ConvError("No page could be drawn.");
        return outs;
    }

    /// <summary>A PDF's words, page by page, as Windows reads them off its pages.</summary>
    public static async Task<DocModel> PdfWords(string src)
    {
        var doc = await OpenPdf(src);
        if (WinOcr.Engine == null) throw new ConvError(WinOcr.Missing);
        var m = new DocModel { Title = Path.GetFileNameWithoutExtension(src) };
        for (uint i = 0; i < doc.PageCount; i++)
        {
            if (i > 0) m.Blocks.Add(new DocBlock());
            var (r, _) = await WinOcr.Page(doc.GetPage(i));
            foreach (var line in r.Lines) m.Blocks.Add(new DocBlock { Runs = { new DocRun { Text = line.Text } } });
        }
        return m;
    }

    private static async Task<Windows.Data.Pdf.PdfDocument> OpenPdf(string src)
    {
        try { return await WinPdf.Open(await File.ReadAllBytesAsync(src)); }
        catch (Exception e)
        {
            if (e.HResult == unchecked((int)0x8007052B) || e.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
                throw new ConvError("The PDF is locked with a password.");
            throw new ConvError("The PDF could not be opened.");
        }
    }
}

/// <summary>
/// Edge's WebView2, out of sight, for the converter: a web page read into paragraphs as the browser lays it out, and a
/// document printed to PDF — on the app's own web data, as the engine's.
/// </summary>
internal static class ConvWeb
{
    private static Task<CoreWebView2Environment>? environment;

    private static async Task<T> With<T>(Uri page, Window? owner, Func<CoreWebView2, CoreWebView2Environment, Task<T>> work)
    {
        environment ??= Engine.Engine.WebEnvironment();
        var env = await environment;
        var window = owner ?? Application.Current.MainWindow ?? throw new InvalidOperationException("No window to lay the page out in.");
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var ctl = await env.CreateCoreWebView2ControllerAsync(hwnd);
        try
        {
            ctl.IsVisible = false;
            ctl.Bounds = new System.Drawing.Rectangle(0, 0, 816, 1056);
            var core = ctl.CoreWebView2;
            core.Settings.IsScriptEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            core.NavigationCompleted += (_, a) => done.TrySetResult(a.IsSuccess);
            core.Navigate(page.AbsoluteUri);
            if (await Task.WhenAny(done.Task, Task.Delay(30_000)) != done.Task) throw new TimeoutException("The page took too long to lay out.");
            return await work(core, env);
        }
        finally
        {
            ctl.Close();
        }
    }

    private static string Scratch()
    {
        var dir = Path.Combine(AppPaths.Root, "Converting");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Guid.NewGuid().ToString("N") + ".html");
    }

    /// <summary>A web page (a file of one, or one written for a document) printed to a PDF on US Letter or A4.</summary>
    public static async Task Print(string? file, string? html, string dest, Window? owner)
    {
        var temp = file == null ? Scratch() : null;
        try
        {
            if (temp != null) await File.WriteAllTextAsync(temp, html ?? "", new UTF8Encoding(false));
            var ok = await With(new Uri(file ?? temp!), owner, async (core, env) =>
            {
                var s = env.CreatePrintSettings();
                s.Orientation = CoreWebView2PrintOrientation.Portrait;
                (s.PageWidth, s.PageHeight) = ConvEngine.Letter ? (8.5, 11) : (8.27, 11.69);
                s.MarginTop = s.MarginBottom = s.MarginLeft = s.MarginRight = 0.75;
                s.ShouldPrintBackgrounds = true;
                s.ShouldPrintHeaderAndFooter = false;
                return await core.PrintToPdfAsync(dest, s);
            });
            if (!ok || !File.Exists(dest)) throw new ConvError("The PDF could not be written.");
        }
        finally
        {
            if (temp != null) try { File.Delete(temp); } catch { }
        }
    }

    /// <summary>A web page's paragraphs and the look of their words, as the browser shows them.</summary>
    public static async Task<DocModel> Read(string file, Window? owner)
    {
        var json = await With(new Uri(file), owner, (core, _) => core.ExecuteScriptAsync(Walker));
        var inner = JsonSerializer.Deserialize<string>(json) ?? "{}";
        using var d = JsonDocument.Parse(inner);
        var m = new DocModel { Title = d.RootElement.TryGetProperty("title", out var t) && t.GetString() is { Length: > 0 } title ? title : Path.GetFileNameWithoutExtension(file) };
        foreach (var b in d.RootElement.GetProperty("blocks").EnumerateArray())
        {
            var block = new DocBlock { Heading = b.GetProperty("h").GetInt32(), List = b.GetProperty("l").GetInt32(), Pre = b.GetProperty("pre").GetBoolean() };
            foreach (var r in b.GetProperty("runs").EnumerateArray())
            {
                var run = new DocRun { Text = r.GetProperty("t").GetString() ?? "" };
                if (r.TryGetProperty("b", out var bold)) run.Bold = bold.GetBoolean();
                if (r.TryGetProperty("i", out var it)) run.Italic = it.GetBoolean();
                if (r.TryGetProperty("u", out var u)) run.Underline = u.GetBoolean();
                if (r.TryGetProperty("s", out var st)) run.Strike = st.GetBoolean();
                if (r.TryGetProperty("z", out var z) && z.ValueKind == JsonValueKind.Number && Math.Abs(z.GetDouble() - 12) > 0.1) run.Size = z.GetDouble();
                if (r.TryGetProperty("c", out var c) && c.GetString() is { Length: 6 } hex && hex != "000000") run.Color = hex;
                if (r.TryGetProperty("a", out var a) && a.ValueKind == JsonValueKind.String) run.Link = a.GetString();
                block.Runs.Add(run);
            }
            m.Blocks.Add(block);
        }
        return m;
    }

    /// <summary>The page's body walked: its blocks (headings, list items, preformatted) and their runs of words.</summary>
    private const string Walker = """
        (() => {
          const out = []; let cur = null;
          const blockish = d => ['block', 'list-item', 'table', 'table-row', 'flex', 'grid', 'table-caption', 'flow-root'].includes(d);
          function mark(el) {
            if (!el) return;
            const t = el.tagName;
            if (/^H[1-6]$/.test(t)) cur.h = +t[1];
            if (t === 'PRE') cur.pre = true;
            if (t === 'LI') {
              const p = el.parentElement;
              if (p && p.tagName === 'OL') { let n = p.start || 1; for (const c of p.children) { if (c === el) break; if (c.tagName === 'LI') n++; } cur.l = n; }
              else cur.l = -1;
            }
          }
          function start(el) {
            if (cur && cur.runs.length === 0) { mark(el); return; }
            cur = { h: 0, l: 0, pre: false, runs: [] }; out.push(cur); mark(el);
          }
          function hex(c) {
            const m = /rgba?\((\d+),\s*(\d+),\s*(\d+)/.exec(c || '');
            return m ? [m[1], m[2], m[3]].map(x => (+x).toString(16).padStart(2, '0')).join('') : null;
          }
          function text(node) {
            const el = node.parentElement, s = getComputedStyle(el);
            let v = node.nodeValue;
            const keep = s.whiteSpace.startsWith('pre') || s.whiteSpace === 'break-spaces';
            if (!keep) v = v.replace(/\s+/g, ' ');
            if (!cur) { if (!v.trim()) return; start(null); }
            if (cur.runs.length === 0 && !keep) v = v.replace(/^\s+/, '');
            if (!v) return;
            const link = el.closest('a[href]'), deco = s.textDecorationLine || '';
            cur.runs.push({ t: v, b: parseInt(s.fontWeight, 10) >= 600, i: s.fontStyle !== 'normal', u: !!el.closest('u,ins') || deco.includes('underline'),
              s: !!el.closest('s,strike,del') || deco.includes('line-through'), z: Math.round(parseFloat(s.fontSize) * 0.75 * 2) / 2, c: hex(s.color), a: link ? link.href : null });
          }
          function walk(node) {
            if (node.nodeType === 3) { text(node); return; }
            if (node.nodeType !== 1) return;
            const el = node, t = el.tagName;
            if (/^(SCRIPT|STYLE|HEAD|NOSCRIPT|TEMPLATE|SVG|IFRAME|OBJECT|BUTTON|SELECT|INPUT|TEXTAREA)$/.test(t)) return;
            const s = getComputedStyle(el);
            if (s.display === 'none' || s.visibility === 'hidden') return;
            if (t === 'BR') { if (!cur) start(null); cur.runs.push({ t: '\n' }); return; }
            const block = blockish(s.display) || /^(P|DIV|LI|H[1-6]|PRE|TR|BLOCKQUOTE)$/.test(t);
            if (block) { if (cur && cur.runs.length > 0) cur = null; start(el); }
            for (const c of el.childNodes) walk(c);
            if ((t === 'TD' || t === 'TH') && cur) cur.runs.push({ t: '\t' });
            if (block && cur && cur.runs.length > 0) cur = null;
          }
          if (document.body) walk(document.body);
          return JSON.stringify({ title: document.title, blocks: out.filter(b => b.runs.some(r => r.t && r.t.trim())) });
        })()
        """;
}

/// <summary>A file in the converter's list, and how its conversion went.</summary>
internal sealed class ConvFile
{
    public enum Stage { Waiting, Working, Done, Failed }

    public required string Path { get; init; }
    public Stage State = Stage.Waiting;
    public string Said = "";
    public List<string> Outputs = new();
    public ConvKind Kind => ConvKinds.Of(Path);
}

/// <summary>
/// What the converter holds (the Mac's FileConverterTool state, kept while the view is built again): the files, what
/// they become, the JPEG and HEIC quality, whether pictures go into one PDF, and what was left out or went wrong.
/// </summary>
internal sealed class ConvState
{
    public static readonly ConvState Shared = new();

    public readonly List<ConvFile> Files = new();
    public string Target = "";
    public double Quality = 0.85;
    public bool Combine = true;
    public bool Busy;
    public List<string> Left = new();
    public string? Message;

    public event Action? Changed;

    public void Raise() => Changed?.Invoke();

    public ConvKind? Kind => Files.Count > 0 ? Files[0].Kind : null;

    /// <summary>What these files can become (not what they already are).</summary>
    public (string Key, string Label)[] Targets
    {
        get
        {
            if (Kind is not { } k) return Array.Empty<(string, string)>();
            var own = Path.GetExtension(Files[0].Path).TrimStart('.').ToLowerInvariant();
            return ConvKinds.Targets(k).Where(t => t.Key != own && !(t.Key == "jpeg" && own == "jpg") && !(own == "htm" && t.Key == "html") && !(own == "tif" && t.Key == "tiff") && !(own == "heif" && t.Key == "heic")).ToArray();
        }
    }

    public bool SplitsPages => Kind == ConvKind.Pdf && Target is "png" or "jpeg";

    public void Add(IEnumerable<string> paths)
    {
        Message = null;
        var real = paths.Where(File.Exists).ToList();
        if (real.Count == 0) { Raise(); return; }
        var first = Kind ?? real.Select(ConvKinds.Of).Cast<ConvKind?>().FirstOrDefault(k => k != ConvKind.Unsupported) ?? ConvKind.Unsupported;
        var skipped = new List<string>();
        foreach (var u in real)
        {
            var k = ConvKinds.Of(u);
            if (k == first && !Files.Any(f => string.Equals(f.Path, u, StringComparison.OrdinalIgnoreCase))) Files.Add(new ConvFile { Path = u });
            else if (k != first) skipped.Add(Path.GetFileName(u));
        }
        Left = skipped;
        if (first == ConvKind.Unsupported)
            Message = "Those files cannot be converted on Windows yet: slides and spreadsheets can be saved as PDF from PowerPoint or Excel.";
        if (!Targets.Any(t => t.Key == Target)) Target = Targets.FirstOrDefault().Key ?? "";
        Raise();
    }

    public void Remove(ConvFile f)
    {
        Files.Remove(f);
        if (Files.Count == 0) Left = new();
        Raise();
    }

    public void Clear()
    {
        Files.Clear();
        Left = new();
        Message = null;
        Raise();
    }

    public void Choose(string target)
    {
        Target = target;
        Raise();
    }

    public async void Convert(Window? owner)
    {
        if (Kind is not { } kind || Target.Length == 0 || Busy) return;
        Message = null;
        var t = Target;
        // where the results go: one file → a save dialog; more → a folder
        string? single = null, folder = null;
        var oneOut = (Files.Count == 1 && !SplitsPages) || (kind == ConvKind.Image && t == "pdf" && Combine && Files.Count > 1);
        if (oneOut)
        {
            var name = Files.Count == 1 ? Path.GetFileNameWithoutExtension(Files[0].Path) : "Pictures";
            single = global::Simpl.Tools.Files.SaveAs(name + "." + ConvKinds.Ext(t), ConvKinds.Filter(t), owner);
            if (single == null) return;
        }
        else
        {
            folder = global::Simpl.Tools.Files.ChooseFolder("Choose where the converted files go.", owner);
            if (folder == null) return;
        }
        Busy = true;
        foreach (var f in Files) { f.State = ConvFile.Stage.Waiting; f.Outputs = new(); f.Said = ""; }
        Raise();
        var files = Files.ToList();
        try
        {
            if (kind == ConvKind.Image && t == "pdf" && Combine && files.Count > 1 && single != null)
            {
                foreach (var f in files) f.State = ConvFile.Stage.Working;
                Raise();
                try
                {
                    await ConvEngine.PdfFromPictures(files.Select(f => f.Path).ToList(), single);
                    foreach (var f in files) { f.State = ConvFile.Stage.Done; f.Said = $"In {Path.GetFileName(single)}"; f.Outputs = new() { single }; }
                    global::Simpl.Tools.Files.Reveal(new[] { single });
                }
                catch (Exception e)
                {
                    foreach (var f in files) { f.State = ConvFile.Stage.Failed; f.Said = Why(e); }
                }
                return;
            }
            var all = new List<string>();
            foreach (var f in files)
            {
                f.State = ConvFile.Stage.Working;
                Raise();
                await Task.Yield();
                var name = Path.GetFileNameWithoutExtension(f.Path);
                var dest = single ?? global::Simpl.Tools.Files.Free(name, ConvKinds.Ext(t), folder ?? Path.GetDirectoryName(f.Path)!);
                try
                {
                    var outs = await Run(kind, t, f.Path, dest, name, folder ?? Path.GetDirectoryName(dest)!, owner);
                    f.Outputs = outs;
                    f.State = ConvFile.Stage.Done;
                    f.Said = outs.Count > 1 ? ToolKit.Plural(outs.Count, "file") : outs.Count == 1 ? global::Simpl.Tools.Files.SizeText(outs[0]) : "Done";
                    all.AddRange(outs);
                }
                catch (Exception e)
                {
                    f.State = ConvFile.Stage.Failed;
                    f.Said = Why(e);
                }
                Raise();
            }
            if (all.Count > 0) global::Simpl.Tools.Files.Reveal(all);
        }
        finally
        {
            Busy = false;
            Raise();
            Shot.Moved();
        }
    }

    private static string Why(Exception e)
    {
        if (e is ConvError) return e.Message;
        Log.Write("convert: " + e);
        return e.Message;
    }

    private async Task<List<string>> Run(ConvKind kind, string target, string src, string dest, string name, string folder, Window? owner)
    {
        var q = Quality;
        switch (kind)
        {
            case ConvKind.Image:
                if (target == "pdf") await ConvEngine.PdfFromPictures(new[] { src }, dest);
                else await ConvEngine.Picture(src, target, q, dest);
                return new() { dest };
            case ConvKind.Document:
            {
                var html = Path.GetExtension(src).ToLowerInvariant() is ".html" or ".htm";
                if (html && target == "pdf")
                {
                    // (a web page printed as it is, its own styles and pictures kept)
                    await ConvEngine.Write(new DocModel(), "pdf", dest, owner, src);
                    return new() { dest };
                }
                var m = await ConvEngine.Document(src, owner);
                await ConvEngine.Write(m, target, dest, owner);
                return new() { dest };
            }
            case ConvKind.Pdf:
                if (target is "png" or "jpeg") return await ConvEngine.Pages(src, target, q, name, folder);
                var words = await ConvEngine.PdfWords(src);
                if (target == "txt")
                {
                    var pages = new List<string>();
                    var page = new List<string>();
                    foreach (var b in words.Blocks)
                    {
                        if (b.Runs.Count == 0) { pages.Add(string.Join("\n", page)); page.Clear(); }
                        else page.Add(b.Plain);
                    }
                    pages.Add(string.Join("\n", page));
                    await File.WriteAllTextAsync(dest, string.Join("\n\n", pages), new UTF8Encoding(false));
                }
                else await ConvEngine.Write(words, target, dest, owner);
                return new() { dest };
            case ConvKind.Csv:
            {
                string text;
                try { text = DocText.Decode(await File.ReadAllBytesAsync(src)); }
                catch { throw new ConvError("The CSV could not be read."); }
                string json;
                try { json = ConvData.CsvToJson(text); }
                catch (InvalidDataException e) { throw new ConvError(e.Message); }
                await File.WriteAllTextAsync(dest, json, new UTF8Encoding(false));
                return new() { dest };
            }
            case ConvKind.Json:
            {
                string csv;
                try { csv = ConvData.JsonToCsv(await File.ReadAllBytesAsync(src)); }
                catch (InvalidDataException e) { throw new ConvError(e.Message); }
                await File.WriteAllTextAsync(dest, csv, new UTF8Encoding(false));
                return new() { dest };
            }
            default:
                throw new ConvError("This kind of file cannot be converted here.");
        }
    }
}

/// <summary>
/// File converter (the Mac's FileConverterTool): the files on the left, what they can become and Convert on the right;
/// each file says how it went, and its results show in File Explorer.
/// </summary>
internal sealed class FileConverterTool : ContentControl
{
    private readonly ToolContext c;
    private readonly ConvState s = ConvState.Shared;
    private readonly Color tint = FileKit.Tint("conv");
    private const string Any = "All files|*.*";

    public FileConverterTool(ToolContext c)
    {
        this.c = c;
        Focusable = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        var handed = c.TakeFiles();
        if (handed.Count == 0) handed = FileKit.Launched();
        if (handed.Count > 0) s.Add(handed);
        Loaded += (_, _) => { s.Changed -= Render; s.Changed += Render; Render(); };
        Unloaded += (_, _) => s.Changed -= Render;
        Render();
    }

    private Window? Owner => FileKit.Owner(this, c);

    private void Render()
    {
        Content = FileKit.Page(Content as ScrollViewer, s.Files.Count == 0 ? Intake() : ToolKit.Columns(List(), Controls(), 380, 940));
    }

    private FrameworkElement Intake()
    {
        var v = K.V(16,
            ToolKit.DropZone("Drop files to convert", "Or click to choose them. Pictures, Word and other documents, PDFs, CSV and JSON.", Any, true, tint, f => s.Add(f), SymbolRegular.ArrowSync24, 260),
            FileKit.Foot("Converted on this PC by Windows itself; nothing is uploaded. Slides and spreadsheets: open them in PowerPoint or Excel and use File ▸ Save As ▸ PDF."));
        if (s.Message != null) v.Children.Add(ToolKit.Note(s.Message));
        return v;
    }

    private FrameworkElement List()
    {
        var add = FileKit.Button("Add…", SymbolRegular.Add24, () => s.Add(Files.Choose(Any, true, Owner)));
        var clear = FileKit.Link("Clear", s.Clear);
        clear.IsEnabled = !s.Busy;
        var card = FileKit.List(s.Files.Select(Row), 54);
        card.AllowDrop = true;
        card.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        card.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] p) s.Add(p);
            e.Handled = true;
        };
        var v = K.V(12, card);
        if (s.Left.Count > 0) v.Children.Add(FileKit.Info($"Left out, being another kind of file than the first: {string.Join(", ", s.Left)}."));
        if (s.Message != null) v.Children.Add(ToolKit.Note(s.Message));
        // (the kind over how many — "PDFs", "1 file" — and Add and Clear at the heading's right: a count never runs into
        //  Add, however narrow the column or long the count)
        var words = K.V(0, K.Text(s.Kind is { } k ? ConvKinds.Label(k) : "Files", Ty.Subtitle), K.Text(ToolKit.Plural(s.Files.Count, "file"), Ty.Caption, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var actions = K.H(8, add, clear);
        actions.VerticalAlignment = VerticalAlignment.Center;
        var head = K.Columns("*, auto", words.Col(0), actions.Margin(12, 0, 0, 0).Col(1));
        head.Margin = new Thickness(0, 0, 0, 10);
        return K.V(0, head, v);
    }

    private FrameworkElement Row(ConvFile f)
    {
        var name = K.Text(Path.GetFileName(f.Path), Ty.Body);
        name.ToolTip = f.Path;
        var words = K.V(2, name, K.Text(Files.SizeText(f.Path), Ty.Caption, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var g = K.Columns("auto, *, auto, auto, auto",
            K.IconTile(ConvKinds.Symbol(f.Kind), tint, 32).Margin(0, 0, 12, 0).Col(0), words.Col(1), Status(f).Col(2));
        if (f.Outputs.Count > 0) g.Children.Add(K.IconButton(SymbolRegular.FolderOpen24, () => Files.Reveal(f.Outputs), "Show in File Explorer").Margin(6, 0, 0, 0).Col(3));
        var remove = K.IconButton(SymbolRegular.Dismiss24, () => s.Remove(f), "Take it off the list");
        remove.IsEnabled = !s.Busy;
        g.Children.Add(remove.Margin(4, 0, 0, 0).Col(4));
        g.Margin = new Thickness(14, 10, 14, 10);
        return g;
    }

    private FrameworkElement Status(ConvFile f)
    {
        switch (f.State)
        {
            case ConvFile.Stage.Working:
                return K.Spinner(16).Margin(8, 0, 0, 0);
            case ConvFile.Stage.Done:
            {
                var t = K.Text(f.Said, Ty.Body);
                t.Foreground = new SolidColorBrush(FileKit.Green);
                var row = K.H(6, K.Icon(SymbolRegular.CheckmarkCircle24, 16, FileKit.Green, filled: true), t);
                row.Margin = new Thickness(8, 0, 0, 0);
                row.VerticalAlignment = VerticalAlignment.Center;
                return row;
            }
            case ConvFile.Stage.Failed:
            {
                var t = K.Wrapped(f.Said, Ty.Body, B.Text, 2);
                t.Foreground = new SolidColorBrush(FileKit.Orange);
                t.MaxWidth = 260;
                var row = K.Columns("auto, *", K.Icon(SymbolRegular.Warning24, 16, FileKit.Orange, filled: true).Margin(0, 2, 6, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0), t.Col(1));
                row.Margin = new Thickness(8, 0, 0, 0);
                row.VerticalAlignment = VerticalAlignment.Center;
                row.ToolTip = f.Said;
                return row;
            }
            default:
                return new Border();
        }
    }

    private FrameworkElement Controls()
    {
        var v = K.V(22);
        var targets = s.Targets;
        UIElement choices;
        if (targets.Length == 0)
            choices = K.Wrapped(s.Kind == ConvKind.Unsupported ? "These cannot be converted on Windows yet." : "Nothing to convert these to.", Ty.Body, B.Text2);
        else
            choices = new MoreGrid(104, 8, targets.Select(t =>
            {
                var b = K.Button(t.Label, () => s.Choose(t.Key), accent: s.Target == t.Key);
                b.HorizontalAlignment = HorizontalAlignment.Stretch;
                b.MinWidth = 0;
                b.Height = 36;
                return (UIElement)b;
            }));
        v.Children.Add(FileKit.Section("Convert to", null, null, choices));
        if (s.Target is "jpeg" or "heic")
        {
            var label = K.Text($"Quality: {Math.Round(s.Quality * 100)}%", Ty.BodyStrong, B.Text2);
            var slider = new Slider { Minimum = 0.3, Maximum = 1, Value = s.Quality, SmallChange = 0.01, LargeChange = 0.1 };
            slider.ValueChanged += (_, e) =>
            {
                s.Quality = e.NewValue;
                label.Text = $"Quality: {Math.Round(s.Quality * 100)}%";
            };
            v.Children.Add(K.V(6, label, slider));
        }
        if (s.Kind == ConvKind.Image && s.Target == "pdf" && s.Files.Count > 1)
        {
            var toggle = new ToggleSwitch { Content = "All in one PDF, a page each", IsChecked = s.Combine };
            toggle.Checked += (_, _) => { s.Combine = true; s.Raise(); };
            toggle.Unchecked += (_, _) => { s.Combine = false; s.Raise(); };
            v.Children.Add(toggle);
        }
        var go = K.Button(s.Busy ? "Converting…" : "Convert", () => s.Convert(Owner), accent: true, icon: SymbolRegular.ArrowSync24);
        go.HorizontalAlignment = HorizontalAlignment.Stretch;
        go.Height = 44;
        go.FontSize = 15;
        go.IsEnabled = !s.Busy && s.Target.Length > 0 && targets.Any(t => t.Key == s.Target);
        v.Children.Add(K.V(10, go, FileKit.Foot(s.Files.Count == 1 && !s.SplitsPages || s.Kind == ConvKind.Image && s.Target == "pdf" && s.Combine ? "You choose where it is saved." : "You choose a folder; each result is saved there.")));
        return v;
    }
}
