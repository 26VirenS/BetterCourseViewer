using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Screens;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;
using WinPdfDocument = Windows.Data.Pdf.PdfDocument;

namespace Simpl.Tools;

// The PDF annotator, as the web's and the Mac's (PDFAnnotator.swift): a PDF opened in the window and marked up like
// paper — a highlighter, an underline and a strike-through for words selected, a note pinned to the page, a box of typed
// text — in six colours, with Undo. Every mark lands on the page and in the panel beside it, each with room for a note,
// and a press on one goes to it. Save writes the marks into the PDF as real annotations (any viewer shows them, and the
// file opens here again as it was left); Copy Notes puts the marked words and the notes on the clipboard, page by page.
// The pages are drawn by Windows (Windows.Data.Pdf) and their words found by Windows' OCR — Windows has no reader of a
// PDF's own text — so words are selected on scans too; a page with no words found is marked by dragging a box. The marks
// are written with PDFsharp. Nothing is uploaded.

#pragma warning disable CA2255
internal static class PdfAnnotatorRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.Register("mark", c => new PdfAnnotatorTool(c));
}
#pragma warning restore CA2255

/// <summary>A word found on a page: where it is (in points of the page as seen), what it says, and its line.</summary>
internal sealed class MarkWord
{
    public required Rect Box { get; init; }
    public required string Text { get; init; }
    public required int Line { get; init; }
}

/// <summary>
/// The annotator's document (the Mac's MarkModel, kept while the view is built again): the PDF, its pages, its marks in
/// page order, the words found on its pages, the words selected, the colour, and whether there is anything to save.
/// </summary>
internal sealed class MarkModel
{
    public static readonly MarkModel Shared = new();
    public const string Filter = "PDF documents|*.pdf";

    public static readonly (string Key, string Name, string Hex)[] Palette =
    {
        ("yellow", "Yellow", "#ffd60a"), ("green", "Green", "#30d158"), ("blue", "Blue", "#5ac8fa"),
        ("pink", "Pink", "#ff6482"), ("red", "Red", "#ff3b30"), ("black", "Black", "#1c1c1e"),
    };

    private readonly List<PdfMark> added = new();
    private readonly HashSet<int> reading = new();
    private readonly SemaphoreSlim readGate = new(1);

    public string? Path { get; private set; }
    public byte[]? Bytes { get; private set; }
    public WinPdfDocument? Render { get; private set; }
    public List<PdfBox> Pages { get; private set; } = new();
    public List<PdfMark> Marks { get; private set; } = new();
    public Dictionary<int, List<MarkWord>> Words { get; } = new();
    public bool Dirty { get; private set; }
    public string ColorKey { get; private set; } = "yellow";
    public string? Message { get; private set; }
    /// <summary>Counts the PDFs opened (work for one that is gone is let go).</summary>
    public int Generation { get; private set; }

    public int SelPage { get; private set; } = -1;
    public int SelFrom { get; private set; } = -1;
    public int SelTo { get; private set; } = -1;
    public Rect? SelArea { get; private set; }

    /// <summary>A PDF opened (everything shown again).</summary>
    public event Action? Opened;
    /// <summary>A mark made, taken off, or undone.</summary>
    public event Action? MarksChanged;
    public event Action? SelectionChanged;
    /// <summary>Saved or not, the colour, Undo, the last word on what went wrong.</summary>
    public event Action? StateChanged;
    /// <summary>A page's words found.</summary>
    public event Action<int>? WordsRead;
    /// <summary>A mark's note changed (a box of text's words show on the page).</summary>
    public event Action<PdfMark>? NoteChanged;
    /// <summary>A mark to show (its row pressed).</summary>
    public event Action<PdfMark>? GoTo;

    public bool IsOpen => Render != null;
    public bool CanUndo => added.Count > 0;
    public bool HasSelection => SelPage >= 0 && (SelArea is { Width: > 1, Height: > 1 } || SelArea == null && SelFrom >= 0 && SelTo >= 0);
    public bool IsReading(int page) => reading.Contains(page);

    public Color Color => K.Hex(Palette.FirstOrDefault(c => c.Key == ColorKey).Hex ?? "#ffd60a");

    public static Color ColorOf(PdfMark m) => Color.FromRgb((byte)Math.Round(m.R * 255), (byte)Math.Round(m.G * 255), (byte)Math.Round(m.B * 255));

    public void SetColor(string key)
    {
        ColorKey = key;
        StateChanged?.Invoke();
    }

    public async void Open(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        byte[] bytes;
        PdfWork.Marked read;
        WinPdfDocument render;
        try
        {
            bytes = await File.ReadAllBytesAsync(path);
            read = await Task.Run(() => PdfWork.Read(bytes));
            render = await WinPdf.Open(read.Plain ?? bytes);
            if (read.Pages.Count == 0 || render.PageCount == 0) throw new InvalidDataException("no pages");
        }
        catch (Exception e)
        {
            Log.Write($"mark open {name}: {e.Message}");
            var locked = e.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase) || e.HResult == unchecked((int)0x8007052B);
            Message = locked ? $"{name} is locked with a password." : $"{name} could not be opened.";
            StateChanged?.Invoke();
            return;
        }
        Path = path;
        Bytes = bytes;
        Render = render;
        Pages = read.Pages;
        Marks = read.Marks;
        Words.Clear();
        reading.Clear();
        added.Clear();
        Dirty = false;
        Message = null;
        SelPage = SelFrom = SelTo = -1;
        SelArea = null;
        Generation++;
        Opened?.Invoke();
        // (the words under the marks the file came with, for their rows and Copy Notes)
        foreach (var page in Marks.Where(x => x.IsMarkup).Select(x => x.Page).Distinct().ToList()) Read(page);
    }

    /// <summary>A page's words found by Windows' OCR (one page at a time), and the words of its marks with them.</summary>
    public async void Read(int page)
    {
        if (Render == null || page < 0 || page >= Pages.Count || Words.ContainsKey(page) || reading.Contains(page)) return;
        var gen = Generation;
        var doc = Render;
        if (WinOcr.Engine == null)
        {
            Words[page] = new();
            WordsRead?.Invoke(page);
            return;
        }
        reading.Add(page);
        await readGate.WaitAsync();
        var found = new List<MarkWord>();
        try
        {
            if (gen != Generation) return;
            var box = Pages[page];
            var (r, k) = await WinOcr.Page(doc.GetPage((uint)page), box.ShownW, box.ShownH);
            var line = 0;
            foreach (var l in r.Lines)
            {
                foreach (var w in l.Words)
                {
                    var b = w.BoundingRect;
                    found.Add(new MarkWord { Box = new Rect(b.X / k, b.Y / k, b.Width / k, b.Height / k), Text = w.Text, Line = line });
                }
                line++;
            }
        }
        catch (Exception e) { Log.Write($"mark words {page}: {e.Message}"); }
        finally
        {
            readGate.Release();
            if (gen == Generation) reading.Remove(page);
        }
        if (gen != Generation) return;
        Words[page] = found;
        foreach (var m in Marks.Where(m => m.Page == page && m.IsMarkup)) m.Words = WordsOf(m);
        WordsRead?.Invoke(page);
        if (page == 0 && found.Count > 0 && Launch.Take("", "mark-demo") != null) Demo();
    }

    // ---- where things are ----

    /// <summary>A mark's rectangle on its page as seen.</summary>
    public Rect Shown(PdfMark m) => Shown(m.Page, m.X1, m.Y1, m.X2, m.Y2);

    public Rect Shown(int page, double x1, double y1, double x2, double y2)
    {
        if (page < 0 || page >= Pages.Count) return Rect.Empty;
        var box = Pages[page];
        var (u1, v1) = box.ToShown(x1, y1);
        var (u2, v2) = box.ToShown(x2, y2);
        return new Rect(new Point(u1, v1), new Point(u2, v2));
    }

    public Point Shown(int page, double x, double y)
    {
        var (u, v) = Pages[page].ToShown(x, y);
        return new Point(u, v);
    }

    /// <summary>The words inside a rectangle of a page.</summary>
    public string WordsIn(int page, Rect r)
    {
        if (!Words.TryGetValue(page, out var ws)) return "";
        r.Inflate(1.5, 1.5);
        return string.Join(" ", ws.Where(w => r.Contains(new Point(w.Box.X + w.Box.Width / 2, w.Box.Y + w.Box.Height / 2))).Select(w => w.Text));
    }

    private string WordsOf(PdfMark m)
    {
        var parts = new List<string>();
        foreach (var q in m.Quads)
        {
            var pts = new[] { Shown(m.Page, q[0], q[1]), Shown(m.Page, q[2], q[3]), Shown(m.Page, q[4], q[5]), Shown(m.Page, q[6], q[7]) };
            var r = new Rect(new Point(pts.Min(p => p.X), pts.Min(p => p.Y)), new Point(pts.Max(p => p.X), pts.Max(p => p.Y)));
            var w = WordsIn(m.Page, r);
            if (w.Length > 0) parts.Add(w);
        }
        return string.Join(" ", parts).Trim();
    }

    // ---- the selection ----

    public void Select(int page, int from, int to)
    {
        SelPage = page;
        SelFrom = from;
        SelTo = to;
        SelArea = null;
        SelectionChanged?.Invoke();
    }

    /// <summary>A box dragged on a page with no words found (a picture, a scan Windows could not read).</summary>
    public void SelectArea(int page, Rect area)
    {
        SelPage = page;
        SelFrom = SelTo = -1;
        SelArea = area;
        SelectionChanged?.Invoke();
    }

    public void ClearSelection()
    {
        if (SelPage < 0) return;
        SelPage = SelFrom = SelTo = -1;
        SelArea = null;
        SelectionChanged?.Invoke();
    }

    /// <summary>The lines of words selected: each line's box (a little taller than its words, as a selection is) and its words.</summary>
    public List<(Rect Box, string Words)> SelectedLines()
    {
        var res = new List<(Rect, string)>();
        if (SelPage < 0) return res;
        if (SelArea is { } a)
        {
            if (a.Width > 1 && a.Height > 1) res.Add((a, WordsIn(SelPage, a)));
            return res;
        }
        if (!Words.TryGetValue(SelPage, out var ws) || ws.Count == 0 || SelFrom < 0) return res;
        var from = Math.Clamp(Math.Min(SelFrom, SelTo), 0, ws.Count - 1);
        var to = Math.Clamp(Math.Max(SelFrom, SelTo), 0, ws.Count - 1);
        foreach (var g in ws.Skip(from).Take(to - from + 1).GroupBy(w => w.Line))
        {
            var r = g.Select(w => w.Box).Aggregate(Rect.Union);
            var pad = r.Height * 0.18;
            r = new Rect(r.X - 1, r.Y - pad, r.Width + 2, r.Height + pad * 2);
            res.Add((r, string.Join(" ", g.Select(w => w.Text))));
        }
        return res;
    }

    public string SelectedText => string.Join("\n", SelectedLines().Select(l => l.Words));

    // ---- marking ----

    private PdfMark New(string kind, int page, Rect shown)
    {
        var box = Pages[page];
        var (x1, y1) = box.ToPdf(shown.Left, shown.Top);
        var (x2, y2) = box.ToPdf(shown.Right, shown.Top);
        var (x3, y3) = box.ToPdf(shown.Left, shown.Bottom);
        var (x4, y4) = box.ToPdf(shown.Right, shown.Bottom);
        var c = Color;
        return new PdfMark
        {
            Kind = kind, Page = page,
            X1 = Math.Min(Math.Min(x1, x2), Math.Min(x3, x4)), Y1 = Math.Min(Math.Min(y1, y2), Math.Min(y3, y4)),
            X2 = Math.Max(Math.Max(x1, x2), Math.Max(x3, x4)), Y2 = Math.Max(Math.Max(y1, y2), Math.Max(y3, y4)),
            Quads = { new[] { x1, y1, x2, y2, x3, y3, x4, y4 } },
            R = c.R / 255.0, G = c.G / 255.0, B = c.B / 255.0,
        };
    }

    private void Insert(PdfMark m)
    {
        var at = Marks.FindLastIndex(x => x.Page <= m.Page) + 1;
        Marks.Insert(at, m);
        added.Add(m);
    }

    /// <summary>The words selected, marked: a highlight, an underline or a strike-through on each line of them.</summary>
    public void MarkSelection(string kind)
    {
        var page = SelPage;
        var lines = SelectedLines();
        if (lines.Count == 0) return;
        foreach (var (box, words) in lines)
        {
            var m = New(kind, page, box);
            m.Words = words;
            Insert(m);
        }
        Dirty = true;
        SelPage = SelFrom = SelTo = -1;
        SelArea = null;
        SelectionChanged?.Invoke();
        MarksChanged?.Invoke();
        StateChanged?.Invoke();
    }

    /// <summary>A note pinned beside the words selected, or in the middle of the page showing (a box of text, or a note's icon).</summary>
    public void AddNote(bool text, int page, Point middle)
    {
        Point spot;
        if (HasSelection && SelectedLines() is { Count: > 0 } lines)
        {
            page = SelPage;
            var b = lines.Select(l => l.Box).Aggregate(Rect.Union);
            spot = new Point(Math.Min(b.Right + 6, Pages[page].ShownW - 30), b.Top);
        }
        else spot = middle;
        if (page < 0 || page >= Pages.Count) return;
        var box = Pages[page];
        var size = text ? new Size(220, 30) : new Size(22, 22);
        var x = Math.Clamp(spot.X, 0, Math.Max(0, box.ShownW - size.Width));
        var y = Math.Clamp(spot.Y, 0, Math.Max(0, box.ShownH - size.Height));
        var m = New(text ? "FreeText" : "Text", page, new Rect(x, y, size.Width, size.Height));
        m.Quads.Clear();
        if (text)
        {
            m.Contents = "Text";
            m.FontSize = 14;
        }
        else
        {
            // (viewers keep a note upright from the top left corner of its rectangle in the page's own points: that corner
            // is put where the note goes on the page as seen)
            var (ux, uy) = box.ToPdf(x, y);
            (m.X1, m.Y2, m.X2, m.Y1) = (ux, uy, ux + 22, uy - 22);
        }
        Insert(m);
        Dirty = true;
        SelPage = SelFrom = SelTo = -1;
        SelArea = null;
        SelectionChanged?.Invoke();
        MarksChanged?.Invoke();
        StateChanged?.Invoke();
    }

    public void Undo()
    {
        if (added.Count == 0) return;
        var m = added[^1];
        added.RemoveAt(added.Count - 1);
        Marks.Remove(m);
        Dirty = true;
        MarksChanged?.Invoke();
        StateChanged?.Invoke();
    }

    public void Remove(PdfMark m)
    {
        Marks.Remove(m);
        added.Remove(m);
        Dirty = true;
        MarksChanged?.Invoke();
        StateChanged?.Invoke();
    }

    public void Show(PdfMark m) => GoTo?.Invoke(m);

    public void SetNote(PdfMark m, string text)
    {
        if (m.Contents == text) return;
        m.Contents = text;
        if (m.Origin >= 0) m.Edited = true;
        var was = Dirty;
        Dirty = true;
        NoteChanged?.Invoke(m);
        if (!was) StateChanged?.Invoke();
    }

    /// <summary>The marks written into the PDF, in its place or a new one.</summary>
    public async void Save(bool newPlace, Window? owner)
    {
        if (Bytes == null) return;
        var dest = Path;
        if (newPlace || dest == null)
        {
            var name = Path != null ? System.IO.Path.GetFileNameWithoutExtension(Path) : "Marked up";
            dest = Files.SaveAs(name + " (marked).pdf", Filter, owner);
            if (dest == null) return;
        }
        var bytes = Bytes;
        var marks = Marks.ToList();
        var gen = Generation;
        try
        {
            var placed = await Task.Run(() => PdfWork.Write(bytes, marks, dest));
            if (gen != Generation) return;
            foreach (var (m, k) in placed) { m.Origin = k; m.Edited = false; }
            Bytes = await File.ReadAllBytesAsync(dest);
            Path = dest;
            Dirty = false;
            Message = null;
        }
        catch (Exception e)
        {
            Log.Write("mark save: " + e);
            Message = "The PDF could not be saved there.";
        }
        StateChanged?.Invoke();
    }

    /// <summary>The marked words and the notes, page by page.</summary>
    public string NotesText
    {
        get
        {
            var lines = new List<string>();
            var last = -1;
            foreach (var m in Marks)
            {
                var note = (m.Contents ?? "").Trim();
                if (m.Words.Length == 0 && note.Length == 0) continue;
                if (m.Page != last)
                {
                    if (lines.Count > 0) lines.Add("");
                    lines.Add($"Page {m.Page + 1}");
                    last = m.Page;
                }
                var line = "• ";
                if (m.Words.Length > 0) line += $"“{m.Words}”";
                if (note.Length > 0) line += m.Words.Length == 0 ? note : $" — {note}";
                lines.Add(line);
            }
            return string.Join("\n", lines);
        }
    }

    /// <summary>For the screenshot suite (--mark-demo): a line highlighted, another underlined, and a note.</summary>
    private void Demo()
    {
        if (!Words.TryGetValue(0, out var ws) || ws.Count == 0) return;
        var lines = ws.Select(w => w.Line).Distinct().ToList();
        int First(int line) => ws.FindIndex(w => w.Line == line);
        int Last(int line) => ws.FindLastIndex(w => w.Line == line);
        if (lines.Count > 1) { Select(0, First(lines[1]), Last(lines[1])); MarkSelection("Highlight"); }
        if (lines.Count > 3)
        {
            ColorKey = "blue";
            Select(0, First(lines[3]), Last(lines[3]));
            MarkSelection("Underline");
            ColorKey = "pink";
            Select(0, First(lines[0]), Last(lines[0]));
            AddNote(false, 0, new Point(0, 0));
            if (Marks.LastOrDefault() is { } note) SetNote(note, "Learn the two stages for the test");
        }
        ColorKey = "yellow";
        StateChanged?.Invoke();
    }
}
