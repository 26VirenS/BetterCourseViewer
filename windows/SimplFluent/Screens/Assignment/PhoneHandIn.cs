using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using QRCoder;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// Send from Phone, in Hand In (the Mac's PhoneHandIn.swift): a panel over the form with a QR code that a phone's camera
/// opens — a page this PC serves on its own Wi-Fi (Support/PhoneServer.cs), no app on the phone, no server anywhere else
/// — and the files the phone sends, each given to the hand-in as if chosen with Choose Files…. The student looks them
/// over and presses Submit on the PC; nothing is ever handed in by itself. The server is up only while the panel is: it
/// stops when the panel closes, when the hand-in is sent or cancelled, and after 15 minutes with nothing from the phone;
/// what came waits in a folder of its own that goes when the hand-in does. An assignment that takes PDFs and not
/// pictures can have the phone's photos made into one PDF, a page each, in the order they came. The first time, the
/// panel says first that Windows will ask whether Simpl may use the network.
/// </summary>
internal sealed class PhoneHandIn
{
    private enum Phase { First, Starting, Waiting, Connected, NoNetwork, Failed, Idle }

    /// <summary>A file from the phone, as the panel shows it arriving.</summary>
    private sealed class Item
    {
        public int Id;
        public string Name = "";
        public long Size, Received;
        public string State = "sending";
        public int Page;
        public ImageSource? Thumb;
        public System.Windows.Controls.ProgressBar? Bar;
        public TextBlock? Words;
    }

    /// <summary>A file for the hand-in (its name, its type, its bytes) — with <c>Photos</c>, the photos' PDF made again, in
    /// place of the one before it.</summary>
    public event Action<string, string, byte[], bool>? Arrived;
    /// <summary>The panel was put away (its server stopped).</summary>
    public event Action? Closed;

    public FrameworkElement View => root;
    public bool IsOpen { get; private set; }

    private readonly Color accent;
    private readonly Grid root = new() { Visibility = Visibility.Collapsed };
    private readonly ContentControl iconHost = new() { Width = 46, Height = 46, Focusable = false };
    private readonly TextBlock title = K.Text("Send from Phone", Ty.Subtitle);
    private readonly TextBlock subtitle = K.Text("", Ty.Body, B.Text2);
    private readonly ContentControl stateHost = new() { Focusable = false };
    private readonly Stack whereNote = new() { Orientation = Orientation.Vertical, Spacing = 4 };
    private readonly ToggleSwitch combine = new() { IsChecked = true };
    private readonly TextBlock addedWords = K.Text("", Ty.Body, B.Text2);
    private readonly Wpf.Ui.Controls.Button done;
    private readonly Stack list = new() { Orientation = Orientation.Vertical, Spacing = 0 };
    /// <summary>The panel's top layer: the code on its way into the corner.</summary>
    private readonly Canvas flight = new() { IsHitTestVisible = false };

    private Phase phase = Phase.Starting;
    private PhoneSetup? setup;
    private PhoneServer? server;
    private PhoneLan.Address? address;
    private QRCodeData? qr;
    private PhoneQrView? bigQr;
    private Border? qrBox;
    private FrameworkElement? waitingView;
    private readonly List<Item> items = new();
    private readonly List<string> folders = new();
    private readonly Stopwatch openedAt = new();
    private System.Windows.Threading.DispatcherTimer? idle;
    /// <summary>The photos as PDF pages, in the order they came, and whether the hand-in has the PDF of them.</summary>
    private readonly List<PhotoPdf.Page> pages = new();
    private Task making = Task.CompletedTask;
    private bool holding;
    private System.Windows.Threading.DispatcherTimer? hold;

    /// <summary>The screenshot suite's test: the server on 127.0.0.1 too (its address in the log), and the picture held until
    /// files have come (windows-fluent.yml posts them).</summary>
    private static bool Testing => Launch.Flag("phone-test");

    public PhoneHandIn(Color accent)
    {
        this.accent = accent;
        done = K.Button("Done", Close);
        done.ToolTip = "Close — then look over your hand-in and press Submit";
        combine.Content = "Combine photos into one PDF";
        Build();
    }

    /// <summary>An assignment that takes PDFs and no pictures: its photos may be made into one PDF.</summary>
    private bool CombineOffered => setup is { } s && s.Allowed.Contains("pdf") && !s.Allowed.Any(PhonePage.Images.Contains);

    public string PdfName
    {
        get
        {
            var t = setup?.Title.Trim() ?? "";
            return PhoneLan.CleanName((t.Length == 0 ? "Photos" : t) + ".pdf", "application/pdf");
        }
    }

    // ---- the panel ----

    private void Build()
    {
        // (the dialog's own ground, as its content's: the panel covers the form)
        var ground = new Border();
        ground.SetResourceReference(Border.BackgroundProperty, "ContentDialogBackground");
        var top = new Border();
        top.SetResourceReference(Border.BackgroundProperty, "ContentDialogTopOverlay");
        root.Children.Add(ground);
        root.Children.Add(top);

        iconHost.Content = K.IconTile(SymbolRegular.Phone24, accent, 40);
        var close = K.IconButton(SymbolRegular.Dismiss24, Close, "Close Send from Phone", 14, 32);
        var words = K.V(2, title, subtitle);
        words.VerticalAlignment = VerticalAlignment.Center;
        var head = K.Columns("auto, *, auto", iconHost.Margin(0, 0, 12, 0).Col(0), words.Col(1), close.Align(HorizontalAlignment.Right, VerticalAlignment.Top).Col(2));
        head.Margin = new Thickness(24, 20, 20, 10);

        var body = K.V(14, stateHost, whereNote, combine);
        var scroll = new ScrollViewer
        {
            Content = body, Padding = new Thickness(24, 4, 24, 8), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var foot = K.Columns("*, auto", addedWords.Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0), done.Col(1));
        foot.Margin = new Thickness(24, 6, 24, 16);
        var grid = K.Rows("auto, *, auto", head.Row(0), scroll.Row(1), foot.Row(2));
        root.Children.Add(grid);
        root.Children.Add(flight);
        root.RenderTransform = new ScaleTransform(1, 1);
        System.Windows.Automation.AutomationProperties.SetName(root, "Send from Phone");
    }

    /// <summary>The panel opened over the form, grown from <paramref name="from"/> (the button, in the panel's own
    /// coordinates): from 96%, fading in, quick to start and settling softly; a plain fade with Windows' animation
    /// effects off.</summary>
    public void Open(PhoneSetup s, Point? from)
    {
        if (IsOpen) return;
        IsOpen = true;
        setup = s;
        subtitle.Text = s.Title;
        root.Visibility = Visibility.Visible;
        var first = !Prefs.Bool("phoneAsked") && Launch.Get("phone") != "true" || Launch.Get("phone") == "first";
        if (first) Show(Phase.First); else Start();
        // (the screenshot suite's test: the picture held until the files sent have come, 40 seconds at most)
        if (Testing && !holding) { holding = true; Shot.Began(); hold = Later(40000, Release); }
        root.UpdateLayout();
        var w = Math.Max(1, root.ActualWidth);
        var h = Math.Max(1, root.ActualHeight);
        root.RenderTransformOrigin = from is { } p ? new Point(Math.Clamp(p.X / w, 0, 1), Math.Clamp(p.Y / h, 0, 1)) : new Point(0.5, 0.8);
        var scale = (ScaleTransform)root.RenderTransform;
        if (Motion.On)
        {
            Motion.FromTo(root, UIElement.OpacityProperty, 0, 1, 240, Strong);
            Motion.FromTo(scale, ScaleTransform.ScaleXProperty, 0.96, 1, 240, Strong);
            Motion.FromTo(scale, ScaleTransform.ScaleYProperty, 0.96, 1, 240, Strong);
        }
        else if (Plain) Fade(root, 0, 1, 150);
        // (the keys go to the panel: Done takes Return, Escape puts it away)
        root.Dispatcher.BeginInvoke(() => done.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        Shot.Moved();
    }

    /// <summary>Fast out, settling softly: cubic-bezier(0.23, 1, 0.32, 1).</summary>
    private static readonly Bezier Strong = new(0.23, 1, 0.32, 1);

    /// <summary>Windows' animation effects off (not the screenshot suite's, which takes everything at once): a short fade.</summary>
    private static bool Plain => !Motion.On && !Shot.Asked && Fx.Motion;

    private static void Fade(UIElement e, double from, double to, double ms, Action? after = null)
    {
        var a = new DoubleAnimation(from, to, Motion.Ms(ms));
        if (after != null) a.Completed += (_, _) => after();
        e.BeginAnimation(UIElement.OpacityProperty, a);
    }

    /// <summary>The panel put away: the server stopped. What came stays in the hand-in, to be looked over and handed in.</summary>
    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        StopServer();
        void Gone()
        {
            if (IsOpen) return;
            root.Visibility = Visibility.Collapsed;
            root.BeginAnimation(UIElement.OpacityProperty, null);
            root.Opacity = 1;
            Closed?.Invoke();
        }
        if (Motion.On)
        {
            var scale = (ScaleTransform)root.RenderTransform;
            Motion.FromTo(scale, ScaleTransform.ScaleXProperty, 1, 0.97, 150, Motion.Accelerate);
            Motion.FromTo(scale, ScaleTransform.ScaleYProperty, 1, 0.97, 150, Motion.Accelerate);
            Motion.FromTo(root, UIElement.OpacityProperty, root.Opacity, 0, 150, Motion.Accelerate, 0, Gone);
        }
        else if (Plain) Fade(root, root.Opacity, 0, 150, Gone);
        else Gone();
    }

    /// <summary>The hand-in over (sent or cancelled): the server stopped, what the phone sent thrown away.</summary>
    public void End()
    {
        IsOpen = false;
        StopServer();
        foreach (var f in folders) { try { Directory.Delete(f, true); } catch { } }
        folders.Clear();
    }

    /// <summary>The photos' PDF taken out of the hand-in: the photos in it are done with.</summary>
    public void PhotosRemoved() => pages.Clear();

    // ---- the server ----

    private void Start()
    {
        StopServer();
        items.Clear();
        list.Children.Clear();
        pages.Clear();
        qr = null;
        openedAt.Restart();
        Sweep();
        address = PhoneLan.Best() ?? (Testing ? new PhoneLan.Address(IPAddress.Loopback, "network") : null);
        if (address == null) { Show(Phase.NoNetwork); return; }
        var s = setup!;
        if (PhonePage.Build(s) is not { } page) { Show(Phase.Failed, "A part of Simpl is missing. Install Simpl again to use your phone."); return; }
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SimplPhone-" + Guid.NewGuid().ToString("N"));
        try { Directory.CreateDirectory(dir); }
        catch { Show(Phase.Failed, "There’s no room to keep what your phone sends."); return; }
        folders.Add(dir);
        Show(Phase.Starting);
        var ui = root.Dispatcher;
        var srv = new PhoneServer(address.Ip, page, dir, s.Limit, 500L * 1024 * 1024, PhonePage.Refusal(s), e => ui.BeginInvoke(() => Handle(e)));
        server = srv;
        var local = srv.Start(Testing);
        if (Testing && local != null) Log.Write($"phone hand-in: test address {local}");
        Touch();
    }

    private void StopServer()
    {
        idle?.Stop();
        idle = null;
        server?.Stop();
        server = null;
        Release();
    }

    private void Release()
    {
        hold?.Stop();
        hold = null;
        if (!holding) return;
        holding = false;
        Shot.Ended();
    }

    /// <summary>(The screenshot suite) the picture let go a little after the last file came, so it shows each one sent.</summary>
    private void SettleSoon()
    {
        if (!holding) return;
        hold?.Stop();
        hold = Later(2500, Release);
    }

    private static System.Windows.Threading.DispatcherTimer Later(int ms, Action act)
    {
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); act(); };
        t.Start();
        return t;
    }

    /// <summary>15 minutes with nothing from the phone: stopped, and said so.</summary>
    private void Touch()
    {
        idle?.Stop();
        idle = Later(15 * 60 * 1000, () =>
        {
            if (server == null) return;
            StopServer();
            Show(Phase.Idle);
        });
    }

    /// <summary>Folders a Hand In left behind when Simpl did not get to clean up (a day old or more), cleaned away.</summary>
    private static void Sweep()
    {
        try
        {
            foreach (var d in Directory.EnumerateDirectories(System.IO.Path.GetTempPath(), "SimplPhone-*"))
                if (Directory.GetLastWriteTimeUtc(d) < DateTime.UtcNow.AddDays(-1)) { try { Directory.Delete(d, true); } catch { } }
        }
        catch { }
    }

    private void Handle(PhoneEvent e)
    {
        if (server == null) return;
        Touch();
        switch (e)
        {
            case PhoneEvent.Ready r:
                qr = new QRCodeGenerator().CreateQrCode(r.Url.ToString(), QRCodeGenerator.ECCLevel.M);
                Log.Write($"phone hand-in: ready on {address?.Kind}");
                Show(Phase.Waiting);
                break;
            case PhoneEvent.Opened:
                if (Shot.Asked) Log.Write("phone hand-in: opened");
                Connect();
                break;
            case PhoneEvent.Began b:
                Connect();
                var item = new Item { Id = b.Id, Name = b.Name, Size = b.Size };
                items.Add(item);
                AddRow(item);
                break;
            case PhoneEvent.Progress p:
                if (items.Find(x => x.Id == p.Id) is { } it)
                {
                    it.Received = p.Got;
                    if (it.Bar != null) it.Bar.Value = it.Size > 0 ? 100.0 * it.Received / it.Size : 0;
                    if (it.Words != null) it.Words.Text = Sending(it);
                }
                break;
            case PhoneEvent.Received got:
                Took(got);
                break;
            case PhoneEvent.Lost l:
                if (items.Find(x => x.Id == l.Id) is { } lost) { lost.State = "lost"; Repaint(lost); }
                break;
            case PhoneEvent.Failed f:
                StopServer();
                Show(Phase.Failed, f.Why);
                break;
        }
    }

    private static string Sending(Item it) =>
        $"{(it.Size > 0 ? Math.Round(100.0 * it.Received / it.Size) : 0)}% of {AssignKit.SizeText(it.Size)}";

    /// <summary>A file arrived whole: shown with its picture, and given to the hand-in — a photo into the photos' PDF when
    /// that is asked for.</summary>
    private void Took(PhoneEvent.Received got)
    {
        if (items.Find(x => x.Id == got.Id) is not { } it) return;
        byte[] data;
        try { data = File.ReadAllBytes(got.Path); }
        catch { it.State = "lost"; Repaint(it); return; }
        it.Received = data.LongLength;
        it.Thumb = Thumbnail(got.Path);
        if (Shot.Asked) Log.Write($"phone hand-in: received {got.Name} {data.Length} bytes");
        var ext = System.IO.Path.GetExtension(got.Name).TrimStart('.').ToLowerInvariant();
        var type = got.Type is "" or "application/octet-stream" ? AssignKit.Mime(got.Name) : got.Type;
        if (CombineOffered && combine.IsChecked == true && PhonePage.Images.Contains(ext)) MakePage(it, data);
        else
        {
            it.State = "added";
            Repaint(it);
            Arrived?.Invoke(got.Name, type, data, false);
            Count();
            SettleSoon();
        }
    }

    /// <summary>A photo made a page (turned upright, no larger than it needs to be), after the ones before it; the PDF made
    /// again.</summary>
    private void MakePage(Item it, byte[] data)
    {
        var before = making;
        making = Run();
        async Task Run()
        {
            try { await before; } catch { }
            try { await Make(); }
            catch (Exception e) { Log.Write("phone hand-in: photos: " + e.Message); }
        }
        async Task Make()
        {
            var page = await PhotoPdf.Make(data);
            if (page == null) { it.State = "unreadable"; Repaint(it); return; }
            pages.Add(page);
            it.State = "page";
            it.Page = pages.Count;
            Repaint(it);
            var all = pages.ToList();
            byte[]? pdf;
            try { pdf = await Task.Run(() => PhotoPdf.Combine(all)); }
            catch (Exception e) { Log.Write("phone hand-in: photos' pdf: " + e.Message); pdf = null; }
            if (pdf == null) return;
            Arrived?.Invoke(PdfName, "application/pdf", pdf, true);
            Count();
            SettleSoon();
        }
    }

    /// <summary>The phone opened the page: the panel moves on to what it sends — the code shrinking into its corner as
    /// "Phone connected", the list fading in.</summary>
    private void Connect()
    {
        if (phase != Phase.Waiting) return;
        phase = Phase.Connected;
        title.Text = "Phone connected";
        subtitle.Text = "Take photos or choose files on your phone.";
        Motion.FadeIn(title, Motion.Normal);
        Motion.FadeIn(subtitle, Motion.Normal);
        var arriving = Arriving();
        void Swap()
        {
            if (qr != null) iconHost.Content = new PhoneQrView(qr, settled: true) { Width = 46, Height = 46 };
            stateHost.Content = arriving;
            if (Motion.On) Motion.FadeIn(arriving, Motion.Normal); else if (Plain) Fade(arriving, 0, 1, 150);
            Shot.Moved();
        }
        if (!Motion.On || qr == null || qrBox == null || waitingView == null || !qrBox.IsLoaded) { Swap(); return; }
        // the big code flies into the corner where the small one will be, while what was under it fades: a copy of it on
        // the panel's top layer (the scrolling part under the heading would cut it off), the code itself hidden meanwhile
        var from = qrBox.TranslatePoint(new Point(0, 0), root);
        var to = iconHost.TranslatePoint(new Point(0, 0), root);
        var size = Math.Max(1, qrBox.ActualWidth);
        var sc = new ScaleTransform(1, 1);
        var tr = new TranslateTransform();
        var flyer = new PhoneQrView(qr, settled: true) { Width = size, Height = size, RenderTransformOrigin = new Point(0, 0) };
        flyer.RenderTransform = new TransformGroup { Children = { sc, tr } };
        Canvas.SetLeft(flyer, from.X);
        Canvas.SetTop(flyer, from.Y);
        flight.Children.Add(flyer);
        qrBox.Opacity = 0;
        foreach (var other in ((Panel)waitingView).Children.OfType<UIElement>().Where(x => x != qrBox)) Motion.FromTo(other, UIElement.OpacityProperty, 1, 0, Motion.Fast, Motion.Accelerate);
        Motion.FadeOut(iconHost, () => { }, Motion.Fast);
        Motion.FromTo(sc, ScaleTransform.ScaleXProperty, 1, 46 / size, Motion.Slow, Strong);
        Motion.FromTo(sc, ScaleTransform.ScaleYProperty, 1, 46 / size, Motion.Slow, Strong);
        Motion.FromTo(tr, TranslateTransform.XProperty, 0, to.X - from.X, Motion.Slow, Strong);
        Motion.FromTo(tr, TranslateTransform.YProperty, 0, to.Y - from.Y, Motion.Slow, Strong, 0, () =>
        {
            iconHost.BeginAnimation(UIElement.OpacityProperty, null);
            iconHost.Opacity = 1;
            Swap();
            flight.Children.Clear();
        });
    }

    // ---- what the panel shows ----

    private void Show(Phase p, string? why = null)
    {
        phase = p;
        title.Text = "Send from Phone";
        subtitle.Text = setup?.Title ?? "";
        iconHost.Content = K.IconTile(SymbolRegular.Phone24, accent, 40);
        FrameworkElement view = p switch
        {
            Phase.First => Problem(SymbolRegular.Shield24, "Windows will ask whether Simpl may use this network.",
                "Choose Allow, so your phone can reach this PC. Simpl only listens while this panel is open.", "Show the Code", () =>
                {
                    Prefs.Set("phoneAsked", true);
                    Start();
                }, accentButton: true),
            Phase.NoNetwork => Problem(SymbolRegular.WifiOff24, "Connect this computer to Wi-Fi to use your phone.",
                "Your phone must be on the same Wi-Fi. Some school networks block this — your phone’s hotspot works.", "Try Again", Start),
            Phase.Failed => Problem(SymbolRegular.Warning24, "Send from Phone couldn’t start.", why, "Try Again", Start),
            Phase.Idle => Problem(SymbolRegular.Clock24, "Stopped after 15 minutes without use.", "Start again to get a new code.", "Start Again", Start),
            _ => Waiting(),
        };
        stateHost.Content = view;
        Where();
        combine.Visibility = CombineOffered && p is Phase.Starting or Phase.Waiting or Phase.Connected ? Visibility.Visible : Visibility.Collapsed;
        combine.ToolTip = $"A page each, in the order they come, as {PdfName}";
        Count();
        Shot.Moved();
    }

    private FrameworkElement Waiting()
    {
        var box = new Border
        {
            Width = 212, Height = 212, CornerRadius = new CornerRadius(14), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
            Effect = K.Shadow(16, 2, 0.12),
        };
        if (qr != null)
        {
            bigQr = new PhoneQrView(qr, settled: false);
            box.Child = bigQr;
            // (after the panel has mostly grown in: then the code arrives)
            bigQr.Reveal(Math.Max(0, 140 - openedAt.Elapsed.TotalMilliseconds), Motion.On, Plain);
            System.Windows.Automation.AutomationProperties.SetName(box, "QR code: scan it with your phone’s camera");
        }
        else box.Child = K.Spinner(20);
        qrBox = box;
        var status = K.H(8, new PulseDot(accent), K.Text(phase == Phase.Starting ? "Getting ready…" : "Waiting for your phone…", Ty.BodyStrong));
        status.HorizontalAlignment = HorizontalAlignment.Center;
        var hint = K.Wrapped("Point your phone’s camera at the code, then open the link it shows.", Ty.Body, B.Text2);
        hint.TextAlignment = TextAlignment.Center;
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        var v = K.V(12, box, status, hint);
        v.Margin = new Thickness(0, 4, 0, 0);
        waitingView = v;
        return v;
    }

    private FrameworkElement Arriving()
    {
        var card = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 4, 12, 4), MinHeight = 64 };
        card.Bg(B.Card).Stroke(B.CardStroke);
        if (items.Count == 0 && list.Children.Count == 0)
        {
            var none = K.H(10, K.Icon(SymbolRegular.ArrowCircleDown24, 20, B.Text2), K.Wrapped("Nothing yet — what you send shows here, and goes into your hand-in.", Ty.Body, B.Text2));
            none.Margin = new Thickness(4, 14, 4, 14);
            list.Children.Add(none);
        }
        AssignKit.Detach(list);
        card.Child = list;
        return card;
    }

    private void Where()
    {
        whereNote.Children.Clear();
        var shown = phase is Phase.Starting or Phase.Waiting or Phase.Connected;
        whereNote.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown) return;
        if (address != null)
        {
            var words = address.Kind == "network" ? $"This PC is at {address.Ip}" : $"This PC is on {address.Kind} at {address.Ip}";
            whereNote.Children.Add(K.H(8, K.Icon(address.Kind == "Wi-Fi" ? SymbolRegular.Wifi124 : SymbolRegular.PlugConnected24, 16, B.Text2), K.Text(words, Ty.Body)));
        }
        whereNote.Children.Add(K.Wrapped("Your phone must be on the same Wi-Fi. Some school networks block this — your phone’s hotspot works.", Ty.Caption, B.Text2));
    }

    private FrameworkElement Problem(SymbolRegular icon, string words, string? more, string button, Action act, bool accentButton = false)
    {
        var v = K.V(10, K.Icon(icon, 32, B.Text2).Align(HorizontalAlignment.Center), K.Wrapped(words, Ty.BodyStrong).Center());
        if (more is { Length: > 0 }) v.Children.Add(K.Wrapped(more, Ty.Body, B.Text2).Center());
        var b = K.Button(button, act, accent: accentButton);
        b.HorizontalAlignment = HorizontalAlignment.Center;
        b.Margin = new Thickness(0, 6, 0, 0);
        v.Children.Add(b);
        v.Margin = new Thickness(8, 26, 8, 18);
        return v;
    }

    /// <summary>How many of the phone's files are in the hand-in, by the Done button (which is lit once there are some).</summary>
    private void Count()
    {
        // (the files the phone's things made in the hand-in: each added one, and the photos' PDF once)
        var n = items.Count(i => i.State == "added") + (items.Any(i => i.State == "page") ? 1 : 0);
        addedWords.Text = n == 0 ? "" : n == 1 ? "1 file in your hand-in" : $"{n} files in your hand-in";
        addedWords.Fg(B.Text2);
        done.Appearance = n > 0 ? ControlAppearance.Primary : ControlAppearance.Secondary;
    }

    private void AddRow(Item it)
    {
        if (list.Children.Count > 0 && items.Count == 1) list.Children.Clear(); // ("Nothing yet" goes)
        var row = Row(it);
        list.Children.Add(row);
        if (Motion.On) Motion.Enter(row, 0, 8, 250);
    }

    private void Repaint(Item it)
    {
        var i = items.IndexOf(it);
        if (i < 0 || i >= list.Children.Count) return;
        // (a panel's child is taken out before another goes in its place: WPF will not set one over another)
        list.Children.RemoveAt(i);
        list.Children.Insert(i, Row(it));
    }

    /// <summary>A file from the phone: its picture, its name, and where it is — coming (with its progress), in the hand-in,
    /// a page of the photos' PDF, or lost on the way.</summary>
    private FrameworkElement Row(Item it)
    {
        FrameworkElement thumb = it.Thumb is { } src
            ? new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(6), Background = new ImageBrush(src) { Stretch = Stretch.UniformToFill } }
            : AssignKit.FileIcon(it.Name, 32).Margin(2);
        var name = K.Text(it.Name, Ty.Body);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        FrameworkElement status;
        it.Bar = null;
        it.Words = null;
        switch (it.State)
        {
            case "sending":
                it.Bar = new System.Windows.Controls.ProgressBar { Height = 4, Width = 140, Minimum = 0, Maximum = 100, Value = it.Size > 0 ? 100.0 * it.Received / it.Size : 0, VerticalAlignment = VerticalAlignment.Center };
                it.Words = K.Text(Sending(it), Ty.Caption, B.Text2);
                status = K.H(8, it.Bar, it.Words);
                break;
            case "added":
                status = K.H(6, K.Icon(SymbolRegular.CheckmarkCircle24, 14, B.Success, filled: true), K.Text("Added to your hand-in", Ty.Caption, B.Success));
                break;
            case "page":
                status = K.H(6, K.Icon(SymbolRegular.DocumentPdf24, 14, B.Success), K.Text($"Page {it.Page} of {PdfName}", Ty.Caption, B.Success));
                break;
            case "unreadable":
                status = K.H(6, K.Icon(SymbolRegular.Warning24, 14, B.Caution, filled: true), K.Text("This photo couldn’t be read", Ty.Caption, B.Caution));
                break;
            default:
                status = K.H(6, K.Icon(SymbolRegular.Warning24, 14, B.Caution, filled: true), K.Text("Didn’t arrive — send it again from your phone", Ty.Caption, B.Caution));
                break;
        }
        var words = K.V(3, name, status);
        words.VerticalAlignment = VerticalAlignment.Center;
        var row = K.Columns("auto, *", thumb.Margin(0, 0, 10, 0).Col(0), words.Col(1));
        row.Margin = new Thickness(0, 8, 0, 8);
        System.Windows.Automation.AutomationProperties.SetName(row, it.Name);
        return row;
    }

    /// <summary>A picture's small copy, turned the way the camera was held; a file's icon otherwise (null).</summary>
    private static ImageSource? Thumbnail(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".tif" or ".tiff" or ".webp" or ".heic")) return null;
        try
        {
            var turn = 0;
            using (var fs = File.OpenRead(path))
            {
                var frame = BitmapFrame.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                if (frame.Metadata is BitmapMetadata m && m.ContainsQuery("System.Photo.Orientation") && m.GetQuery("System.Photo.Orientation") is ushort o)
                    turn = o switch { 6 => 90, 3 => 180, 8 => 270, _ => 0 };
            }
            var b = new BitmapImage();
            b.BeginInit();
            b.UriSource = new Uri(path);
            b.DecodePixelWidth = 96;
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.EndInit();
            b.Freeze();
            if (turn == 0) return b;
            var t = new TransformedBitmap(b, new RotateTransform(turn));
            t.Freeze();
            return t;
        }
        catch { return null; }
    }
}

/// <summary>"Waiting for your phone…"'s dot, breathing softly (a ring swelling out of it and fading); still with Windows'
/// animation effects off.</summary>
internal sealed class PulseDot : Grid
{
    public PulseDot(Color color)
    {
        Width = 8;
        Height = 8;
        VerticalAlignment = VerticalAlignment.Center;
        var ring = new Ellipse { Fill = new SolidColorBrush(Color.FromArgb(90, color.R, color.G, color.B)), RenderTransformOrigin = new Point(0.5, 0.5) };
        var scale = new ScaleTransform(1, 1);
        ring.RenderTransform = scale;
        Children.Add(ring);
        Children.Add(new Ellipse { Fill = new SolidColorBrush(color) });
        if (!Motion.On) { ring.Visibility = Visibility.Collapsed; return; }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var grow = new DoubleAnimation(1, 2.4, Motion.Ms(1800)) { EasingFunction = ease, RepeatBehavior = RepeatBehavior.Forever };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        ring.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, Motion.Ms(1800)) { EasingFunction = ease, RepeatBehavior = RepeatBehavior.Forever });
    }
}

/// <summary>
/// The QR code (the Mac's PhoneQRCode), drawn in one pass by one clock: dark, softly rounded modules on white with four
/// modules of quiet space — white in dark mode too, so a camera always reads it. It arrives once: the three finder
/// squares settle first (from 85%, fading in), then the data ripples out from the centre — each module from 60% and
/// transparent to whole over 120 ms, later the further out it is — all of it in place within 420 ms, never from nothing.
/// With Windows' animation effects off, a plain 150 ms fade. Once in place it is drawn no more (WPF keeps the drawing).
/// </summary>
internal sealed class PhoneQrView : FrameworkElement
{
    private const double Length = 0.42;
    private static readonly Color Ink = Color.FromRgb(0x14, 0x14, 0x1A);
    private static readonly SolidColorBrush[] Inks = Enumerable.Range(0, 33).Select(i =>
    {
        var b = new SolidColorBrush(Color.FromArgb((byte)Math.Round(i * 255 / 32.0), Ink.R, Ink.G, Ink.B));
        b.Freeze();
        return b;
    }).ToArray();

    private readonly int n;
    /// <summary>The dark data modules (not the finders'): where, and how far from the centre (0 at it, 1 at a corner).</summary>
    private readonly (int X, int Y, double D)[] modules;
    private readonly Stopwatch clock = new();
    private double delay;
    private bool running, settled, plain;

    public PhoneQrView(QRCodeData data, bool settled)
    {
        // (QRCoder's own quiet zone cut away: the symbol is where the dark modules are)
        var m = data.ModuleMatrix;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = 0; y < m.Count; y++)
            for (var x = 0; x < m[y].Count; x++)
                if (m[y][x]) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
        n = Math.Max(0, maxX - minX + 1);
        var mid = (n - 1) / 2.0;
        var far = Math.Max(1, mid * Math.Sqrt(2));
        var list = new List<(int, int, double)>();
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
                if (m[minY + y][minX + x] && !InFinder(x, y, n)) list.Add((x, y, Math.Sqrt((x - mid) * (x - mid) + (y - mid) * (y - mid)) / far));
        modules = list.ToArray();
        this.settled = settled;
        SnapsToDevicePixels = false;
        IsHitTestVisible = false;
    }

    private static bool InFinder(int x, int y, int n) => (x < 7 && y < 7) || (x >= n - 7 && y < 7) || (x < 7 && y >= n - 7);

    /// <summary>The code's arrival, after <paramref name="delayMs"/>: rippled in, faded (<paramref name="fade"/>), or at once.</summary>
    public void Reveal(double delayMs, bool animate, bool fade)
    {
        if (!animate && !fade) { settled = true; InvalidateVisual(); return; }
        settled = false;
        plain = !animate;
        delay = delayMs / 1000;
        clock.Restart();
        if (!running)
        {
            running = true;
            CompositionTarget.Rendering += Tick;
        }
        Unloaded += (_, _) => Stop();
    }

    private void Stop()
    {
        if (!running) return;
        running = false;
        CompositionTarget.Rendering -= Tick;
    }

    private void Tick(object? sender, EventArgs e)
    {
        if (clock.Elapsed.TotalSeconds - delay >= (plain ? 0.15 : Length))
        {
            settled = true;
            Stop();
        }
        InvalidateVisual();
    }

    /// <summary>Fast out, settling softly: close to cubic-bezier(0.23, 1, 0.32, 1).</summary>
    private static double Ease(double x)
    {
        var p = Math.Clamp(x, 0, 1);
        return 1 - Math.Pow(1 - p, 5);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var side = Math.Min(ActualWidth, ActualHeight);
        if (side <= 0 || n < 21) return;
        var ox = (ActualWidth - side) / 2;
        var oy = (ActualHeight - side) / 2;
        var cell = side / (n + 8);
        var qx = ox + cell * 4;
        var qy = oy + cell * 4;
        var t = settled ? Length + 1 : clock.Elapsed.TotalSeconds - delay;
        var corner = Math.Max(6, side * 0.07);
        dc.DrawRoundedRectangle(Brushes.White, null, new Rect(ox, oy, side, side), corner, corner);
        var fade = plain && !settled ? Math.Clamp(t / 0.15, 0, 1) : 1;
        if (fade < 1) dc.PushOpacity(fade);
        var done = settled || plain;
        static Rect Box(double cx, double cy, double s) => new(cx - s / 2, cy - s / 2, s, s);
        // the finders first: each a rounded ring around a rounded square
        var finders = new[] { (0, 0), (n - 7, 0), (0, n - 7) };
        for (var i = 0; i < 3; i++)
        {
            var p = done ? 1 : Ease((t - i * 0.03) / 0.16);
            if (p <= 0) continue;
            var k = (0.85 + 0.15 * p) * cell;
            var cx = qx + (finders[i].Item1 + 3.5) * cell;
            var cy = qy + (finders[i].Item2 + 3.5) * cell;
            var brush = Inks[(int)Math.Round(p * 32)];
            var ring = new GeometryGroup { FillRule = FillRule.EvenOdd };
            ring.Children.Add(new RectangleGeometry(Box(cx, cy, 7 * k), 1.9 * k, 1.9 * k));
            ring.Children.Add(new RectangleGeometry(Box(cx, cy, 5 * k), 1.25 * k, 1.25 * k));
            dc.DrawGeometry(brush, null, ring);
            dc.DrawRoundedRectangle(brush, null, Box(cx, cy, 3 * k), 0.85 * k, 0.85 * k);
        }
        // then the data, rippling out from the centre
        var s = cell * 0.92;
        foreach (var (x, y, d) in modules)
        {
            var p = done ? 1 : Ease((t - 0.06 - d * 0.2) / 0.12);
            if (p <= 0) continue;
            var size = s * (0.6 + 0.4 * p);
            dc.DrawRoundedRectangle(Inks[(int)Math.Round(p * 32)], null, Box(qx + (x + 0.5) * cell, qy + (y + 0.5) * cell, size), size * 0.3, size * 0.3);
        }
        if (fade < 1) dc.Pop();
    }
}

/// <summary>
/// Photos made into one PDF for Send from Phone (the Mac's PhotoPDF): each turned upright (its camera's orientation),
/// made no larger than 2200 pixels on its long side and saved as a JPEG of good quality (WPF's own codecs), then laid on a page of the
/// region's paper — Letter where it is used, A4 elsewhere — turned to landscape for a wide photo, a page each, in order.
/// </summary>
internal static class PhotoPdf
{
    public sealed record Page(byte[] Jpeg, int Width, int Height);

    /// <summary>A photo made a page, away from the window's thread (WPF's own codecs, which need nothing awaited).</summary>
    public static Task<Page?> Make(byte[] data) => Task.Run(() => MakeNow(data));

    private static Page? MakeNow(byte[] data)
    {
        try
        {
            using var input = new MemoryStream(data);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            BitmapSource image = decoder.Frames[0];
            // (the way the camera was held: a quarter, half or three-quarter turn)
            var turn = 0;
            try
            {
                if (decoder.Frames[0].Metadata is BitmapMetadata m && m.ContainsQuery("System.Photo.Orientation") && m.GetQuery("System.Photo.Orientation") is ushort o)
                    turn = o switch { 6 => 90, 3 => 180, 8 => 270, _ => 0 };
            }
            catch { }
            var big = Math.Max(image.PixelWidth, image.PixelHeight);
            if (big > 2200) image = new TransformedBitmap(image, new ScaleTransform(2200.0 / big, 2200.0 / big));
            if (turn != 0) image = new TransformedBitmap(image, new RotateTransform(turn));
            if (image.Format != PixelFormats.Bgr24 && image.Format != PixelFormats.Bgr32 && image.Format != PixelFormats.Gray8)
                image = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
            var encoder = new JpegBitmapEncoder { QualityLevel = 72 };
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var output = new MemoryStream();
            encoder.Save(output);
            return new Page(output.ToArray(), image.PixelWidth, image.PixelHeight);
        }
        catch (Exception e)
        {
            Log.Write("phone hand-in: photo: " + e.Message);
            return null;
        }
    }

    public static byte[] Combine(IReadOnlyList<Page> pages)
    {
        var letter = !System.Globalization.RegionInfo.CurrentRegion.IsMetric;
        double shortSide = letter ? 612 : 595.28, longSide = letter ? 792 : 841.89;
        var images = new List<XImage>();
        try
        {
            using var doc = new PdfDocument();
            doc.Info.Creator = "Simpl";
            foreach (var p in pages)
            {
                var image = XImage.FromStream(new MemoryStream(p.Jpeg));
                images.Add(image);
                var wide = p.Width > p.Height;
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(wide ? longSide : shortSide);
                page.Height = XUnit.FromPoint(wide ? shortSide : longSide);
                using var gfx = XGraphics.FromPdfPage(page);
                const double margin = 18;
                var fit = Math.Min((page.Width.Point - margin * 2) / p.Width, (page.Height.Point - margin * 2) / p.Height);
                double w = p.Width * fit, h = p.Height * fit;
                gfx.DrawImage(image, (page.Width.Point - w) / 2, (page.Height.Point - h) / 2, w, h);
            }
            using var ms = new MemoryStream();
            doc.Save(ms);
            return ms.ToArray();
        }
        finally
        {
            foreach (var i in images) i.Dispose();
        }
    }
}
