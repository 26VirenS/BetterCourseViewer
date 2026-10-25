using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Shell;

/// <summary>
/// (0.3.27, the Mac's Developer window) The developer tools, for whoever knows the way in (Support/DevMode): laid out as
/// Settings is — the pages down a pane at the left, the page on the right. Overview (what this copy is, and the way
/// out), Logs (simpl.log and page-errors.log as they are written: found in, the problems alone, followed, copied, saved,
/// cleared), System (Simpl's processor and memory, and WebView2's, a second at a time for two minutes, drawn; threads,
/// handles, .NET's heap, the window's stalls, what is kept on disk), Stats (every call into the page: how many, failed,
/// how long; the screens' switches and builds), Engine (a call made by hand, its answer shown), Updates, Knewton
/// Alta (Save Page Code) and Preferences (every key and its value). One window at a time.
/// </summary>
public sealed class DeveloperWindow : FluentWindow
{
    private static DeveloperWindow? open;

    private readonly Engine.Engine engine;
    private readonly NavList nav = new();
    private readonly ContentControl titleHost = new() { Focusable = false };
    private readonly ContentControl pageHost = new() { Focusable = false };
    private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Monitor monitor = new();
    private string page = "overview";
    private Action? onTick;

    private static readonly (string Key, string Title, SymbolRegular Icon, Color Tint)[] Pages =
    {
        ("overview", "Overview", SymbolRegular.Info24, Color.FromRgb(0x00, 0x78, 0xd4)),
        ("logs", "Logs", SymbolRegular.TextAlignLeft24, Color.FromRgb(0x76, 0x76, 0x7c)),
        ("crashes", "Crashes", SymbolRegular.ErrorCircle24, Color.FromRgb(0xc4, 0x2b, 0x1c)), // (0.3.51)
        ("system", "System", SymbolRegular.Gauge24, Color.FromRgb(0x10, 0x89, 0x3e)),
        ("stats", "Stats", SymbolRegular.DataBarVertical24, Color.FromRgb(0x88, 0x64, 0xd8)),
        ("engine", "Engine", SymbolRegular.Code24, Color.FromRgb(0xe3, 0x6c, 0x0a)),
        ("updates", "Updates", SymbolRegular.ArrowSync24, Color.FromRgb(0x00, 0x99, 0xbc)),
        ("popups", "Knewton Alta", SymbolRegular.Library24, Color.FromRgb(0xd1, 0x34, 0x3b)),
        ("prefs", "Preferences", SymbolRegular.List24, Color.FromRgb(0xd1, 0x34, 0x8b)),
    };

    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static void Show(Window owner, Engine.Engine engine)
    {
        if (!DevMode.On) return;
        if (open != null)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }
        open = new DeveloperWindow(owner, engine);
        open.Show();
    }

    /// <summary>The developer tools turned off: the window goes with them.</summary>
    public static void CloseOpen() => open?.Close();

    private DeveloperWindow(Window owner, Engine.Engine engine)
    {
        this.engine = engine;
        Owner = owner;
        Title = "Developer";
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/Simpl-256.png"));
        WindowBackdropType = Appearance.Backdrop;
        ExtendsContentIntoTitleBar = true;
        WindowCornerPreference = WindowCornerPreference.Round;
        FontFamily = K.TextFace;
        SetResourceReference(ForegroundProperty, B.Text);
        if (Fx.Opaque) SetResourceReference(BackgroundProperty, B.Window);
        Placement.Restore(this, "developer", 1080, 760, 720, 480);
        if (Prefs.String("window:developer") == null) WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(new TitleBar
        {
            Title = "Developer", Height = 48, Margin = new Thickness(8, 0, 0, 0),
            Icon = new ImageIcon { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/Simpl-256.png")), Width = 16, Height = 16 },
        });
        var head = K.V(2, K.Text("Developer tools", Ty.BodyStrong), K.Text($"Simpl {Updates.Version}", Ty.Caption, B.Text2)).Margin(20, 8, 16, 16);
        var pane = K.Rows("auto, *", head.Row(0), new ScrollViewer { Content = nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }.Row(1));
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(248) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(pane.Col(0));
        var right = K.Rows("auto, *", titleHost.Margin(32, 4, 32, 16).Row(0), pageHost.Margin(32, 0, 32, 24).Row(1));
        body.Children.Add(right.Col(1));
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        Content = root;

        tick.Tick += (_, _) => { monitor.Sample(); onTick?.Invoke(); };
        monitor.Sample();
        tick.Start();
        DevMode.Changed += OnDevMode;
        Closed += (_, _) =>
        {
            tick.Stop();
            DevMode.Changed -= OnDevMode;
            open = null;
            owner.Activate();
        };
        SizeChanged += (_, _) => Placement.Save(this, "developer");
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Go(Prefs.String("developer:page") ?? "overview");
    }

    private void OnDevMode() => Dispatcher.BeginInvoke(() => { if (!DevMode.On) Close(); });

    private void Go(string key)
    {
        if (Pages.All(p => p.Key != key)) key = "overview";
        page = key;
        Prefs.Set("developer:page", key == "overview" ? null : key);
        nav.Set(Pages.Select(p => new NavEntry { Key = p.Key, Title = p.Title, Icon = p.Icon, Tint = p.Tint, Go = () => Go(p.Key) }), page);
        titleHost.Content = K.Text(Pages.First(p => p.Key == key).Title, Ty.Title);
        onTick = null;
        FrameworkElement content = key switch
        {
            "logs" => LogsPage(),
            "crashes" => CrashesPage(),
            "system" => SystemPage(),
            "stats" => StatsPage(),
            "engine" => EnginePage(),
            "updates" => UpdatesPage(),
            "popups" => PopupsPage(),
            "prefs" => PrefsPage(),
            _ => OverviewPage(),
        };
        pageHost.Content = content;
        Motion.Enter(content, 0, 16, 300);
    }

    // ---- pieces ----

    private static ScrollViewer Scrolled(UIElement e) => new() { Content = e, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

    private static TextBlock Value(string s) => K.Text(s, Ty.Body, B.Text2).Align(HorizontalAlignment.Right, VerticalAlignment.Center);

    private static System.Windows.Controls.TextBox Code(string text, bool wrap = false) => new()
    {
        Text = text, IsReadOnly = true, FontFamily = Mono, FontSize = 12, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, AcceptsReturn = true,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto,
        Padding = new Thickness(10, 8, 10, 8),
    };

    private static Border Box(UIElement child) => new Border { Child = child, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) }.Bg(B.Card).Stroke(B.CardStroke);

    private void Copy(string text, string what)
    {
        try { Clipboard.SetText(text); Log.Write($"developer: {what} copied"); } catch { System.Media.SystemSounds.Beep.Play(); }
    }

    private static string? SaveText(string name, string text)
    {
        try
        {
            var dir = Screens.AltaSession.Downloads();
            Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, $"{name} {DateTime.Now.ToString("yyyy-MM-dd 'at' HH.mm.ss", Inv)}.txt");
            File.WriteAllText(path, text);
            return path;
        }
        catch { return null; }
    }

    private static void ShowFile(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); } catch { }
    }

    private static void OpenFolder(string dir)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true }); } catch { }
    }

    private static string Size(double bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => (bytes / (1024.0 * 1024 * 1024)).ToString("0.00", Inv) + " GB",
        >= 1024 * 1024 => (bytes / (1024.0 * 1024)).ToString("0.0", Inv) + " MB",
        >= 1024 => (bytes / 1024.0).ToString("0", Inv) + " KB",
        _ => bytes.ToString("0", Inv) + " B",
    };

    private static string Span(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : t.TotalMinutes >= 1 ? $"{t.Minutes} min {t.Seconds} s" : $"{t.Seconds} s";

    private static DateTime Started
    {
        get { try { return Process.GetCurrentProcess().StartTime; } catch { return DateTime.Now; } }
    }

    // ---- Overview ----

    private FrameworkElement OverviewPage()
    {
        var off = K.Button("Turn off developer tools", DevMode.Lock);
        var v = K.V(0,
            K.SectionHeader("This copy", 0),
            K.Group(
                K.Row(SymbolRegular.Info24, "Version", null, Value(ScriptBundle.ExtensionVersion is { Length: > 0 } ext ? $"{Updates.Version} · interface {ext}" : Updates.Version)),
                K.Row(SymbolRegular.Timer24, "Running for", $"Since {Started.ToString("g", CultureInfo.CurrentCulture)}", Value(Span(DateTime.Now - Started))),
                K.Row(SymbolRegular.Globe24, "School", engine.Host, Value($"{engine.LmsName} · {engine.Phase}")),
                K.Row(SymbolRegular.Folder24, "Data folder", AppPaths.Root, K.Button("Open", () => OpenFolder(AppPaths.Root))),
                K.Row(SymbolRegular.ArrowDownload24, "Installed", Updates.Installed ? "By Simpl’s installer" : "Run from its folder", null)),
            K.SectionHeader("What it runs on"),
            Box(Code(Perf.MachineText().Replace(" · ", "\n"), wrap: true)),
            K.SectionHeader("How it started (ms from the process’s start)"),
            Box(Code(Perf.StartText().Replace(" · ", "\n"), wrap: true)),
            K.SectionHeader("Diagnostics"),
            K.Group(
                K.Row(SymbolRegular.Copy24, "Copy diagnostics", "This copy, the PC, its start, memory, the calls and the stalls, in one text.", K.Button("Copy", () => Copy(Diagnostics(), "diagnostics"))),
                K.Row(SymbolRegular.Save24, "Save diagnostics", "The same, with the log’s last lines, as a file in Downloads.", K.Button("Save…", () =>
                {
                    var text = Diagnostics() + "\n\n==== simpl.log (last 400 lines) ====\n" + string.Join("\n", Tail(LogPath("simpl.log"), 400));
                    if (SaveText("Simpl Diagnostics", text) is { } p) ShowFile(p); else System.Media.SystemSounds.Beep.Play();
                })),
                K.Row(SymbolRegular.WindowDevTools24, "Turn off developer tools", "They come back the way they came: seven clicks on Settings ▸ About ▸ Version, or Ctrl+Alt+Shift+D.", off)));
        return Scrolled(v);
    }

    private string Diagnostics()
    {
        var m = Perf.Memory();
        var lines = new List<string>
        {
            $"Simpl for Windows {Updates.Version}" + (ScriptBundle.ExtensionVersion is { Length: > 0 } ext ? $" · interface {ext}" : ""),
            $"Running for {Span(DateTime.Now - Started)} · {engine.LmsName} · {engine.Phase}",
            "Machine: " + Perf.MachineText(),
            "Start: " + Perf.StartText(),
            "Memory: " + Perf.MemoryText(m),
            "Stalls: " + Perf.StallText(),
            "Calls (n · p50 · p95 · max ms): " + string.Join("; ", Perf.CallLines(30)),
            "Switches: " + string.Join("; ", Perf.SwitchLines()),
            "Builds: " + string.Join("; ", Perf.BuildLines()),
            $"Updates: {Updates.State}{(Updates.Message is { } msg ? " · " + msg : "")} · beta {(Updates.Beta ? "on" : "off")} · paused {(Updates.Paused ? "yes" : "no")}",
        };
        return string.Join("\n", lines);
    }

    // ---- Logs ----

    private static string LogPath(string name) => System.IO.Path.Combine(AppPaths.Root, name);

    /// <summary>The file's last lines (only its last 4 MB read: a long-lived copy's log can be large).</summary>
    private static List<string> Tail(string path, int count)
    {
        try
        {
            if (!File.Exists(path)) return new();
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            const long most = 4 * 1024 * 1024;
            var from = Math.Max(0, f.Length - most);
            f.Seek(from, SeekOrigin.Begin);
            using var r = new StreamReader(f, Encoding.UTF8);
            var all = r.ReadToEnd().Split('\n');
            var lines = all.Skip(from > 0 ? 1 : 0).Where(l => l.Length > 0).ToList();
            return lines.Count > count ? lines.GetRange(lines.Count - count, count) : lines;
        }
        catch { return new(); }
    }

    private static readonly string[] ProblemWords = { "fail", "error", "exception", "could not", "not understood", "went away", "denied", "timed out", "crash", "warning", "did not" };

    private static bool Problem(string line)
    {
        var l = line.ToLowerInvariant();
        return ProblemWords.Any(l.Contains);
    }

    private FrameworkElement LogsPage()
    {
        var file = Prefs.String("developer:log") ?? "simpl.log";
        var problems = Prefs.Bool("developer:log:problems");
        var follow = true;
        var find = new TextBox { PlaceholderText = "Find in the log", Icon = new SymbolIcon { Symbol = SymbolRegular.Search24 }, IconPlacement = ElementPlacement.Left, MinWidth = 240 };
        var which = new ComboBox { MinWidth = 150, ItemsSource = new[] { "simpl.log", "page-errors.log" }, SelectedItem = file };
        var only = new ToggleSwitch { Content = "Problems only", IsChecked = problems };
        var follows = new ToggleSwitch { Content = "Follow", IsChecked = true };
        var text = Code("");
        var count = K.Text("", Ty.Caption, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Center);
        long size = -1;
        string? lastQuery = null;
        bool? lastOnly = null;
        List<string> shown = new();

        void Load(bool force)
        {
            var path = LogPath(file);
            long now;
            try { now = File.Exists(path) ? new FileInfo(path).Length : 0; } catch { now = 0; }
            var q = find.Text?.Trim() ?? "";
            if (!force && now == size && q == lastQuery && problems == lastOnly) return;
            size = now;
            lastQuery = q;
            lastOnly = problems;
            var lines = Tail(path, 5000);
            var total = lines.Count;
            if (problems) lines = lines.Where(Problem).ToList();
            if (q.Length > 0) lines = lines.Where(l => l.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            shown = lines;
            var atEnd = text.VerticalOffset + text.ViewportHeight >= text.ExtentHeight - 4;
            text.Text = lines.Count == 0 ? (total == 0 ? $"({file} is empty)" : "(nothing matches)") : string.Join("\n", lines);
            count.Text = lines.Count == total ? $"{total:N0} lines" : $"{lines.Count:N0} of {total:N0} lines";
            if (follow && atEnd || force) text.ScrollToEnd();
        }

        find.TextChanged += (_, _) => Load(false);
        which.SelectionChanged += (_, _) => { file = which.SelectedItem as string ?? "simpl.log"; Prefs.Set("developer:log", file == "simpl.log" ? null : file); Load(true); };
        only.Checked += (_, _) => { problems = true; Prefs.Set("developer:log:problems", true); Load(false); };
        only.Unchecked += (_, _) => { problems = false; Prefs.Remove("developer:log:problems"); Load(false); };
        follows.Checked += (_, _) => { follow = true; text.ScrollToEnd(); };
        follows.Unchecked += (_, _) => follow = false;

        var buttons = K.H(8,
            K.Button("Copy", () => Copy(string.Join("\n", shown), file), icon: SymbolRegular.Copy24),
            K.Button("Save…", () => { if (SaveText("Simpl " + System.IO.Path.GetFileNameWithoutExtension(file), string.Join("\n", shown)) is { } p) ShowFile(p); else System.Media.SystemSounds.Beep.Play(); }, icon: SymbolRegular.Save24),
            K.Button("Open folder", () => OpenFolder(AppPaths.Root), icon: SymbolRegular.Folder24),
            K.Button("Clear", async () =>
            {
                if (!await Dialogs.Ask(this, $"Clear {file}?", "What it holds now is gone; Simpl keeps writing to it.", "Clear", "Cancel", danger: true)) return;
                try { File.WriteAllText(LogPath(file), ""); } catch { }
                Load(true);
            }, icon: SymbolRegular.Delete24));
        var bar = K.Columns("auto, auto, auto, auto, *", find.Col(0), which.Margin(8, 0, 0, 0).Col(1), only.Margin(16, 0, 0, 0).Col(2), follows.Margin(16, 0, 0, 0).Col(3));
        var foot = K.Columns("*, auto", count.Col(0), buttons.Col(1));
        Load(true);
        onTick = () => Load(false);
        return K.Rows("auto, *, auto", bar.Margin(0, 0, 0, 12).Row(0), Box(text).Row(1), foot.Margin(0, 12, 0, 0).Row(2));
    }

    // ---- Crashes ----

    /// <summary>(0.3.51, the Mac's 1.3.146) The crash log (Support/CrashLog): each run that went down — an exception nothing
    /// caught, or a run that ended without quitting, with Windows' own report of it — and each exception the window's
    /// catch let pass; with the last of simpl.log before it. The list at the left, the one picked read at the right.</summary>
    private FrameworkElement CrashesPage()
    {
        var list = new System.Windows.Controls.ListBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        var text = Code("", wrap: true); // (a report's long lines, and the empty log's words, read whole)
        var count = K.Text("", Ty.Caption, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Center);
        List<CrashLog.Entry> entries = new();
        CrashLog.Entry? Picked() => list.SelectedIndex >= 0 && list.SelectedIndex < entries.Count ? entries[list.SelectedIndex] : null;

        void Load()
        {
            var keep = Picked()?.Path;
            entries = CrashLog.All();
            list.Items.Clear();
            foreach (var e in entries)
            {
                var when = K.Text(e.At.ToString("g", CultureInfo.CurrentCulture) + " · " + e.Version, Ty.Caption, B.Text2);
                var what = K.Text(e.Title, Ty.Body);
                what.TextWrapping = TextWrapping.Wrap;
                what.MaxHeight = 40;
                var kind = K.Text(e.Fatal ? "Crash" : "Kept going", Ty.Caption, e.Fatal ? B.Text : B.Text2);
                kind.FontWeight = FontWeights.SemiBold;
                if (e.Fatal) kind.Foreground = new SolidColorBrush(Color.FromRgb(0xc4, 0x2b, 0x1c));
                list.Items.Add(K.V(2, kind, what, when).Margin(4, 6, 4, 6));
            }
            count.Text = entries.Count == 0 ? "No crashes kept" : $"{entries.Count} kept · {entries.Count(e => e.Fatal)} crashes";
            var at = keep == null ? 0 : Math.Max(0, entries.FindIndex(e => e.Path == keep));
            list.SelectedIndex = entries.Count > 0 ? at : -1;
            if (entries.Count == 0) text.Text = "No crashes kept.\n\nA run of Simpl that goes down — or ends without quitting — is kept here the next time it opens, with what stopped it, Windows’ own report and the last of simpl.log.";
        }

        list.SelectionChanged += (_, _) => { if (Picked() is { } e) { text.Text = CrashLog.Text(e); text.ScrollToHome(); } };
        var buttons = K.H(8,
            K.Button("Reload", Load, icon: SymbolRegular.ArrowClockwise24),
            K.Button("Copy", () => { if (Picked() is { } e) Copy(CrashLog.Text(e), "crash"); }, icon: SymbolRegular.Copy24),
            K.Button("Save…", () => { if (Picked() is { } e && SaveText("Simpl Crash", CrashLog.Text(e)) is { } p) ShowFile(p); else System.Media.SystemSounds.Beep.Play(); }, icon: SymbolRegular.Save24),
            K.Button("Delete", () => { if (Picked() is { } e) { CrashLog.Delete(e); Load(); } }, icon: SymbolRegular.Delete24),
            K.Button("Open folder", () => { Directory.CreateDirectory(CrashLog.Folder); OpenFolder(CrashLog.Folder); }, icon: SymbolRegular.Folder24),
            K.Button("Delete all", async () =>
            {
                if (!await Dialogs.Ask(this, "Delete every crash kept?", "Windows’ own reports stay in its Application log.", "Delete All", "Cancel", danger: true)) return;
                CrashLog.DeleteAll();
                Load();
            }, icon: SymbolRegular.DeleteDismiss24),
            K.Button("Crash now…", async () =>
            {
                if (!await Dialogs.Ask(this, "Stop Simpl now, as a crash would?", "For trying the crash log: Simpl stops at once, and the next time it opens the crash is here.", "Crash Now", "Cancel", danger: true)) return;
                CrashLog.CrashNow();
            }, icon: SymbolRegular.ErrorCircle24));
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(Box(list).Margin(0, 0, 12, 0).Col(0));
        body.Children.Add(Box(text).Col(1));
        Load();
        return K.Rows("*, auto", body.Row(0), K.Columns("*, auto", count.Col(0), buttons.Col(1)).Margin(0, 12, 0, 0).Row(1));
    }

    // ---- System ----

    /// <summary>A second at a time while the window is open, the last two minutes kept: Simpl's processor (all its
    /// threads, as a share of one processor's time, as Activity Monitor counts it — 100% a whole core), WebView2's, and
    /// their memory.</summary>
    private sealed class Monitor
    {
        public const int Keep = 120;
        public readonly List<double> SimplCpu = new(), WebCpu = new(), SimplMem = new(), WebMem = new();
        public PerfMemory.Reading? Last;
        public int Threads, Handles;
        private TimeSpan simplWas;
        private readonly Dictionary<int, TimeSpan> webWas = new();
        private long at;
        private bool busy;

        public async void Sample()
        {
            if (busy) return;
            busy = true;
            try
            {
                var r = await Task.Run(() =>
                {
                    var now = Stopwatch.GetTimestamp();
                    var m = Perf.Memory();
                    var me = Process.GetCurrentProcess();
                    var simplTime = me.TotalProcessorTime;
                    var threads = me.Threads.Count;
                    var handles = me.HandleCount;
                    var web = new Dictionary<int, TimeSpan>();
                    foreach (var p in m.Web)
                        try { using var pr = Process.GetProcessById(p.Pid); web[p.Pid] = pr.TotalProcessorTime; } catch { }
                    return (now, m, simplTime, threads, handles, web);
                });
                if (at != 0)
                {
                    var secs = (r.now - at) / (double)Stopwatch.Frequency;
                    if (secs > 0)
                    {
                        Add(SimplCpu, (r.simplTime - simplWas).TotalSeconds / secs * 100);
                        var webDelta = r.web.Sum(w => webWas.TryGetValue(w.Key, out var was) ? (w.Value - was).TotalSeconds : 0);
                        Add(WebCpu, webDelta / secs * 100);
                    }
                }
                at = r.now;
                simplWas = r.simplTime;
                webWas.Clear();
                foreach (var (k, v) in r.web) webWas[k] = v;
                Last = r.m;
                Threads = r.threads;
                Handles = r.handles;
                Add(SimplMem, r.m.Simpl.Private);
                Add(WebMem, r.m.WebPrivate);
            }
            catch { }
            finally { busy = false; }
        }

        private static void Add(List<double> l, double v)
        {
            l.Add(Math.Max(0, v));
            if (l.Count > Keep) l.RemoveAt(0);
        }
    }

    /// <summary>A line of the last two minutes, filled under, its scale the larger of <paramref name="floor"/> and its own highest.</summary>
    private sealed class Spark : FrameworkElement
    {
        private readonly Func<IReadOnlyList<double>> values;
        private readonly Color color;
        private readonly double floor;

        public Spark(Func<IReadOnlyList<double>> values, Color color, double floor)
        {
            this.values = values;
            this.color = color;
            this.floor = floor;
            Height = 64;
            SnapsToDevicePixels = true;
        }

        public void Redraw() => InvalidateVisual();

        protected override void OnRender(DrawingContext dc)
        {
            var w = ActualWidth;
            var h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
            var v = values();
            if (v.Count < 2) return;
            var top = Math.Max(floor, v.Max()) * 1.1;
            var step = w / (Monitor.Keep - 1);
            var x0 = w - (v.Count - 1) * step;
            var line = new StreamGeometry();
            var fill = new StreamGeometry();
            using (var g = line.Open())
            using (var f = fill.Open())
            {
                Point P(int i) => new(x0 + i * step, h - v[i] / top * (h - 2) - 1);
                g.BeginFigure(P(0), false, false);
                f.BeginFigure(new Point(x0, h), true, true);
                f.LineTo(P(0), false, false);
                for (var i = 1; i < v.Count; i++) { g.LineTo(P(i), true, true); f.LineTo(P(i), false, false); }
                f.LineTo(new Point(w, h), false, false);
            }
            line.Freeze();
            fill.Freeze();
            dc.DrawGeometry(K.Brush(K.WithAlpha(color, 0.18)), null, fill);
            dc.DrawGeometry(null, new Pen(K.Brush(color), 1.5) { LineJoin = PenLineJoin.Round }, line);
        }
    }

    private FrameworkElement SystemPage()
    {
        var green = Color.FromRgb(0x10, 0x89, 0x3e);
        var blue = Color.FromRgb(0x00, 0x78, 0xd4);
        var purple = Color.FromRgb(0x88, 0x64, 0xd8);
        var orange = Color.FromRgb(0xe3, 0x6c, 0x0a);
        var cpu = new Spark(() => monitor.SimplCpu, green, 25);
        var webCpu = new Spark(() => monitor.WebCpu, blue, 25);
        var mem = new Spark(() => monitor.SimplMem, purple, 100);
        var webMem = new Spark(() => monitor.WebMem, orange, 200);
        var cpuNow = K.Text("—", Ty.Subtitle);
        var webCpuNow = K.Text("—", Ty.Subtitle);
        var memNow = K.Text("—", Ty.Subtitle);
        var webMemNow = K.Text("—", Ty.Subtitle);

        FrameworkElement Chart(string name, TextBlock now, Spark s, Color c) =>
            K.Card(K.V(6, K.Columns("auto, *", K.H(8, K.Dot(c), K.Text(name, Ty.Caption, B.Text2)).Col(0), now.Align(HorizontalAlignment.Right).Col(1)), s), new Thickness(14, 12, 14, 12));

        var charts = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        foreach (var c in new[] { Chart("Simpl’s processor", cpuNow, cpu, green), Chart("WebView2’s processor", webCpuNow, webCpu, blue), Chart("Simpl’s memory (private)", memNow, mem, purple), Chart("WebView2’s memory (private)", webMemNow, webMem, orange) })
        {
            c.Margin = new Thickness(0, 0, 8, 8);
            charts.Children.Add(c);
        }

        var facts = new Stack { Orientation = Orientation.Vertical, Spacing = 4 };
        var procs = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        var storage = new Stack { Orientation = Orientation.Vertical, Spacing = 4 };
        FrameworkElement Fact(SymbolRegular icon, string name, string value) => K.Row(icon, name, null, Value(value));

        void Draw()
        {
            static string Pc(List<double> l) => l.Count == 0 ? "—" : l[^1].ToString("0.0", Inv) + "%";
            cpuNow.Text = Pc(monitor.SimplCpu);
            webCpuNow.Text = Pc(monitor.WebCpu);
            foreach (var s in new[] { cpu, webCpu, mem, webMem }) s.Redraw();
            if (monitor.Last is not { } m) return;
            memNow.Text = m.Simpl.Private.ToString("0", Inv) + " MB";
            webMemNow.Text = m.WebPrivate.ToString("0", Inv) + " MB";
            facts.Children.Clear();
            facts.Children.Add(Fact(SymbolRegular.Timer24, "Running for", Span(DateTime.Now - Started)));
            facts.Children.Add(Fact(SymbolRegular.Gauge24, "Memory, all together", $"{m.Private:0} MB private · {m.WorkingSet:0} MB in RAM · {m.PrivateWs:0} MB as Task Manager counts it"));
            facts.Children.Add(Fact(SymbolRegular.Code24, ".NET’s heap", $"{m.GcHeap:0} MB · {m.GcCommitted:0} MB committed · {m.GcFragmented:0} MB fragmented · collections {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}"));
            facts.Children.Add(Fact(SymbolRegular.Wrench24, "Threads and handles", $"{monitor.Threads} threads · {monitor.Handles:N0} handles"));
            facts.Children.Add(Fact(SymbolRegular.Warning24, "The window’s stalls", Perf.StallText()));
            facts.Children.Add(Fact(SymbolRegular.Globe24, "WebView2’s processes", $"{m.Web.Count}"));
            procs.Children.Clear();
            procs.Children.Add(ProcRow("Process", "Id", "Private", "In RAM", true));
            procs.Children.Add(ProcRow("Simpl", m.Simpl.Pid.ToString(Inv), $"{m.Simpl.Private:0} MB", $"{m.Simpl.WorkingSet:0} MB", false));
            foreach (var p in m.Web.OrderByDescending(p => p.Private))
                procs.Children.Add(ProcRow(p.Kind, p.Pid.ToString(Inv), $"{p.Private:0} MB", $"{p.WorkingSet:0} MB", false));
        }

        void Measure()
        {
            storage.Children.Clear();
            storage.Children.Add(K.Reading("Measuring…"));
            _ = Task.Run(() =>
            {
                long Of(string path)
                {
                    try
                    {
                        if (File.Exists(path)) return new FileInfo(path).Length;
                        if (!Directory.Exists(path)) return 0;
                        return new DirectoryInfo(path).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).Sum(f => { try { return f.Length; } catch { return 0L; } });
                    }
                    catch { return 0; }
                }
                var all = Of(AppPaths.Root);
                var web = Of(System.IO.Path.Combine(AppPaths.Root, "WebView2"));
                var answers = Of(System.IO.Path.Combine(AppPaths.Root, "Answers"));
                var log = Of(LogPath("simpl.log")) + Of(LogPath("page-errors.log"));
                return (all, web, answers, log);
            }).ContinueWith(t => Dispatcher.BeginInvoke(() =>
            {
                if (t.IsFaulted) return;
                var (all, web, answers, log) = t.Result;
                storage.Children.Clear();
                storage.Children.Add(Fact(SymbolRegular.HardDrive24, "Everything Simpl keeps", Size(all)));
                storage.Children.Add(Fact(SymbolRegular.Globe24, "WebView2’s data (the school’s pages, its cache)", Size(web)));
                storage.Children.Add(Fact(SymbolRegular.Storage24, "Answers kept for a quick start", Size(answers)));
                storage.Children.Add(Fact(SymbolRegular.TextAlignLeft24, "Logs", Size(log)));
                storage.Children.Add(Fact(SymbolRegular.DocumentText24, "Everything else", Size(Math.Max(0, all - web - answers - log))));
            }));
        }

        var buttons = K.H(8,
            K.Button("Copy snapshot", () => Copy(Diagnostics(), "system snapshot"), icon: SymbolRegular.Copy24),
            K.Button("Collect garbage", () => { var before = GC.GetTotalMemory(false); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Log.Write($"developer: collected {(before - GC.GetTotalMemory(true)) / 1048576.0:0.0} MB"); Draw(); }, icon: SymbolRegular.Broom24),
            K.Button("Measure again", Measure, icon: SymbolRegular.ArrowClockwise24),
            K.Button("Task Manager", () => { try { Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); } catch { } }, icon: SymbolRegular.WindowDevTools24));
        var v = K.V(0,
            buttons.Margin(0, 0, 0, 16),
            charts,
            K.SectionHeader("Now", 16), facts,
            K.SectionHeader("Processes"), Box(procs),
            K.SectionHeader("On disk"), storage);
        Draw();
        Measure();
        onTick = Draw;
        return Scrolled(v);
    }

    private static FrameworkElement ProcRow(string kind, string pid, string priv, string ws, bool head)
    {
        var ty = head ? Ty.Caption : Ty.Body;
        var brush = head ? B.Text2 : B.Text;
        var row = K.Columns("*, 90, 110, 110",
            K.Text(kind, ty, brush).Col(0), K.Text(pid, ty, brush).Align(HorizontalAlignment.Right).Col(1),
            K.Text(priv, ty, brush).Align(HorizontalAlignment.Right).Col(2), K.Text(ws, ty, brush).Align(HorizontalAlignment.Right).Col(3));
        row.Margin = new Thickness(14, head ? 10 : 5, 14, head ? 6 : 5);
        if (head) foreach (var t in row.Children.OfType<TextBlock>()) t.FontWeight = FontWeights.SemiBold;
        return row;
    }

    // ---- Stats ----

    private FrameworkElement StatsPage()
    {
        var sort = Prefs.String("developer:stats:sort") ?? "total";
        var rows = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        var summary = K.Text("", Ty.Caption, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Center);
        var switches = Code("", wrap: true);
        var builds = Code("", wrap: true);
        var cols = "*, 70, 70, 80, 80, 80, 80, 80";
        var heads = new (string Key, string Title)[] { ("name", "Call"), ("n", "Count"), ("failed", "Failed"), ("mean", "Mean"), ("p50", "Middle"), ("p95", "95th"), ("max", "Longest"), ("last", "Last") };

        FrameworkElement Head()
        {
            var g = K.Columns(cols);
            for (var i = 0; i < heads.Length; i++)
            {
                var (key, title) = heads[i];
                var t = K.Text(title + (sort == key ? " ▾" : ""), Ty.Caption, sort == key ? B.Text : B.Text2).Strong();
                var b = K.Subtle(t, () => { sort = key; Prefs.Set("developer:stats:sort", key == "total" ? null : key); Draw(); }, new Thickness(4, 2, 4, 2));
                b.HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                g.Children.Add(b.Col(i));
            }
            g.Margin = new Thickness(10, 8, 10, 4);
            return g;
        }

        void Draw()
        {
            var all = DevStats.Rows();
            IEnumerable<(string Name, long N, long Failed, double Mean, double P50, double P95, double Max, double Last, DateTime At, double Total)> s = sort switch
            {
                "name" => all.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
                "n" => all.OrderByDescending(r => r.N),
                "failed" => all.OrderByDescending(r => r.Failed).ThenByDescending(r => r.N),
                "mean" => all.OrderByDescending(r => r.Mean),
                "p50" => all.OrderByDescending(r => r.P50),
                "p95" => all.OrderByDescending(r => r.P95),
                "max" => all.OrderByDescending(r => r.Max),
                "last" => all.OrderByDescending(r => r.At),
                _ => all.OrderByDescending(r => r.Total),
            };
            rows.Children.Clear();
            rows.Children.Add(Head());
            rows.Children.Add(K.Divider());
            if (all.Count == 0) rows.Children.Add(K.Text("No calls yet.", Ty.Body, B.Text2).Margin(14, 12, 14, 12));
            foreach (var r in s)
            {
                string Ms(double v) => v.ToString("0", Inv);
                var g = K.Columns(cols,
                    K.Text(r.Name, Ty.Body).Tip($"Last at {r.At.ToString("T", CultureInfo.CurrentCulture)} · {r.Total / 1000:0.0} s in all").Col(0),
                    Value(r.N.ToString("N0", Inv)).Col(1),
                    K.Text(r.Failed.ToString(Inv), Ty.Body, r.Failed > 0 ? B.Critical : B.Text2).Align(HorizontalAlignment.Right).Col(2),
                    Value(Ms(r.Mean)).Col(3), Value(Ms(r.P50)).Col(4), Value(Ms(r.P95)).Col(5), Value(Ms(r.Max)).Col(6), Value(Ms(r.Last)).Col(7));
                g.Margin = new Thickness(14, 5, 14, 5);
                rows.Children.Add(g);
            }
            var calls = all.Sum(r => r.N);
            var failed = all.Sum(r => r.Failed);
            summary.Text = $"{calls:N0} calls to {all.Count} kinds, {failed:N0} failed, {all.Sum(r => r.Total) / 1000:0.0} s in all, since {DevStats.Since.ToString("T", CultureInfo.CurrentCulture)} · times in ms";
            switches.Text = string.Join("\n", Perf.SwitchLines().DefaultIfEmpty("(none yet)"));
            builds.Text = string.Join("\n", Perf.BuildLines().DefaultIfEmpty("(none yet)"));
        }

        var buttons = K.H(8,
            K.Button("Copy", () => Copy(DevStats.Text(), "stats"), icon: SymbolRegular.Copy24),
            K.Button("Reset", () => { DevStats.Reset(); Draw(); }, icon: SymbolRegular.ArrowClockwise24));
        var v = K.V(0,
            K.Columns("*, auto", summary.Col(0), buttons.Col(1)).Margin(0, 0, 0, 12),
            Box(rows),
            K.SectionHeader("Screens switched to (n · drawn p50 · max · answered p50 · max ms)"), Box(switches),
            K.SectionHeader("Screens built (n · p50 · max ms)"), Box(builds));
        Draw();
        var every = 0;
        onTick = () => { if (++every % 2 == 0) Draw(); };
        return Scrolled(v);
    }

    // ---- Engine ----

    private FrameworkElement EnginePage()
    {
        var name = new TextBox { PlaceholderText = "Call (dashboard, course, assignment…)", Text = Prefs.String("developer:call") ?? "", MinWidth = 260 };
        var args = new TextBox { PlaceholderText = "Arguments, as JSON ({\"course\": \"123\"})", Text = Prefs.String("developer:args") ?? "", FontFamily = Mono };
        var answer = Code("", wrap: true);
        var said = K.Text("", Ty.Caption, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Center);
        var running = false;

        async void Run()
        {
            if (running) return;
            var call = name.Text.Trim();
            if (call.Length == 0) { name.Focus(); return; }
            object? a = null;
            if (args.Text.Trim() is { Length: > 0 } raw)
            {
                try { a = JsonNode.Parse(raw); }
                catch (Exception e) { said.Text = "The arguments are not JSON: " + e.Message; return; }
            }
            Prefs.Set("developer:call", call);
            Prefs.Set("developer:args", args.Text.Trim().Length > 0 ? args.Text.Trim() : null);
            running = true;
            said.Text = $"Calling {call}…";
            var sw = Stopwatch.StartNew();
            try
            {
                var text = await engine.CallText(call, a);
                sw.Stop();
                try { text = JsonNode.Parse(text)?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? text; } catch { }
                answer.Text = text;
                said.Text = $"{call} answered in {sw.ElapsedMilliseconds:N0} ms · {Size(Encoding.UTF8.GetByteCount(text))}";
            }
            catch (Exception e)
            {
                answer.Text = "";
                said.Text = $"{call} failed after {sw.ElapsedMilliseconds:N0} ms: {e.Message}";
            }
            running = false;
        }

        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Run(); e.Handled = true; } };
        args.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Run(); e.Handled = true; } };
        var bar = K.Columns("auto, *, auto", name.Col(0), args.Margin(8, 0, 8, 0).Col(1), K.Button("Run", Run, accent: true, icon: SymbolRegular.Play24).Col(2));
        var tools = K.H(8,
            K.Button("Copy answer", () => Copy(answer.Text, "answer"), icon: SymbolRegular.Copy24),
            K.Button("Refresh", () => engine.Refresh(), icon: SymbolRegular.ArrowClockwise24),
            K.Button("Reload the page", () => engine.Reload(), icon: SymbolRegular.ArrowSync24),
            K.Button("Page’s DevTools", () => { try { engine.Web.CoreWebView2?.OpenDevToolsWindow(); } catch { } }, icon: SymbolRegular.WindowDevTools24));
        return K.Rows("auto, auto, *, auto",
            bar.Row(0),
            K.Wrapped("A call into the school’s page, made as the screens make theirs; its answer as the page gives it. Enter runs it.", Ty.Caption, B.Text2).Margin(2, 8, 0, 12).Row(1),
            Box(answer).Row(2),
            K.Columns("*, auto", said.Col(0), tools.Col(1)).Margin(0, 12, 0, 0).Row(3));
    }

    // ---- Updates ----

    private FrameworkElement UpdatesPage()
    {
        var state = Value("");
        var last = Value("");
        void Draw()
        {
            state.Text = Updates.State + (Updates.Found is { } f && Updates.State is Updates.Mode.Available or Updates.Mode.Downloading or Updates.Mode.Ready ? $" · {f.Version}" : "") + (Updates.Message is { Length: > 0 } m ? " · " + m : "");
            last.Text = Updates.LastCheck is { } t ? t.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "never";
        }
        var beta = new ToggleSwitch { IsChecked = Updates.Beta };
        beta.Checked += (_, _) => Updates.Beta = true;
        beta.Unchecked += (_, _) => Updates.Beta = false;
        var paused = new ToggleSwitch { IsChecked = Updates.Paused };
        paused.Checked += (_, _) => Updates.Paused = true;
        paused.Unchecked += (_, _) => Updates.Paused = false;
        var v = K.V(0,
            K.SectionHeader("This copy", 0),
            K.Group(
                K.Row(SymbolRegular.Info24, "Version", null, Value(Updates.Version)),
                K.Row(SymbolRegular.ArrowSync24, "State", null, state),
                K.Row(SymbolRegular.Timer24, "Last looked", null, last)),
            K.SectionHeader("Channel"),
            K.Group(
                K.Row(SymbolRegular.Beaker24, "Get beta updates", "The betas are offered too, not only the usual releases.", beta),
                K.Row(SymbolRegular.Pause24, "Pause updates", "Looked for all the same, but nothing installs by itself (a Critical Update still does).", paused),
                K.Row(SymbolRegular.ArrowDownload24, "Look now", Updates.Off ? "A development run never updates." : null, K.Button("Check for updates", () => _ = Updates.Check()))));
        Draw();
        onTick = Draw;
        return Scrolled(v);
    }

    // ---- Knewton Alta ----

    private FrameworkElement PopupsPage()
    {
        var files = new Stack { Orientation = Orientation.Vertical, Spacing = 4 };
        void List()
        {
            files.Children.Clear();
            try
            {
                var dir = new DirectoryInfo(Screens.AltaSession.Downloads());
                var saved = dir.Exists ? dir.EnumerateFiles("Simpl *.txt").Where(f => f.Name.Contains(" Code ") || f.Name.Contains(" Error Logs ")).OrderByDescending(f => f.LastWriteTime).Take(12).ToList() : new();
                if (saved.Count == 0) files.Children.Add(K.Text("Nothing saved yet.", Ty.Body, B.Text2).Margin(2, 4, 0, 4));
                foreach (var f in saved)
                    files.Children.Add(K.Row(SymbolRegular.DocumentText24, f.Name, $"{f.LastWriteTime.ToString("g", CultureInfo.CurrentCulture)} · {Size(f.Length)}", K.Button("Show", () => ShowFile(f.FullName))));
            }
            catch { }
        }
        var v = K.V(0,
            K.SectionHeader("Save Page Code", 0),
            K.Wrapped("With the developer tools on, Knewton Alta’s popup has Save Page Code at the top: the page, every frame in it, and the scripts and style sheets they load, in one text file in Downloads (addresses without their queries). Alta’s Error logs show there too whenever its page was not recognised.", Ty.Body, B.Text2),
            K.SectionHeader("Saved"),
            files,
            K.H(8, K.Button("Open Downloads", () => OpenFolder(Screens.AltaSession.Downloads()), icon: SymbolRegular.Folder24), K.Button("Look again", List, icon: SymbolRegular.ArrowClockwise24)).Margin(0, 12, 0, 0));
        List();
        return Scrolled(v);
    }

    // ---- Preferences ----

    private FrameworkElement PrefsPage()
    {
        var find = new TextBox { PlaceholderText = "Find a key", Icon = new SymbolIcon { Symbol = SymbolRegular.Search24 }, IconPlacement = ElementPlacement.Left };
        var list = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        var count = K.Text("", Ty.Caption, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Center);
        void Draw()
        {
            var q = find.Text?.Trim() ?? "";
            var all = Prefs.All();
            var shown = all.Where(p => q.Length == 0 || p.Key.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Value.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            count.Text = shown.Count == all.Count ? $"{all.Count} keys" : $"{shown.Count} of {all.Count} keys";
            list.Children.Clear();
            foreach (var (key, value) in shown)
            {
                var shortValue = value.Length > 160 ? value[..160] + "…" : value;
                var k = K.Text(key, Ty.Body);
                k.FontFamily = Mono;
                k.FontSize = 12;
                var val = K.Text(shortValue, Ty.Caption, B.Text2);
                val.FontFamily = Mono;
                val.TextTrimming = TextTrimming.CharacterEllipsis;
                val.ToolTip = value.Length > 160 ? value[..Math.Min(value.Length, 2000)] : null;
                var remove = K.IconButton(SymbolRegular.Delete24, async () =>
                {
                    if (!await Dialogs.Ask(this, $"Remove “{key}”?", "Simpl goes back to what it does without it. Some keys are read only when Simpl starts.", "Remove", "Cancel", danger: true)) return;
                    Prefs.Remove(key);
                    Draw();
                }, "Remove this key");
                var row = K.Columns("*, auto", K.V(1, k, val).Col(0), remove.Col(1));
                row.Margin = new Thickness(14, 6, 8, 6);
                list.Children.Add(row);
                list.Children.Add(K.Divider(14));
            }
        }
        find.TextChanged += (_, _) => Draw();
        var buttons = K.H(8, K.Button("Copy all", () => Copy(string.Join("\n", Prefs.All().Select(p => $"{p.Key} = {p.Value}")), "preferences"), icon: SymbolRegular.Copy24));
        Draw();
        return K.Rows("auto, *, auto",
            find.Margin(0, 0, 0, 12).Row(0),
            Box(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }).Row(1),
            K.Columns("*, auto", count.Col(0), buttons.Col(1)).Margin(0, 12, 0, 0).Row(2));
    }
}
