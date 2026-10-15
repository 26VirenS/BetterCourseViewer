using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Pdf.IO;
using Simpl.Kit;
using Simpl.Screens;
using Simpl.Support;
using Wpf.Ui.Controls;
using Image = System.Windows.Controls.Image;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

// Compress files (0.3.11), as the Mac's: pictures and PDFs dropped in and made small enough to upload — a JPEG written
// again at a chosen quality and no wider than the preset allows, a PNG kept for its see-through parts, a PDF's embedded
// pictures written again inside it (its pages never drawn as pictures) — all on this PC (Tools/Files/Shrink.cs). The
// smaller copies go to Downloads as "<name>-smaller", the originals never touched; each row says what it was and what it
// is now, and the first picture made smaller is shown before and after.

#pragma warning disable CA2255
internal static class CompressFilesRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.Register("shrink", c => new CompressFilesTool(c), c => new CompressFilesCompact(c).View);
}
#pragma warning restore CA2255

/// <summary>A file on the compressor's list, and how it went.</summary>
internal sealed class ShrinkFile
{
    public enum Stage { Waiting, Working, Done, Kept, Failed }

    public required string Path { get; init; }
    public long Size;
    public Stage State = Stage.Waiting;
    /// <summary>What it is: "2400 × 1600" for a picture, "2 pages · 3 pictures" for a PDF.</summary>
    public string Detail = "";
    public string Said = "";
    public string? Output;
    public long OutSize;
    public bool Reached = true;
    public BitmapSource? Before, After;
    public bool IsPdf => Shrink.IsPdf(Path);
    public string Name => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// What the compressor holds (kept while its view is built again): the files, the preset and the limit for Fit under
/// (both remembered), whether it is at work, and what was left out or went wrong.
/// </summary>
internal sealed class ShrinkState
{
    public static readonly ShrinkState Shared = new();

    private const string PresetKey = "SimplShrink.preset", FitKey = "SimplShrink.fit";

    public readonly List<ShrinkFile> Items = new();
    public ShrinkPreset Preset = ShrinkPresets.Parse(Prefs.String(PresetKey)) ?? ShrinkPreset.Balanced;
    public string FitMb = Prefs.String(FitKey) ?? "1";
    public bool Busy;
    public List<string> Left = new();
    public string? Message;

    public event Action? Changed;

    public void Raise() => Changed?.Invoke();

    public double? FitBytes => Shrink.Megabytes(FitMb);

    public bool AllDone => Items.Count > 0 && Items.All(f => f.State is ShrinkFile.Stage.Done or ShrinkFile.Stage.Kept or ShrinkFile.Stage.Failed);

    public long TotalBefore => Items.Where(f => f.State is ShrinkFile.Stage.Done or ShrinkFile.Stage.Kept).Sum(f => f.Size);
    public long TotalAfter => Items.Where(f => f.State is ShrinkFile.Stage.Done or ShrinkFile.Stage.Kept).Sum(f => f.State == ShrinkFile.Stage.Done ? f.OutSize : f.Size);

    public void Add(IEnumerable<string> paths)
    {
        Message = null;
        var real = paths.Where(File.Exists).ToList();
        if (real.Count == 0) { Raise(); return; }
        var skipped = new List<string>();
        var added = new List<ShrinkFile>();
        foreach (var p in real)
        {
            if (!Shrink.Takes(p)) { skipped.Add(Path.GetFileName(p)); continue; }
            if (Items.Any(f => string.Equals(f.Path, p, StringComparison.OrdinalIgnoreCase))) continue;
            var f = new ShrinkFile { Path = p, Size = new FileInfo(p).Length };
            Items.Add(f);
            added.Add(f);
        }
        Left = skipped;
        Raise();
        foreach (var f in added) Describe(f);
    }

    /// <summary>A file's own facts read a moment after it is added: a picture's size in pixels, a PDF's pages and pictures.</summary>
    private async void Describe(ShrinkFile f)
    {
        string detail;
        try
        {
            detail = await Task.Run(() =>
            {
                var data = File.ReadAllBytes(f.Path);
                if (Shrink.IsPdf(f.Path))
                {
                    int pages;
                    using (var doc = PdfReader.Open(new MemoryStream(data), PdfDocumentOpenMode.Import)) pages = doc.PageCount;
                    return $"{ToolKit.Plural(pages, "page")} · {Shrink.PicturesText(Shrink.PictureCount(data))}";
                }
                var frame = BitmapDecoder.Create(new MemoryStream(data), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                return $"{frame.PixelWidth} × {frame.PixelHeight}";
            });
        }
        catch (Exception e)
        {
            Log.Write($"shrink describe {f.Name}: {e.Message}");
            detail = "";
        }
        if (!Items.Contains(f)) return;
        f.Detail = detail;
        Raise();
    }

    public void Remove(ShrinkFile f)
    {
        Items.Remove(f);
        if (Items.Count == 0) Left = new();
        Raise();
    }

    public void Clear()
    {
        Items.Clear();
        Left = new();
        Message = null;
        Raise();
    }

    public void Choose(ShrinkPreset p)
    {
        Preset = p;
        Prefs.Set(PresetKey, ShrinkPresets.Key(p));
        Raise();
    }

    public void SetFit(string mb)
    {
        FitMb = mb;
        Prefs.Set(FitKey, mb);
    }

    /// <summary>Compress: each file in turn, its smaller copy saved to Downloads (never over the original), the row saying
    /// how it went — one that cannot be read never stops the rest.</summary>
    public async void Compress()
    {
        if (Busy || Items.Count == 0) return;
        if (Preset == ShrinkPreset.Fit && FitBytes == null) { Message = "Fit under needs a size in MB, like 0.5 or 2."; Raise(); return; }
        Busy = true;
        Message = null;
        Shot.Began();
        foreach (var f in Items) { f.State = ShrinkFile.Stage.Waiting; f.Said = ""; f.Output = null; f.OutSize = 0; f.Before = null; f.After = null; }
        Raise();
        var folder = Engine.FilePreview.Downloads;
        var preset = Preset;
        var fit = FitBytes;
        var previewed = false;
        foreach (var f in Items.ToList())
        {
            f.State = ShrinkFile.Stage.Working;
            Raise();
            await Task.Yield();
            try
            {
                var data = await File.ReadAllBytesAsync(f.Path);
                var ext = Path.GetExtension(f.Path);
                var r = await Task.Run(() => f.IsPdf ? Shrink.Pdf(data, preset, fit) : Shrink.Image(data, ext, preset, fit));
                f.Reached = r.Reached;
                if (ReferenceEquals(r.Bytes, data))
                {
                    f.State = ShrinkFile.Stage.Kept;
                    f.OutSize = data.Length;
                    f.Said = r.Note ?? "Already as small as it gets: left as it was.";
                }
                else
                {
                    var dest = Files.Free(Shrink.OutputName(f.Path), r.Ext, folder);
                    await File.WriteAllBytesAsync(dest, r.Bytes);
                    f.Output = dest;
                    f.OutSize = r.Bytes.Length;
                    f.State = ShrinkFile.Stage.Done;
                    f.Said = r.Note ?? "";
                    if (!previewed && !f.IsPdf)
                    {
                        // (the first picture made smaller, before and after, as the student sees it: no wider than 900)
                        previewed = true;
                        try
                        {
                            var after = r.Bytes;
                            (f.Before, f.After) = await Task.Run(() => (Shrink.Read(data, 900).Source, Shrink.Read(after, 900).Source));
                        }
                        catch (Exception e) { Log.Write($"shrink preview: {e.Message}"); }
                    }
                }
            }
            catch (ShrinkError e)
            {
                f.State = ShrinkFile.Stage.Failed;
                f.Said = e.Message;
            }
            catch (Exception e)
            {
                Log.Write($"shrink {f.Name}: {e.Message}");
                f.State = ShrinkFile.Stage.Failed;
                f.Said = f.IsPdf ? "The PDF could not be written again." : "The picture could not be written again.";
            }
            Raise();
        }
        Busy = false;
        Raise();
        Shot.Ended();
        Shot.Moved();
        if (Shot.Asked || Launch.IsDevelopment)
            Log.Write($"shrink: done at {ShrinkPresets.Key(preset)}, {Shrink.Size(TotalBefore)} to {Shrink.Size(TotalAfter)} ({Shrink.Saved(TotalBefore, TotalAfter)}% saved) across {Items.Count} files");
    }

    /// <summary>The screenshot suite's start: --shrink-files "sample" (three files made on the spot: a photo, a diagram with
    /// see-through parts, a PDF with both in it) or paths joined by "|"; --shrink-preset balanced; --shrink-fit 0.5;
    /// --shrink-go compresses them at once.</summary>
    public void FromLaunch()
    {
        if (ShrinkPresets.Parse(Launch.Take("", "shrink-preset")) is { } p) Choose(p);
        if (Launch.Take("", "shrink-fit") is { Length: > 0 } fit) SetFit(fit);
        if (Launch.Take("", "shrink-files") is { Length: > 0 } v)
        {
            var paths = v.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).SelectMany(s => s == "sample" ? ShrinkSamples.Make() : new[] { s }).ToList();
            Add(paths);
            if (Shot.Asked) Log.Write($"shrink: shows {Items.Count} files at {ShrinkPresets.Key(Preset)}");
            if (Launch.Take("", "shrink-go") != null) Compress();
        }
    }
}

/// <summary>
/// Sample files for the screenshot suite, made on the spot in the profile's folder: a photo of a lab bench (a drawn
/// scene with the grain of a camera's sensor, so it weighs what a photo weighs), a diagram with see-through parts, and
/// a PDF of the two as its pages.
/// </summary>
internal static class ShrinkSamples
{
    public static IReadOnlyList<string> Make()
    {
        var made = new List<string>();
        try
        {
            var photo = Path.Combine(AppPaths.Root, "Lab bench.jpg");
            var photoBytes = Photo(2400, 1600);
            File.WriteAllBytes(photo, photoBytes);
            made.Add(photo);
            var diagram = Path.Combine(AppPaths.Root, "Cell diagram.png");
            var diagramBytes = Diagram(1400, 1000);
            File.WriteAllBytes(diagram, diagramBytes);
            made.Add(diagram);
            var pdf = Path.Combine(AppPaths.Root, "Lab report.pdf");
            PdfWork.Pictures(new[] { new PdfWork.Picture(photoBytes, 612, 408), new PdfWork.Picture(diagramBytes, 612, 437) }, pdf);
            made.Add(pdf);
        }
        catch (Exception e) { Log.Write("shrink samples: " + e.Message); }
        return made;
    }

    /// <summary>A lab bench as a camera sees it: a warm wall, a dark bench, glassware — and sensor grain on every pixel.</summary>
    private static byte[] Photo(int w, int h)
    {
        var dv = new DrawingVisual();
        using (var g = dv.RenderOpen())
        {
            g.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0xf2, 0xe4, 0xc8), Color.FromRgb(0xc9, 0xb3, 0x8d), 90), null, new Rect(0, 0, w, h));
            g.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x3a, 0x33, 0x2e), Color.FromRgb(0x1d, 0x1a, 0x17), 90), null, new Rect(0, h * 0.62, w, h * 0.38));
            g.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x55, 0x4a, 0x42)), null, new Rect(0, h * 0.60, w, h * 0.03));
            var glass = new SolidColorBrush(Color.FromArgb(0x70, 0xd8, 0xee, 0xf6));
            var rim = new Pen(new SolidColorBrush(Color.FromArgb(0xa0, 0xff, 0xff, 0xff)), 6);
            for (var i = 0; i < 4; i++)
            {
                double x = w * (0.14 + i * 0.22), bh = h * (0.22 + (i % 2) * 0.08), bw = w * 0.09;
                g.DrawRoundedRectangle(glass, rim, new Rect(x, h * 0.62 - bh, bw, bh), 14, 14);
                var liquid = new SolidColorBrush(new[] { Color.FromRgb(0x3f, 0x8e, 0xf7), Color.FromRgb(0xff, 0x6b, 0x35), Color.FromRgb(0x2f, 0xb3, 0x7a), Color.FromRgb(0xa2, 0x59, 0xff) }[i]);
                g.DrawRectangle(liquid, null, new Rect(x + 6, h * 0.62 - bh * 0.45, bw - 12, bh * 0.45 - 6));
            }
            g.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x60, 0xff, 0xf6, 0xe0)), null, new Point(w * 0.8, h * 0.18), w * 0.18, h * 0.16);
        }
        var rt = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rt.Render(dv);
        var px = new byte[w * h * 4];
        rt.CopyPixels(px, w * 4, 0);
        var rnd = new Random(7);
        for (var i = 0; i < px.Length; i += 4)
        {
            var n = rnd.Next(-14, 15);
            for (var k = 0; k < 3; k++) px[i + k] = (byte)Math.Clamp(px[i + k] + n + rnd.Next(-4, 5), 0, 255);
            px[i + 3] = 255;
        }
        var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, px, w * 4);
        var e = new JpegBitmapEncoder { QualityLevel = 96 };
        e.Frames.Add(BitmapFrame.Create(b));
        var ms = new MemoryStream();
        e.Save(ms);
        return ms.ToArray();
    }

    /// <summary>A cell diagram on nothing: a cell, its nucleus and organelles, with see-through parts round them.</summary>
    private static byte[] Diagram(int w, int h)
    {
        var dv = new DrawingVisual();
        using (var g = dv.RenderOpen())
        {
            var membrane = new Pen(new SolidColorBrush(Color.FromRgb(0x2f, 0xa8, 0x4f)), 10);
            g.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x50, 0xa8, 0xe6, 0xb0)), membrane, new Point(w * 0.5, h * 0.5), w * 0.42, h * 0.42);
            g.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x5e, 0x5c, 0xe6)), new Pen(new SolidColorBrush(Color.FromRgb(0x3a, 0x38, 0xb0)), 6), new Point(w * 0.46, h * 0.5), w * 0.13, h * 0.17);
            var rnd = new Random(3);
            for (var i = 0; i < 9; i++)
            {
                var c = new Point(w * (0.2 + rnd.NextDouble() * 0.6), h * (0.2 + rnd.NextDouble() * 0.6));
                g.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xff, 0x9f, 0x0a)), new Pen(new SolidColorBrush(Color.FromRgb(0xc0, 0x6a, 0x00)), 4), c, w * 0.035, h * 0.022);
            }
            var line = new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)), 4);
            g.DrawLine(line, new Point(w * 0.46, h * 0.5), new Point(w * 0.9, h * 0.12));
            g.DrawLine(line, new Point(w * 0.2, h * 0.3), new Point(w * 0.05, h * 0.08));
        }
        var rt = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rt.Render(dv);
        var e = new PngBitmapEncoder();
        e.Frames.Add(BitmapFrame.Create(rt));
        var ms = new MemoryStream();
        e.Save(ms);
        return ms.ToArray();
    }
}

/// <summary>
/// Compress files (the Mac's CompressFilesTool): the files on the left, each saying what it was and what it is now; how
/// small to go and Compress on the right; the first picture made smaller shown before and after.
/// </summary>
internal sealed class CompressFilesTool : ContentControl
{
    private readonly ToolContext c;
    private readonly ShrinkState s = ShrinkState.Shared;
    private readonly Color tint = FileKit.Tint("shrink");
    private readonly TextBox fitBox;

    public CompressFilesTool(ToolContext c)
    {
        this.c = c;
        Focusable = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        fitBox = ToolKit.Box(s.FitMb, "1", t => s.SetFit(t));
        fitBox.Width = 72;
        System.Windows.Automation.AutomationProperties.SetName(fitBox, "Fit under, megabytes");
        fitBox.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; s.Compress(); } };
        if (ShrinkPresets.Parse(c.TakeInfo("preset")) is { } p) s.Choose(p);
        var handed = c.TakeFiles();
        if (handed.Count == 0) handed = FileKit.Launched();
        if (handed.Count > 0) s.Add(handed);
        s.FromLaunch();
        Loaded += (_, _) => { s.Changed -= Render; s.Changed += Render; Render(); };
        Unloaded += (_, _) => s.Changed -= Render;
        Render();
    }

    private Window? Owner => FileKit.Owner(this, c);

    private void Render()
    {
        FileKit.Detach(fitBox);
        Content = FileKit.Page(Content as ScrollViewer, s.Items.Count == 0 ? Intake() : ToolKit.Columns(List(), Controls(), 360, 860));
    }

    private FrameworkElement Intake()
    {
        var v = K.V(16,
            ToolKit.DropZone("Drop pictures or PDFs to make smaller", "Or click to choose them. JPEG, PNG, HEIC, WebP and PDF — small enough to upload.", Shrink.Filter, true, tint,
                f => s.Add(f), SymbolRegular.ArrowMinimize24, 260),
            FileKit.Foot("Compressed on this PC; nothing is uploaded. The originals stay as they are: a smaller copy of each goes to Downloads as “name-smaller”."));
        if (s.Message != null) v.Children.Add(ToolKit.Note(s.Message));
        return v;
    }

    private FrameworkElement List()
    {
        var add = FileKit.Button("Add…", SymbolRegular.Add24, () => s.Add(Files.Choose(Shrink.Filter, true, Owner)));
        var clear = FileKit.Link("Clear", s.Clear);
        clear.IsEnabled = !s.Busy;
        var card = FileKit.List(s.Items.Select(Row), 54);
        card.AllowDrop = true;
        card.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        card.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] p) s.Add(p);
            e.Handled = true;
        };
        var v = K.V(12, card);
        if (s.Left.Count > 0) v.Children.Add(FileKit.Info($"Left out, not a picture or a PDF: {string.Join(", ", s.Left)}."));
        if (s.Message != null) v.Children.Add(ToolKit.Note(s.Message));
        if (s.AllDone) v.Children.Add(Summary());
        var words = K.V(0, K.Text("Files", Ty.Subtitle), K.Text(ToolKit.Plural(s.Items.Count, "file"), Ty.Caption, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var actions = K.H(8, add, clear);
        actions.VerticalAlignment = VerticalAlignment.Center;
        var head = K.Columns("*, auto", words.Col(0), actions.Margin(12, 0, 0, 0).Col(1));
        head.Margin = new Thickness(0, 0, 0, 10);
        var list = K.V(0, head, v);
        if (s.Items.FirstOrDefault(f => f.Before != null && f.After != null) is { } shown) list.Children.Add(Preview(shown).Margin(0, 24, 0, 0));
        return list;
    }

    /// <summary>A file's row: what it is, its size before and after, and what was saved.</summary>
    private FrameworkElement Row(ShrinkFile f)
    {
        var name = K.Text(f.Name, Ty.Body);
        name.ToolTip = f.Path;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var facts = string.Join(" · ", new[] { Shrink.Size(f.Size), f.Detail }.Where(x => x.Length > 0));
        var words = K.V(2, name, K.Text(facts, Ty.Caption, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var g = K.Columns("auto, *, auto, auto, auto",
            K.IconTile(f.IsPdf ? SymbolRegular.DocumentPdf24 : SymbolRegular.Image24, tint, 32).Margin(0, 0, 12, 0).Col(0), words.Col(1), Status(f).Col(2));
        if (f.Output != null) g.Children.Add(K.IconButton(SymbolRegular.FolderOpen24, () => Files.Reveal(new[] { f.Output }), "Show in File Explorer").Margin(6, 0, 0, 0).Col(3));
        var remove = K.IconButton(SymbolRegular.Dismiss24, () => s.Remove(f), "Take it off the list");
        remove.IsEnabled = !s.Busy;
        g.Children.Add(remove.Margin(4, 0, 0, 0).Col(4));
        g.Margin = new Thickness(14, 10, 14, 10);
        if (f.State is ShrinkFile.Stage.Kept or ShrinkFile.Stage.Failed || (f.State == ShrinkFile.Stage.Done && f.Said.Length > 0))
        {
            // (a line under the row for what was said: left as it was, the smallest it could reach, what went wrong)
            var said = K.Wrapped(f.Said, Ty.Caption, f.State == ShrinkFile.Stage.Failed ? B.Text : B.Text2);
            if (f.State == ShrinkFile.Stage.Failed) said.Foreground = new SolidColorBrush(FileKit.Orange);
            said.Margin = new Thickness(58, 0, 14, 10);
            return K.V(0, g, said);
        }
        return g;
    }

    private FrameworkElement Status(ShrinkFile f)
    {
        switch (f.State)
        {
            case ShrinkFile.Stage.Working:
                return K.Spinner(16).Margin(8, 0, 0, 0);
            case ShrinkFile.Stage.Done:
            {
                var saved = Shrink.Saved(f.Size, f.OutSize);
                var size = K.Text($"{Shrink.Size(f.Size)} → {Shrink.Size(f.OutSize)}", Ty.Body);
                Typography.SetNumeralAlignment(size, FontNumeralAlignment.Tabular);
                var chip = K.Chip($"−{saved}%", f.Reached ? FileKit.Green : FileKit.Orange, strong: true);
                var row = K.H(8, size, chip);
                row.Margin = new Thickness(8, 0, 0, 0);
                row.VerticalAlignment = VerticalAlignment.Center;
                System.Windows.Automation.AutomationProperties.SetName(row, $"{Shrink.Size(f.Size)} to {Shrink.Size(f.OutSize)}, {saved} percent saved");
                return row;
            }
            case ShrinkFile.Stage.Kept:
            {
                var t = K.Text("Left as it was", Ty.Body, B.Text2);
                var row = K.H(6, K.Icon(SymbolRegular.Info24, 16, B.Text2), t);
                row.Margin = new Thickness(8, 0, 0, 0);
                row.VerticalAlignment = VerticalAlignment.Center;
                return row;
            }
            case ShrinkFile.Stage.Failed:
            {
                var t = K.Text("Could not", Ty.Body);
                t.Foreground = new SolidColorBrush(FileKit.Orange);
                var row = K.H(6, K.Icon(SymbolRegular.Warning24, 16, FileKit.Orange, filled: true), t);
                row.Margin = new Thickness(8, 0, 0, 0);
                row.VerticalAlignment = VerticalAlignment.Center;
                return row;
            }
            default:
                return K.Text(s.Busy ? "Waiting" : "", Ty.Body, B.Text2).Margin(8, 0, 0, 0);
        }
    }

    /// <summary>What the whole batch saved, once every file has been through, and where the copies are.</summary>
    private FrameworkElement Summary()
    {
        var before = s.TotalBefore;
        var after = s.TotalAfter;
        var done = s.Items.Count(f => f.State == ShrinkFile.Stage.Done);
        var outputs = s.Items.Where(f => f.Output != null).Select(f => f.Output!).ToList();
        string words;
        if (done == 0) words = s.Items.Any(f => f.State == ShrinkFile.Stage.Kept) ? "Nothing came out smaller: the files are left as they were." : "Nothing could be made smaller.";
        else words = $"Saved {Shrink.Size(before - after)} ({Shrink.Saved(before, after)}%): {Shrink.Size(before)} to {Shrink.Size(after)} across {ToolKit.Plural(s.Items.Count, "file")}. The smaller {(done == 1 ? "copy is" : "copies are")} in Downloads.";
        var note = done > 0 ? FileKit.Done(words) : FileKit.Info(words);
        if (outputs.Count == 0) return note;
        var reveal = FileKit.Link("Show in File Explorer", () => Files.Reveal(outputs), SymbolRegular.FolderOpen24);
        reveal.HorizontalAlignment = HorizontalAlignment.Left;
        return K.V(6, note, reveal.Margin(24, 0, 0, 0));
    }

    /// <summary>The first picture made smaller, before and after, side by side (one over the other on a narrow page).</summary>
    private FrameworkElement Preview(ShrinkFile f)
    {
        FrameworkElement Side(BitmapSource src, string caption)
        {
            var img = new Image { Source = src, Stretch = Stretch.Uniform, MaxHeight = 280, HorizontalAlignment = HorizontalAlignment.Left };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var frame = new Border { Child = img, CornerRadius = new CornerRadius(8), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left };
            frame.Stroke(B.CardStroke);
            img.SizeChanged += (_, e) => frame.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width + 2, e.NewSize.Height + 2), 8, 8);
            return K.V(6, frame, K.Text(caption, Ty.Caption, B.Text2));
        }
        var before = Side(f.Before!, $"Before · {Shrink.Size(f.Size)}{(f.Detail.Length > 0 ? $" · {f.Detail}" : "")}");
        var after = Side(f.After!, $"After · {Shrink.Size(f.OutSize)}{(f.After!.PixelWidth != f.Before!.PixelWidth ? " · made smaller" : "")}");
        var pair = new MoreGrid(300, 16, new UIElement[] { before, after }, stretch: true);
        return FileKit.Section("Before and after", f.Name, null, pair);
    }

    /// <summary>How small to go: the four presets, the limit for Fit under, and Compress.</summary>
    private FrameworkElement Controls()
    {
        var v = K.V(22);
        var presets = new MoreGrid(120, 8, ShrinkPresets.All.Select(p =>
        {
            var b = K.Button(ShrinkPresets.Label(p), () => s.Choose(p), accent: s.Preset == p);
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.MinWidth = 0;
            b.Height = 36;
            b.ToolTip = ShrinkPresets.Line(p);
            return (UIElement)b;
        }));
        var how = K.V(10, presets, K.Wrapped(ShrinkPresets.Line(s.Preset), Ty.Caption, B.Text2));
        if (s.Preset == ShrinkPreset.Fit)
        {
            if (fitBox.Text != s.FitMb) fitBox.Text = s.FitMb;
            var mb = K.Text("MB", Ty.Body, B.Text2).Margin(8, 0, 0, 0);
            mb.VerticalAlignment = VerticalAlignment.Center;
            var row = K.H(0, K.Text("Under", Ty.Body, B.Text2).Margin(0, 0, 8, 0), fitBox, mb);
            foreach (UIElement e in row.Children) if (e is TextBlock t) t.VerticalAlignment = VerticalAlignment.Center;
            how.Children.Add(row);
        }
        v.Children.Add(FileKit.Section("How small", null, null, how));
        var go = K.Button(s.Busy ? "Compressing…" : "Compress", s.Compress, accent: true, icon: SymbolRegular.ArrowMinimize24);
        go.HorizontalAlignment = HorizontalAlignment.Stretch;
        go.Height = 44;
        go.FontSize = 15;
        go.IsEnabled = !s.Busy && s.Items.Count > 0;
        v.Children.Add(K.V(10, go, FileKit.Foot("Each smaller copy is saved to Downloads as “name-smaller”; the original stays as it is. A PDF keeps its pages and words: only its pictures are written smaller.")));
        return v;
    }
}

/// <summary>Compress files' pin, opened: a small drop zone — files dropped or chosen open the whole tool on them at
/// Balanced, the copies going to Downloads.</summary>
internal sealed class CompressFilesCompact
{
    public FrameworkElement View { get; }

    public CompressFilesCompact(ToolContext c)
    {
        var tint = FileKit.Tint("shrink");
        var zone = ToolKit.DropZone("Drop pictures or PDFs", "Balanced: up to 2048 px, good quality. Smaller copies go to Downloads.", Shrink.Filter, true, tint,
            files => c.OpenFull(new Dictionary<string, string> { ["preset"] = "balanced" }, files), SymbolRegular.ArrowMinimize24, 150);
        View = K.V(10, zone);
    }
}
