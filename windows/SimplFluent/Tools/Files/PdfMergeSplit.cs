using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Simpl.Kit;
using Simpl.Screens;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using Image = System.Windows.Controls.Image;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;
using WinPdfDocument = Windows.Data.Pdf.PdfDocument;

namespace Simpl.Tools;

// Merge & split PDFs, as the web's and the Mac's (PDFTools.swift). Merge: every PDF added lays its pages out as
// thumbnails, and the merged document is a tray under them — a page pressed goes into the tray in the order wanted (or
// the whole file, with Add All), pages are moved along the tray, turned or taken out — then a name and Merge. Split: one
// PDF's pages laid out, each kept or taken out with a press and turned with its button; the trimmed copy is saved under
// a name, or every page as a PDF of its own. Pages are drawn by Windows (Windows.Data.Pdf) and copied as they are
// (PDFsharp): text, pictures and links stay exactly what they were. Nothing is uploaded.

#pragma warning disable CA2255
internal static class PdfMergeSplitRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.Register("pdfx", c => new PdfMergeSplitTool(c));
}
#pragma warning restore CA2255

/// <summary>A PDF added: its file, its bytes (to copy pages from), Windows' reading of it (to draw them) and its colour.</summary>
internal sealed class PdfxFile
{
    public Guid Id { get; } = Guid.NewGuid();
    public required string Path { get; init; }
    public required byte[] Bytes { get; init; }
    public required WinPdfDocument Doc { get; init; }
    public required string Hex { get; init; }
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
    public Color Color => K.Hex(Hex);
    public int Count => (int)Doc.PageCount;
}

/// <summary>A page in the merged PDF: which file, which page, and how far it is turned.</summary>
internal sealed class PdfxPick
{
    public Guid Id { get; } = Guid.NewGuid();
    public required Guid File { get; init; }
    public required int Page { get; init; }
    public int Rotation;
}

/// <summary>
/// What Merge &amp; split holds (the Mac's PDFMergeSplitTool state, kept while the view is built again): the mode, the
/// PDFs added and the merged pages, the PDF being split with its pages taken out and turned, and the last word on a save.
/// </summary>
internal sealed class PdfxState
{
    public static readonly PdfxState Shared = new();
    public static readonly string[] Palette = { "#0a84ff", "#ff9500", "#30b0c7", "#5856d6", "#ff2d55", "#34c759", "#af52de", "#ff3b30" };
    public const string Filter = "PDF documents|*.pdf";

    public string Mode = "merge";
    public readonly List<PdfxFile> Files = new();
    public readonly List<PdfxPick> Picks = new();
    public string Name = "Merged";
    public PdfxFile? Single;
    public readonly HashSet<int> Removed = new();
    public readonly Dictionary<int, int> Turns = new();
    public string? Note;
    public string? Failed;

    /// <summary>What changed: "files", "picks", "split", "notes", "mode".</summary>
    public event Action<string>? Changed;

    public void Raise(string what) => Changed?.Invoke(what);

    public void SetMode(string m)
    {
        if (Mode == m) return;
        Mode = m;
        Note = null;
        Failed = null;
        Raise("mode");
    }

    /// <summary>Files handed over: one is split, more are merged.</summary>
    public void Take(IReadOnlyList<string> files)
    {
        if (files.Count == 1)
        {
            Mode = "split";
            Raise("mode");
            OpenSingle(files[0]);
        }
        else if (files.Count > 1) Add(files);
    }

    private static async Task<(byte[]? Bytes, WinPdfDocument? Doc, string? Why)> Load(string path)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var doc = await WinPdf.Open(bytes);
            return (bytes, doc, null);
        }
        catch (Exception e)
        {
            Log.Write($"pdf open {System.IO.Path.GetFileName(path)}: {e.Message}");
            var locked = e.HResult == unchecked((int)0x8007052B) || e.Message.Contains("password", StringComparison.OrdinalIgnoreCase);
            return (null, null, locked ? "locked" : "unreadable");
        }
    }

    public async void Add(IEnumerable<string> paths)
    {
        Failed = null;
        Note = null;
        var bad = new List<string>();
        foreach (var p in paths)
        {
            var (bytes, doc, why) = await Load(p);
            if (bytes == null || doc == null)
            {
                bad.Add(System.IO.Path.GetFileName(p) + (why == "locked" ? " (locked)" : ""));
                continue;
            }
            Files.Add(new PdfxFile { Path = p, Bytes = bytes, Doc = doc, Hex = Palette[Files.Count % Palette.Length] });
        }
        if (Files.Count == 1 && Name == "Merged") Name = Files[0].Name + " merged";
        if (bad.Count > 0) Failed = $"These could not be opened: {string.Join(", ", bad)}.";
        Raise("files");
        Raise("notes");
    }

    public void RemoveFile(PdfxFile f)
    {
        Files.Remove(f);
        Picks.RemoveAll(p => p.File == f.Id);
        Raise("files");
    }

    public void AddAll(PdfxFile f)
    {
        for (var n = 0; n < f.Count; n++) Picks.Add(new PdfxPick { File = f.Id, Page = n });
        Raise("picks");
    }

    public void AddPage(PdfxFile f, int n)
    {
        Picks.Add(new PdfxPick { File = f.Id, Page = n });
        Raise("picks");
    }

    public void Move(PdfxPick p, int step)
    {
        var i = Picks.IndexOf(p);
        var j = i + step;
        if (i < 0 || j < 0 || j >= Picks.Count) return;
        (Picks[i], Picks[j]) = (Picks[j], Picks[i]);
        Raise("picks");
    }

    public void TakeOut(PdfxPick p)
    {
        Picks.Remove(p);
        Raise("picks");
    }

    public void ClearPicks()
    {
        Picks.Clear();
        Raise("picks");
    }

    public PdfxFile? FileOf(PdfxPick p) => Files.FirstOrDefault(f => f.Id == p.File);

    public async void SaveMerged(Window? owner)
    {
        var picks = Picks.Select(p => (Pick: p, File: Files.FindIndex(f => f.Id == p.File))).Where(x => x.File >= 0).ToList();
        if (picks.Count == 0) return;
        var trimmed = Name.Trim();
        var name = trimmed.Length == 0 ? "Merged" : trimmed;
        if (global::Simpl.Tools.Files.SaveAs(name + ".pdf", Filter, owner) is not { } path) return;
        var sources = Files.Select(f => f.Bytes).ToList();
        try
        {
            var n = await Task.Run(() => PdfWork.Assemble(sources, picks.Select(x => new PdfWork.Pick(x.File, x.Pick.Page, x.Pick.Rotation)), path));
            Note = $"Saved {System.IO.Path.GetFileName(path)}: {ToolKit.Plural(n, "page")}.";
            Failed = null;
            global::Simpl.Tools.Files.Reveal(new[] { path });
        }
        catch (Exception e)
        {
            Log.Write("merge: " + e);
            Note = null;
            Failed = "The merged PDF could not be saved there.";
        }
        Raise("notes");
    }

    // ---- split ----

    public async void OpenSingle(string path)
    {
        Failed = null;
        Note = null;
        var (bytes, doc, _) = await Load(path);
        if (bytes == null || doc == null)
        {
            Failed = $"{System.IO.Path.GetFileName(path)} could not be opened.";
            Raise("notes");
            return;
        }
        Single = new PdfxFile { Path = path, Bytes = bytes, Doc = doc, Hex = Palette[0] };
        Removed.Clear();
        Turns.Clear();
        Raise("split");
        Raise("notes");
    }

    private IEnumerable<PdfWork.Pick> Kept(PdfxFile f, int? only = null)
    {
        for (var n = 0; n < f.Count; n++)
            if (!Removed.Contains(n) && (only == null || only == n)) yield return new PdfWork.Pick(0, n, Turns.GetValueOrDefault(n));
    }

    public async void SaveTrimmed(Window? owner)
    {
        if (Single is not { } f) return;
        var kept = Kept(f).ToList();
        if (kept.Count == 0) return;
        if (global::Simpl.Tools.Files.SaveAs(f.Name + " (trimmed).pdf", Filter, owner) is not { } path) return;
        try
        {
            var n = await Task.Run(() => PdfWork.Assemble(new[] { f.Bytes }, kept, path));
            Note = $"Saved {System.IO.Path.GetFileName(path)}: {ToolKit.Plural(n, "page")}.";
            Failed = null;
            global::Simpl.Tools.Files.Reveal(new[] { path });
        }
        catch (Exception e)
        {
            Log.Write("trim: " + e);
            Note = null;
            Failed = "The PDF could not be saved there.";
        }
        Raise("notes");
    }

    public async void SaveEachPage(Window? owner)
    {
        if (Single is not { } f) return;
        if (global::Simpl.Tools.Files.ChooseFolder($"Choose where the pages of {f.Name} go, one PDF each.", owner) is not { } folder) return;
        var written = new List<string>();
        var pages = Enumerable.Range(0, f.Count).Where(n => !Removed.Contains(n)).ToList();
        foreach (var n in pages)
        {
            var path = global::Simpl.Tools.Files.Free($"{f.Name} - page {n + 1}", "pdf", folder);
            try
            {
                var kept = Kept(f, n).ToList();
                await Task.Run(() => PdfWork.Assemble(new[] { f.Bytes }, kept, path));
                written.Add(path);
            }
            catch (Exception e) { Log.Write($"split page {n}: {e.Message}"); }
        }
        if (written.Count == 0)
        {
            Note = null;
            Failed = "The pages could not be saved there.";
        }
        else
        {
            Failed = null;
            Note = $"Saved {ToolKit.Plural(written.Count, "PDF")} in {System.IO.Path.GetFileName(folder.TrimEnd('\\', '/'))}.";
            global::Simpl.Tools.Files.Reveal(written);
        }
        Raise("notes");
    }
}

/// <summary>
/// A page drawn small (the Mac's PDFThumb): white, edged, the page inside it turned as far as it is turned — drawn once
/// it shows, and kept for the next time.
/// </summary>
internal sealed class PdfThumb : Border
{
    private static readonly Dictionary<(Guid, int, int), BitmapSource> Cache = new();
    private readonly RotateTransform turn = new();
    private readonly Image image = new() { Stretch = Stretch.Uniform };
    private int rotation;

    public PdfThumb(PdfxFile? f, int page, double width, int rotation = 0)
    {
        Width = width;
        Height = Math.Round(width * 1.3);
        CornerRadius = new CornerRadius(6);
        Background = Brushes.White;
        BorderBrush = new SolidColorBrush(Color.FromArgb(31, 0, 0, 0));
        BorderThickness = new Thickness(1);
        Padding = new Thickness(2);
        SnapsToDevicePixels = true;
        this.rotation = rotation;
        turn.Angle = rotation;
        image.LayoutTransform = turn;
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        Child = image;
        if (f != null && page >= 0 && page < f.Count) Draw(f, page, (int)Math.Round(width * 2));
    }

    private async void Draw(PdfxFile f, int page, int px)
    {
        var key = (f.Id, page, px);
        if (Cache.TryGetValue(key, out var hit)) { image.Source = hit; return; }
        try
        {
            var pic = await WinPdf.Picture(f.Doc.GetPage((uint)page), px);
            Cache[key] = pic;
            image.Source = pic;
        }
        catch (Exception e) { Log.Write($"thumb {page}: {e.Message}"); }
    }

    /// <summary>Turned a quarter more, the turn shown.</summary>
    public int Rotation
    {
        get => rotation;
        set
        {
            var from = value == 0 && rotation == 270 ? -90 : rotation;
            rotation = value;
            Motion.FromTo(turn, RotateTransform.AngleProperty, from, value, Motion.Medium, Motion.Decelerate);
        }
    }
}

/// <summary>Merge &amp; split PDFs (the Mac's PDFMergeSplitTool).</summary>
internal sealed class PdfMergeSplitTool : ContentControl
{
    private readonly ToolContext c;
    private readonly PdfxState s = PdfxState.Shared;
    private readonly Color tint = FileKit.Tint("pdfx");
    private readonly ContentControl notes = new() { Focusable = false };
    private readonly ContentControl filesHost = new() { Focusable = false };
    private readonly ContentControl mergedHost = new() { Focusable = false };
    private readonly ContentControl splitHost = new() { Focusable = false };
    private readonly SelectorBar modes;
    private readonly TextBox name;
    private ScrollViewer? page;
    private ScrollViewer? tray;

    public PdfMergeSplitTool(ToolContext c)
    {
        this.c = c;
        Focusable = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        modes = new SelectorBar(new[] { "Merge", "Split" }, s.Mode == "merge" ? 0 : 1);
        modes.Changed += i => s.SetMode(i == 0 ? "merge" : "split");
        name = ToolKit.Box(s.Name, "Name", t => s.Name = t);
        name.Width = 280;
        var handed = c.TakeFiles();
        if (handed.Count == 0) handed = FileKit.Launched();
        if (handed.Count > 0) s.Take(handed);
        Loaded += (_, _) => { s.Changed -= Changed; s.Changed += Changed; Changed("mode"); };
        Unloaded += (_, _) => s.Changed -= Changed;
        Build();
    }

    private Window? Owner => FileKit.Owner(this, c);

    private void Changed(string what)
    {
        switch (what)
        {
            case "mode": Build(); break;
            case "notes": Notes(); break;
            case "files": Files(); Merged(); break;
            case "picks": Merged(); break;
            case "split": Split(); break;
        }
    }

    private void Build()
    {
        modes.Select(s.Mode == "merge" ? 0 : 1, false);
        FileKit.Detach(modes);
        FileKit.Detach(filesHost);
        FileKit.Detach(mergedHost);
        FileKit.Detach(splitHost);
        FileKit.Detach(notes);
        Notes();
        if (s.Mode == "merge") { Files(); Merged(); } else Split();
        var body = s.Mode == "merge" ? K.V(20, filesHost, mergedHost) : (UIElement)splitHost;
        page = FileKit.Page(page, K.V(20, modes, notes, body));
        Content = page;
    }

    private void Notes()
    {
        var v = K.V(8);
        if (s.Failed != null) v.Children.Add(ToolKit.Note(s.Failed));
        if (s.Note != null) v.Children.Add(FileKit.Done(s.Note));
        notes.Content = v;
        notes.Visibility = v.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- merge ----

    private void Files()
    {
        var add = FileKit.Button("Add PDFs…", SymbolRegular.Add24, () =>
        {
            var f = global::Simpl.Tools.Files.Choose(PdfxState.Filter, true, Owner);
            if (f.Count > 0) s.Add(f);
        });
        UIElement content;
        if (s.Files.Count == 0)
            content = ToolKit.DropZone("Drop PDFs here", "Or click to choose them. Their pages show here; press one to put it in the merged PDF.", PdfxState.Filter, true, tint, f => s.Add(f), SymbolRegular.DocumentCopy24, 220);
        else
        {
            var list = FileKit.List(s.Files.Select(FileRow), 16);
            list.AllowDrop = true;
            list.DragOver += (_, e) => { e.Effects = e.Data.GetData(DataFormats.FileDrop) is string[] p && global::Simpl.Tools.Files.Filter(p, PdfxState.Filter).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            list.Drop += (_, e) =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] p && global::Simpl.Tools.Files.Filter(p, PdfxState.Filter) is { Count: > 0 } ok) s.Add(ok);
                e.Handled = true;
            };
            content = list;
        }
        filesHost.Content = FileKit.Section("Files", null, add, content);
    }

    private FrameworkElement FileRow(PdfxFile f)
    {
        var all = FileKit.Button("Add All", null, () => s.AddAll(f));
        var remove = K.IconButton(SymbolRegular.Dismiss24, () => s.RemoveFile(f), "Take this file away");
        var head = K.Columns("auto, auto, auto, *, auto, auto",
            K.Dot(f.Color, 10).Margin(0, 0, 10, 0).Col(0),
            K.Text(f.Name, Ty.BodyStrong).Margin(0, 0, 10, 0).Col(1),
            K.Text(ToolKit.Plural(f.Count, "page"), Ty.Body, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(2),
            all.Margin(8, 0, 6, 0).Col(4), remove.Col(5));
        var pages = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 4, 2, 4) };
        for (var n = 0; n < f.Count; n++)
        {
            var page = n;
            var cell = K.V(4, new PdfThumb(f, n, 84), K.Text($"{n + 1}", Ty.Caption, B.Text2).Center());
            var b = K.Subtle(cell, () => s.AddPage(f, page), new Thickness(4), 6);
            b.ToolTip = $"Put page {n + 1} in the merged PDF";
            b.Margin = new Thickness(0, 0, 6, 0);
            pages.Children.Add(b);
        }
        return K.V(10, head, Sideways(pages)).Margin(16, 10, 16, 10);
    }

    private void Merged()
    {
        var clear = s.Picks.Count == 0 ? null : FileKit.Link("Clear", s.ClearPicks);
        var v = K.V(16);
        if (s.Picks.Count == 0)
        {
            v.Children.Add(K.Wrapped(s.Files.Count == 0 ? "Add some PDFs first." : "Press a page above to put it here, or Add All for a whole file.", Ty.Body, B.Text2).Margin(0, 8, 0, 8));
            tray = null;
        }
        else
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 8, 4, 8) };
            for (var i = 0; i < s.Picks.Count; i++) row.Children.Add(TrayPage(i, s.Picks[i]));
            var keep = tray;
            var h = keep?.HorizontalOffset ?? 0;
            tray = Sideways(row);
            if (h > 0) tray.Loaded += (_, _) => tray?.ScrollToHorizontalOffset(h);
            v.Children.Add(tray);
        }
        var merge = FileKit.Button("Merge…", SymbolRegular.Merge24, () => s.SaveMerged(Owner), accent: true);
        merge.IsEnabled = s.Picks.Count > 0;
        if (name.Text != s.Name) name.Text = s.Name;
        FileKit.Detach(name);
        var foot = new WrapPanel { Orientation = Orientation.Horizontal };
        foot.Children.Add(name.Margin(0, 0, 10, 8));
        foot.Children.Add(K.Text(".pdf", Ty.Body, B.Text2).Margin(0, 6, 10, 8));
        foot.Children.Add(merge.Margin(0, 0, 0, 8));
        v.Children.Add(foot);
        mergedHost.Content = FileKit.Section("Merged", s.Picks.Count == 0 ? null : ToolKit.Plural(s.Picks.Count, "page"), clear, v);
    }

    private FrameworkElement TrayPage(int i, PdfxPick p)
    {
        var f = s.FileOf(p);
        var thumb = new PdfThumb(f, p.Page, 110, p.Rotation);
        var bar = new Border { Height = 4, VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(f?.Color ?? Colors.Gray), Margin = new Thickness(1, 0, 1, 1) };
        var face = new Grid { Children = { thumb, bar }, HorizontalAlignment = HorizontalAlignment.Center };
        var caption = K.Text($"{f?.Name ?? ""} · {p.Page + 1}", Ty.Caption, B.Text2).Center();
        caption.Width = 120;
        Button Small(SymbolRegular sym, string tip, Action a, bool enabled = true)
        {
            var b = K.IconButton(sym, a, tip, 12, 26);
            b.Height = 24;
            b.IsEnabled = enabled;
            return b;
        }
        var buttons = K.H(2,
            Small(SymbolRegular.ChevronLeft24, "Move earlier", () => s.Move(p, -1), i > 0),
            Small(SymbolRegular.ArrowRotateClockwise24, "Turn a quarter", () =>
            {
                p.Rotation = (p.Rotation + 90) % 360;
                thumb.Rotation = p.Rotation;
            }),
            Small(SymbolRegular.Dismiss24, "Take out", () => s.TakeOut(p)),
            Small(SymbolRegular.ChevronRight24, "Move later", () => s.Move(p, 1), i < s.Picks.Count - 1));
        buttons.HorizontalAlignment = HorizontalAlignment.Center;
        return K.V(6, face, caption, buttons).Margin(0, 0, 14, 0);
    }

    /// <summary>A row that scrolls sideways (the wheel with Shift held; the page itself scrolls with the wheel alone).</summary>
    private static ScrollViewer Sideways(UIElement content)
    {
        var sv = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        sv.PreviewMouseWheel += (_, e) =>
        {
            e.Handled = true;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta); return; }
            var up = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = sv };
            (VisualTreeHelper.GetParent(sv) as UIElement)?.RaiseEvent(up);
        };
        return sv;
    }

    // ---- split ----

    private void Split()
    {
        if (s.Single is not { } f)
        {
            splitHost.Content = ToolKit.DropZone("Drop a PDF to split", "Or click to choose one. Take pages out, turn them, or save every page on its own.", PdfxState.Filter, false, tint,
                files => s.OpenSingle(files[0]), SymbolRegular.SplitVertical24, 220);
            return;
        }
        var kept = K.Text("", Ty.Body, B.Text2);
        var keepAll = FileKit.Link("Keep All", () => { s.Removed.Clear(); Split(); });
        var another = FileKit.Button("Choose Another…", SymbolRegular.Document24, () =>
        {
            var pick = global::Simpl.Tools.Files.Choose(PdfxState.Filter, false, Owner);
            if (pick.Count > 0) s.OpenSingle(pick[0]);
        });
        var title = K.Text(f.Name, Ty.Subtitle);
        var head = K.Columns("auto, auto, *, auto, auto", title.Margin(0, 0, 12, 0).Col(0), kept.Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(1),
            keepAll.Margin(8, 0, 8, 0).Col(3), another.Col(4));
        var save = FileKit.Button("Save Kept Pages…", SymbolRegular.Save24, () => s.SaveTrimmed(Owner), accent: true);
        var each = FileKit.Button("Split into Single Pages…", SymbolRegular.SplitVertical24, () => s.SaveEachPage(Owner));
        void Counts()
        {
            kept.Text = $"{f.Count - s.Removed.Count} of {ToolKit.Plural(f.Count, "page")} kept";
            keepAll.IsEnabled = s.Removed.Count > 0;
            save.IsEnabled = each.IsEnabled = s.Removed.Count < f.Count;
        }
        var cells = new List<UIElement>();
        for (var n = 0; n < f.Count; n++) cells.Add(SplitPage(f, n, Counts));
        var grid = new MoreGrid(150, 18, cells);
        var foot = new WrapPanel { Orientation = Orientation.Horizontal };
        foot.Children.Add(save.Margin(0, 0, 10, 8));
        foot.Children.Add(each.Margin(0, 0, 14, 8));
        foot.Children.Add(FileKit.Foot("Press a page to take it out (or put it back); its button turns it.").Margin(0, 8, 0, 8));
        Counts();
        splitHost.Content = K.V(18, head, grid, foot);
    }

    private FrameworkElement SplitPage(PdfxFile f, int n, Action counts)
    {
        var thumb = new PdfThumb(f, n, 130, s.Turns.GetValueOrDefault(n));
        var mark = new Grid { Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        mark.Children.Add(new System.Windows.Shapes.Ellipse { Fill = Brushes.White, Margin = new Thickness(4) });
        mark.Children.Add(K.Icon(SymbolRegular.DismissCircle24, 32, FileKit.Red, filled: true));
        var face = new Grid { Children = { thumb, mark } };
        var button = K.Subtle(face, null, new Thickness(4), 8);
        button.HorizontalAlignment = HorizontalAlignment.Center;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        void Paint()
        {
            var o = s.Removed.Contains(n);
            thumb.Opacity = o ? 0.3 : 1;
            mark.Visibility = o ? Visibility.Visible : Visibility.Collapsed;
            button.ToolTip = o ? $"Put page {n + 1} back" : $"Take page {n + 1} out";
        }
        button.Click += (_, e) =>
        {
            e.Handled = true;
            if (!s.Removed.Remove(n)) s.Removed.Add(n);
            Paint();
            counts();
        };
        Paint();
        var turn = K.IconButton(SymbolRegular.ArrowRotateClockwise24, () =>
        {
            var r = (s.Turns.GetValueOrDefault(n) + 90) % 360;
            s.Turns[n] = r;
            thumb.Rotation = r;
        }, "Turn a quarter", 14, 28);
        var caption = K.H(6, K.Text($"Page {n + 1}", Ty.Caption, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Center), turn);
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        return K.V(6, button, caption);
    }
}
