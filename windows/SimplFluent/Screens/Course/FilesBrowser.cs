using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>A folder of a course's or a group's files, pushed on its Files (the Mac's FilesView for a folder).</summary>
public sealed class FolderScreen : CoursePartScreen
{
    public FolderScreen(Engine.Engine engine, string ctx, string id, string name)
        : base(engine, new CourseFilesPart(engine, ctx, id, string.IsNullOrEmpty(name) ? "Folder" : name)) { }
}

/// <summary>
/// A folder of a course's or a group's files (the Mac's FilesBrowser; its root is Files): the folders in it, each
/// opening its own list, then its files with their Explorer icon, kind, size and date. On a wide page the list keeps the
/// left and the file picked is shown on the right as it is — drawn by Windows' own renderers, fetched the moment it is
/// picked — with Open, Open With… and Download over it: a click picks a file, a double-click (or Space, or Enter) opens
/// it in its app, ↑ ↓ walk the files. On a narrower page a click opens a file at once. A locked file is listed, not
/// opened. Every file's menu: Open in its app, Open With…, Download, Open in Canvas, Copy Link.
/// </summary>
internal sealed class CourseFilesPart : CoursePart
{
    /// <summary>The narrowest page (the content's width) that keeps a preview beside the list (the Mac's 960 for the whole).</summary>
    private const double WideFrom = 880;

    private readonly string folder, name;
    private readonly Loader<FilesData> model;
    private readonly ScrollViewer list = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly ContentControl pane = new() { Focusable = false };
    private readonly ContentControl note = new() { Focusable = false, Visibility = Visibility.Collapsed };
    private readonly Dictionary<string, (Button Row, TextBlock Name)> rows = new();
    private FrameworkElement? commands;
    private string? picked;
    private bool previewLoading;
    private string? previewPath, previewError;
    private int previewGen;
    private MediaElement? media;
    private bool wideNow;
    private double paneWidth = 480;

    public CourseFilesPart(Engine.Engine engine, string ctx, string folder, string name) : base(engine, ctx)
    {
        this.folder = folder;
        this.name = name;
        model = Watch(new Loader<FilesData>());
        list.PreviewKeyDown += OnKey;
        list.IsKeyboardFocusWithinChanged += (_, _) => { if (picked != null && rows.TryGetValue(picked, out var r)) Wash(r.Row, true); };
    }

    private object Args => new { ctx, folder = folder.Length == 0 ? null : folder };

    public override string Title => name;
    public override string? Subtitle => model.Data?.Context;
    /// <summary>The list scrolls on its own, the preview beside it staying put.</summary>
    public override bool Scrolls => false;
    public override FrameworkElement? Commands => commands ??= CourseKit.MoreButton(engine, () => $"/{ctx}/files", () => name);

    public override void Refresh() => model.Ensure(engine, "files", Args);

    public override bool LayoutChanges(double before, double now) => (before >= WideFrom) != (now >= WideFrom) || (before + 80 < 560) != (now + 80 < 560);

    public override void Hide()
    {
        media?.Stop();
        media = null;
    }

    public override FrameworkElement Build()
    {
        if (model.Data is not { } d) return K.LoadState(model.Error, () => model.Reload(engine, "files", Args));
        if (picked != null && d.Files.All(f => f.Id != picked)) { picked = null; previewPath = null; previewError = null; }
        // (the screenshot suite: --open file:f1 picks that file for the preview beside the list)
        if (picked == null && Launch.Take("file:") is { } launchId && d.Files.Any(f => f.Id == launchId)) { picked = launchId; LoadPreview(); }

        var page = Width() + 80;
        var pad = page < 560 ? 20 : 40;
        var wide = Width() >= WideFrom;
        wideNow = wide;
        var offset = list.VerticalOffset;
        Detach(list);
        Detach(pane);
        rows.Clear();
        var lists = Lists(d, wide);
        lists.Margin = new Thickness(lists.Margin.Left, lists.Margin.Top, lists.Margin.Right, lists.Margin.Bottom + pad); // (room at the foot that scrolls with the list)
        list.Content = lists;
        list.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => list.ScrollToVerticalOffset(offset));
        if (!wide)
        {
            list.Padding = new Thickness(pad, 0, pad, 0);
            return list;
        }
        // the list on the left, the file picked on the right (it stays put while the list scrolls)
        list.Padding = new Thickness(pad, 0, 16, 0);
        var g = new Grid();
        var listCol = new ColumnDefinition { Width = new GridLength(ListWidth(page)) };
        g.ColumnDefinitions.Add(listCol);
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.SizeChanged += (_, e) =>
        {
            listCol.Width = new GridLength(ListWidth(e.NewSize.Width));
            paneWidth = Math.Max(240, e.NewSize.Width - ListWidth(e.NewSize.Width) - 8 - pad - 20);
        };
        paneWidth = Math.Max(240, page - ListWidth(page) - 8 - pad - 20);
        pane.Margin = new Thickness(8, 0, pad, pad);
        pane.Content = Pane(d);
        g.Children.Add(list.Col(0));
        g.Children.Add(pane.Col(1));
        return g;
    }

    private static double ListWidth(double page) => Math.Min(640, Math.Max(420, Math.Round(page * 0.44)));

    private static void Detach(FrameworkElement e)
    {
        switch (e.Parent)
        {
            case Panel p: p.Children.Remove(e); break;
            case ContentControl c: c.Content = null; break;
            case Decorator b: b.Child = null; break;
        }
    }

    // ---- the lists ----

    private FrameworkElement Lists(FilesData d, bool wide)
    {
        var color = K.Hex(d.Color);
        Detach(note);
        var v = K.V(26, note);
        if (d.Folders.Count == 0 && d.Files.Count == 0)
            v.Children.Add(K.Card(K.Empty(SymbolRegular.Folder24, d.Empty ?? "This folder is empty"), new Thickness(16, 0, 16, 0)));
        if (d.Folders.Count > 0)
            v.Children.Add(K.V(0, Heading.Section("Folders", Count(d.Folders.Count)), K.Card(CourseKit.Divided(d.Folders, f => FolderLine(f, color)), new Thickness(8))));
        if (d.Files.Count > 0)
            v.Children.Add(K.V(0, Heading.Section("Files", Count(d.Files.Count)), K.Card(CourseKit.Divided(d.Files, f => FileLine(f, wide), 54), new Thickness(8))));
        return v;
    }

    private static string Count(int n) => n.ToString(CultureInfo.CurrentCulture);

    private FrameworkElement FolderLine(FolderRow f, Color color)
    {
        void Open() => engine.Push(Route.Folder(ctx, f.Id, f.Name));
        var b = CourseKit.RowLink(CourseKit.InfoRow(f.Name, f.Sub, f.Locked == true ? SymbolRegular.LockClosed24 : SymbolRegular.Folder24, color, K.Icon(SymbolRegular.ChevronRight20, 12, B.Text3)), Open);
        CourseKit.Menu(b, items => items.Add(CourseKit.MenuEntry("Open", SymbolRegular.FolderOpen24, Open)));
        return b;
    }

    private FrameworkElement FileLine(FileRow f, bool wide)
    {
        var page = $"/{ctx}/files/{f.Id}"; // (the file's page on the school's site: its download address carries a one-time key)
        if (f.Url is { } u && f.Locked != true)
        {
            var on = wide && picked == f.Id;
            var (label, nameText) = FileLabel(f, on);
            var b = K.Subtle(label, () => { if (wide) Pick(f); else OpenFile(u, f.Name); }, new Thickness(8, 7, 8, 7), 6);
            if (wide)
            {
                b.MouseDoubleClick += (_, e) => { e.Handled = true; OpenFile(u, f.Name); };
                rows[f.Id] = (b, nameText);
                if (on) Wash(b, true);
            }
            b.ToolTip = wide ? $"Click to preview {f.Name}; double-click to open it" : $"Open {f.Name}";
            CourseKit.Menu(b, items => FileMenu(items, u, f.Name, page));
            return b;
        }
        var still = CourseKit.StillRow(FileLabel(f, false).Label);
        var g = new Grid { Background = Brushes.Transparent, Children = { still } };
        CourseKit.Menu(g, items =>
        {
            items.Add(CourseKit.MenuEntry($"Open in {engine.LmsName}", SymbolRegular.Globe24, () => engine.OpenWebScreen(page, f.Name)));
            items.Add(CourseKit.MenuEntry("Copy Link", SymbolRegular.Link24, () => CourseKit.CopyLink(engine, page)));
        });
        return g;
    }

    /// <summary>Its icon (faded, with a lock, when locked), its name over its line, and what it is at the end.</summary>
    private (FrameworkElement Label, TextBlock Name) FileLabel(FileRow f, bool selected)
    {
        var locked = f.Locked == true || f.Url == null;
        var icon = CourseFiles.Icon(f.Name, 32);
        icon.Opacity = locked ? 0.5 : 1;
        var box = new Grid { Width = 34, Height = 34, Children = { icon } };
        if (locked)
        {
            var badge = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Child = K.Icon(SymbolRegular.LockClosed16, 10, B.Text2, filled: true) }.Bg(B.Solid);
            box.Children.Add(badge);
        }
        var nameText = K.Wrapped(f.Name, Ty.Body, B.Text, 2);
        if (selected) nameText.FontWeight = FontWeights.SemiBold;
        var words = K.V(2, nameText);
        if (!string.IsNullOrEmpty(f.Sub)) words.Children.Add(K.Text(f.Sub!, Ty.Caption, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var kind = K.Text(locked ? "Locked" : CourseFiles.Kind(f.Name, f.Kind), Ty.Caption, B.Text2);
        kind.VerticalAlignment = VerticalAlignment.Center;
        var g = K.Columns("auto, *, auto", box.Margin(0, 0, 12, 0).Col(0), words.Col(1), kind.Margin(8, 0, 0, 0).Col(2));
        return (g, nameText);
    }

    /// <summary>The menu of a file's ways out (the Mac's FileMenuItems): its app, another app, a copy in Downloads; the
    /// school's own page for it, its link.</summary>
    private void FileMenu(ItemCollection items, string url, string fileName, string page)
    {
        items.Add(CourseKit.MenuEntry(CourseFiles.OpenTitle(fileName), SymbolRegular.Open24, () => OpenFile(url, fileName)));
        items.Add(CourseKit.MenuEntry("Open With…", SymbolRegular.AppGeneric24, () => OpenWith(url, fileName)));
        items.Add(CourseKit.MenuEntry("Download", SymbolRegular.ArrowDownload24, () => Download(url, fileName)));
        items.Add(new Separator());
        items.Add(CourseKit.MenuEntry($"Open in {engine.LmsName}", SymbolRegular.Globe24, () => engine.OpenWebScreen(page, fileName)));
        items.Add(CourseKit.MenuEntry("Copy Link", SymbolRegular.Link24, () => CourseKit.CopyLink(engine, page)));
    }

    /// <summary>The row picked: the accent's wash under it (deeper while the list has the keyboard), its name in bold.</summary>
    private void Wash(Button row, bool on)
    {
        row.Background = on ? K.Brush(K.WithAlpha(Appearance.Accent, list.IsKeyboardFocusWithin ? 0.2 : 0.12)) : Brushes.Transparent;
        AutomationProperties.SetItemStatus(row, on ? "Selected" : "");
    }

    // ---- the preview beside the list ----

    private FrameworkElement Pane(FilesData d)
    {
        FrameworkElement content;
        if (PickedFile(d) is { Url: { } u } f)
            content = K.Rows("auto, auto, *", PaneHead(f, u).Margin(18).Row(0), K.Divider().Row(1), PaneBody(f, u).Margin(10).Row(2));
        else
        {
            var v = K.V(12,
                K.Icon(SymbolRegular.DocumentSearch24, 40, B.Text3),
                K.Text("No File Selected", Ty.Subtitle).Center(),
                K.Wrapped("Click a file to see it here. Double-click it, or press Space, to open it in its app.", Ty.Body, B.Text2).Center());
            v.MaxWidth = 340;
            v.HorizontalAlignment = HorizontalAlignment.Center;
            v.VerticalAlignment = VerticalAlignment.Center;
            content = new Grid { Children = { v } };
        }
        var card = K.Card(content, new Thickness(0));
        card.VerticalAlignment = VerticalAlignment.Stretch;
        // (what is drawn inside keeps to the card's corners)
        content.SizeChanged += (_, e) => content.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 7, 7);
        return card;
    }

    private FrameworkElement PaneHead(FileRow f, string u)
    {
        var title = K.Wrapped(f.Name, Ty.BodyLarge, B.Text, 2);
        var line = string.Join(" · ", new[] { CourseFiles.Kind(f.Name, f.Kind), f.Sub ?? "" }.Where(s => s.Length > 0));
        var words = K.V(3, title, K.Text(line, Ty.Caption, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var top = K.Columns("auto, *", CourseFiles.Icon(f.Name, 44).Margin(0, 0, 14, 0).Col(0), words.Col(1));
        var open = K.Button(CourseFiles.OpenTitle(f.Name), () => OpenFile(u, f.Name), accent: true, icon: SymbolRegular.Open24);
        open.ToolTip = $"Open {f.Name} in the app Windows has for it (Enter)";
        var with = K.Button("Open With…", () => OpenWith(u, f.Name), icon: SymbolRegular.AppGeneric24);
        with.ToolTip = $"Choose the app to open {f.Name} in";
        var download = K.Button("Download", () => Download(u, f.Name), icon: SymbolRegular.ArrowDownload24);
        download.ToolTip = $"Save a copy of {f.Name} in Downloads";
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, -8, -8) };
        foreach (var b in new FrameworkElement[] { open, with, download }) { b.Margin = new Thickness(0, 0, 8, 8); buttons.Children.Add(b); }
        return K.V(14, top, buttons);
    }

    private FrameworkElement PaneBody(FileRow f, string u)
    {
        if (previewPath != null)
        {
            try { return CourseFiles.Preview(previewPath, paneWidth, m => media = m); }
            catch (Exception e) { return NoPreview(e.Message, true); }
        }
        if (previewError != null) return NoPreview(previewError, true);
        if (!previewLoading && !CourseFiles.CanShow(f.Name))
            return NoPreview("Windows can’t show this kind of file here. Open it in its app to see it.", false, () => OpenFile(u, f.Name), CourseFiles.OpenTitle(f.Name));
        var v = K.V(10, K.Spinner(24), K.Text($"Loading {f.Name}…", Ty.Caption, B.Text2).Center());
        v.HorizontalAlignment = HorizontalAlignment.Center;
        v.VerticalAlignment = VerticalAlignment.Center;
        return new Grid { Children = { v } };
    }

    /// <summary>Why there is nothing to see (the Mac's "No Preview"), and what can be done: Try Again, or open it.</summary>
    private FrameworkElement NoPreview(string why, bool retry, Action? open = null, string? openLabel = null)
    {
        var v = K.V(12,
            K.Icon(SymbolRegular.Warning24, 36, B.Text3),
            K.Text("No Preview", Ty.Subtitle).Center(),
            K.Wrapped(why, Ty.Body, B.Text2).Center());
        if (retry)
        {
            var again = K.Button("Try Again", () => { previewError = null; previewPath = null; LoadPreview(); });
            again.HorizontalAlignment = HorizontalAlignment.Center;
            v.Children.Add(again);
        }
        else if (open != null)
        {
            var b = K.Button(openLabel ?? "Open", open, accent: true);
            b.HorizontalAlignment = HorizontalAlignment.Center;
            v.Children.Add(b);
        }
        v.MaxWidth = 360;
        v.HorizontalAlignment = HorizontalAlignment.Center;
        v.VerticalAlignment = VerticalAlignment.Center;
        return new Grid { Children = { v } };
    }

    private FileRow? PickedFile(FilesData d) => picked == null ? null : d.Files.FirstOrDefault(f => f.Id == picked);

    private void ShowPane()
    {
        if (wideNow && model.Data is { } d) pane.Content = Pane(d);
        Shot.Moved();
    }

    // ---- doing ----

    private void Pick(FileRow f)
    {
        if (rows.TryGetValue(f.Id, out var now)) now.Row.Focus();
        if (picked == f.Id) return;
        if (picked != null && rows.TryGetValue(picked, out var before)) { Wash(before.Row, false); before.Name.FontWeight = FontWeights.Normal; }
        picked = f.Id;
        if (rows.TryGetValue(f.Id, out var r)) { Wash(r.Row, true); r.Name.FontWeight = FontWeights.SemiBold; }
        previewPath = null;
        previewError = null;
        LoadPreview();
    }

    /// <summary>↑ ↓ walk the files (the first, with none picked; the one picked brought into view); Space and Enter open it.</summary>
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (!wideNow || model.Data is not { } d) return;
        switch (e.Key)
        {
            case Key.Down: e.Handled = Move(1, d); break;
            case Key.Up: e.Handled = Move(-1, d); break;
            case Key.Space or Key.Enter:
                if (PickedFile(d) is { Url: { } u } f && f.Locked != true) { OpenFile(u, f.Name); e.Handled = true; }
                break;
        }
    }

    private bool Move(int step, FilesData d)
    {
        var open = d.Files.Where(f => f.Url != null && f.Locked != true).ToList();
        if (open.Count == 0) return false;
        var at = open.FindIndex(f => f.Id == picked);
        var next = at >= 0 ? Math.Clamp(at + step, 0, open.Count - 1) : step > 0 ? 0 : open.Count - 1;
        Pick(open[next]);
        if (rows.TryGetValue(open[next].Id, out var r)) r.Row.BringIntoView();
        return true;
    }

    /// <summary>The file picked, fetched for the pane (a file fetched before shows at once).</summary>
    private async void LoadPreview()
    {
        media?.Stop();
        media = null;
        var gen = ++previewGen;
        var f = model.Data?.Files.FirstOrDefault(x => x.Id == picked);
        if (f?.Url is not { } u || f.Locked == true || !CourseFiles.CanShow(f.Name))
        {
            previewLoading = false;
            ShowPane();
            return;
        }
        previewLoading = true;
        ShowPane();
        Shot.Began();
        try
        {
            var path = await CourseFiles.Local(engine, u, f.Name);
            if (gen != previewGen) return;
            previewPath = path;
            previewError = null;
        }
        catch (Exception e)
        {
            if (gen != previewGen) return;
            previewError = e.Message;
        }
        finally { Shot.Ended(); }
        previewLoading = false;
        ShowPane();
    }

    /// <summary>A file opened in the app Windows has for it, fetched first (the Mac's Quick Look, the Windows way).</summary>
    private void OpenFile(string url, string fileName)
    {
        if (engine.Absolute(url) is { } a) FilePreview.Open(a, fileName, engine.Web);
    }

    private async void OpenWith(string url, string fileName)
    {
        Note(K.Columns("auto, *", K.Spinner(16).Margin(0, 0, 10, 0).Col(0), K.Text($"Fetching {fileName}…", Ty.Body, B.Text2).Col(1)), false);
        try
        {
            await CourseFiles.OpenWith(engine, url, fileName);
            ClearNote();
        }
        catch (Exception e) { Failed(fileName, e.Message); }
    }

    /// <summary>A copy kept in Downloads, then said so — with Show in Folder (the Mac's toast of a file kept).</summary>
    private async void Download(string url, string fileName)
    {
        Note(K.Columns("auto, *", K.Spinner(16).Margin(0, 0, 10, 0).Col(0), K.Text($"Saving {fileName} to Downloads…", Ty.Body, B.Text2).Col(1)), false);
        try
        {
            var path = await CourseFiles.Keep(engine, url, fileName);
            var kept = K.Columns("auto, *, auto, auto",
                K.Icon(SymbolRegular.CheckmarkCircle24, 18, CourseKit.Green, filled: true).Margin(0, 0, 10, 0).Col(0),
                K.Wrapped($"Saved {System.IO.Path.GetFileName(path)} to Downloads", Ty.Body).Col(1),
                K.Button("Show in Folder", () => CourseFiles.Reveal(path), icon: SymbolRegular.FolderOpen24).Margin(12, 0, 0, 0).Col(2),
                K.IconButton(SymbolRegular.Dismiss24, ClearNote, "Close", 14, 32).Margin(4, 0, 0, 0).Col(3));
            Note(kept, true);
            var shown = note.Content;
            await Task.Delay(12_000);
            if (note.Content == shown) ClearNote();
        }
        catch (Exception e) { Failed(fileName, e.Message); }
    }

    private void Failed(string fileName, string why)
    {
        var line = K.Columns("auto, *, auto",
            K.Icon(SymbolRegular.Warning24, 18, K.Tone("orange"), filled: true).Margin(0, 0, 10, 0).Col(0),
            K.Wrapped($"{fileName} could not be fetched. {why}", Ty.Body).Col(1),
            K.IconButton(SymbolRegular.Dismiss24, ClearNote, "Close", 14, 32).Margin(4, 0, 0, 0).Col(2));
        Note(line, true);
    }

    private void ClearNote()
    {
        note.Content = null;
        note.Visibility = Visibility.Collapsed;
    }

    /// <summary>A word over the lists about a file on its way, kept or not.</summary>
    private void Note(FrameworkElement content, bool settled)
    {
        foreach (var c in content is Grid g ? g.Children.OfType<FrameworkElement>() : Enumerable.Empty<FrameworkElement>()) c.VerticalAlignment = VerticalAlignment.Center;
        var card = K.Card(content, new Thickness(14, 10, 10, 10));
        note.Content = card;
        note.Visibility = Visibility.Visible;
        if (settled) Motion.Enter(card, 0, 8, Motion.Medium);
        Shot.Moved();
    }
}
