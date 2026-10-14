using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Simpl.Engine;
using Simpl.Screens;
using Simpl.Support;

namespace Simpl.Shell;

/// <summary>
/// The live check (the Mac's LiveCheck; README, Live checks): while the window shows, the screen on show reads what it
/// shows again once that is a minute old — a course's page once it is ten minutes old — with the engine's maxAge, the
/// pane's counts beside it; an answer the same as the one on screen changes nothing (no build, no flicker, the scroll and
/// the keyboard where they were), a new one is shown in place (the Dashboard's parts, one by one). Nothing while the
/// window is minimized or in the notification area (memory given back there stays given back), offline, or while the
/// page is not ready; back after more than a minute away, at once. It waits while the student is busy with something it
/// would disturb (<see cref="Held"/>): a quiz, an Alta or Macmillan assignment, typing, What-If, Hand In, a dialog, a
/// menu, the tour, a drag, a counter's panel or a quick look — and what it found waits with it. One line in simpl.log a
/// check: "live: dashboard unchanged · 0 reads", "live: dashboard updated counter:today,list(~1) · 7 reads".
/// <see cref="LiveClock"/> keeps the timing (Support/LiveRules.cs, tested on its own).
/// </summary>
internal sealed class LiveCheck : IDisposable
{
    /// <summary>The screenshot suite's live runs (--live-every): each check's line, and each screen's build, said in
    /// simpl.log for its checks.</summary>
    public static bool Says => Launch.IsDevelopment && Launch.Get("live-every") != null;

    /// <summary>On, unless --no-live (development); in a picture run only when --live-every asks for it (a picture is of
    /// what the screen showed, nothing read again behind it).</summary>
    public static bool Enabled => !(Launch.IsDevelopment && Launch.Flag("no-live")) && (!Shot.Asked || Says);

    /// <summary>The base interval: a minute, or what --live-every says in a development run (seconds).</summary>
    private static double Every => Says ? Math.Max(1, Launch.Number("live-every", LiveRules.Every)) : LiveRules.Every;

    private static long lastKey;
    private static bool offline, listening;

    private readonly Engine.Engine engine;
    /// <summary>The window whose screen it checks: the main one, or a place in a window of its own (PlaceWindow).</summary>
    private readonly Window window;
    private readonly Func<Screen?> current;
    /// <summary>The main window's check: the one that changes the mock school for the screenshot suite.</summary>
    private readonly bool main;
    private readonly LiveClock clock = new(Every);
    private readonly DispatcherTimer timer;
    /// <summary>What a check found new, waiting to be shown (held: the student is busy), and for which screen.</summary>
    private List<LivePending>? found;
    private Screen? foundFor;
    private string foundCost = "";
    private bool foundOk;
    /// <summary>What held the last check, said once in the log while it holds.</summary>
    private string? heldBy;
    private bool disposed, polling;

    public LiveCheck(Engine.Engine engine, Window window, Func<Screen?> current, bool main)
    {
        this.engine = engine;
        this.window = window;
        this.current = current;
        this.main = main;
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(Math.Min(1, Every / 5)) };
        timer.Tick += (_, _) => Tick();
        if (!Enabled) return;
        Listen();
        window.IsVisibleChanged += Moved;
        window.StateChanged += Moved;
        Moved(null, EventArgs.Empty);
        if (!main) return;
        if (Says) Log.Write($"live: on, every {Every:0.#} s (a course's page every {LiveRules.Interval(true, Every):0.#} s)");
        ChangeLater();
    }

    /// <summary>The window out of sight (minimized, in the notification area): the timer stops — not a tick until it is
    /// back, when it looks at once.</summary>
    private bool Away => !window.IsVisible || window.WindowState == WindowState.Minimized;

    private void Moved(object? sender, EventArgs e)
    {
        if (disposed) return;
        if (Away)
        {
            // (the clock told the window went, so it knows how long it was away)
            clock.Step(Environment.TickCount64, 0, 0, away: true, offline: false, () => true);
            timer.Stop();
            return;
        }
        if (!timer.IsEnabled) timer.Start();
        Poke();
    }

    private void Moved(object? sender, DependencyPropertyChangedEventArgs e) => Moved(sender, EventArgs.Empty);

    /// <summary>Looked at now (a screen shown, the window back): a screen opened again with what it shows a minute old
    /// is checked at once.</summary>
    public void Poke()
    {
        if (!Enabled || disposed) return;
        window.Dispatcher.BeginInvoke(DispatcherPriority.Background, Tick);
    }

    /// <summary>Keys and the network, listened to once for the whole app.</summary>
    private static void Listen()
    {
        if (listening) return;
        listening = true;
        InputManager.Current.PostProcessInput += (_, a) =>
        {
            if (a.StagingItem.Input is KeyEventArgs { IsDown: true }) lastKey = Environment.TickCount64;
        };
        try
        {
            offline = !NetworkInterface.GetIsNetworkAvailable();
            NetworkChange.NetworkAvailabilityChanged += (_, e) => offline = !e.IsAvailable;
        }
        catch { offline = false; }
    }

    private void Tick()
    {
        try { Look(); }
        catch (Exception e) { Log.Write("live: " + e); }
    }

    /// <summary>A look at the clock: what a check found shown once nothing holds it; else a check begun when one is due
    /// and nothing holds it.</summary>
    private void Look()
    {
        if (disposed || polling) return;
        var now = Environment.TickCount64;
        // (what a check found, held until the student is done)
        if (found != null) { Show(); return; }
        var s = current();
        var reads = s?.LiveReads ?? Array.Empty<ILiveRead>();
        var interval = s == null || reads.Count == 0 ? 0 : LiveRules.Interval(s.CoursePage, Every);
        var pane = engine.PaneCounts;
        var oldest = reads.Select(r => r.FreshAt).DefaultIfEmpty(0).Min();
        if (pane.CanCheck) oldest = Math.Min(oldest, pane.FreshAt);
        var unready = !engine.Ready || engine.Phase != Phase.Native || engine.Login.Covers || engine.Refreshing;
        string? why = null;
        var step = clock.Step(now, interval, oldest, Away, offline || unready, () => (why = Held(s)) != null);
        if (step == LiveStep.Held)
        {
            if (why != heldBy && s != null) Log.Write($"live: {s.LiveName} waits ({why})");
            heldBy = why;
            return;
        }
        heldBy = null;
        if (step != LiveStep.Check || s == null) return;
        _ = Run(s, reads, interval);
    }

    /// <summary>
    /// One check of the screen on show: each read asked again at its age, the pane's counts with them, all at once (the
    /// engine asks the school once for what several need); what the page read of the school meanwhile counted. Nothing
    /// new: the line says so, and that is all. Something new: shown now, or once what holds it ends.
    /// </summary>
    private async Task Run(Screen s, IReadOnlyList<ILiveRead> reads, double interval)
    {
        clock.Began();
        polling = true;
        var ok = false;
        try
        {
            var before = await engine.Reads();
            var all = reads.ToList();
            if (engine.PaneCounts.CanCheck) all.Add(engine.PaneCounts);
            var results = await Task.WhenAll(all.Select(r => r.Check(engine, LiveRules.AgeFor(r.Call ?? "", interval, Every))));
            var after = await engine.Reads();
            var cost = before >= 0 && after >= before ? $"{after - before} reads" : "? reads";
            var failed = results.Count(r => r.Kind == LiveAnswerKind.Failed);
            ok = failed == 0;
            var news = results.Where(r => r.Kind == LiveAnswerKind.New && r.Apply != null).ToList();
            if (disposed) return;
            if (news.Count == 0)
            {
                Log.Write(ok ? $"live: {s.LiveName} unchanged · {cost}" : $"live: {s.LiveName} could not check {failed} of {results.Length} · {cost}");
                return;
            }
            found = news;
            foundFor = s;
            foundCost = cost;
            foundOk = ok;
        }
        catch (Exception e) { Log.Write($"live: {s.LiveName} check failed: {e.Message}"); }
        finally
        {
            clock.Ended(Environment.TickCount64, ok, interval);
            polling = false;
            if (!ok && clock.Failures > 0) Log.Write($"live: next try in {LiveRules.Backoff(clock.Failures, interval, Every):0} s");
        }
        if (found != null) Show();
    }

    /// <summary>What a check found, shown — unless the screen it was for is no longer on show (it is checked again when it
    /// is), or something holds it: then at a later tick.</summary>
    private void Show()
    {
        if (found == null || foundFor == null) return;
        var s = foundFor;
        if (current() != s || disposed)
        {
            Log.Write($"live: {s.LiveName} let go of what it found (no longer on show)");
            found = null;
            foundFor = null;
            return;
        }
        if (Held(s) is { } why)
        {
            if (why != heldBy) Log.Write($"live: {s.LiveName} holds what it found ({why})");
            heldBy = why;
            return;
        }
        heldBy = null;
        var news = found;
        found = null;
        foundFor = null;
        var parts = new List<string>();
        var shown = new List<ILiveRead>();
        try
        {
            shown = news.Where(p => p.Apply!()).Select(p => p.Read).ToList();
            var mine = shown.Where(r => !ReferenceEquals(r, engine.PaneCounts)).ToList();
            if (mine.Count > 0 && s.LiveApplied(mine) is { Length: > 0 } said) parts.Add(said);
            if (shown.Count > mine.Count) parts.Add("pane");
        }
        catch (Exception e)
        {
            // (never the window's end: the screen built again from what it has, as for any answer)
            Log.Write($"live: {s.LiveName} could not show what changed in place: {e}");
            s.Render();
            parts.Add("page");
        }
        Log.Write(parts.Count == 0
            ? $"live: {s.LiveName} unchanged (changed meanwhile) · {foundCost}"
            : $"live: {s.LiveName} updated {string.Join(",", parts)}{(foundOk ? "" : " (some could not check)")} · {foundCost}");
    }

    /// <summary>
    /// Why a check waits now, or null: a quiz, an Alta or Macmillan assignment open; the student typing (the keyboard in a
    /// box, or a key in the last five seconds); the screen's own (What-If, a counter's panel, a reply); Hand In or Send
    /// from Phone; a sheet, a dialog, a menu or the tour; a drag; a quick look.
    /// </summary>
    private string? Held(Screen? s)
    {
        if (Updates.Holds("quiz")) return "a quiz";
        if (Updates.Holds("Knewton Alta") || Updates.Holds("Macmillan Learning")) return "an assignment's popup";
        if (Keyboard.FocusedElement is FrameworkElement { IsVisible: true } focused && focused is TextBoxBase or System.Windows.Controls.PasswordBox or HwndHost) return "typing";
        if (LiveRules.Typing(Environment.TickCount64, lastKey)) return "typing";
        if (s?.LiveHold is { } own) return own;
        if (HandInSheet.Open) return "Hand In";
        // (a dialog over this window or the main one; a popup floating over the main window — a tool's is not one: it is
        //  used beside the page, and may stay open all day)
        var home = Application.Current?.MainWindow as MainWindow;
        if (window is ISheetWindow { Sheets.IsOpen: true } || home?.Sheets.IsOpen == true) return "a dialog";
        if (home != null && FloatPopup.Over(home).Any(p => !p.Sheet.Large)) return "a popup";
        if (home?.Touring == true) return "the tour";
        if (UpdateWatch.MenuOpen()) return "a menu";
        if (Mouse.LeftButton == MouseButtonState.Pressed || Mouse.Captured != null) return "a drag";
        if (WorkPreview.IsOpen) return "a quick look";
        return null;
    }

    /// <summary>The screenshot suite's --live-change done:20 (comma-separated for more): the mock school changed behind
    /// the engine's back that many seconds after the window's screens first show, for a check to find.</summary>
    private async void ChangeLater()
    {
        if (!Says || Launch.Get("live-change") is not { Length: > 0 } asked) return;
        try
        {
            while (!disposed && (!engine.Ready || current() == null)) await Task.Delay(250);
            var start = Environment.TickCount64;
            foreach (var one in asked.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var bits = one.Split(':');
                var after = bits.Length > 1 && double.TryParse(bits[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 20;
                var wait = (long)(after * 1000) - (Environment.TickCount64 - start);
                if (wait > 0) await Task.Delay(TimeSpan.FromMilliseconds(wait));
                if (disposed) return;
                Log.Write($"live: the mock school changed: {bits[0]} ({await engine.LiveDevChange(bits[0])})");
            }
        }
        catch (Exception e) { Log.Write("live: the mock school could not be changed: " + e.Message); }
    }

    public void Dispose()
    {
        disposed = true;
        timer.Stop();
        window.IsVisibleChanged -= Moved;
        window.StateChanged -= Moved;
    }
}
