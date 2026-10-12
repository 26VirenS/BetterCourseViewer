using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Support;
using Windows.Graphics.Imaging;
using Wpf.Ui.Controls;
using Image = System.Windows.Controls.Image;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

// Image to text, as the web's and the Mac's (ImageToText.swift): the words read off a picture — a photo of the board, a
// screenshot of a slide, a scanned PDF — into text that can be copied, saved or fixed up. The reading is Windows' own
// (Windows.Media.Ocr, in the languages of the student's profile), on this PC; nothing is uploaded. Windows has no reader
// of a PDF's own text, so every PDF's pages are drawn and read, scanned or not.

#pragma warning disable CA2255
internal static class ImageToTextRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.Register("ocr", c => new ImageToTextTool(c));
}
#pragma warning restore CA2255

/// <summary>
/// What Image to text is reading and has read (the Mac's ImageToTextTool state, kept while the view is built again): the
/// picture, its words, whether it is still reading and how far, and what went wrong.
/// </summary>
internal sealed class OcrState
{
    public static readonly OcrState Shared = new();
    public const int MaxPages = 30;
    public const string Filter = "Pictures and PDFs|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.heic;*.heif;*.webp;*.jxr;*.ico;*.pdf";

    public string? Name;
    public BitmapSource? Preview;
    public string? Path;
    public string Text = "";
    public bool Busy;
    public string Progress = "";
    public string? Error;
    public bool Copied;
    private int run;
    private DispatcherTimer? waiting;

    /// <summary>A picture, its words, its progress or a failure came (not the student's own typing).</summary>
    public event Action? Changed;

    private void Raise() => Changed?.Invoke();

    public bool HasSource => Preview != null;

    public int Words => Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    public void Open(string path)
    {
        Error = null;
        Copied = false;
        if (System.IO.Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) { ReadPdf(path); return; }
        OpenPicture(path);
    }

    private async void OpenPicture(string path)
    {
        var me = ++run;
        try
        {
            var d = await WinImage.Read(path, Math.Min(4096, WinOcr.MaxSide), BitmapAlphaMode.Premultiplied);
            if (me != run) return;
            Read(System.IO.Path.GetFileName(path), d.Bitmap, path);
        }
        catch (Exception e)
        {
            Log.Write("ocr open: " + e.Message);
            if (me != run) return;
            Error = "That file could not be opened as a picture.";
            Raise();
        }
    }

    /// <summary>A picture read: shown, and its words read.</summary>
    private async void Read(string name, SoftwareBitmap picture, string? path)
    {
        var me = ++run;
        Name = name;
        Path = path;
        Preview = WinImage.Show(picture);
        Text = "";
        Error = null;
        Copied = false;
        Busy = true;
        Progress = "Reading…";
        Raise();
        try
        {
            var t = await WinOcr.Text(picture);
            if (me != run) return;
            Text = t;
        }
        catch (Exception e)
        {
            if (me != run) return;
            Error = e is InvalidOperationException ? e.Message : $"It could not be read: {e.Message}";
        }
        Busy = false;
        Raise();
        Shot.Moved();
    }

    /// <summary>A PDF read page by page: each page drawn and read, the words coming in as they are read.</summary>
    private async void ReadPdf(string path)
    {
        var me = ++run;
        Windows.Data.Pdf.PdfDocument doc;
        try
        {
            doc = await WinPdf.Open(await File.ReadAllBytesAsync(path));
            if (doc.PageCount == 0) throw new InvalidDataException("no pages");
        }
        catch (Exception e)
        {
            Log.Write("ocr pdf: " + e.Message);
            if (me != run) return;
            Error = "That PDF could not be opened.";
            Raise();
            return;
        }
        if (me != run) return;
        Name = System.IO.Path.GetFileName(path);
        Path = path;
        Text = "";
        Error = null;
        Copied = false;
        Busy = true;
        Progress = "Reading…";
        try { Preview = await WinPdf.Picture(doc.GetPage(0), 900); } catch (Exception e) { Log.Write("ocr preview: " + e.Message); }
        if (me != run) return;
        Raise();
        var count = (int)Math.Min(doc.PageCount, MaxPages);
        var parts = new List<string>();
        for (var i = 0; i < count; i++)
        {
            Progress = $"Page {i + 1} of {count}…";
            Raise();
            try
            {
                var (r, _) = await WinOcr.Page(doc.GetPage((uint)i));
                if (me != run) return;
                parts.Add(string.Join("\n", r.Lines.Select(l => l.Text)).Trim());
            }
            catch (InvalidOperationException e)
            {
                if (me != run) return;
                Error = e.Message;
                break;
            }
            catch (Exception e)
            {
                Log.Write($"ocr page {i}: {e.Message}");
                if (me != run) return;
            }
            Text = string.Join("\n\n", parts.Where(p => p.Length > 0));
        }
        if (doc.PageCount > count && Error == null) Error = $"Only the first {count} pages were read.";
        Busy = false;
        Raise();
        Shot.Moved();
    }

    /// <summary>A picture (or a picture file) from the clipboard.</summary>
    public async void Paste()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var files = Files.Filter(Clipboard.GetFileDropList().Cast<string>(), Filter);
                if (files.Count > 0) { Open(files[0]); return; }
            }
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } img)
            {
                Read("Pasted picture", await WinImage.From(img), null);
                return;
            }
        }
        catch (Exception e) { Log.Write("ocr paste: " + e.Message); }
        Error = "There is no picture on the clipboard.";
        Raise();
    }

    /// <summary>
    /// Windows' own screen snip (the Snipping Tool's): drag over part of the screen, and that part is read once it is on
    /// the clipboard.
    /// </summary>
    public void Capture()
    {
        Error = null;
        var before = WinOcr.GetClipboardSequenceNumber();
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-screenclip:") { UseShellExecute = true }); }
        catch (Exception e)
        {
            Log.Write("ocr capture: " + e.Message);
            Error = "Windows’ screen snip could not be opened.";
            Raise();
            return;
        }
        waiting?.Stop();
        var started = DateTime.UtcNow;
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        waiting = t;
        t.Tick += async (_, _) =>
        {
            if ((DateTime.UtcNow - started).TotalMinutes > 2) { t.Stop(); return; }
            if (WinOcr.GetClipboardSequenceNumber() == before) return;
            try
            {
                if (!Clipboard.ContainsImage() || Clipboard.GetImage() is not { } img) return;
                t.Stop();
                Read("Screen capture", await WinImage.From(img), null);
            }
            catch (Exception e) { Log.Write("ocr capture read: " + e.Message); }
        };
        t.Start();
    }

    public void Copy()
    {
        FileKit.Copy(Text);
        Copied = true;
        Raise();
    }

    /// <summary>The student's own changes to the words.</summary>
    public void Typed(string text) => Text = text;

    public void Save(Window? owner)
    {
        var name = (Path != null ? System.IO.Path.GetFileNameWithoutExtension(Path) : null) ?? "Text";
        if (Files.SaveAs(name + ".txt", "Text|*.txt", owner) is not { } dest) return;
        try { File.WriteAllText(dest, Text, new UTF8Encoding(false)); }
        catch (Exception e)
        {
            Error = $"It could not be saved: {e.Message}";
            Raise();
        }
    }
}

/// <summary>
/// Image to text (the Mac's ImageToTextTool): a picture or a scan dropped, chosen, pasted or snipped from the screen, its
/// words read and shown beside it to copy, save or fix up.
/// </summary>
internal sealed class ImageToTextTool : ContentControl
{
    private readonly ToolContext c;
    private readonly OcrState s = OcrState.Shared;
    private readonly Color tint = FileKit.Tint("ocr");
    private readonly TextBox box;
    private readonly TextBlock count = K.Text("", Ty.Body, B.Text2);
    private readonly TextBlock none = K.Text("No words found on it.", Ty.Body, B.Text2);
    private Grid? columns;
    private bool wide;
    private FrameworkElement? copyButton, saveButton;

    public ImageToTextTool(ToolContext c)
    {
        this.c = c;
        Focusable = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        box = new TextBox
        {
            AcceptsReturn = true, AcceptsTab = false, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 360, MaxHeight = 640, FontSize = 15, VerticalContentAlignment = VerticalAlignment.Top, ClearButtonEnabled = false,
            Padding = new Thickness(12, 10, 12, 10),
        };
        box.TextChanged += (_, _) =>
        {
            if (box.Text == s.Text) return;
            s.Typed(box.Text);
            Counts();
        };
        none.IsHitTestVisible = false;
        none.HorizontalAlignment = HorizontalAlignment.Center;
        none.VerticalAlignment = VerticalAlignment.Center;
        var handed = c.TakeFiles();
        if (handed.Count == 0) handed = FileKit.Launched();
        if (handed.Count > 0) s.Open(handed[0]);
        Loaded += (_, _) => { s.Changed -= Render; s.Changed += Render; Render(); };
        Unloaded += (_, _) => s.Changed -= Render;
        Render();
    }

    private Window? Owner => FileKit.Owner(this, c);

    private void Render()
    {
        FileKit.Detach(box);
        FileKit.Detach(none);
        FileKit.Detach(count);
        Content = FileKit.Page(Content as ScrollViewer, !s.HasSource && !s.Busy ? Intake() : Reading());
        if (box.Text != s.Text) box.Text = s.Text;
        Counts();
    }

    private void Counts()
    {
        count.Text = s.Busy ? s.Progress : ToolKit.Plural(s.Words, "word");
        none.Visibility = !s.Busy && s.Text.Length == 0 && s.Error == null ? Visibility.Visible : Visibility.Collapsed;
        if (copyButton != null) copyButton.IsEnabled = s.Text.Length > 0;
        if (saveButton != null) saveButton.IsEnabled = s.Text.Length > 0;
    }

    private FrameworkElement Intake()
    {
        var v = K.V(18,
            ToolKit.DropZone("Drop a picture or a scanned PDF", "Or click to choose one. A photo of the board, a screenshot of a slide, a scan.", OcrState.Filter, false, tint,
                files => s.Open(files[0]), SymbolRegular.ScanText24, 260),
            Actions());
        if (s.Error != null) v.Children.Add(ToolKit.Note(s.Error));
        v.Children.Add(FileKit.Foot("Read on this PC by Windows itself. Nothing is uploaded."));
        return v;
    }

    private FrameworkElement Actions()
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        void Add(UIElement e) { ((FrameworkElement)e).Margin = new Thickness(0, 0, 10, 8); row.Children.Add(e); }
        Add(FileKit.Button("Paste Picture", SymbolRegular.ClipboardPaste24, s.Paste));
        Add(FileKit.Button("Capture from Screen…", SymbolRegular.Screenshot24, s.Capture, tip: "Drag over part of the screen; its words are read"));
        if (s.HasSource)
            Add(FileKit.Button("Choose Another…", SymbolRegular.ImageMultiple24, () =>
            {
                var f = Files.Choose(OcrState.Filter, false, Owner);
                if (f.Count > 0) s.Open(f[0]);
            }));
        return row;
    }

    private FrameworkElement Reading()
    {
        var preview = Preview();
        var output = Output();
        columns = new Grid();
        columns.Children.Add(preview);
        columns.Children.Add(output);
        wide = false;
        Lay(columns, preview, output, true);
        columns.SizeChanged += (_, e) => Lay(columns, preview, output, false);
        return columns;
    }

    /// <summary>The picture beside its words on a wide window, over them on a narrow one.</summary>
    private void Lay(Grid g, FrameworkElement preview, FrameworkElement output, bool first)
    {
        var w = g.ActualWidth <= 0 ? (ActualWidth > 0 ? ActualWidth - 80 : 1000) : g.ActualWidth;
        var now = w >= 340 + 26 + 460;
        if (!first && now == wide) return;
        wide = now;
        g.ColumnDefinitions.Clear();
        g.RowDefinitions.Clear();
        if (now)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42, GridUnitType.Star), MinWidth = 340, MaxWidth = 520 });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58, GridUnitType.Star), MinWidth = 460 });
            Grid.SetColumn(preview, 0); Grid.SetRow(preview, 0);
            Grid.SetColumn(output, 2); Grid.SetRow(output, 0);
        }
        else
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(preview, 0); Grid.SetRow(preview, 0);
            Grid.SetColumn(output, 0); Grid.SetRow(output, 2);
        }
    }

    private FrameworkElement Preview()
    {
        var v = K.V(14);
        v.VerticalAlignment = VerticalAlignment.Top;
        if (s.Preview is { } p)
        {
            var img = new Image { Source = p, Stretch = Stretch.Uniform, MaxHeight = 520, HorizontalAlignment = HorizontalAlignment.Left };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var frame = new Border { Child = img, CornerRadius = new CornerRadius(8), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left, AllowDrop = true };
            frame.Stroke(B.CardStroke);
            img.SizeChanged += (_, e) => frame.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width + 2, e.NewSize.Height + 2), 8, 8);
            frame.DragOver += (_, e) => { e.Effects = e.Data.GetData(DataFormats.FileDrop) is string[] f && Files.Filter(f, OcrState.Filter).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            frame.Drop += (_, e) =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] f && Files.Filter(f, OcrState.Filter) is { Count: > 0 } ok) s.Open(ok[0]);
                e.Handled = true;
            };
            v.Children.Add(frame);
            v.Children.Add(K.Text(s.Name ?? "", Ty.Body, B.Text2));
        }
        v.Children.Add(Actions());
        return v;
    }

    private FrameworkElement Output()
    {
        var right = s.Busy ? K.H(8, K.Spinner(16), count) : (UIElement)count;
        var head = K.Columns("auto, *, auto", K.Text("Text", Ty.Subtitle).Col(0), ((FrameworkElement)right).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(2));
        var v = K.V(12, head);
        v.VerticalAlignment = VerticalAlignment.Top;
        if (s.Error != null) v.Children.Add(ToolKit.Note(s.Error));
        v.Children.Add(new Grid { Children = { box, none } });
        var copy = FileKit.Button(s.Copied ? "Copied" : "Copy Text", s.Copied ? SymbolRegular.Checkmark24 : SymbolRegular.Copy24, s.Copy, accent: true);
        var save = FileKit.Button("Save as Text…", SymbolRegular.Save24, () => s.Save(Owner));
        copyButton = copy;
        saveButton = save;
        v.Children.Add(K.H(10, copy, save));
        return v;
    }
}
