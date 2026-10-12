using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Screens;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using Image = System.Windows.Controls.Image;
using MenuItem = System.Windows.Controls.MenuItem;
using Path = System.IO.Path;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

/// <summary>
/// The PDF itself (the Mac's PDFView in MarkPDFView): its pages one under another, as wide as the pane at first and
/// zoomed with Ctrl and the wheel or the buttons under them; each page drawn by Windows as it comes into sight, its marks
/// drawn over it, and its words selected by dragging over them (a double press takes a word, a triple its line).
/// </summary>
internal sealed class MarkPages : Grid
{
    private const double Pad = 16, Gap = 12;

    private sealed class PageView
    {
        public required int Index;
        public required Border Root;
        public required Grid Face;
        public required Image Image;
        public required Canvas Marks;
        public required Canvas Selection;
        public required Canvas Input;
        public double Rendered;
        public bool Rendering;
    }

    private readonly MarkModel m = MarkModel.Shared;
    private readonly ScrollViewer sv;
    private readonly StackPanel stack = new() { Margin = new Thickness(Pad), HorizontalAlignment = HorizontalAlignment.Center };
    private readonly ScaleTransform zoom = new(1, 1);
    private readonly List<PageView> pages = new();
    private readonly DispatcherTimer settle = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly TextBlock percent = K.Text("100%", Ty.Body);
    private readonly Border hint;
    private readonly TextBlock hintText = K.Text("", Ty.Caption);
    private double scale = 1;
    private bool fit = true;
    private bool pumping;
    private int built = -1;
    // a drag in progress
    private PageView? dragPage;
    private Point dragFrom;
    private int anchor = -1;
    private bool moved;

    public MarkPages()
    {
        Focusable = true;
        FocusVisualStyle = null;
        sv = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        var ground = new Border { Child = sv };
        ground.Bg(B.Solid2);
        Children.Add(ground);
        Children.Add(ZoomBar());
        hint = new Border { Child = hintText, CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 5, 12, 6), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        hint.Bg(B.Flyout).Stroke(B.FlyoutStroke);
        Children.Add(hint);
        sv.ScrollChanged += (_, _) => Soon();
        sv.SizeChanged += (_, _) => { if (fit) Fit(); Soon(); };
        sv.PreviewMouseWheel += (_, e) =>
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
            e.Handled = true;
            Zoom(scale * Math.Pow(1.1, e.Delta / 120.0), e.GetPosition(sv));
        };
        settle.Tick += (_, _) => { settle.Stop(); Pump(); };
        Loaded += (_, _) =>
        {
            Hook(true);
            if (built != m.Generation) Build();
            else { DrawAllMarks(); DrawSelection(); }
            Soon();
        };
        Unloaded += (_, _) => Hook(false);
    }

    private void Hook(bool on)
    {
        m.Opened -= Build;
        m.MarksChanged -= DrawAllMarks;
        m.SelectionChanged -= DrawSelection;
        m.WordsRead -= WordsRead;
        m.NoteChanged -= NoteChanged;
        m.GoTo -= Show;
        if (!on) return;
        m.Opened += Build;
        m.MarksChanged += DrawAllMarks;
        m.SelectionChanged += DrawSelection;
        m.WordsRead += WordsRead;
        m.NoteChanged += NoteChanged;
        m.GoTo += Show;
    }

    private double Dpi => VisualTreeHelper.GetDpi(this).DpiScaleX;

    // ---- the pages ----

    private void Build()
    {
        built = m.Generation;
        pages.Clear();
        stack.Children.Clear();
        for (var i = 0; i < m.Pages.Count; i++)
        {
            var box = m.Pages[i];
            var image = new Image { Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            Canvas Layer() => new() { Width = box.ShownW, Height = box.ShownH, LayoutTransform = zoom, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            var marks = Layer();
            var selection = Layer();
            var input = Layer();
            marks.IsHitTestVisible = false;
            selection.IsHitTestVisible = false;
            input.Background = Brushes.Transparent;
            input.Cursor = Cursors.IBeam;
            var face = new Grid { Background = Brushes.White, ClipToBounds = true, Children = { image, marks, selection, input } };
            var root = new Border { Child = face, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, Gap), HorizontalAlignment = HorizontalAlignment.Center, SnapsToDevicePixels = true };
            root.SetResourceReference(Border.BorderBrushProperty, B.CardStroke);
            var view = new PageView { Index = i, Root = root, Face = face, Image = image, Marks = marks, Selection = selection, Input = input };
            input.MouseLeftButtonDown += (_, e) => Down(view, e);
            input.MouseMove += (_, e) => Move(view, e);
            input.MouseLeftButtonUp += (_, e) => Up(view, e);
            input.LostMouseCapture += (_, _) => { if (dragPage == view) dragPage = null; };
            pages.Add(view);
            stack.Children.Add(root);
        }
        fit = true;
        Fit();
        sv.ScrollToHome();
        DrawAllMarks();
        DrawSelection();
        Soon();
    }

    private double MaxShownW => m.Pages.Count == 0 ? 612 : m.Pages.Max(p => p.ShownW);

    private void Fit()
    {
        var room = sv.ViewportWidth > 0 ? sv.ViewportWidth : sv.ActualWidth;
        if (room <= 0) { SetScale(scale); return; }
        SetScale((room - Pad * 2 - 2 - 4) / MaxShownW);
    }

    private void SetScale(double s)
    {
        scale = Math.Clamp(s, 0.2, 6);
        zoom.ScaleX = zoom.ScaleY = scale;
        for (var i = 0; i < pages.Count; i++)
        {
            pages[i].Face.Width = Math.Round(m.Pages[i].ShownW * scale);
            pages[i].Face.Height = Math.Round(m.Pages[i].ShownH * scale);
        }
        percent.Text = $"{Math.Round(scale / (96.0 / 72) * 100)}%";
        Soon();
    }

    /// <summary>Zoomed, the point under the pointer kept where it is.</summary>
    private void Zoom(double s, Point? at = null)
    {
        var old = scale;
        var p = at ?? new Point(sv.ViewportWidth / 2, sv.ViewportHeight / 2);
        var x = sv.HorizontalOffset + p.X;
        var y = sv.VerticalOffset + p.Y;
        fit = false;
        SetScale(s);
        var k = scale / old;
        sv.UpdateLayout();
        sv.ScrollToHorizontalOffset(Math.Max(0, x * k - p.X));
        sv.ScrollToVerticalOffset(Math.Max(0, (y - Pad) * k + Pad - p.Y));
    }

    private FrameworkElement ZoomBar()
    {
        var less = K.IconButton(SymbolRegular.ZoomOut24, () => Zoom(scale / 1.25), "Zoom out (Ctrl and the wheel)", 16, 30);
        var more = K.IconButton(SymbolRegular.ZoomIn24, () => Zoom(scale * 1.25), "Zoom in (Ctrl and the wheel)", 16, 30);
        var width = K.IconButton(SymbolRegular.PageFit24, () => { fit = true; Fit(); }, "Fit the page’s width", 16, 30);
        percent.MinWidth = 46;
        percent.TextAlignment = TextAlignment.Center;
        percent.VerticalAlignment = VerticalAlignment.Center;
        var bar = new Border
        {
            Child = K.H(2, less, percent, more, width), CornerRadius = new CornerRadius(8), Padding = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 14),
            Effect = K.Shadow(16, 4, 0.18),
        };
        bar.Bg(B.Flyout).Stroke(B.FlyoutStroke);
        return bar;
    }

    // ---- drawing pages as they come into sight ----

    private void Soon()
    {
        settle.Stop();
        settle.Start();
    }

    /// <summary>The top of each page in the scrolled stack.</summary>
    private double Top(int i)
    {
        double y = Pad;
        for (var j = 0; j < i; j++) y += Math.Round(m.Pages[j].ShownH * scale) + 2 + Gap;
        return y;
    }

    private (int First, int Last) InSight(double screens)
    {
        if (pages.Count == 0) return (0, -1);
        var top = sv.VerticalOffset - sv.ViewportHeight * screens;
        var bottom = sv.VerticalOffset + sv.ViewportHeight * (1 + screens);
        int first = -1, last = -1;
        double y = Pad;
        for (var i = 0; i < pages.Count; i++)
        {
            var h = Math.Round(m.Pages[i].ShownH * scale) + 2;
            if (y + h >= top && y <= bottom) { if (first < 0) first = i; last = i; }
            y += h + Gap;
        }
        return first < 0 ? (0, -1) : (first, last);
    }

    private double Want(int i) => Math.Min(4096, Math.Round(m.Pages[i].ShownW * scale * Dpi));

    private async void Pump()
    {
        if (pumping || m.Render == null || !IsLoaded) return;
        pumping = true;
        try
        {
            // (pages far out of sight let go of their pictures)
            var (keepFirst, keepLast) = InSight(4);
            foreach (var p in pages)
                if ((p.Index < keepFirst || p.Index > keepLast) && p.Image.Source != null) { p.Image.Source = null; p.Rendered = 0; }
            while (IsLoaded)
            {
                var (first, last) = InSight(1);
                var center = (first + last) / 2.0;
                PageView? next = null;
                for (var i = first; i <= last && i < pages.Count; i++)
                {
                    var p = pages[i];
                    var want = Want(i);
                    if (p.Rendering || p.Rendered > 0 && Math.Abs(p.Rendered - want) / want < 0.15) continue;
                    if (next == null || Math.Abs(i - center) < Math.Abs(next.Index - center)) next = p;
                }
                if (next == null) break;
                var gen = m.Generation;
                var doc = m.Render;
                var px = Want(next.Index);
                var box = m.Pages[next.Index];
                next.Rendering = true;
                BitmapSource? pic = null;
                try { pic = await WinPdf.Picture(doc.GetPage((uint)next.Index), px, px * box.ShownH / box.ShownW); }
                catch (Exception e) { Log.Write($"mark page {next.Index}: {e.Message}"); }
                next.Rendering = false;
                if (gen != m.Generation) break;
                next.Rendered = px;
                if (pic != null) next.Image.Source = pic;
                m.Read(next.Index);
                Shot.Moved();
            }
        }
        finally
        {
            pumping = false;
        }
    }

    // ---- the marks on the pages ----

    private void DrawAllMarks()
    {
        foreach (var p in pages) DrawMarks(p);
    }

    private void NoteChanged(PdfMark mark)
    {
        if (mark.Kind == "FreeText" && mark.Page < pages.Count) DrawMarks(pages[mark.Page]);
    }

    private void DrawMarks(PageView p)
    {
        p.Marks.Children.Clear();
        foreach (var mark in m.Marks.Where(x => x.Page == p.Index)) Draw(p.Marks, mark);
    }

    private void Draw(Canvas c, PdfMark mark)
    {
        var color = MarkModel.ColorOf(mark);
        Point S(double x, double y) => m.Shown(mark.Page, x, y);
        switch (mark.Kind)
        {
            case "Highlight" or "Underline" or "StrikeOut":
                foreach (var q in mark.Quads)
                {
                    Point tl = S(q[0], q[1]), tr = S(q[2], q[3]), bl = S(q[4], q[5]), br = S(q[6], q[7]);
                    if (mark.Kind == "Highlight")
                    {
                        c.Children.Add(new Polygon { Points = new PointCollection { tl, tr, br, bl }, Fill = new SolidColorBrush(K.WithAlpha(color, mark.Opacity < 1 ? Math.Max(0.2, mark.Opacity) : 0.42)) });
                        continue;
                    }
                    var at = mark.Kind == "Underline" ? 0.08 : 0.5;
                    var h = (tl - bl).Length;
                    var a = bl + (tl - bl) * at;
                    var b = br + (tr - br) * at;
                    c.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = new SolidColorBrush(color), StrokeThickness = Math.Max(0.8, h / 14) });
                }
                break;
            case "Text":
            {
                // (a note stands upright from its rectangle's top left corner, however the page is turned)
                var r = new Rect(S(mark.X1, mark.Y2), new Size(22, 22));
                var icon = new Canvas { Width = 22, Height = 22 };
                icon.Children.Add(new Rectangle { Width = 19, Height = 19, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(color), Stroke = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)), StrokeThickness = 0.8 });
                Canvas.SetLeft(icon.Children[0], 1.5);
                Canvas.SetTop(icon.Children[0], 1.5);
                foreach (var (y, x2) in new[] { (6.5, 17.0), (11.0, 17.0), (15.5, 13.0) })
                    icon.Children.Add(new Line { X1 = 5, X2 = x2, Y1 = y, Y2 = y, Stroke = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)), StrokeThickness = 1 });
                Canvas.SetLeft(icon, r.Left);
                Canvas.SetTop(icon, r.Top);
                c.Children.Add(icon);
                break;
            }
            case "FreeText":
            {
                var r = m.Shown(mark);
                var t = new TextBlock
                {
                    Text = mark.Contents, FontFamily = new FontFamily("Arial"), FontSize = mark.FontSize > 0 ? mark.FontSize : 14,
                    Foreground = new SolidColorBrush(color), Padding = new Thickness(2, 1, 2, 0), LineHeight = (mark.FontSize > 0 ? mark.FontSize : 14) * 1.2,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                };
                Canvas.SetLeft(t, r.Left);
                Canvas.SetTop(t, r.Top);
                c.Children.Add(t);
                break;
            }
            case "Ink":
                foreach (var stroke in mark.Ink)
                    c.Children.Add(new Polyline
                    {
                        Points = new PointCollection(stroke.Select(p => S(p.X, p.Y))), Stroke = new SolidColorBrush(color), StrokeThickness = Math.Max(0.5, mark.Border),
                        StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                    });
                break;
            case "Square" or "Circle":
            {
                var r = m.Shown(mark);
                var w = Math.Max(0.5, mark.Border);
                Shape s = mark.Kind == "Square" ? new Rectangle() : new Ellipse();
                s.Width = Math.Max(1, r.Width - w);
                s.Height = Math.Max(1, r.Height - w);
                s.Stroke = new SolidColorBrush(color);
                s.StrokeThickness = w;
                Canvas.SetLeft(s, r.Left + w / 2);
                Canvas.SetTop(s, r.Top + w / 2);
                c.Children.Add(s);
                break;
            }
        }
    }

    // ---- selecting words ----

    private void DrawSelection()
    {
        foreach (var p in pages) p.Selection.Children.Clear();
        if (m.SelPage < 0 || m.SelPage >= pages.Count) return;
        var layer = pages[m.SelPage].Selection;
        foreach (var (box, _) in m.SelectedLines())
        {
            var r = new Rectangle { Width = box.Width, Height = box.Height, Opacity = 0.32, RadiusX = 1, RadiusY = 1 };
            r.SetResourceReference(Shape.FillProperty, B.Accent);
            Canvas.SetLeft(r, box.Left);
            Canvas.SetTop(r, box.Top);
            layer.Children.Add(r);
        }
    }

    private void WordsRead(int page)
    {
        if (page >= pages.Count) return;
        var none = m.Words.TryGetValue(page, out var ws) && ws.Count == 0;
        pages[page].Input.Cursor = none ? Cursors.Cross : Cursors.IBeam;
        if (hint.Visibility == Visibility.Visible && dragPage == null) hint.Visibility = Visibility.Collapsed;
    }

    private void Hint(string text)
    {
        hintText.Text = text;
        hintText.Fg(B.Text);
        hint.Visibility = Visibility.Visible;
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        t.Tick += (_, _) => { t.Stop(); hint.Visibility = Visibility.Collapsed; };
        t.Start();
    }

    /// <summary>The word nearest a point, in reading order: the one under it, else the nearest on its line, else the line's end before it.</summary>
    private static int Nearest(List<MarkWord> ws, Point p)
    {
        for (var i = 0; i < ws.Count; i++)
        {
            var b = ws[i].Box;
            b.Inflate(2, 2);
            if (b.Contains(p)) return i;
        }
        var lines = ws.Select((w, i) => (w, i)).GroupBy(x => x.w.Line).Select(g => (Box: g.Select(x => x.w.Box).Aggregate(Rect.Union), First: g.First().i, Last: g.Last().i)).ToList();
        foreach (var l in lines)
        {
            if (p.Y < l.Box.Top - 2 || p.Y > l.Box.Bottom + 2) continue;
            var best = l.First;
            for (var i = l.First; i <= l.Last; i++)
                if (Math.Abs(ws[i].Box.Left + ws[i].Box.Width / 2 - p.X) < Math.Abs(ws[best].Box.Left + ws[best].Box.Width / 2 - p.X)) best = i;
            return best;
        }
        var above = lines.Where(l => l.Box.Bottom < p.Y).OrderByDescending(l => l.Box.Bottom).FirstOrDefault();
        return above.Box.IsEmpty || above == default ? 0 : above.Last;
    }

    private void Down(PageView p, MouseButtonEventArgs e)
    {
        Focus();
        var pt = e.GetPosition(p.Input);
        if (!m.Words.TryGetValue(p.Index, out var ws))
        {
            m.Read(p.Index);
            Hint("Finding the words on this page…");
            return;
        }
        e.Handled = true;
        if (ws.Count > 0 && e.ClickCount >= 2)
        {
            var i = Nearest(ws, pt);
            if (e.ClickCount == 2) m.Select(p.Index, i, i);
            else
            {
                var line = ws[i].Line;
                m.Select(p.Index, ws.FindIndex(w => w.Line == line), ws.FindLastIndex(w => w.Line == line));
            }
            return;
        }
        dragPage = p;
        dragFrom = pt;
        moved = false;
        anchor = ws.Count > 0 ? Nearest(ws, pt) : -1;
        p.Input.CaptureMouse();
    }

    private void Move(PageView p, MouseEventArgs e)
    {
        if (dragPage != p || e.LeftButton != MouseButtonState.Pressed) return;
        var pt = e.GetPosition(p.Input);
        if (!moved && (pt - dragFrom).Length < 3) return;
        moved = true;
        var box = m.Pages[p.Index];
        pt = new Point(Math.Clamp(pt.X, 0, box.ShownW), Math.Clamp(pt.Y, 0, box.ShownH));
        if (anchor >= 0 && m.Words.TryGetValue(p.Index, out var ws) && ws.Count > 0)
            m.Select(p.Index, anchor, Nearest(ws, pt));
        else m.SelectArea(p.Index, new Rect(dragFrom, pt));
    }

    private void Up(PageView p, MouseButtonEventArgs e)
    {
        if (dragPage != p) return;
        dragPage = null;
        p.Input.ReleaseMouseCapture();
        if (!moved) m.ClearSelection();
    }

    // ---- where things are ----

    /// <summary>The page in the middle of the view, and the point of it there.</summary>
    public (int Page, Point Middle) Middle()
    {
        if (pages.Count == 0) return (-1, default);
        var y = sv.VerticalOffset + sv.ViewportHeight / 2;
        var x = sv.HorizontalOffset + sv.ViewportWidth / 2;
        var page = pages.Count - 1;
        for (var i = 0; i < pages.Count; i++)
            if (Top(i) + Math.Round(m.Pages[i].ShownH * scale) + 2 + Gap / 2 >= y) { page = i; break; }
        var box = m.Pages[page];
        var left = Left(box);
        var px = Math.Clamp((x - left) / scale, 0, box.ShownW);
        var py = Math.Clamp((y - Top(page)) / scale, 0, box.ShownH);
        return (page, new Point(px, py));
    }

    /// <summary>Where a page's face starts across the scrolled view (the pages are centred in the stack, the stack in the view).</summary>
    private double Left(PdfBox box)
    {
        var stackLeft = Math.Max(Pad, (sv.ViewportWidth - stack.ActualWidth) / 2);
        return stackLeft + (stack.ActualWidth - (Math.Round(box.ShownW * scale) + 2)) / 2 + 1;
    }

    /// <summary>A mark brought into sight (its row pressed).</summary>
    private void Show(PdfMark mark)
    {
        if (mark.Page < 0 || mark.Page >= pages.Count) return;
        var r = m.Shown(mark);
        var y = Top(mark.Page) + r.Top * scale;
        sv.ScrollToVerticalOffset(Math.Max(0, y - sv.ViewportHeight / 3));
        var x = Left(m.Pages[mark.Page]) + (r.Left + r.Width / 2) * scale;
        if (x < sv.HorizontalOffset || x > sv.HorizontalOffset + sv.ViewportWidth) sv.ScrollToHorizontalOffset(Math.Max(0, x - sv.ViewportWidth / 2));
    }

    public void ZoomIn() => Zoom(scale * 1.25);
    public void ZoomOut() => Zoom(scale / 1.25);
    public void FitWidth() { fit = true; Fit(); }
}

/// <summary>
/// PDF annotator (the Mac's PDFAnnotatorTool): the PDF in the middle, the marking tools over it, and the marks with their
/// notes beside it.
/// </summary>
internal sealed class PdfAnnotatorTool : ContentControl
{
    private readonly ToolContext c;
    private readonly MarkModel m = MarkModel.Shared;
    private readonly Color tint = FileKit.Tint("mark");
    private readonly ContentControl toolbar = new() { Focusable = false };
    private readonly ContentControl message = new() { Focusable = false };
    private readonly ContentControl panelBody = new() { Focusable = false };
    private readonly Dictionary<PdfMark, TextBlock> rowWords = new();
    private Wpf.Ui.Controls.Button? copyNotes;
    private MarkPages? pagesView;
    private Grid? editor;

    public PdfAnnotatorTool(ToolContext c)
    {
        this.c = c;
        Focusable = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        var handed = c.TakeFiles();
        if (handed.Count == 0) handed = FileKit.Launched();
        if (handed.Count > 0) m.Open(handed[0]);
        PreviewKeyDown += Key;
        Loaded += (_, _) => { Hook(true); Render(); };
        Unloaded += (_, _) => Hook(false);
        Render();
    }

    private Window? Owner => FileKit.Owner(this, c);

    private void Hook(bool on)
    {
        m.Opened -= Render;
        m.StateChanged -= State;
        m.SelectionChanged -= Selected;
        m.MarksChanged -= Panel;
        m.WordsRead -= Words;
        m.NoteChanged -= Note;
        if (!on) return;
        m.Opened += Render;
        m.StateChanged += State;
        m.SelectionChanged += Selected;
        m.MarksChanged += Panel;
        m.WordsRead += Words;
        m.NoteChanged += Note;
    }

    private void Render()
    {
        if (!m.IsOpen)
        {
            editor = null;
            pagesView = null;
            var v = K.V(16, ToolKit.DropZone("Drop a PDF to mark up", "Or click to choose one. Highlight, underline and add notes; Save keeps them in the PDF.", MarkModel.Filter, false, tint,
                f => m.Open(f[0]), SymbolRegular.Highlight24, 260));
            if (m.Message != null) v.Children.Add(ToolKit.Note(m.Message));
            Content = ToolKit.Page(v);
            return;
        }
        if (editor == null)
        {
            pagesView = new MarkPages();
            var frame = new Border { Child = pagesView, CornerRadius = new CornerRadius(8), ClipToBounds = true };
            frame.Stroke(B.CardStroke);
            frame.SizeChanged += (_, e) => frame.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 8, 8);
            var side = K.Rows("auto, *");
            var copy = FileKit.Link("Copy Notes", () => FileKit.Copy(m.NotesText));
            copyNotes = copy;
            side.Children.Add(K.Columns("auto, *, auto", K.Text("Marks", Ty.Subtitle).Col(0), copy.Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(2)).Margin(0, 0, 0, 12).Row(0));
            side.Children.Add(panelBody.Row(1));
            var body = K.Columns("*, 20, 330", frame.Col(0), side.Col(2));
            editor = K.Rows("auto, auto, *", toolbar.Row(0), message.Row(1), body.Row(2));
            editor.Margin = new Thickness(40, 8, 40, 24);
        }
        Content = editor;
        Toolbar();
        State();
        Panel();
    }

    private void State()
    {
        if (!m.IsOpen) { Render(); return; }
        message.Content = m.Message == null ? null : ToolKit.Note(m.Message).Margin(0, 0, 0, 12);
        Toolbar();
        if (copyNotes != null) copyNotes.IsEnabled = m.NotesText.Length > 0;
    }

    // ---- the toolbar ----

    private bool shownSelection;

    /// <summary>The selection changed: the marking buttons come on or go off (only when there comes to be one, or none).</summary>
    private void Selected()
    {
        if (m.HasSelection != shownSelection) Toolbar();
    }

    private void Toolbar()
    {
        if (!m.IsOpen) return;
        shownSelection = m.HasSelection;
        var tools = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        void Add(FrameworkElement e, double right = 6) { e.Margin = new Thickness(0, 0, right, 8); tools.Children.Add(e); }
        var has = m.HasSelection;
        foreach (var (name, symbol, kind) in new[] { ("Highlight", SymbolRegular.Highlight24, "Highlight"), ("Underline", SymbolRegular.TextUnderline24, "Underline"), ("Strike", SymbolRegular.TextStrikethrough24, "StrikeOut") })
        {
            var b = FileKit.Button(name, symbol, () => m.MarkSelection(kind), accent: has, tip: has ? $"{name} the words selected" : "Select some words on the page first");
            b.IsEnabled = has;
            ToolTipService.SetShowOnDisabled(b, true);
            Add(b, kind == "StrikeOut" ? 14 : 6);
        }
        Add(FileKit.Button("Note", SymbolRegular.NoteAdd24, () => AddNote(false), tip: "Pin a note beside the words selected, or in the middle of the page"));
        Add(FileKit.Button("Text", SymbolRegular.Textbox24, () => AddNote(true), tip: "Put a box of text on the page; type it in the panel"), 12);
        var dots = K.H(2);
        foreach (var (key, name, hex) in MarkModel.Palette)
        {
            var on = m.ColorKey == key;
            var ring = new Ellipse { Width = 28, Height = 28, StrokeThickness = 2, Opacity = on ? 0.9 : 0 };
            ring.SetResourceReference(Shape.StrokeProperty, B.Text);
            var dot = new Ellipse { Width = 20, Height = 20, Fill = new SolidColorBrush(K.Hex(hex)) };
            var face = new Grid { Width = 28, Height = 28, Children = { dot, ring } };
            var b = K.Subtle(face, () => m.SetColor(key), new Thickness(2), 16);
            b.ToolTip = name;
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            dots.Children.Add(b);
        }
        Add(dots, 12);
        var undo = FileKit.Button("Undo", SymbolRegular.ArrowUndo24, m.Undo, tip: "Take back the last mark (Ctrl+Z)");
        undo.IsEnabled = m.CanUndo;
        Add(undo);

        var file = K.Text(m.Path != null ? Path.GetFileName(m.Path) : "", Ty.Body, B.Text2);
        file.MaxWidth = 220;
        file.ToolTip = m.Path;
        file.VerticalAlignment = VerticalAlignment.Center;
        var save = FileKit.Button(m.Dirty ? "Save" : "Saved", SymbolRegular.Save24, () => m.Save(false, Owner), accent: m.Dirty, tip: "Write the marks into the PDF (Ctrl+S)");
        save.IsEnabled = m.Dirty || m.Path == null;
        ToolTipService.SetShowOnDisabled(save, true);
        save.CornerRadius = new CornerRadius(4, 0, 0, 4);
        var more = new Wpf.Ui.Controls.Button { Icon = new SymbolIcon { Symbol = SymbolRegular.ChevronDown24 }, Padding = new Thickness(8, 6, 8, 6), CornerRadius = new CornerRadius(0, 4, 4, 0), Margin = new Thickness(1, 0, 0, 0), ToolTip = "More ways to save" };
        var menu = MoreKit.Menu(
            ("Save As…", SymbolRegular.Save24, () => m.Save(true, Owner)),
            ("Open Another PDF…", SymbolRegular.FolderOpen24, () =>
            {
                var f = Files.Choose(MarkModel.Filter, false, Owner);
                if (f.Count > 0) m.Open(f[0]);
            }));
        more.Click += (_, e) => { e.Handled = true; MoreKit.Open(menu, more); };
        var right = K.H(10, file, K.H(0, save, more));
        right.VerticalAlignment = VerticalAlignment.Top;
        right.Margin = new Thickness(8, 0, 0, 8);
        toolbar.Content = K.Columns("*, auto", tools.Col(0), right.Col(1));
    }

    private void AddNote(bool text)
    {
        var (page, middle) = pagesView?.Middle() ?? (0, new Point(0, 0));
        m.AddNote(text, page, middle);
    }

    // ---- the marks beside the page ----

    private static string KindName(string kind) => kind switch
    {
        "Highlight" => "Highlight", "Underline" => "Underline", "StrikeOut" => "Strike-through", "Text" => "Note", "FreeText" => "Text",
        "Ink" => "Drawing", "Square" => "Box", "Circle" => "Circle", _ => kind,
    };

    private void Panel()
    {
        if (!m.IsOpen) return;
        rowWords.Clear();
        if (copyNotes != null) copyNotes.IsEnabled = m.NotesText.Length > 0;
        if (m.Marks.Count == 0)
        {
            panelBody.Content = K.Wrapped("Select words on the page and press Highlight, Underline or Strike. Notes and text boxes go beside the words selected.", Ty.Body, B.Text2);
            return;
        }
        var list = FileKit.List(m.Marks.Select(Row), 12);
        var keep = panelBody.Content as ScrollViewer;
        var offset = keep?.VerticalOffset ?? 0;
        var sv = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Padding = new Thickness(0, 0, 4, 0) };
        if (offset > 0) sv.Loaded += (_, _) => sv.ScrollToVerticalOffset(offset);
        panelBody.Content = sv;
    }

    private FrameworkElement Row(PdfMark mark)
    {
        var dot = K.Dot(MarkModel.ColorOf(mark), 9).Margin(0, 0, 8, 0);
        var kind = K.Text($"{KindName(mark.Kind)} · page {mark.Page + 1}", Ty.Caption, B.Text2).Strong();
        kind.VerticalAlignment = VerticalAlignment.Center;
        var go = K.IconButton(SymbolRegular.ArrowCircleRight24, () => m.Show(mark), "Go to it", 16, 28);
        var off = K.IconButton(SymbolRegular.Delete24, () => m.Remove(mark), "Take it off", 16, 28);
        var head = K.Columns("auto, *, auto, auto", dot.Col(0), kind.Col(1), go.Col(2), off.Col(3));
        var words = K.Wrapped(mark.Words.Length > 0 ? $"“{mark.Words}”" : "", Ty.Body, B.Text, 3);
        words.TextTrimming = TextTrimming.CharacterEllipsis;
        words.Visibility = mark.Words.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        rowWords[mark] = words;
        var note = new TextBox
        {
            Text = mark.Contents, PlaceholderText = mark.Kind == "FreeText" ? "Text on the page" : "A note", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MinHeight = 32, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, ClearButtonEnabled = false,
        };
        note.TextChanged += (_, _) => m.SetNote(mark, note.Text);
        var v = K.V(6, head, words, note);
        v.Margin = new Thickness(12, 10, 12, 10);
        var row = new Border { Child = v, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        row.MouseLeftButtonUp += (_, e) =>
        {
            for (var d = e.OriginalSource as DependencyObject; d != null && d != row; d = VisualTreeHelper.GetParent(d))
                if (d is System.Windows.Controls.Primitives.TextBoxBase or Button) return;
            m.Show(mark);
        };
        return row;
    }

    private void Words(int page)
    {
        foreach (var (mark, t) in rowWords)
        {
            if (mark.Page != page) continue;
            t.Text = mark.Words.Length > 0 ? $"“{mark.Words}”" : "";
            t.Visibility = mark.Words.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        if (copyNotes != null) copyNotes.IsEnabled = m.NotesText.Length > 0;
    }

    private void Note(PdfMark mark)
    {
        if (copyNotes != null) copyNotes.IsEnabled = m.NotesText.Length > 0;
    }

    // ---- the keyboard ----

    private void Key(object sender, KeyEventArgs e)
    {
        if (!m.IsOpen || Keyboard.Modifiers != ModifierKeys.Control && Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift)) return;
        var typing = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase;
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case System.Windows.Input.Key.S:
                m.Save(shift, Owner);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Z when !typing:
                m.Undo();
                e.Handled = true;
                break;
            case System.Windows.Input.Key.C when !typing && m.HasSelection:
                FileKit.Copy(m.SelectedText);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.OemPlus or System.Windows.Input.Key.Add when !typing:
                pagesView?.ZoomIn();
                e.Handled = true;
                break;
            case System.Windows.Input.Key.OemMinus or System.Windows.Input.Key.Subtract when !typing:
                pagesView?.ZoomOut();
                e.Handled = true;
                break;
            case System.Windows.Input.Key.D0 or System.Windows.Input.Key.NumPad0 when !typing:
                pagesView?.FitWidth();
                e.Handled = true;
                break;
        }
    }
}
