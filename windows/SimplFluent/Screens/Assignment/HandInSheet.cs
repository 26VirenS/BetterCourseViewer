using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Web.WebView2.Wpf;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Shell;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Screens;

/// <summary>
/// Hand In, as a dialog over the assignment (the Mac's HandInSheet): the kinds it takes from here — text, a web address,
/// files (chosen in the Open dialog, from Pictures, or dropped on the dialog or the page) — a note for the teacher, and
/// Submit. A file of a kind the assignment does not take is converted to one it does where the web's converter can; the
/// assignment's own file types are checked before anything is sent. A file chosen is looked at in a pane at the dialog's
/// right, the dialog widening for it (the Mac's HandInPreview). While it goes, the dialog says so, and says why if it
/// could not.
/// </summary>
internal sealed class HandInSheet
{
    /// <summary>A Hand In dialog is up (one at a time; a drop on the page then goes to it).</summary>
    public static bool Open { get; private set; }

    private sealed record Picked(Guid Id, string Name, string Type, byte[] Data)
    {
        public string SizeText => AssignKit.SizeText(Data.LongLength);
    }

    private sealed class ConvertInfo
    {
        public List<string> Exts { get; set; } = new();
        public string Line { get; set; } = "";
    }

    private sealed class ConvertedFile
    {
        [System.Text.Json.Serialization.JsonPropertyName("converted")] public bool IsConverted { get; set; }
        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? Data { get; set; }
    }

    private const long Limit = 50 * 1024 * 1024; // (read whole into memory and handed to the page: kept to a sane size)
    private const double FormWidth = 580;

    private readonly Engine.Engine engine;
    private readonly AssignmentData a;
    private readonly Action done;
    private readonly Sheet sheet;
    private readonly SheetButton submit, cancel;
    private string type;
    private readonly List<Picked> files = new();
    private readonly List<string> converting = new();
    private ConvertInfo convertible = new();
    private bool sending, kindShown;

    // the form
    private readonly Grid body = new() { AllowDrop = true, MinHeight = 460 };
    private readonly ContentControl kindHost = new() { Focusable = false };
    private readonly TextBox text, url, note;
    private readonly Stack filesList = new() { Orientation = Orientation.Vertical, Spacing = 4 };
    private readonly TextBlock filesFoot, error, hint;
    private readonly FrameworkElement errorRow, filesSection;
    private readonly ProgressRing busy = K.Spinner(16);
    private SelectorBar? kinds;
    private readonly Grid overlay = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };

    // the preview pane
    private readonly ColumnDefinition paneCol = new() { Width = new GridLength(0) };
    private readonly ColumnDefinition lineCol = new() { Width = new GridLength(0) };
    private readonly Grid pane = new() { Visibility = Visibility.Collapsed, ClipToBounds = true };
    private Guid? showing;
    private string? folder;
    private readonly List<WebView2> owned = new();
    private double formWidth = FormWidth, paneWidth = 540;

    public static void Show(Engine.Engine engine, AssignmentData a, IReadOnlyList<string> dropped, string? link, Action done)
    {
        if (Open) return;
        var s = new HandInSheet(engine, a, done);
        Open = true;
        Dialogs.Show(s.sheet);
        if (dropped.Count > 0 || link != null) s.Take(dropped, link == null ? Array.Empty<string>() : new[] { link });
        // (the screenshot suite: --handin-preview <file> puts a file in and opens its preview, a moment after the dialog is up)
        if (Launch.Get("handin-preview") is { Length: > 0 } path && File.Exists(path))
        {
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            t.Tick += (_, _) => { t.Stop(); s.AddPath(path); };
            t.Start();
        }
    }

    private HandInSheet(Engine.Engine engine, AssignmentData a, Action done)
    {
        this.engine = engine;
        this.a = a;
        this.done = done;
        type = a.Types.FirstOrDefault() ?? "";
        text = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 200, VerticalContentAlignment = VerticalAlignment.Top,
            PlaceholderText = "Write your answer", VerticalScrollBarVisibility = ScrollBarVisibility.Auto, ClearButtonEnabled = false,
        };
        System.Windows.Automation.AutomationProperties.SetName(text, "Your text");
        text.TextChanged += (_, _) => Paint();
        text.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { e.Handled = true; Submit(); }
        };
        url = new TextBox { PlaceholderText = "https://", Icon = new SymbolIcon { Symbol = SymbolRegular.Link24 } };
        System.Windows.Automation.AutomationProperties.SetName(url, "Website address");
        url.TextChanged += (_, _) => Paint();
        note = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinLines = 2, MaxLines = 6, MinHeight = 60, MaxHeight = 140,
            VerticalContentAlignment = VerticalAlignment.Top, PlaceholderText = "A note for your teacher", VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ClearButtonEnabled = false,
        };
        System.Windows.Automation.AutomationProperties.SetName(note, "Comment");
        error = K.Wrapped("", Ty.Body, B.Critical);
        errorRow = K.Columns("auto, *", K.Icon(SymbolRegular.Warning24, 16, B.Critical, filled: true).Margin(0, 2, 8, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0), error.Col(1));
        errorRow.Visibility = Visibility.Collapsed;
        hint = K.Text("", Ty.Caption, B.Text3);
        filesFoot = K.Wrapped("", Ty.Caption, B.Text2);
        filesSection = FilesSection();

        sheet = new Sheet { Bare = true, Width = FormWidth, Body = body };
        submit = sheet.Add("Submit", SheetButtonKind.Primary, Submit, closes: false);
        cancel = sheet.Add("Cancel", SheetButtonKind.Close);
        sheet.Closed += Closed;
        Build();
        ShowKind();
        body.Loaded += (_, _) => Paint();
        LoadConvertible();
    }

    // ---- the dialog ----

    private static string Label(string t) => t switch
    {
        "online_text_entry" => "Text",
        "online_url" => "Website",
        "online_upload" => "Files",
        _ => t,
    };

    private Color Accent => K.Hex(a.Color, Appearance.Accent);

    private void Build()
    {
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(lineCol);
        body.ColumnDefinitions.Add(paneCol);

        // the head: Hand In (Again), the assignment and its attempts
        var subtitle = string.Join(" · ", new[] { a.Title, a.AttemptsText ?? "" }.Where(s => s.Length > 0));
        var head = K.Columns("auto, *",
            K.IconTile(SymbolRegular.ArrowUpload24, Accent, 36).Margin(0, 0, 12, 0).Col(0),
            K.V(2, K.Text(a.Resubmit == true ? "Hand In Again" : "Hand In", Ty.Subtitle), K.Text(subtitle, Ty.Body, B.Text2)).Align(HorizontalAlignment.Stretch, VerticalAlignment.Center).Col(1));
        head.Margin = new Thickness(24, 22, 24, 10);

        var form = new Stack { Orientation = Orientation.Vertical, Spacing = 18 };
        if (a.Types.Count > 1)
        {
            kinds = new SelectorBar(a.Types.Select(Label).ToList(), Math.Max(0, a.Types.IndexOf(type)));
            kinds.Changed += i => SetType(a.Types[i]);
            System.Windows.Automation.AutomationProperties.SetName(kinds, "Hand in as");
            form.Children.Add(K.V(4, K.Text("Hand in as", Ty.Caption, B.Text2), kinds.Margin(-10, 0, 0, 0)));
        }
        form.Children.Add(kindHost);
        form.Children.Add(K.V(6, K.Text("Comment (optional)", Ty.BodyStrong), note));
        form.Children.Add(errorRow);
        var scroll = new ScrollViewer
        {
            Content = form, Padding = new Thickness(24, 4, 24, 12), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var status = K.H(10, busy, hint);
        status.Margin = new Thickness(24, 0, 24, 14);
        var left = K.Rows("auto, *, auto", head.Row(0), scroll.Row(1), status.Row(2));
        body.Children.Add(left.Col(0));
        var line = new Border { Width = 1 }.Bg(B.Divider);
        body.Children.Add(line.Col(1));
        body.Children.Add(pane.Col(2));

        // while files are dragged over the dialog: that they will be added
        overlay.Children.Add(new Rectangle
        {
            RadiusX = 8, RadiusY = 8, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 2.5 }, Margin = new Thickness(8),
            Fill = new SolidColorBrush(K.WithAlpha(Appearance.Accent, 0.07)), Stroke = new SolidColorBrush(Appearance.Accent),
        });
        var label = new Border
        {
            Child = K.H(8, K.Icon(SymbolRegular.DocumentArrowDown24, 18, B.Text, filled: true),
                K.Text(a.Types.Contains("online_upload") ? "Drop to Add Files" : "Drop to Add the Link", Ty.BodyStrong)),
            Padding = new Thickness(18, 10, 18, 10), CornerRadius = new CornerRadius(20), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Effect = K.Shadow(20, 4, 0.18),
        };
        label.Bg(B.Flyout).Stroke(B.FlyoutStroke);
        overlay.Children.Add(label);
        body.Children.Add(overlay);
        Grid.SetColumnSpan(overlay, 3);
        body.PreviewDragEnter += DragOver;
        body.PreviewDragOver += DragOver;
        body.PreviewDragLeave += (_, e) =>
        {
            var p = e.GetPosition(body);
            if (p.X > 2 && p.Y > 2 && p.X < body.ActualWidth - 2 && p.Y < body.ActualHeight - 2) return;
            overlay.Visibility = Visibility.Collapsed;
        };
        body.PreviewDrop += (_, e) =>
        {
            overlay.Visibility = Visibility.Collapsed;
            if (sending || !TakesDrops) return;
            var (paths, links) = AssignmentScreen.Dropped(e.Data);
            if (paths.Count == 0 && links.Count == 0) return;
            e.Handled = true;
            Take(paths, links);
        };
    }

    private bool TakesDrops => a.Types.Contains("online_upload") || a.Types.Contains("online_url");

    private void DragOver(object sender, DragEventArgs e)
    {
        if (sending || !TakesDrops) return;
        var (paths, links) = AssignmentScreen.Dropped(e.Data);
        if (paths.Count == 0 && links.Count == 0) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        if (overlay.Visibility != Visibility.Visible)
        {
            overlay.Visibility = Visibility.Visible;
            Motion.FadeIn(overlay, Motion.Fast);
        }
    }

    /// <summary>The fields of the kind of hand-in chosen (they swap in place).</summary>
    private void ShowKind()
    {
        foreach (var e in new FrameworkElement[] { text, url, filesSection }) AssignKit.Detach(e);
        FrameworkElement? content = type switch
        {
            "online_text_entry" => K.V(6, K.Text("Your text", Ty.BodyStrong), text),
            "online_url" => K.V(6, K.Text("Website address", Ty.BodyStrong), url),
            "online_upload" => filesSection,
            _ => null,
        };
        kindHost.Content = content;
        if (content != null && kindShown) Motion.Enter(content, 0, 8, 250); // (the kind's own fields swap in place)
        kindShown = true;
        Paint();
    }

    private void SetType(string t)
    {
        if (t == type) return;
        type = t;
        if (kinds != null && a.Types.IndexOf(t) is var i and >= 0 && kinds.Selected != i) kinds.Select(i, false);
        ShowKind();
    }

    private FrameworkElement FilesSection()
    {
        var choose = K.Button("Choose Files…", ChooseFiles, icon: SymbolRegular.Folder24);
        var pictures = K.Button("Choose Pictures…", ChoosePictures, icon: SymbolRegular.ImageMultiple24);
        pictures.ToolTip = "Pictures and videos from your Pictures folder";
        return K.V(8, K.Text("Files", Ty.BodyStrong), filesList, K.H(10, choose, pictures).Margin(0, 4, 0, 0), filesFoot);
    }

    /// <summary>What the dialog says now: the files, the line under them, whether Submit can be pressed, the error, the
    /// progress or the hint.</summary>
    private void Paint()
    {
        filesList.Children.Clear();
        if (files.Count == 0) filesList.Children.Add(DropZone());
        foreach (var f in files) filesList.Children.Add(FileRow(f));
        foreach (var n in converting) filesList.Children.Add(K.H(10, K.Spinner(16), K.Text($"Converting {n}…", Ty.Body, B.Text2)).Margin(4, 6, 0, 6));
        var allowed = a.Allowed ?? new List<string>();
        filesFoot.Text = allowed.Count > 0 ? $"This assignment takes {AllowedList} files." + (convertible.Line.Length == 0 ? "" : $" {convertible.Line}.") : "";
        filesFoot.Visibility = allowed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        submit.Enabled = Ready && !sending;
        cancel.Enabled = !sending;
        sheet.Modal = sending;
        foreach (var e in new UIElement[] { text, url, note, filesSection }) e.IsEnabled = !sending;
        if (kinds != null) kinds.IsEnabled = !sending;
        busy.Visibility = sending ? Visibility.Visible : Visibility.Collapsed;
        hint.Text = sending ? ProgressWord : type == "online_text_entry" ? "Ctrl+Enter to submit" : "";
        hint.Fg(sending ? B.Text2 : B.Text3);
    }

    private void Fail(string? message)
    {
        error.Text = message ?? "";
        errorRow.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>A press on it opens the Open dialog, as Choose Files… does.</summary>
    private FrameworkElement DropZone()
    {
        var words = K.V(6,
            K.Icon(SymbolRegular.DocumentArrowDown24, 26, B.Text2).Align(HorizontalAlignment.Center),
            K.Text("Drop files here", Ty.BodyStrong).Center(),
            K.Text("or click to choose them", Ty.Caption, B.Text2).Center());
        var dash = new Rectangle { RadiusX = 8, RadiusY = 8, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 3.4, 2.6 } };
        dash.SetResourceReference(Shape.StrokeProperty, B.StrongStroke);
        var g = new Grid { Children = { dash, words.Margin(0, 18, 0, 18) } };
        var b = K.Subtle(g, ChooseFiles, new Thickness(0), 8);
        b.ToolTip = "Choose files to hand in";
        System.Windows.Automation.AutomationProperties.SetName(b, "Drop files here, or choose them");
        return b;
    }

    /// <summary>A file to hand in: a press shows it in the pane at the dialog's right (again: puts it away); × takes it out.</summary>
    private FrameworkElement FileRow(Picked f)
    {
        var open = showing == f.Id;
        var sub = K.Text(open ? $"{f.SizeText} · Previewing" : $"{f.SizeText} · Click to preview", Ty.Caption, open ? B.AccentText : B.Text2);
        var words = K.V(1, K.Text(f.Name, Ty.Body), sub);
        words.VerticalAlignment = VerticalAlignment.Center;
        var eye = K.Icon(SymbolRegular.Eye24, 16, open ? B.AccentText : B.Text2, filled: open);
        var row = K.Columns("auto, *, auto", AssignKit.FileIcon(f.Name, 28).Margin(0, 0, 10, 0).Col(0), words.Col(1), eye.Margin(8, 0, 4, 0).Col(2));
        var press = K.Subtle(row, () => Toggle(f), new Thickness(6, 5, 6, 5), 6);
        press.ToolTip = open ? "Close the preview" : $"Preview {f.Name}";
        System.Windows.Automation.AutomationProperties.SetHelpText(press, open ? "Closes its preview" : "Shows a preview in this dialog");
        var remove = K.IconButton(SymbolRegular.DismissCircle24, () => Remove(f), $"Remove {f.Name}", 16, 30);
        var g = K.Columns("*, auto", press.Col(0), remove.Margin(4, 0, 0, 0).Col(1));
        var menu = new ContextMenu();
        menu.Items.Add(AssignKit.Item(open ? "Close Preview" : "Preview", SymbolRegular.Eye24, () => Toggle(f)));
        menu.Items.Add(AssignKit.Item("Remove", SymbolRegular.Delete24, () => Remove(f)));
        g.ContextMenu = menu;
        return g;
    }

    // ---- what it knows ----

    private string ProgressWord => type != "online_upload" ? "Handing in…" : files.Count == 1 ? "Uploading 1 file…" : $"Uploading {files.Count} files…";

    private bool Ready => type switch
    {
        "online_text_entry" => !string.IsNullOrWhiteSpace(text.Text),
        "online_url" => !string.IsNullOrWhiteSpace(url.Text),
        "online_upload" => files.Count > 0,
        _ => false,
    };

    private string AllowedList => string.Join(", ", (a.Allowed ?? new List<string>()).Select(x => "." + x));

    private bool AllowedName(string name)
    {
        if (a.Allowed is not { Count: > 0 } allowed) return true;
        return allowed.Contains(System.IO.Path.GetExtension(name).TrimStart('.').ToLowerInvariant());
    }

    /// <summary>What else the hand-in takes, converted (the web's converter): the extensions and the line saying so.</summary>
    private async void LoadConvertible()
    {
        if (a.Allowed is not { Count: > 0 } allowed) return;
        try
        {
            convertible = await engine.Call<ConvertInfo>("convertInfo", new { allowed });
            Paint();
        }
        catch { }
    }

    // ---- taking files ----

    /// <summary>What was dropped (on the dialog, or on the page before it): files to hand in, or a web address for a website.</summary>
    private void Take(IReadOnlyList<string> paths, IReadOnlyList<string> links)
    {
        if (paths.Count > 0 && a.Types.Contains("online_upload"))
        {
            SetType("online_upload");
            foreach (var p in paths) AddPath(p);
            return;
        }
        if (links.Count > 0 && a.Types.Contains("online_url"))
        {
            SetType("online_url");
            url.Text = links[0];
            Fail(null);
            return;
        }
        if (paths.Count > 0 || links.Count > 0) Fail(paths.Count == 0 ? "This assignment doesn’t take a web address." : "This assignment doesn’t take files.");
    }

    /// <summary>A file on this PC, read to be handed in (not a folder, and not over the size the page can take).</summary>
    private void AddPath(string path)
    {
        var name = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        if (Directory.Exists(path)) { Fail($"{name} is a folder. Choose the files in it."); return; }
        try
        {
            var info = new FileInfo(path);
            if (info.Length > Limit) { Fail($"{name} is larger than 50 MB. Hand it in on {engine.LmsName}’s own page."); return; }
            Add(new Picked(Guid.NewGuid(), name, AssignKit.Mime(name), File.ReadAllBytes(path)));
        }
        catch { Fail($"{name} could not be read."); }
    }

    private void Add(Picked f)
    {
        if (f.Data.LongLength > Limit) { Fail($"{f.Name} is larger than 50 MB. Hand it in on {engine.LmsName}’s own page."); return; }
        if (!AllowedName(f.Name)) { Convert(f); return; }
        Fail(null);
        if (files.Any(x => x.Name == f.Name && x.Data.LongLength == f.Data.LongLength)) return; // (dropped twice)
        files.Add(f);
        Paint();
        OpenPreview(f); // (a file added is looked at straight away — the last one added, where several come at once)
    }

    /// <summary>A file of a type the assignment doesn't take: converted to one it does, as the web hand-in does (Word,
    /// text and pictures to PDF, and so on); said plainly when it can't be.</summary>
    private async void Convert(Picked f)
    {
        if (a.Allowed is not { } allowed || converting.Contains(f.Name)) return;
        Fail(null);
        converting.Add(f.Name);
        Paint();
        try
        {
            var r = await engine.Call<ConvertedFile>("convertFile", new { name = f.Name, type = f.Type, data = System.Convert.ToBase64String(f.Data), allowed });
            converting.Remove(f.Name);
            if (!r.IsConverted || r.Name == null || r.Data == null) Fail($"{f.Name} can’t be turned into {AllowedList} here. This assignment takes {AllowedList} files.");
            else Add(new Picked(Guid.NewGuid(), r.Name, r.Type ?? "application/octet-stream", System.Convert.FromBase64String(r.Data)));
        }
        catch (Exception e)
        {
            converting.Remove(f.Name);
            Fail($"{f.Name} could not be converted: {e.Message}");
        }
        Paint();
    }

    private void Remove(Picked f)
    {
        if (showing == f.Id) ClosePreview();
        files.RemoveAll(x => x.Id == f.Id);
        Paint();
    }

    private void ChooseFiles()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Choose files to hand in", CheckFileExists = true };
        if (a.Allowed is { Count: > 0 } allowed)
        {
            var exts = allowed.Concat(convertible.Exts).Select(x => x.ToLowerInvariant().TrimStart('.')).Distinct().ToList();
            dlg.Filter = $"Files this assignment takes ({string.Join(", ", exts.Select(x => "." + x))})|{string.Join(";", exts.Select(x => "*." + x))}";
        }
        if (Ask(dlg) != true) return;
        if (!a.Types.Contains("online_upload")) return;
        SetType("online_upload");
        foreach (var p in dlg.FileNames) AddPath(p);
    }

    private static bool? Ask(Microsoft.Win32.OpenFileDialog dlg) =>
        Application.Current?.MainWindow is { } owner ? dlg.ShowDialog(owner) : dlg.ShowDialog();

    /// <summary>Pictures and videos from the Pictures folder (the Mac's Choose from Photos…).</summary>
    private void ChoosePictures()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true, Title = "Choose pictures to hand in", CheckFileExists = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Filter = "Pictures and videos|*.jpg;*.jpeg;*.png;*.gif;*.heic;*.heif;*.webp;*.bmp;*.tif;*.tiff;*.mp4;*.mov;*.m4v;*.avi;*.wmv",
        };
        if (Ask(dlg) != true) return;
        SetType("online_upload");
        foreach (var p in dlg.FileNames) AddPath(p);
    }

    // ---- the preview pane ----

    /// <summary>A file's row pressed: its preview opened in the dialog, or — the one already open — put away.</summary>
    private void Toggle(Picked f)
    {
        if (showing == f.Id) ClosePreview();
        else OpenPreview(f);
    }

    /// <summary>A file's preview opened at the dialog's right, the dialog widening for it.</summary>
    private void OpenPreview(Picked f)
    {
        if (showing == f.Id) return;
        if (Write(f) is not { } path) return;
        Fit();
        showing = f.Id;
        pane.Children.Clear();
        pane.RowDefinitions.Clear();
        pane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        pane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        pane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var close = K.IconButton(SymbolRegular.Dismiss24, ClosePreview, "Close the preview", 14, 30);
        var top = K.Columns("auto, *, auto", K.Icon(SymbolRegular.Eye24, 16, B.Text2).Margin(0, 0, 8, 0).Col(0), K.Text(f.Name, Ty.BodyStrong).Col(1), close.Col(2));
        top.Margin = new Thickness(16, 10, 10, 10);
        pane.Children.Add(top.Row(0));
        pane.Children.Add(new Border().Bg(B.Divider).Row(1));
        var shown = AssignKit.Preview(path, f.Name, owned);
        var ground = new Border { Child = shown };
        ground.Bg(B.Layer);
        pane.Children.Add(ground.Row(2));
        var wasOpen = pane.Visibility == Visibility.Visible;
        pane.Visibility = Visibility.Visible;
        lineCol.Width = new GridLength(1);
        paneCol.Width = new GridLength(paneWidth);
        Widen(formWidth + 1 + paneWidth);
        if (!wasOpen) Motion.SlideIn(pane, true, 40);
        Paint();
    }

    private void ClosePreview()
    {
        if (showing == null) return;
        showing = null;
        pane.Visibility = Visibility.Collapsed;
        pane.Children.Clear();
        lineCol.Width = new GridLength(0);
        paneCol.Width = new GridLength(0);
        Release();
        formWidth = FormWidth;
        Widen(FormWidth);
        Paint();
    }

    /// <summary>The dialog made as wide as the form and the pane together, kept inside the window.</summary>
    private void Widen(double width)
    {
        if (sheet.Card is not { } card) return;
        if (sheet.Host is { ActualWidth: > 0 } host) card.MaxWidth = Math.Max(280, host.ActualWidth - 48);
        Motion.To(card, FrameworkElement.WidthProperty, width, Motion.Slow + 67, Motion.Decelerate);
        Shot.Moved();
    }

    /// <summary>The widths for the window: the pane up to 540, the form 580 — the form giving up to 120 of its own before
    /// the pane goes below 380 — together never wider than the window, less a margin.</summary>
    private void Fit()
    {
        var hostWidth = sheet.Host is { ActualWidth: > 0 } h ? h.ActualWidth : Application.Current?.MainWindow?.ActualWidth ?? 1400;
        var room = Math.Max(700, hostWidth - 48);
        var form = FormWidth;
        var p = Math.Min(540, room - form);
        if (p < 380)
        {
            form = Math.Max(FormWidth - 120, room - 380);
            p = room - form;
        }
        formWidth = form;
        paneWidth = Math.Max(300, p);
    }

    /// <summary>The file written where the preview can read it, under its own name (a folder of its own each time).</summary>
    private string? Write(Picked f)
    {
        Release();
        try
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"SimplHandInPreview-{f.Id}");
            Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, AssignKit.Clean(f.Name));
            File.WriteAllBytes(path, f.Data);
            folder = dir;
            return path;
        }
        catch { return null; }
    }

    /// <summary>The preview's web views let go of, and its folder cleaned away.</summary>
    private void Release()
    {
        foreach (var w in owned) { try { w.Dispose(); } catch { } }
        owned.Clear();
        if (folder != null)
        {
            var f = folder;
            folder = null;
            // (a moment for the web view to let go of the file)
            _ = Task.Delay(800).ContinueWith(_ => { try { Directory.Delete(f, true); } catch { } });
        }
    }

    // ---- sending ----

    private async void Submit()
    {
        if (!Ready || sending) return;
        sending = true;
        Fail(null);
        Paint();
        object args = type switch
        {
            "online_text_entry" => new { course = a.Course, id = a.Id, type, comment = note.Text, text = text.Text },
            "online_url" => new { course = a.Course, id = a.Id, type, comment = note.Text, url = url.Text.Trim() },
            _ => new { course = a.Course, id = a.Id, type, comment = note.Text, files = files.Select(f => new { name = f.Name, type = f.Type, data = System.Convert.ToBase64String(f.Data) }).ToArray() },
        };
        try
        {
            await engine.Call<OK>("submit", args);
            sending = false;
            sheet.Close();
            done(); // (after the dialog goes: the assignment shows the work arriving)
            return;
        }
        catch (Exception e) { Fail(e.Message); }
        sending = false;
        Paint();
    }

    private void Closed()
    {
        Open = false;
        showing = null;
        Release();
    }
}
