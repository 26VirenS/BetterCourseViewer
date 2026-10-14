using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// The assignment's small pieces (the Mac's Components, Rows and FileViews, as an assignment uses them): words that can be
/// selected, a file's icon as File Explorer draws it, a file's menu of ways out, a file of the school's fetched with the
/// session, the words of a status in its colour.
/// </summary>
internal static class AssignKit
{
    // ---- words ----

    /// <summary>Words that can be selected and copied (the Mac's textSelection): a read-only box drawn as plain text,
    /// the page's scroll wheel passing over it.</summary>
    public static System.Windows.Controls.TextBox Selectable(string text, Ty ty = Ty.Body, string brush = B.Text, int maxLines = 0)
    {
        var (size, weight, face, _) = K.Ramp(ty);
        var t = new System.Windows.Controls.TextBox
        {
            Text = text, IsReadOnly = true, IsUndoEnabled = false, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, Padding = new Thickness(0), Margin = new Thickness(-2, 0, -2, 0), FontSize = size, FontWeight = weight,
            FontFamily = face, FocusVisualStyle = null, IsTabStop = false, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        t.Style = new Style(typeof(System.Windows.Controls.TextBox)); // (the plain box: no field drawn round it)
        if (maxLines > 0) t.MaxLines = maxLines;
        t.SetResourceReference(Control.ForegroundProperty, brush);
        t.SetResourceReference(TextBoxBase.SelectionBrushProperty, B.Accent);
        t.PreviewMouseWheel += (_, e) =>
        {
            e.Handled = true;
            var up = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = t };
            (VisualTreeHelper.GetParent(t) as UIElement ?? t.Parent as UIElement)?.RaiseEvent(up);
        };
        return t;
    }

    /// <summary>A status's colour by the page's word for it (bad, warn, good, info, purple; else grey) — the Mac's StatusChip.</summary>
    public static Color StatusTone(string? kind) => kind switch
    {
        "bad" => K.Tone("red"),
        "warn" => K.Tone("orange"),
        "good" => K.Tone("green"),
        "info" => K.Tone("blue"),
        "purple" => K.Tone("purple"),
        _ => K.Tone(null),
    };

    public static string Num(double v)
    {
        if (!double.IsFinite(v)) return "—";
        if (v == Math.Round(v) && Math.Abs(v) < 1e15) return ((long)v).ToString(CultureInfo.InvariantCulture);
        return v.ToString("G6", CultureInfo.InvariantCulture);
    }

    public static string SizeText(long n) => n < 1024 ? $"{n} bytes" : n < 1024 * 1024 ? $"{Math.Round(n / 1024.0)} KB" : $"{n / 1048576.0:0.#} MB";

    public static void Copy(string text)
    {
        try { Clipboard.SetText(text); } catch { /* the clipboard busy: nothing copied */ }
    }

    /// <summary>A kit element taken from where it was shown, to be shown again in a page built anew (one parent at a time).</summary>
    public static void Detach(FrameworkElement e)
    {
        switch (e.Parent)
        {
            case Panel p: p.Children.Remove(e); break;
            case Decorator d: d.Child = null; break;
            case ContentControl c: c.Content = null; break;
        }
    }

    public static MenuItem Item(string text, SymbolRegular icon, Action act, bool enabled = true)
    {
        var mi = new MenuItem { Header = text, Icon = new SymbolIcon { Symbol = icon }, IsEnabled = enabled };
        mi.Click += (_, _) => act();
        return mi;
    }

    // ---- files ----

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr Icon;
        public int Index;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryString(uint flags, uint str, string assoc, string? extra, StringBuilder? output, ref uint size);

    private static readonly Dictionary<string, ImageSource?> Icons = new();

    /// <summary>A kind of file's icon as File Explorer draws it (by its name's extension), or null.</summary>
    private static ImageSource? ShellIcon(string name, bool large)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        var key = ext + (large ? ":l" : ":s");
        if (Icons.TryGetValue(key, out var kept)) return kept;
        ImageSource? src = null;
        try
        {
            var info = new ShFileInfo();
            const uint icon = 0x100, small = 0x1, useAttributes = 0x10, normal = 0x80;
            var r = SHGetFileInfo("file" + (ext.Length > 0 ? ext : ".bin"), normal, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), icon | useAttributes | (large ? 0 : small));
            if (r != IntPtr.Zero && info.Icon != IntPtr.Zero)
            {
                try
                {
                    var b = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    b.Freeze();
                    src = b;
                }
                finally { DestroyIcon(info.Icon); }
            }
        }
        catch { src = null; }
        Icons[key] = src;
        return src;
    }

    /// <summary>A file's icon as File Explorer draws it, by the kind its name says it is (a symbol of its kind where
    /// Windows has none to give).</summary>
    public static FrameworkElement FileIcon(string name, double size)
    {
        if (ShellIcon(name, size > 20) is { } src)
            return new System.Windows.Controls.Image { Source = src, Width = size, Height = size, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true };
        var (sym, tone) = Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".pdf" => (SymbolRegular.DocumentPdf24, "red"),
            ".doc" or ".docx" or ".odt" or ".rtf" or ".pages" => (SymbolRegular.DocumentText24, "blue"),
            ".xls" or ".xlsx" or ".csv" or ".ods" or ".numbers" => (SymbolRegular.DocumentTable24, "green"),
            ".ppt" or ".pptx" or ".odp" or ".key" => (SymbolRegular.SlideText24, "orange"),
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".heic" or ".webp" or ".bmp" or ".svg" => (SymbolRegular.Image24, "purple"),
            ".mp4" or ".mov" or ".avi" or ".webm" or ".mkv" => (SymbolRegular.Video24, "purple"),
            ".mp3" or ".wav" or ".m4a" or ".aac" or ".ogg" => (SymbolRegular.MusicNote224, "orange"),
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => (SymbolRegular.Archive24, "gray"),
            ".py" or ".java" or ".c" or ".cpp" or ".cs" or ".js" or ".ts" or ".html" or ".css" or ".json" or ".ipynb" => (SymbolRegular.Code24, "blue"),
            _ => (SymbolRegular.Document24, "gray"),
        };
        return K.IconTile(sym, K.Tone(tone), size);
    }

    /// <summary>The app Windows opens a kind of file in, by name ("Microsoft Edge"), or null.</summary>
    public static string? AppFor(string name)
    {
        var ext = Path.GetExtension(name);
        if (ext.Length == 0) return null;
        try
        {
            uint size = 0;
            const uint friendlyAppName = 4, noTruncate = 0x20;
            AssocQueryString(noTruncate, friendlyAppName, ext, null, null, ref size);
            if (size == 0) return null;
            var sb = new StringBuilder((int)size);
            if (AssocQueryString(noTruncate, friendlyAppName, ext, null, sb, ref size) != 0) return null;
            var s = sb.ToString().Trim();
            return s.Length == 0 ? null : s;
        }
        catch { return null; }
    }

    /// <summary>"Open in Microsoft Edge", or "Open in default app" when Windows names none.</summary>
    public static string OpenTitle(string name) => AppFor(name) is { } app ? $"Open in {app}" : "Open in default app";

    /// <summary>A file of the school's opened (fetched with the session, kept in Downloads, opened in its app).</summary>
    public static void OpenFile(Engine.Engine engine, string url, string name)
    {
        if (engine.Absolute(url) is { } u) Simpl.Engine.FilePreview.Open(u, name, engine.Web);
    }

    /// <summary>A copy of a file of the school's kept in Downloads, shown in File Explorer.</summary>
    public static async void Download(Engine.Engine engine, string url, string name)
    {
        try
        {
            var path = await Fetch(engine, url, name, Simpl.Engine.FilePreview.Downloads);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception e) { Shell.Dialogs.Fail($"{name} could not be downloaded", e.Message); }
    }

    /// <summary>The menu of a file's ways out: its app, a copy in Downloads, its link (the Mac's FileMenuItems).</summary>
    public static ContextMenu FileMenu(Engine.Engine engine, string url, string name)
    {
        var m = new ContextMenu();
        m.Items.Add(Item(OpenTitle(name), SymbolRegular.Open24, () => OpenFile(engine, url, name)));
        m.Items.Add(Item("Download", SymbolRegular.ArrowDownload24, () => Download(engine, url, name)));
        m.Items.Add(new Separator());
        m.Items.Add(Item("Copy link", SymbolRegular.Link24, () => { if (engine.Absolute(url) is { } u) Copy(u.ToString()); }));
        return m;
    }

    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true, UseCookies = false, AutomaticDecompression = DecompressionMethods.All });

    /// <summary>A file of the school's fetched with the signed-in session (the engine's cookies) into a folder: its path.</summary>
    public static async Task<string> Fetch(Engine.Engine engine, string url, string name, string folder)
    {
        var u = engine.Absolute(url) ?? throw new Simpl.Engine.EngineException("This file has no address.");
        var req = new HttpRequestMessage(HttpMethod.Get, u);
        if (engine.Web.CoreWebView2 is { } core)
        {
            var cookies = await core.CookieManager.GetCookiesAsync(u.ToString());
            if (cookies.Count > 0) req.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}")));
            req.Headers.TryAddWithoutValidation("User-Agent", core.Settings.UserAgent);
        }
        Shot.Began();
        try
        {
            using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!res.IsSuccessStatusCode) throw new Exception($"The school answered {(int)res.StatusCode} {res.ReasonPhrase}.");
            var fileName = res.Content.Headers.ContentDisposition?.FileNameStar ?? res.Content.Headers.ContentDisposition?.FileName?.Trim('"');
            if (string.IsNullOrWhiteSpace(fileName)) fileName = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(u.AbsolutePath) : name;
            Directory.CreateDirectory(folder);
            var path = Unique(Path.Combine(folder, Clean(fileName)));
            await using (var f = File.Create(path)) await res.Content.CopyToAsync(f);
            return path;
        }
        finally { Shot.Ended(); }
    }

    public static string Clean(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "File" : name;
    }

    private static string Unique(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var p = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(p)) return p;
        }
    }

    /// <summary>A file's kind as the web names it (application/pdf), by its extension: Windows' own list, then a few known.</summary>
    public static string Mime(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (ext.Length == 0) return "application/octet-stream";
        try
        {
            if (Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(ext)?.GetValue("Content Type") is string t && t.Length > 0) return t;
        }
        catch { }
        return ext switch
        {
            ".pdf" => "application/pdf", ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".txt" => "text/plain", ".csv" => "text/csv", ".html" or ".htm" => "text/html", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif", ".heic" => "image/heic", ".mp4" => "video/mp4", ".mov" => "video/quicktime", ".mp3" => "audio/mpeg",
            ".zip" => "application/zip", ".json" => "application/json",
            _ => "application/octet-stream",
        };
    }

    // ---- the school's pages and a file's preview, in a web view with the session ----

    /// <summary>A web view on the app's own web data (the school's session), started and sent to an address.</summary>
    public static WebView2 Web(Uri url, Func<CoreWebView2, Task>? setup = null)
    {
        var web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Transparent };
        web.Loaded += async (_, _) =>
        {
            if (web.CoreWebView2 != null) return;
            try
            {
                var env = await Simpl.Engine.Engine.WebEnvironment();
                await web.EnsureCoreWebView2Async(env);
                if (web.CoreWebView2 is not { } core) return; // (let go of while it started)
                core.Settings.IsPasswordAutosaveEnabled = false;
                core.Settings.IsGeneralAutofillEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.DownloadStarting += (_, e) => { e.Handled = true; Simpl.Engine.FilePreview.Downloading(e); };
                if (setup != null) await setup(core);
                core.Navigate(url.ToString());
            }
            catch (Exception e) { Log.Write("assignment web view: " + e.Message); }
        };
        return web;
    }

    private static readonly HashSet<string> Pictures = new() { ".png", ".jpg", ".jpeg", ".jfif", ".gif", ".bmp", ".tif", ".tiff", ".ico" };
    private static readonly HashSet<string> WebKinds = new() { ".pdf", ".svg", ".webp", ".avif", ".mp4", ".webm", ".m4v", ".mp3", ".wav", ".m4a", ".ogg", ".oga", ".html", ".htm" };
    private static readonly HashSet<string> Plain = new()
    {
        ".txt", ".md", ".csv", ".tsv", ".json", ".xml", ".log", ".py", ".java", ".c", ".h", ".cpp", ".hpp", ".cs", ".js", ".ts", ".css", ".tex",
        ".r", ".m", ".swift", ".kt", ".go", ".rb", ".sql", ".yaml", ".yml", ".ini", ".sh", ".bat", ".ps1",
    };

    /// <summary>
    /// A file on this PC looked at in a page (the Mac's Quick Look view): a picture as itself, text as text, a PDF, a film,
    /// a sound or a web page in a web view; any other kind its icon and Open (in its own app). Web views made for it are
    /// added to <paramref name="owned"/>, to be let go of with the page.
    /// </summary>
    public static FrameworkElement Preview(string path, string name, List<WebView2> owned)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (Pictures.Contains(ext))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                return new System.Windows.Controls.Image { Source = bmp, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(16) };
            }
            if (Plain.Contains(ext))
            {
                var text = ReadSome(path);
                var box = Selectable(text, Ty.Body);
                box.FontFamily = new FontFamily("Cascadia Mono, Consolas");
                box.FontSize = 13;
                box.TextWrapping = TextWrapping.Wrap;
                return new ScrollViewer { Content = box, Padding = new Thickness(18, 14, 18, 14), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            }
            if (WebKinds.Contains(ext))
            {
                var web = Web(new Uri(path));
                owned.Add(web);
                return web;
            }
        }
        catch (Exception e) { Log.Write("preview: " + e.Message); }
        var open = K.Button(OpenTitle(name), () => { try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { } }, icon: SymbolRegular.Open24);
        open.HorizontalAlignment = HorizontalAlignment.Center;
        var v = K.V(10, FileIcon(name, 48).Align(HorizontalAlignment.Center), K.Wrapped(name, Ty.BodyStrong).Center(),
            K.Wrapped("There is no preview for this kind of file here.", Ty.Body, B.Text2).Center(), open.Margin(0, 6, 0, 0));
        v.VerticalAlignment = VerticalAlignment.Center;
        v.HorizontalAlignment = HorizontalAlignment.Center;
        v.MaxWidth = 320;
        return new Grid { Children = { v } };
    }

    private static string ReadSome(string path)
    {
        using var f = File.OpenRead(path);
        var buf = new byte[Math.Min(f.Length, 512 * 1024)];
        var n = f.Read(buf, 0, buf.Length);
        return Encoding.UTF8.GetString(buf, 0, n);
    }
}

/// <summary>The scroll offset as a property that can be animated (a page going to one of its parts).</summary>
internal static class AssignScroll
{
    public static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached("Offset", typeof(double), typeof(AssignScroll),
        new PropertyMetadata(0.0, (o, e) => (o as ScrollViewer)?.ScrollToVerticalOffset((double)e.NewValue)));

    /// <summary>A scroller brought to an offset (gently, as Windows scrolls to something asked for).</summary>
    public static void To(ScrollViewer s, double offset)
    {
        var to = Math.Max(0, Math.Min(offset, s.ScrollableHeight));
        Motion.FromTo(s, OffsetProperty, s.VerticalOffset, to, Motion.Slow + 67, Motion.Decelerate);
    }
}

/// <summary>An assignment's next step, as its button says it (the Mac's AssignStep): the words, the symbol, the tooltip; the
/// shorter words for beside another step; and a quiz's own kinds of step (its feedback, a new attempt).</summary>
internal sealed record AssignStep(string Title, SymbolRegular Symbol, string Help, string? Short = null, bool Feedback = false, bool Again = false, bool Pending = false, bool Off = false);

/// <summary>The first of its children that fits the width it is given (the Mac's ViewThatFits): a step's words, then its
/// shorter words, then those without the symbol.</summary>
internal sealed class AssignFit : Panel
{
    private int chosen;

    protected override Size MeasureOverride(Size available)
    {
        chosen = Math.Max(0, InternalChildren.Count - 1);
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var c = InternalChildren[i];
            c.Measure(new Size(double.PositiveInfinity, available.Height));
            if (c.DesiredSize.Width <= available.Width + 0.5) { chosen = i; break; }
        }
        return InternalChildren.Count == 0 ? new Size() : new Size(Math.Min(available.Width, InternalChildren[chosen].DesiredSize.Width), InternalChildren[chosen].DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var c = InternalChildren[i];
            if (i == chosen)
            {
                c.Opacity = 1;
                c.Arrange(new Rect(new Point((final.Width - c.DesiredSize.Width) / 2, 0), new Size(Math.Min(final.Width, c.DesiredSize.Width), final.Height)));
            }
            else
            {
                c.Opacity = 0;
                c.Arrange(new Rect(0, 0, 0, 0));
            }
        }
        return final;
    }
}

/// <summary>
/// Writing a comment for the teacher, in the comments' own card (the Mac's CommentComposer): the box takes the keys at
/// once, Ctrl+Enter sends, Escape puts it away. It stays until the words are sent, and says so if they could not be.
/// </summary>
internal sealed class AssignComposer : Grid
{
    private readonly Wpf.Ui.Controls.TextBox box;
    private readonly TextBlock error;
    private readonly FrameworkElement errorRow;
    private readonly Wpf.Ui.Controls.Button send, cancel;
    private readonly Func<string, Task> sendWords;
    private bool sending;

    public AssignComposer(Func<string, Task> send, Action cancel)
    {
        sendWords = send;
        box = new Wpf.Ui.Controls.TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 110, VerticalContentAlignment = VerticalAlignment.Top,
            PlaceholderText = "Write a comment for your teacher", VerticalScrollBarVisibility = ScrollBarVisibility.Auto, ClearButtonEnabled = false,
        };
        System.Windows.Automation.AutomationProperties.SetName(box, "Comment");
        box.TextChanged += (_, _) => Paint();
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { e.Handled = true; Go(); }
        };
        error = K.Wrapped("", Ty.Body, B.Critical);
        errorRow = K.Columns("auto, *", K.Icon(SymbolRegular.Warning24, 16, B.Critical, filled: true).Margin(0, 2, 8, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0), error.Col(1));
        errorRow.Visibility = Visibility.Collapsed;
        this.cancel = K.Button("Cancel", cancel);
        this.send = K.Button("Send", Go, accent: true);
        var foot = K.Columns("*, auto, auto",
            K.Text("Ctrl+Enter to send · Esc to cancel", Ty.Caption, B.Text3).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0),
            this.cancel.Margin(8, 0, 0, 0).Col(1), this.send.Margin(8, 0, 0, 0).Col(2));
        Children.Add(K.V(8, box, errorRow, foot));
        Paint();
        // (in the window first, then the box takes the keys)
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => { box.Focus(); Keyboard.Focus(box); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private bool Empty => string.IsNullOrWhiteSpace(box.Text);

    private void Paint()
    {
        send.IsEnabled = !sending && !Empty;
        cancel.IsEnabled = !sending;
        box.IsEnabled = !sending;
        send.Content = sending ? K.Spinner(16) : "Send";
    }

    private async void Go()
    {
        if (Empty || sending) return;
        sending = true;
        errorRow.Visibility = Visibility.Collapsed;
        Paint();
        try { await sendWords(box.Text); }
        catch (Exception e)
        {
            error.Text = e.Message;
            errorRow.Visibility = Visibility.Visible;
        }
        sending = false;
        Paint();
    }
}

/// <summary>
/// How the class did, as the web draws it (the Mac's AssignClassPlot): a box plot on a track from nothing to the points
/// possible — the whiskers from the low to the high, the box from the lower to the upper quartile, a line at the median, a
/// ring at the mean — and your own score as a dot in the course's colour. The pointer over it reads the mark nearest to it
/// (its name and value in a small label over it, the mark itself a little larger).
/// </summary>
internal sealed class AssignClassPlot : FrameworkElement
{
    private readonly ClassStats stats;
    private readonly Color tint;
    private readonly List<(string Name, double Value, string Key)> marks;
    private int lit = -1;

    public AssignClassPlot(ClassStats stats, Color tint)
    {
        this.stats = stats;
        this.tint = tint;
        Height = 24;
        marks = new (string, double?, string)[]
            {
                ("You", stats.Mine, "me"), ("Low", stats.Min ?? 0, "lo"), ("Lower quartile", stats.LowerQ, "q1"), ("Median", stats.Median, "md"),
                ("Upper quartile", stats.UpperQ, "q3"), ("High", stats.Max, "hi"), ("Mean", stats.Mean, "mean"),
            }
            .Where(m => m.Item2 != null).Select(m => (m.Item1, m.Item2!.Value, m.Item3)).ToList();
        System.Windows.Automation.AutomationProperties.SetName(this,
            $"How the class did, out of {Num(OutOf)}: " + string.Join(", ", marks.Select(m => $"{m.Name} {Num(m.Value)}")));
        MouseMove += (_, e) =>
        {
            var x = e.GetPosition(this).X;
            var near = marks.Count == 0 ? -1 : Enumerable.Range(0, marks.Count).OrderBy(i => Math.Abs(X(marks[i].Value) - x)).First();
            if (near != lit) { lit = near; InvalidateVisual(); }
        };
        MouseLeave += (_, _) => { lit = -1; InvalidateVisual(); };
    }

    /// <summary>Out of what: the points possible, or the largest number there is.</summary>
    private double OutOf => stats.Possible is > 0 ? stats.Possible.Value : Math.Max(stats.Max ?? 0, Math.Max(stats.Mine ?? 0, 1));

    private double X(double v) => Math.Min(1, Math.Max(0, v / OutOf)) * ActualWidth;

    private static string Num(double v)
    {
        if (!double.IsFinite(v)) return "—";
        if (v == Math.Round(v) && Math.Abs(v) < 1e15) return ((long)v).ToString(CultureInfo.InvariantCulture);
        return (Math.Round(v * 10, MidpointRounding.AwayFromZero) / 10).ToString("0.#", CultureInfo.InvariantCulture);
    }

    private Color Res(string key) => (TryFindResource(key) as SolidColorBrush)?.Color ?? Colors.Gray;

    private static Brush Solid(Color c, double a)
    {
        var b = K.Brush(Color.FromArgb((byte)(c.A * a), c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        const double cy = 12;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, 24));
        // (a clear fill under it all: the whole strip answers the pointer)
        var ink = Res(B.Text);
        var ink2 = Res(B.Text2);
        var ground = Res(B.Solid);
        var key = lit >= 0 ? marks[lit].Key : null;
        double Dim(string k) => key == null || key == k || (k == "box" && key is "q1" or "q3") ? 1 : 0.45;
        double Grow(string k) => key == k ? 1.35 : 1;
        var lo = stats.Min ?? 0;
        var hi = stats.Max ?? lo;
        // the track, nothing to the points possible
        dc.DrawRoundedRectangle(Solid(ink, 0.08), null, new Rect(0, cy - 1, w, 2), 1, 1);
        // the whiskers, low to high, with their caps
        dc.DrawRectangle(Solid(ink2, 0.55), null, new Rect(X(lo), cy - 1, Math.Max(1, X(hi) - X(lo)), 2));
        foreach (var (v, k) in new[] { (lo, "lo"), (hi, "hi") })
        {
            var h = 10 * Grow(k);
            dc.DrawRoundedRectangle(Solid(ink2, 0.8 * Dim(k)), null, new Rect(X(v) - 1, cy - h / 2, 2, h), 1, 1);
        }
        // the box, lower to upper quartile
        if (stats.LowerQ is { } q1 && stats.UpperQ is { } q3)
        {
            var edge = new Pen(Solid(key is "q1" or "q3" ? ink : ink2, 0.7 * Dim("box")), 1.5);
            edge.Freeze();
            dc.DrawRoundedRectangle(Solid(ink, 0.08 * Dim("box")), edge, new Rect(X(q1), cy - 7, Math.Max(3, X(q3) - X(q1)), 14), 4, 4);
        }
        if (stats.Median is { } md)
        {
            var h = 14 * Grow("md");
            dc.DrawRoundedRectangle(Solid(ink, 0.75 * Dim("md")), null, new Rect(X(md) - 1, cy - h / 2, 2, h), 1, 1);
        }
        if (stats.Mean is { } mean)
        {
            var r = 3.5 * Grow("mean");
            var edge = new Pen(Solid(ink, 0.75 * Dim("mean")), 1.5);
            edge.Freeze();
            dc.DrawEllipse(Solid(ground, 1), edge, new Point(X(mean), cy), r, r);
        }
        if (stats.Mine is { } me)
        {
            var r = 6 * Grow("me");
            var edge = new Pen(Solid(ground, 1), 2);
            edge.Freeze();
            dc.DrawEllipse(Solid(tint, 1), edge, new Point(X(me), cy), r, r);
        }
        if (lit >= 0) Tip(dc, marks[lit], w);
    }

    /// <summary>The mark's name and value over it: near-black with white words by day, near-white with black ones by night.</summary>
    private void Tip(DrawingContext dc, (string Name, double Value, string Key) m, double w)
    {
        var dark = Appearance.Dark;
        var groundC = dark ? Color.FromRgb(240, 240, 240) : Color.FromRgb(31, 31, 31);
        var inkC = dark ? Color.FromRgb(15, 15, 15) : Colors.White;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var name = new FormattedText(m.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(K.SmallFace, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal), 12, Solid(inkC, 0.8), dpi);
        var value = new FormattedText(Num(m.Value), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(K.TextFace, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 13, Solid(inkC, 1), dpi);
        var tw = name.Width + 5 + value.Width + 18;
        var th = Math.Max(name.Height, value.Height) + 8;
        var cx = Math.Min(Math.Max(X(m.Value), tw / 2), w - tw / 2);
        var rect = new Rect(cx - tw / 2, -12 - th / 2, tw, th);
        dc.DrawRoundedRectangle(Solid(groundC, 1), null, rect, 8, 8);
        dc.DrawText(name, new Point(rect.X + 9, rect.Y + (th - name.Height) / 2 + 1));
        dc.DrawText(value, new Point(rect.X + 9 + name.Width + 5, rect.Y + (th - value.Height) / 2));
    }
}

/// <summary>A small scale bounce (the Mac's symbol bounce): up past its size, then settling.</summary>
internal static class AssignBounce
{
    public static void Bounce(FrameworkElement e)
    {
        if (!Motion.On) return;
        var s = new ScaleTransform(1, 1);
        e.RenderTransformOrigin = new Point(0.5, 0.5);
        e.RenderTransform = s;
        var a = new DoubleAnimationUsingKeyFrames { Duration = Motion.Ms(560) };
        foreach (var (ms, v) in new[] { (0.0, 1.0), (140, 1.35), (300, 0.92), (430, 1.06), (560, 1.0) })
            a.KeyFrames.Add(new SplineDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), new KeySpline(0.3, 0, 0.3, 1)));
        s.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }
}
