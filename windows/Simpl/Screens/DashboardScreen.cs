using System.Numerics;
using Simpl.Components;
using Simpl.Engine;
using Simpl.Graphics;
using Simpl.Models;
using Simpl.Support;
using Simpl.UI;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Simpl.Screens;

/// <summary>
/// The Dashboard (the Mac's DashboardView, measured on its pictures): the day at a glance — a greeting; six counters,
/// each growing in place into a panel of what it counted; then one of three views, chosen in the toolbar and kept —
/// Cards (the courses as cards), List (everything coming up, day by day, ticked off where it is done) and Activity
/// (the school's recent activity). Beside the view on a wide window, under it on a narrow one: the day's work, the
/// grades skyline and the week's load per course.
/// </summary>
public sealed class DashboardScreen
{
    private readonly Engine.Engine engine;
    private readonly GlassHost host;
    private readonly Loader<Today> today;
    private readonly Loader<TodayCounts> countsQuick, countsFresh;
    private readonly Loader<DashCoursesData> courses;
    private readonly Loader<Dictionary<string, CourseProgress>> progress;
    private readonly Loader<DashSkylineData> skyKept, skyFresh;
    private readonly Loader<DashListData> list;
    private readonly Loader<DashActivityData> activity;
    private readonly Loader<ItemsSheetData> sheet;
    private readonly Dictionary<string, bool> ticked = new();
    private readonly HashSet<string> seen = new();
    private int view; // 0 Cards, 1 List, 2 Activity
    private bool? hideDone;
    private int overdueAdjust;
    private static readonly string[] Views = { "Cards", "List", "Activity" };

    // a counter's panel: which, where it grew from, and how far it has grown
    private string? openKey;
    private R openTile;
    private R panelRect;
    private bool sheetAsked;
    private double shownAt = -1;

    public DashboardScreen(Engine.Engine engine, GlassHost host)
    {
        this.engine = engine;
        this.host = host;
        today = new(host);
        countsQuick = new(host);
        countsFresh = new(host);
        courses = new(host);
        progress = new(host);
        skyKept = new(host);
        skyFresh = new(host);
        list = new(host);
        activity = new(host);
        sheet = new(host);
        view = Launch.Get("dash-view") switch { "cards" => 0, "activity" => 2, "list" => 1, _ => (int)Prefs.Double("dashView", 1) };
    }

    private string[] ViewsHere => engine.OnBrightspace ? Views[..2] : Views;
    private int View => Math.Min(view, ViewsHere.Length - 1);

    /// <summary>Cards · List · Activity and New Task, from the right (the Mac's toolbar items); the width they take.</summary>
    public float Toolbar(Ui ui, float right, float y)
    {
        var items = ViewsHere;
        const float segW = 70, plus = Ui.ToolbarItem;
        var w = segW * items.Length + 6;
        var total = w + 11 + plus;
        if (right < -1000) return total;
        if (ui.ToolbarButton("dash:new", right - plus, y, "plus", true, 15)) engine.AskNewTask();
        var v = View;
        if (ui.Segmented("dash:view", new R(right - plus - 11 - w, y, w, Ui.ToolbarItem), items, ref v, true, segW))
        {
            view = v;
            Prefs.Set("dashView", v);
        }
        return total;
    }

    /// <summary>Escape: the open counter's panel folds back.</summary>
    public void Escape() { if (openKey != null) SetOpen(null); }

    public void Frame(Ui ui, R content)
    {
        var page = PageFrame.Begin(ui, content);
        var t = ui.T;
        today.Ensure(engine, "today");
        countsQuick.Ensure(engine, "todayCounts", new { kept = true });
        countsFresh.Ensure(engine, "todayCounts", new { kept = false });
        courses.Ensure(engine, "dashCourses");
        progress.Ensure(engine, "coursesProgress");
        skyKept.Ensure(engine, "dashSkyline", new { kept = true });
        skyFresh.Ensure(engine, "dashSkyline", new { kept = false });
        if (View == 1) list.Ensure(engine, "dashList", hideDone is { } hd ? new { hideDone = hd } : null);
        if (View == 2) activity.Ensure(engine, "dashActivity");
        if (shownAt < 0) shownAt = ui.Time;

        var d = today.Data;
        var x = page.X;
        var width = page.W;
        var y = page.Top;
        if (d == null)
        {
            LoadState(ui, content, today.Error, () => today.Again());
            page.End(ui, content.Bottom);
            return;
        }
        // (the screenshot suite: --sheet next opens a counter's panel)
        if (!sheetAsked && Launch.Get("sheet") is { Length: > 0 } key) { sheetAsked = true; openKey = key; }

        y += Headings.Screen(ui, Greeting(d), d.DateLine, x, y, width) + 26;
        y = Counters(ui, d, x, y, width) + 26;

        var two = width >= 900;
        var railW = Math.Clamp(MathF.Round(width * 0.3f), 340, 440);
        var mainW = two ? width - railW - 28 : width;
        float end;
        if (two)
        {
            var mainEnd = Main(ui, d, x, y, mainW);
            var railEnd = Rail(ui, d, x + mainW + 28, y, railW, false);
            end = Math.Max(mainEnd, railEnd);
        }
        else
        {
            if (View != 1) y = TodaySection(ui, d, x, y, width, true) + 26;
            y = Main(ui, d, x, y, width) + 26;
            end = Rail(ui, d, x, y, width, true);
        }
        Panel(ui, d, x, width, content);
        page.End(ui, end);
    }

    private static string Greeting(Today d)
    {
        var first = (d.Me?.Name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var hour = DateTime.Now.Hour;
        var part = hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
        return first.Length == 0 ? part : $"{part}, {first}";
    }

    /// <summary>The Mac's LoadState: a spinner in the middle of the page, or why it could not load and Try Again.</summary>
    private void LoadState(Ui ui, R content, string? error, Action retry)
    {
        var t = ui.T;
        if (error == null) { ui.Spinner(content.CenterX, content.CenterY, 11); return; }
        ui.Icon("exclamationmark.triangle", content.CenterX, content.CenterY - 60, 40, t.Ink2);
        Lines.Text(ui, "Couldn’t Load", content.X, content.CenterY - 24, Theme.Title2, t.Ink(), content.W, TextAlignment.Center);
        Lines.Text(ui, error, content.X + 40, content.CenterY + 8, Theme.Callout, t.Ink2, content.W - 80, TextAlignment.Center);
        var w = ui.GlassButtonWidth("Try Again", false);
        if (ui.GlassButton("load:again", content.CenterX - w / 2, content.CenterY + 40, "Try Again")) { retry(); host.Invalidate(); }
    }

    // ---- the counters (the Mac's DashCounterTile: 18-radius glass, 14 padding, symbol 14, label 14, count 34, note 12) ----

    private static readonly string[] Order = { "today", "tomorrow", "next", "overdue", "unread", "graded" };

    private static string Symbol(string key) => key switch
    {
        "today" => "clock", "next" => "calendar", "unread" => "megaphone", "overdue" => "exclamationmark.circle", "tomorrow" => "sunrise", "graded" => "chart.bar", _ => "circle",
    };

    private Color4 CounterColor(string key) => key switch
    {
        "today" => Theme.Hex("#ff453a"), "next" => Theme.Hex("#34c759"), "unread" => Theme.Hex("#ff9500"), "overdue" => Theme.Hex("#ff453a"),
        "tomorrow" => Theme.Hex("#ff9f0a"), "graded" => Theme.Hex("#5856d6"), _ => host.Ui.T.Accent,
    };

    private static string FullName(string key, string label) => key switch
    {
        "today" => "Due today", "next" => "Next 7 days", "unread" => "Unread announcements", "overdue" => "Overdue", "tomorrow" => "Due tomorrow", "graded" => "Graded this week", _ => label,
    };

    private readonly Dictionary<string, R> tiles = new();

    private float Counters(Ui ui, Today d, float x, float y, float width)
    {
        var t = ui.T;
        var cols = width >= 900 ? 6 : width >= 520 ? 3 : 2;
        var items = d.Counters.OrderBy(c => Array.IndexOf(Order, c.Key) is var i && i < 0 ? 99 : i).ToList();
        const float gap = 14, h = 112;
        var w = (width - gap * (cols - 1)) / cols;
        for (var k = 0; k < items.Count; k++)
        {
            var c = items[k];
            var r = new R(x + (k % cols) * (w + gap), y + (k / cols) * (h + gap), w, h);
            tiles[c.Key] = r;
            if (openKey == c.Key) continue; // (grown into its panel: the row keeps its shape)
            var (value, note) = Count(c);
            var alert = c.Tone == "red" && (value ?? 0) > 0;
            ui.Glass(r, 18, GlassKind.Panel, alert ? t.Red : null, alert ? 0.22f : 0);
            var i = ui.Press("dash:counter:" + c.Key, r);
            var hover = ui.Animate("dash:counter:" + c.Key + ":h", i.Hovered ? 1 : 0, 0.24f, 1);
            if (hover > 0.01f) ui.Fill(r, 18, t.Primary(0.03f * hover));
            Tile(ui, c, value, note, r, alert);
            if (i.Clicked) SetOpen(openKey == c.Key ? null : c.Key);
        }
        var rows = (items.Count + cols - 1) / cols;
        return y + rows * h + Math.Max(0, rows - 1) * gap;
    }

    private void Tile(Ui ui, Counter c, int? value, string? note, R r, bool alert)
    {
        var t = ui.T;
        ui.Icon(Symbol(c.Key), r.X + 14 + 7, r.Y + 14 + 9, 14, CounterColor(c.Key), FontWeight.SemiBold);
        Lines.Text(ui, c.Label, r.X + 35, r.Y + 14, Theme.Medium(Theme.Callout), t.Ink2, r.W - 49);
        var count = value?.ToString() ?? "–";
        ui.TextIn(count, new R(r.X + 14, r.Y + 38, r.W - 28, 40), Theme.Count, value == null ? t.Ink3 : alert ? t.Red : t.Ink(), TextAlignment.Leading);
        if (!string.IsNullOrEmpty(note)) Lines.Text(ui, note!, r.X + 14, r.Y + 84, Theme.Caption, t.Ink2, r.W - 28);
    }

    /// <summary>A counter's number and note: never one from before — Overdue and Graded from the counts made on their own, the rest from Today.</summary>
    private (int? Value, string? Note) Count(Counter c)
    {
        var fresh = countsFresh.Data;
        var quick = countsQuick.Data;
        if (c.Key == "overdue")
        {
            var k = fresh ?? quick;
            if (k?.Overdue is { } o)
            {
                var n = Math.Max(0, o + overdueAdjust);
                return (n, overdueAdjust != 0 ? (n > 0 ? $"{n} not submitted" : "Nothing overdue") : k.OverdueNote);
            }
            return (null, null);
        }
        if (c.Key == "graded")
        {
            var k = fresh ?? quick;
            return k?.Graded is { } g ? (g, k.GradedNote) : (null, null);
        }
        if (today.Data == null || c.Pending == true) return (null, c.Note);
        return (c.Value, c.Note);
    }

    private void SetOpen(string? key)
    {
        if (key == openKey) return;
        if (key != null && tiles.TryGetValue(key, out var tr)) openTile = tr;
        openKey = key;
        if (key != null) host.Ui.S("dash:panel", 0, 0.42f, 0.92f).Snap(0);
        host.Invalidate();
    }

    // ---- the panel a counter grows into (the Mac's DashCounterPanel: 440 wide, 22-radius glass tinted its colour) ----

    private void Panel(Ui ui, Today d, float x, float width, R content)
    {
        if (openKey == null) return;
        var key = openKey;
        var c = d.Counters.FirstOrDefault(k => k.Key == key);
        if (c == null) { openKey = null; return; }
        if (!tiles.TryGetValue(key, out var tile)) return;
        sheet.Ensure(engine, "todaySheet", new { key });
        var data = sheet.Data;
        var t = ui.T;
        var p = ui.Animate("dash:panel", 1, 0.42f, 0.92f);
        var w = Math.Min(440, width);
        var along = width > tile.W ? Math.Clamp((tile.X - x) / (width - tile.W), 0, 1) : 0;
        var px = Math.Clamp(tile.X + (tile.W - w) * along, x, Math.Max(x, x + width - w));
        var (value, note) = Count(c);
        var alert = c.Tone == "red" && (value ?? 0) > 0;

        // how tall: the header, then the lists capped at 240 (scrolling inside past it)
        var headerH = 16 + 20 + 6 + 46;
        var listH = 0f;
        if (data != null) listH = data.Sections.Count == 0 ? 42 : data.Sections.Sum(s => SectionHeight(ui, s, w - 44)) + 14 * Math.Max(0, data.Sections.Count - 1);
        else listH = 30;
        var capped = Math.Min(listH, 240);
        var fullH = headerH + 16 + capped + 20;
        var target = new R(px, tile.Y, w, fullH);
        var r = new R(tile.X + (target.X - tile.X) * p, tile.Y, tile.W + (target.W - tile.W) * p, tile.H + (target.H - tile.H) * p);
        panelRect = r;
        var alpha = Math.Clamp((p - 0.45f) / 0.55f, 0, 1);

        ui.Layer(Ui.PageOver);
        ui.Glass(r, 22, GlassKind.Panel, CounterColor(key), 0.10f, Ui.Shadow.Panel);
        ui.Block(r);
        // a click anywhere round it folds it back (the click still does what it does)
        if (ui.Pressed && !r.Contains(ui.Mouse)) SetOpen(null);
        if (alpha <= 0.01f) { ui.Layer(Ui.Page); return; }
        ui.PushClip(r);
        var ink = Theme.WithAlpha(t.Ink(), alpha);
        var ink2 = Theme.WithAlpha(t.Ink2, alpha);
        var hx = r.X + 22;
        var hy = r.Y + 16;
        // the header: the tile's symbol and name, its number larger with the line beside it; ✕ at the end
        ui.Icon(Symbol(key), hx + 8, hy + 10, 16, Theme.WithAlpha(CounterColor(key), alpha), FontWeight.SemiBold);
        Lines.Text(ui, data?.Title ?? FullName(key, c.Label), hx + 24, hy, Theme.Headline, ink, r.W - 44 - 24 - 40);
        var countF = Theme.Sized(Theme.Count, 40);
        var cw = ui.Measure(value?.ToString() ?? "–", countF).X;
        ui.TextIn(value?.ToString() ?? "–", new R(hx, hy + 26, cw + 4, 46), countF, value == null ? Theme.WithAlpha(t.Ink3, alpha) : alert ? Theme.WithAlpha(t.Red, alpha) : ink, TextAlignment.Leading);
        var line = data?.Note ?? note;
        if (!string.IsNullOrEmpty(line)) Lines.Wrapped(ui, line!, hx + cw + 16, hy + 26 + 6, Theme.Callout, ink2, r.W - 44 - cw - 16 - 30, 2);
        var head = new R(r.X, r.Y, r.W, headerH + 8);
        if (ui.RoundIconButton("dash:panel:close", r.Right - 22 - 15, hy + 15, "xmark", 13, ink2, FontWeight.Bold)) SetOpen(null);
        else if (ui.Press("dash:panel:head", head).Clicked) SetOpen(null);

        // what it counted: each section's title, then its rows, each with the course's colour at its start
        var ly = r.Y + headerH + 16;
        var listRect = new R(hx, ly, r.W - 44, capped);
        if (data == null)
        {
            if (sheet.Error is { } err) ui.EmptyNote(err, hx, ly, listRect.W, "exclamationmark.triangle");
            else { ui.Spinner(hx + 12, ly + 12, 7); Lines.Text(ui, "Loading…", hx + 30, ly + 4, Theme.Callout, ink2); }
        }
        else if (data.Sections.Count == 0) ui.EmptyNote(data.Empty ?? "Nothing here.", hx, ly, listRect.W);
        else
        {
            var scrolls = listH > capped + 0.5f;
            var off = scrolls ? ui.BeginScroll("dash:panel:list", listRect) : 0;
            if (!scrolls) ui.PushClip(listRect);
            var sy = ly - off;
            foreach (var s in data.Sections)
            {
                sy = Section(ui, key, s, hx, sy, listRect.W, alpha) + 14;
            }
            if (scrolls) ui.EndScroll("dash:panel:list", listRect, listH); else ui.PopClip();
        }
        ui.PopClip();
        ui.Layer(Ui.Page);
    }

    private float SectionHeight(Ui ui, SheetSection s, float w)
    {
        var h = s.Title.Length > 0 ? 22f : 0f;
        foreach (var row in s.Rows) h += RowHeight(ui, row, w) + 4;
        return Math.Max(0, h - 4);
    }

    private float RowHeight(Ui ui, SheetRow row, float w)
    {
        var textW = w - 16 - 4 - 12 - 30;
        var lines = Lines.Count(ui, row.Title, Theme.Body, textW, 2);
        var h = 18 * lines + (string.IsNullOrEmpty(row.Sub) ? 0 : 2 + 17);
        return 9 + Math.Max(34, h) + 9;
    }

    /// <summary>A section of the panel: its title 13 semibold, its rows (a bar of the course's colour, the title, the line, a chevron or Dismiss).</summary>
    private float Section(Ui ui, string key, SheetSection s, float x, float y, float w, float alpha)
    {
        var t = ui.T;
        var quiet = s.Quiet == true;
        if (s.Title.Length > 0)
        {
            Lines.Text(ui, s.Title, x + 8, y, Theme.Semibold(Theme.Subheadline), Theme.WithAlpha(quiet ? t.Ink3 : t.Ink2, alpha), w - 16);
            y += 16 + 2 + 4;
        }
        foreach (var row in s.Rows)
        {
            var rh = RowHeight(ui, row, w);
            var r = new R(x, y, w, rh);
            var id = $"dash:sheet:{key}:{row.Key ?? row.Url ?? row.Title}";
            var i = ui.RowWash(id, r);
            var rq = row.Quiet == true;
            var barC = Theme.Hex(row.Color);
            ui.Fill(new R(r.X + 8, r.CenterY - 17, 4, 34), 2, Theme.WithAlpha(barC, (rq ? 0.5f : 1) * alpha));
            var tx = r.X + 8 + 4 + 12;
            var clearable = row.Clearable == true && row.Key != null;
            var right = r.Right - 8 - (clearable ? ui.GlassButtonWidth("Dismiss", false) + 8 : row.Url != null ? 20 : 0);
            var ty = r.Y + 9;
            var titleH = Lines.Wrapped(ui, row.Title, tx, ty, Theme.Body, Theme.WithAlpha(rq ? t.Ink2 : t.Ink(), alpha), right - tx, 2);
            if (!string.IsNullOrEmpty(row.Sub)) Lines.Text(ui, row.Sub!, tx, ty + titleH + 2, Theme.Callout, Theme.WithAlpha(t.Ink2, alpha), right - tx);
            if (clearable)
            {
                if (ui.GlassButton(id + ":dismiss", r.Right - 8 - ui.GlassButtonWidth("Dismiss", false), r.CenterY - 13, "Dismiss", h: 26)) Clear(row.Key!);
            }
            else if (row.Url != null)
            {
                ui.Icon("chevron.right", r.Right - 14, r.CenterY, 11, Theme.WithAlpha(t.Ink3, alpha), FontWeight.SemiBold);
                if (i.Clicked) engine.OpenWeb(row.Url, row.Title);
            }
            y += rh + 4;
        }
        return y - 4;
    }

    /// <summary>Dismissed from Overdue: the row goes at once, the tile's number with it; the school told, the list read again.</summary>
    private async void Clear(string k)
    {
        if (sheet.Data is { } d)
        {
            foreach (var s in d.Sections) s.Rows.RemoveAll(r => r.Key == k);
            d.Sections.RemoveAll(s => s.Rows.Count == 0);
        }
        overdueAdjust--;
        host.Invalidate();
        try
        {
            var left = await engine.Call<TodayCounts>("clearOverdue", new { key = k });
            overdueAdjust = 0;
            if (countsFresh.Data != null) countsFresh.Data.Overdue = left.Overdue;
            engine.DataChanged();
        }
        catch { overdueAdjust++; }
        sheet.Again();
        host.Invalidate();
    }

    // ---- the view chosen, and the side column ----

    private float Main(Ui ui, Today d, float x, float y, float w) => View switch
    {
        0 => CoursesSection(ui, x, y, w),
        2 => ActivitySection(ui, x, y, w),
        _ => Upcoming(ui, x, y, w),
    };

    private float Rail(Ui ui, Today d, float x, float y, float w, bool narrow)
    {
        if (!narrow && View != 1) y = TodaySection(ui, d, x, y, w, false) + 26;
        if (narrow && w >= 760)
        {
            var half = (w - 22) / 2;
            var a = Skyline(ui, x, y, half);
            var b = WeekLoad(ui, d, x + half + 22, y, half);
            return Math.Max(a, b);
        }
        y = Skyline(ui, x, y, w);
        if (skyKept.Data != null || skyFresh.Data != null) y += 26;
        return WeekLoad(ui, d, x, y, w);
    }

    // ---- Today: the day's work beside the cards ----

    private float TodaySection(Ui ui, Today d, float x, float y, float w, bool narrow)
    {
        var t = ui.T;
        y += Headings.Section(ui, d.List.Heading.Length > 0 ? d.List.Heading : "Today", x, y, w, d.List.Note) + 10;
        var rows = d.List.Rows;
        var heights = rows.Select(r => narrow ? WorkRowHeight(ui, r, w - 16) : TodayLineHeight(ui, r, w - 16)).ToList();
        var cardH = 16 + (rows.Count == 0 ? 42 : heights.Sum());
        var card = new R(x, y, w, cardH);
        ui.Glass(card, 18);
        var ry = card.Y + 8;
        if (rows.Count == 0) ui.EmptyNote(d.List.Empty ?? "Nothing due.", card.X + 8 + 10, ry, w - 36);
        for (var k = 0; k < rows.Count; k++)
        {
            var row = rows[k];
            var r = new R(card.X + 8, ry, w - 16, heights[k]);
            if (k > 0) ui.RowDivider(r.X + 42, r.Right - 8, r.Y);
            if (narrow) WorkRow(ui, "dash:today:" + row.Id, r, row);
            else TodayLine(ui, "dash:today:" + row.Id, r, row);
            ry += heights[k];
        }
        return card.Bottom;
    }

    private bool Done(string id, bool done) => ticked.TryGetValue(id, out var tk) ? tk : done;

    private float TodayLineHeight(Ui ui, WorkRow row, float w)
    {
        var textW = w - 8 - 22 - 11 - 70 - 8;
        var lines = Lines.Count(ui, row.Title, Theme.Body, textW, 2);
        var h = 18 * lines + 3 + 15 + (row.Flag != null ? 3 + 17 : 0);
        return 9 + h + 9;
    }

    /// <summary>The Mac's DashTodayLine: tick, title over its course (a dot in its colour) and line, its time at the end.</summary>
    private void TodayLine(Ui ui, string id, R r, WorkRow row)
    {
        var t = ui.T;
        var done = Done(row.Id, row.Done);
        var i = ui.RowWash(id, r);
        if (ui.CheckCircle(id + ":tick", r.X + 8 + 11, r.Y + 9 + 11, done, t.Green)) Tick(row.Id, !done);
        else if (i.Clicked && !string.IsNullOrEmpty(row.Url)) engine.OpenWeb(row.Url!, row.Title);
        var tx = r.X + 8 + 22 + 11;
        var timeW = string.IsNullOrEmpty(row.Time) ? 0 : ui.Measure(row.Time!, Theme.Callout).X;
        var right = r.Right - 8 - (timeW > 0 ? timeW + 6 : 0);
        var ty = r.Y + 9;
        var th = Lines.Wrapped(ui, row.Title, tx, ty, Theme.Body, done ? t.Ink2 : t.Ink(), right - tx, 2, done);
        var ly = ty + th + 3;
        var lx = tx;
        if (!string.IsNullOrEmpty(row.Course))
        {
            ui.Circle(lx + 3.5f, ly + 7.5f, 3.5f, Theme.Hex(row.Color));
            lx += 7 + 6;
            var cw = Math.Min(ui.Measure(row.Course!, Theme.Caption).X, (right - lx) * 0.6f);
            Lines.Text(ui, row.Course!, lx, ly, Theme.Caption, t.Ink2, cw);
            lx += cw + 6;
        }
        if (!string.IsNullOrEmpty(row.Sub)) Lines.Text(ui, row.Sub!, lx, ly, Theme.Caption, t.Ink2, right - lx);
        if (row.Flag != null) ui.Chip(row.Flag.Word, tx, ly + 15 + 3 + 8, t.Tone(row.Flag.Kind));
        if (timeW > 0) Lines.Text(ui, row.Time!, r.Right - 8 - timeW, ty, Theme.Callout, t.Ink2);
    }

    private float WorkRowHeight(Ui ui, WorkRow row, float w)
    {
        var textW = w - 8 - 22 - 12 - 180;
        var lines = Lines.Count(ui, row.Title, Theme.Body, textW, 2);
        var h = 18 * lines + (string.IsNullOrEmpty(row.Sub) ? 0 : 2 + 17);
        return 9 + Math.Max(22, h) + 9;
    }

    /// <summary>The Mac's WorkRowView (Today on a narrow page): tick, title and line, its flag, its course's chip, its time.</summary>
    private void WorkRow(Ui ui, string id, R r, WorkRow row)
    {
        var t = ui.T;
        var done = Done(row.Id, row.Done);
        var i = ui.RowWash(id, r);
        if (ui.CheckCircle(id + ":tick", r.X + 8 + 11, r.CenterY, done, t.Green)) Tick(row.Id, !done);
        else if (i.Clicked && !string.IsNullOrEmpty(row.Url)) engine.OpenWeb(row.Url!, row.Title);
        var right = r.Right - 8;
        if (!string.IsNullOrEmpty(row.Time))
        {
            var tw = Math.Max(58, ui.Measure(row.Time!, Theme.Callout).X);
            Lines.Text(ui, row.Time!, right - tw, r.CenterY - 8.5f, Theme.Callout, t.Ink2, tw, TextAlignment.Trailing);
            right -= tw + 12;
        }
        if (!string.IsNullOrEmpty(row.Course)) { var cw = ui.ChipWidth(row.Course!); ui.CourseChip(row.Course!, right - cw, r.CenterY, row.Color); right -= cw + 12; }
        if (row.Flag != null) { var fw = ui.ChipWidth(row.Flag.Word); ui.Chip(row.Flag.Word, right - fw, r.CenterY, t.Tone(row.Flag.Kind)); right -= fw + 12; }
        var tx = r.X + 8 + 22 + 12;
        var h = 18 * Lines.Count(ui, row.Title, Theme.Body, right - tx, 2) + (string.IsNullOrEmpty(row.Sub) ? 0 : 19);
        var ty = r.CenterY - h / 2;
        var th = Lines.Wrapped(ui, row.Title, tx, ty, Theme.Body, done ? t.Ink2 : t.Ink(), right - tx, 2, done);
        if (!string.IsNullOrEmpty(row.Sub)) Lines.Text(ui, row.Sub!, tx, ty + th + 2, Theme.Callout, t.Ink2, right - tx);
    }

    /// <summary>Ticked off (or back on): shown at once; the school told; read again after a moment, so the tick shows before the row moves.</summary>
    private async void Tick(string id, bool done)
    {
        ticked[id] = done;
        host.Invalidate();
        var ok = await engine.Act("complete", new { id, done });
        if (!ok) ticked[id] = !done;
        else
        {
            await Task.Delay(450);
            ticked.Remove(id);
            list.Again();
            today.Again();
            engine.DataChanged();
        }
        host.Invalidate();
    }

    // ---- Coming up: the List view (the Mac's DashDayList) ----

    private float Upcoming(Ui ui, float x, float y, float w)
    {
        var t = ui.T;
        var l = list.Data;
        string? trailing = null;
        if (l is { Total: > 0 }) trailing = $"{l.Total - (l.Done ?? 0)} to do · {l.Done ?? 0} done";
        var showButton = l != null && ((l.Done ?? 0) > 0 || l.HideDone == true);
        var hiding = l?.HideDone == true;
        var label = hiding ? $"Show Completed · {l!.Done ?? 0}" : "Hide Completed";
        var bw = showButton ? ui.GlassButtonWidth(label, true) : 0;
        Headings.Section(ui, "Coming up", x, y, w, trailing, bw);
        if (showButton && ui.GlassButton("dash:hidedone", x + w - bw, y - 2, label, hiding ? "eye" : "checkmark.circle", h: 26))
        {
            hideDone = !hiding;
            list.Again();
        }
        y += 23 + 10;
        if (l == null)
        {
            if (list.Error is { } err) return y + ui.EmptyNote(err, x, y, w, "exclamationmark.triangle");
            ui.Spinner(x + 12, y + 12 + 9, 7);
            Lines.Text(ui, "Reading your planner…", x + 30, y + 12 + 1, Theme.Callout, t.Ink2);
            return y + 42;
        }
        if (l.Days.Count == 0) return y + ui.EmptyNote(l.Empty ?? "Nothing coming up.", x, y, w, "sun.max");
        var wide = w >= 760;
        foreach (var day in l.Days)
        {
            // the day's heading: its name 16 semibold, its date 14 secondary, how many at the end
            var tw = ui.Measure(day.Title, Theme.Headline).X;
            Lines.Text(ui, day.Title, x + 6, y, Theme.Headline, t.Ink());
            if (!string.IsNullOrEmpty(day.Date))
            {
                var date = day.Date!.StartsWith(day.Title + ", ") ? day.Date![(day.Title.Length + 2)..] : day.Date!;
                Lines.Text(ui, date, x + 6 + tw + 10, y + 2, Theme.Callout, t.Ink2, w * 0.5f);
            }
            var n = $"{day.Rows.Count} {(day.Rows.Count == 1 ? "item" : "items")}";
            Lines.Text(ui, n, x + w - 6 - ui.Measure(n, Theme.Caption).X, y + 3, Theme.Caption, t.Ink3);
            y += 20 + 8;
            var heights = day.Rows.Select(r => WorkLineHeight(ui, r, w - 16, wide)).ToList();
            var card = new R(x, y, w, 16 + heights.Sum());
            ui.Glass(card, 18);
            var ry = card.Y + 8;
            for (var k = 0; k < day.Rows.Count; k++)
            {
                var r = new R(card.X + 8, ry, w - 16, heights[k]);
                if (k > 0) ui.RowDivider(r.X + 86, r.Right - 8, r.Y);
                WorkLine(ui, "dash:list:" + day.Rows[k].Id, r, day.Rows[k], wide);
                ry += heights[k];
            }
            y = card.Bottom + 22;
        }
        return y - 22;
    }

    private float WorkLineHeight(Ui ui, DashRow row, float w, bool wide)
    {
        var textW = w - 8 - 22 - 12 - 32 - 12 - (wide ? 64 + 8 + 112 : 90 + 60) - 8;
        var lines = Lines.Count(ui, row.Title, Theme.Body, textW, 2);
        var h = 15 + 3 + 18 * lines;
        return 9 + Math.Max(32, h) + 9;
    }

    /// <summary>The Mac's DashWorkLine: tick; its kind in its course's colour; its course and kind with its flags; its title; its points; when it is due.</summary>
    private void WorkLine(Ui ui, string id, R r, DashRow row, bool wide)
    {
        var t = ui.T;
        var done = Done(row.Id, row.Done);
        var i = ui.RowWash(id, r);
        if (ui.CheckCircle(id + ":tick", r.X + 8 + 11, r.CenterY, done, t.Green)) Tick(row.Id, !done);
        else if (i.Clicked && !string.IsNullOrEmpty(row.Url)) engine.OpenWeb(row.Url!, row.Title);
        var color = Theme.Hex(row.Color);
        ui.IconTile(Glyphs.Work(row.Type ?? row.Kind), r.X + 8 + 22 + 12, r.CenterY - 16, color, 32);
        var tx = r.X + 8 + 22 + 12 + 32 + 12;
        // the end: when it is due (90 or 112 wide), its points before it
        var due = row.Due ?? row.Time ?? "";
        var dueW = Math.Max(wide ? 112 : 90, ui.Measure(due, Theme.Callout).X);
        Lines.Text(ui, due, r.Right - 8 - dueW, r.CenterY - 8.5f, Theme.Callout, t.Ink2, dueW, TextAlignment.Trailing);
        var right = r.Right - 8 - dueW - 8;
        if (!string.IsNullOrEmpty(row.Points))
        {
            var pw = Math.Max(wide ? 64 : 0, ui.Measure(row.Points!, Theme.Callout).X);
            Lines.Text(ui, row.Points!, right - pw, r.CenterY - 8.5f, Theme.Callout, t.Ink2, pw, TextAlignment.Trailing);
            right -= pw + 8;
        }
        var h = 15 + 3 + 18 * Lines.Count(ui, row.Title, Theme.Body, right - tx, 2);
        var ty = r.CenterY - h / 2;
        var lx = tx;
        var sub = string.Join(" · ", new[] { row.Course, row.Kind }.Where(s => !string.IsNullOrEmpty(s)));
        if (sub.Length > 0)
        {
            var sw = Math.Min(ui.Measure(sub, Theme.Medium(Theme.Caption)).X, right - lx);
            Lines.Text(ui, sub, lx, ty, Theme.Medium(Theme.Caption), t.Ink2, sw);
            lx += sw + 6;
        }
        foreach (var f in row.Flags ?? new()) lx += ui.Chip(f.Word, lx, ty + 7.5f, t.Tone(f.Kind)) + 6;
        Lines.Wrapped(ui, row.Title, tx, ty + 15 + 3, Theme.Body, done ? t.Ink2 : t.Ink(), right - tx, 2, done);
    }

    // ---- Cards: the courses (the Mac's DashCourseCard) ----

    private float CoursesSection(Ui ui, float x, float y, float w)
    {
        var t = ui.T;
        var c = courses.Data;
        var trailing = c == null ? null : $"{c.Rows.Count} {(c.Rows.Count == 1 ? "course" : "courses")}";
        var lw = ui.Measure("All Courses", Theme.Callout).X;
        Headings.Section(ui, "Courses", x, y, w, trailing, lw);
        if (ui.LinkButton("dash:allcourses", x + w - lw, y + 3, "All Courses")) engine.Go(Place.Courses);
        y += 23 + 10;
        if (c == null)
        {
            if (courses.Error is { } err) return y + ui.EmptyNote(err, x, y, w, "exclamationmark.triangle");
            ui.Spinner(x + 12, y + 21, 7);
            Lines.Text(ui, "Reading your courses…", x + 30, y + 13, Theme.Callout, t.Ink2);
            return y + 42;
        }
        if (c.Rows.Count == 0) return y + ui.EmptyNote(c.Empty ?? "No courses chosen yet.", x, y, w, "books.vertical");
        const float gap = 18;
        var cols = Math.Max(1, (int)MathF.Floor((w + gap) / (260 + gap)));
        var cw = (w - gap * (cols - 1)) / cols;
        var heights = c.Rows.Select(r => 96 + 13 + 56 + 12 + 15 + 12 + 17 + (QuickLinks(r) > 0 ? 12 + 26 : 0) + 12).ToList();
        var rowsN = (c.Rows.Count + cols - 1) / cols;
        var yy = y;
        for (var row = 0; row < rowsN; row++)
        {
            var rowH = 0f;
            for (var col = 0; col < cols; col++)
            {
                var k = row * cols + col;
                if (k >= c.Rows.Count) break;
                var r = new R(x + col * (cw + gap), yy, cw, heights[k]);
                CourseCard(ui, c.Rows[k], r);
                rowH = Math.Max(rowH, heights[k]);
            }
            yy += rowH + gap;
        }
        return yy - gap;
    }

    private static int QuickLinks(DashCourse c) => Math.Min(c.Links?.Count ?? 0, 4);

    private void CourseCard(Ui ui, DashCourse course, R r)
    {
        var t = ui.T;
        var color = Theme.Hex(course.Color);
        ui.Glass(r, 20);
        var i = ui.Press("dash:card:" + course.Id, r);
        var hover = ui.Animate("dash:card:" + course.Id + ":h", i.Hovered ? 1 : 0, 0.24f, 1);
        // its colour at the top (a hero), its code on it, what wants attention at its corner
        var hero = new R(r.X, r.Y, r.W, 96);
        ui.FillTopRounded(hero, 20, color, Theme.WithAlpha(color, 0.78f));
        if (hover > 0.01f) ui.FillTopRounded(hero, 20, new Color4(1, 1, 1, 0.06f * hover), new Color4(1, 1, 1, 0.06f * hover));
        var bx = r.Right - 12;
        if (course.Unread is > 0)
        {
            var pw = ui.Measure(course.Unread.Value.ToString(), Theme.Bold(Theme.Caption)).X + 32;
            ui.Pill(course.Unread.Value.ToString(), bx - pw, r.Y + 12 + 10, new Color4(1, 1, 1, 1), color, "megaphone");
            bx -= pw + 6;
        }
        if (course.DueToday is > 0)
        {
            var s = $"{course.DueToday} due today";
            var pw = ui.Measure(s, Theme.Bold(Theme.Caption)).X + 16;
            ui.Pill(s, bx - pw, r.Y + 12 + 10, t.Red, new Color4(1, 1, 1, 1));
        }
        var codeF = new Font(Theme.TextFace, 20, FontWeight.Bold);
        var codeLines = Lines.Count(ui, course.Code, codeF, r.W - 32, 2);
        ui.Text(course.Code, r.X + 16 + 0.5f, hero.Bottom - 12 - 25 * codeLines + 1, codeF, new Color4(0, 0, 0, 0.22f), r.W - 32, TextAlignment.Leading, 2);
        ui.Text(course.Code, r.X + 16, hero.Bottom - 12 - 25 * codeLines, codeF, new Color4(1, 1, 1, 1), r.W - 32, TextAlignment.Leading, 2);
        // its name, term and teacher; its score in a ring
        var by = hero.Bottom + 13;
        var bx0 = r.X + 16;
        var bw = r.W - 32;
        Lines.Text(ui, course.Name ?? course.Code, bx0, by + 9, Theme.Semibold(Theme.Body), t.Ink(), bw - 56 - 12);
        Lines.Text(ui, course.Sub ?? " ", bx0, by + 9 + 18 + 3, Theme.Callout, t.Ink2, bw - 56 - 12);
        ScoreRing(ui, course, r.Right - 16 - 28, by + 28, 56);
        by += 56 + 12;
        // how much is handed in
        var p = progress.Data != null ? (progress.Data.TryGetValue(course.Id, out var pr) ? pr : new CourseProgress { Done = 0, Total = 0 }) : null;
        var handed = p == null ? "Counting…" : p.Total > 0 ? $"{p.Done} of {p.Total} handed in" : "Nothing to hand in";
        var hw = ui.Measure(handed, Theme.Caption).X;
        ui.Bar("dash:card:bar:" + course.Id, bx0, by + 4, bw - hw - 10, p is { Total: > 0 } ? (double)p.Done / p.Total : 0, color);
        Lines.Text(ui, handed, r.Right - 16 - hw, by, Theme.Caption, t.Ink2);
        by += 15 + 12;
        // what is next
        if (course.Next is { } next)
        {
            var nx = bx0;
            var nw = ui.Measure("Next  ", Theme.Callout).X;
            Lines.Text(ui, "Next  ", nx, by, Theme.Callout, t.Ink2);
            nx += nw;
            var when = next.When is { Length: > 0 } wh ? $"  ·  {wh}" : "";
            var whenW = when.Length > 0 ? ui.Measure(when, Theme.Callout).X : 0;
            var titleW = Math.Min(ui.Measure(next.Title, Theme.Medium(Theme.Callout)).X, bw - nw - whenW);
            Lines.Text(ui, next.Title, nx, by, Theme.Medium(Theme.Callout), t.Ink(), titleW);
            if (when.Length > 0) Lines.Text(ui, when, nx + titleW, by, Theme.Callout, t.Ink2, bw - nw - titleW);
        }
        else Lines.Text(ui, "Nothing due", bx0, by, Theme.Callout, t.Ink2, bw);
        // its quick links, over the card's foot
        var links = (course.Links ?? new()).Take(4).ToList();
        for (var k = 0; k < links.Count; k++)
        {
            var link = links[k];
            if (ui.RoundIconButton($"dash:card:{course.Id}:link:{k}", r.X + 9 + 15 + k * 32, r.Bottom - 8 - 15, Glyphs.Section(link.Kind ?? ""), 13.5f, t.Ink2))
            {
                if (link.Kind is { } kind && Native.Contains(kind)) engine.Go(Place.Section("courses/" + course.Id, kind));
                else if (link.Url != null) engine.OpenWeb(link.Url, link.Label ?? course.Code);
            }
        }
        if (i.Clicked) engine.OpenWeb(course.Url, course.Code);
    }

    private static readonly HashSet<string> Native = new() { "announcements", "assignments", "discussions", "files", "grades", "modules", "pages", "people", "quizzes", "syllabus" };

    /// <summary>The Mac's DashScoreRing: the score in a ring of the course's colour, its letter under the number.</summary>
    private void ScoreRing(Ui ui, DashCourse course, float cx, float cy, float size)
    {
        var t = ui.T;
        ui.GradeRing("dash:ring:" + course.Id, cx, cy, size, course.Score, Theme.Hex(course.Color), 5);
        var score = course.ScoreText ?? "N/A";
        var hasGrade = !string.IsNullOrEmpty(course.Grade);
        var sf = new Font(Theme.TextFace, 12, FontWeight.SemiBold);
        ui.TextIn(score, new R(cx - size / 2 + 7, cy - (hasGrade ? 13 : 7), size - 14, 14), sf, course.Score == null ? t.Ink2 : t.Ink());
        if (hasGrade) ui.TextIn(course.Grade!, new R(cx - size / 2 + 7, cy + 1, size - 14, 13), new Font(Theme.TextFace, 10.5f, FontWeight.SemiBold), t.Ink2);
    }

    // ---- Activity (the Mac's DashActivityList) ----

    private float ActivitySection(Ui ui, float x, float y, float w)
    {
        var t = ui.T;
        var a = activity.Data;
        var fresh = a?.Rows.Count(r => r.Unread == true && !seen.Contains(r.Id)) ?? 0;
        Headings.Section(ui, "Recent activity", x, y, w, fresh > 0 ? $"{fresh} new" : null);
        y += 23 + 10;
        if (a == null)
        {
            if (activity.Error is { } err) return y + ui.EmptyNote(err, x, y, w, "exclamationmark.triangle");
            ui.Spinner(x + 12, y + 21, 7);
            Lines.Text(ui, "Reading recent activity…", x + 30, y + 13, Theme.Callout, t.Ink2);
            return y + 42;
        }
        if (a.Rows.Count == 0) return y + ui.EmptyNote(a.Empty ?? "No recent activity.", x, y, w, "clock");
        var heights = a.Rows.Select(r => ActivityHeight(ui, r, w - 16)).ToList();
        var card = new R(x, y, w, 16 + heights.Sum());
        ui.Glass(card, 18);
        var ry = card.Y + 8;
        for (var k = 0; k < a.Rows.Count; k++)
        {
            var row = a.Rows[k];
            var r = new R(card.X + 8, ry, w - 16, heights[k]);
            if (k > 0) ui.RowDivider(r.X + 74, r.Right - 8, r.Y);
            var i = ui.RowWash("dash:act:" + row.Id, r);
            var unread = row.Unread == true && !seen.Contains(row.Id);
            if (unread) ui.Circle(r.X + 8 + 4, r.Y + 9 + 13 + 4, 4, t.Accent);
            ui.IconTile(Glyphs.Activity(row.Type), r.X + 8 + 8 + 12, r.Y + 9, Theme.Hex(row.Color), 34);
            var tx = r.X + 8 + 8 + 12 + 34 + 12;
            var ty = r.Y + 9;
            var whenW = string.IsNullOrEmpty(row.When) ? 0 : ui.Measure(row.When!, Theme.Caption).X;
            if (whenW > 0) Lines.Text(ui, row.When!, r.Right - 8 - whenW, ty + 2, Theme.Caption, t.Ink2);
            var titleW = r.Right - 8 - (whenW > 0 ? whenW + 8 : 0) - tx;
            var th = Lines.Wrapped(ui, row.Title, tx, ty, unread ? Theme.Semibold(Theme.Body) : Theme.Body, t.Ink(), titleW, 2);
            ty += th + 3;
            var sub = string.Join(" · ", new[] { row.Kind, row.Course }.Where(s => !string.IsNullOrEmpty(s)));
            if (sub.Length > 0) ty += Lines.Text(ui, sub, tx, ty, Theme.Callout, t.Ink2, r.Right - 8 - tx) + 3;
            if (!string.IsNullOrEmpty(row.Preview)) Lines.Wrapped(ui, row.Preview!, tx, ty, Theme.Callout, t.Ink2, r.Right - 8 - tx, 2);
            if (i.Clicked) OpenActivity(row);
            ry += heights[k];
        }
        return card.Bottom;
    }

    private float ActivityHeight(Ui ui, DashActivityRow row, float w)
    {
        var tx = 8 + 8 + 12 + 34 + 12;
        var whenW = string.IsNullOrEmpty(row.When) ? 0 : ui.Measure(row.When!, Theme.Caption).X + 8;
        var h = 18 * Lines.Count(ui, row.Title, Theme.Body, w - tx - 8 - whenW, 2);
        var sub = string.Join(" · ", new[] { row.Kind, row.Course }.Where(s => !string.IsNullOrEmpty(s)));
        if (sub.Length > 0) h += 3 + 17;
        if (!string.IsNullOrEmpty(row.Preview)) h += 3 + 17 * Lines.Count(ui, row.Preview!, Theme.Callout, w - tx - 8, 2);
        return 9 + Math.Max(34, h) + 9;
    }

    /// <summary>An activity item opened: its dot goes (here and on the web Dashboard), and it opens.</summary>
    private void OpenActivity(DashActivityRow row)
    {
        if (row.Unread == true) { seen.Add(row.Id); _ = engine.Act("dashSeen", new { id = row.Id }); }
        if (!string.IsNullOrEmpty(row.Url)) engine.OpenWeb(row.Url!, row.Title);
    }

    // ---- the grades skyline (the Mac's DashSkyline) ----

    private static Color4 Lit(string band) => band switch
    {
        "A" => Theme.Hex("#4cd964"), "B" => Theme.Hex("#b5e036"), "C" => Theme.Hex("#ffd426"), "D" => Theme.Hex("#ff9f0a"), _ => Theme.Hex("#ff453a"),
    };

    private float Skyline(Ui ui, float x, float y, float w)
    {
        var t = ui.T;
        var s = skyFresh.Data ?? skyKept.Data;
        if (s == null) return y;
        var lw = ui.Measure("All Grades", Theme.Callout).X;
        Headings.Section(ui, "Grades", x, y, w, null, lw);
        if (ui.LinkButton("dash:allgrades", x + w - lw, y + 3, "All Grades")) engine.Go(Place.Grades);
        y += 23 + 10;
        const float height = 150;
        var courses = s.Courses;
        if (courses.Count == 0)
        {
            var empty = new R(x, y, w, 16 + 42 + 14);
            ui.Glass(empty, 18);
            ui.EmptyNote("No courses chosen yet.", x + 18, y + 16, w - 36, "building.2");
            return empty.Bottom;
        }
        var card = new R(x, y, w, 16 + height + 26 + 1 + 8 + 14 + 14 + 15 + 14);
        ui.Glass(card, 18);
        var cx0 = card.X + 18;
        var cw = w - 36;
        var towerW = courses.Count switch { <= 5 => 54, 6 => 48, 7 or 8 => 40, _ => 32 };
        var colW = cw / courses.Count;
        var street = card.Y + 16 + height + 26;
        var risen = ui.Time - shownAt;
        for (var k = 0; k < courses.Count; k++)
        {
            var c = courses[k];
            var fraction = c.Score is { } sc ? Math.Max(0.16, Math.Min(1, sc / 100)) : 0.42;
            var th = MathF.Round((float)(height * fraction));
            var cx = cx0 + colW * k + colW / 2;
            var delay = Math.Min(k, 8) * 0.07;
            var up = ui.Animate($"dash:sky:{c.Id}", risen > 0.08 + delay ? 1 : 0, 0.42f, 0.92f);
            var color = Theme.Hex(c.Color);
            var ghost = c.Score == null;
            var tr = new R(cx - towerW / 2f, street - th * up, towerW, th * up);
            var tid = "dash:sky:" + c.Id;
            var i = ui.Press(tid, new R(tr.X, street - th, towerW, th));
            if (tr.H > 0.5f)
            {
                if (ghost)
                {
                    ui.FillTopRounded(tr, 7, t.Primary(0.05f), t.Primary(0.05f));
                    ui.DashedTopRounded(tr, 7, Theme.WithAlpha(t.Gray, 0.6f), 3);
                }
                else
                {
                    var bright = i.Hovered ? 0.06f : 0;
                    ui.FillTopRounded(tr, 7, Theme.Mix(color, new Color4(1, 1, 1, 1), bright), Theme.Mix(Theme.Mix(color, new Color4(0, 0, 0, 1), 0.34f), new Color4(1, 1, 1, 1), bright));
                }
                if (c.Windows != null) Windows(ui, c.Windows, tr, ghost);
            }
            var label = c.Score is { } s2 ? $"{(int)Math.Round(s2)}%" : "—";
            var labelA = ui.Animate($"dash:sky:{c.Id}:l", risen > 0.3 + delay ? 1 : 0, 0.42f, 0.92f);
            ui.TextIn(label, new R(cx - colW / 2, street - th - 6 - 16, colW, 16), Theme.Bold(Theme.Subheadline), Theme.WithAlpha(c.Score == null ? t.Ink2 : t.Ink(), labelA));
            if (i.Clicked) engine.Go(Place.Section("courses/" + c.Id, "grades"));
        }
        ui.Fill(new R(cx0, street, cw, 1), t.Primary(0.14f));
        for (var k = 0; k < courses.Count; k++)
        {
            var name = courses[k].Name.ToUpperInvariant();
            var f = new Font(Theme.TextFace, 11, FontWeight.SemiBold);
            ui.TextIn(name, new R(cx0 + colW * k + 2, street + 8, colW - 4, 14), f, t.Ink2);
        }
        // the legend
        var ly = street + 8 + 14 + 14;
        var lx = cx0;
        Lines.Text(ui, "Window colour = score", lx, ly, Theme.Caption, t.Ink2);
        lx += ui.Measure("Window colour = score", Theme.Caption).X + 12;
        foreach (var b in new[] { "A", "B", "C", "D", "F" })
        {
            ui.Fill(new R(lx, ly + 3.5f, 8, 8), 1.5f, Lit(b));
            Lines.Text(ui, b, lx + 11, ly + 1, Theme.Semibold(Theme.Caption2), t.Ink2);
            lx += 11 + ui.Measure(b, Theme.Semibold(Theme.Caption2)).X + 8;
        }
        return card.Bottom;
    }

    /// <summary>A tower's windows: what counts from the top floor down, what does not on the floors at the street (the web skyline's fit).</summary>
    private void Windows(Ui ui, List<DashSkyWindow> windows, R tower, bool ghost)
    {
        if (windows.Count == 0 || tower.W <= 12 || tower.H <= 12) return;
        var up = windows.Where(w => w.Free != true).ToList();
        var down = windows.Where(w => w.Free == true).ToList();
        const float top = 7;
        var bottom = down.Count == 0 ? 0 : 7f;
        var between = up.Count == 0 || down.Count == 0 ? 0 : 6f;
        var (cols, s, g) = Fit(windows.Count, tower.W - 10, tower.H - top - bottom - between);
        var gridW = cols * s + (cols - 1) * g;
        var x0 = MathF.Round(tower.X + (tower.W - gridW) / 2);
        for (var k = 0; k < up.Count; k++)
        {
            var r = k / cols;
            var c = k % cols;
            ui.Fill(new R(x0 + c * (s + g), tower.Y + top + r * (s + g), s, s), 1.5f, WindowColor(up[k], ghost));
        }
        var downRows = (down.Count + cols - 1) / cols;
        var y0 = tower.Bottom - bottom - downRows * s - Math.Max(downRows - 1, 0) * g;
        for (var k = 0; k < down.Count; k++)
        {
            var r = k / cols;
            var c = k % cols;
            ui.Fill(new R(x0 + c * (s + g), y0 + r * (s + g), s, s), 1.5f, WindowColor(down[k], ghost));
        }
    }

    private static (int Cols, float Size, float Gap) Fit(int n, float w, float h)
    {
        const float g = 3;
        var bestCols = 4;
        var bestSize = 0f;
        foreach (var cols in new[] { 4, 3, 5, 6, 7, 8 })
        {
            var rows = Math.Max(1, (n + cols - 1) / cols);
            var across = (w - (cols - 1) * g) / cols;
            var down = (h - (rows - 1) * g) / rows;
            var s = Math.Min(7, Math.Min(across, down));
            if (s > bestSize + 0.01f) { bestCols = cols; bestSize = s; }
        }
        return (bestCols, Math.Max(2, MathF.Floor(bestSize * 2) / 2), g);
    }

    private Color4 WindowColor(DashSkyWindow w, bool ghost)
    {
        if (w.Band is { } band) return w.Free == true ? new Color4(0.62f, 0.62f, 0.62f, 1) : Lit(band);
        if (w.Free == true) return new Color4(92 / 255f, 92 / 255f, 97 / 255f, 0.92f);
        return ghost ? Theme.WithAlpha(host.Ui.T.Gray, 0.3f) : new Color4(0, 0, 0, 0.22f);
    }

    // ---- the week's load (the Mac's DashWeekLoad) ----

    private float WeekLoad(Ui ui, Today d, float x, float y, float w)
    {
        var t = ui.T;
        if (d.Load == null || d.HasCourses == false) return y;
        Headings.Section(ui, "Week load", x, y, w, "Handed in of assigned");
        y += 23 + 10;
        var load = d.Load;
        var idle = d.Idle is > 0 ? $"{d.Idle} {(d.Idle == 1 ? "course" : "courses")} with nothing assigned this week" : null;
        var h = 36 + (load.Count == 0 ? 42 : load.Count * 30 + (load.Count - 1) * 14) + (idle != null ? 14 + 15 : 0);
        var card = new R(x, y, w, h);
        ui.Glass(card, 18);
        var ry = card.Y + 18;
        var rx = card.X + 18;
        var rw = w - 36;
        if (load.Count == 0) ry += ui.EmptyNote("Nothing assigned this week.", rx, ry - 12, rw, "sun.max") - 12;
        foreach (var r in load)
        {
            var row = new R(rx - 6, ry - 4, rw + 12, 38);
            var i = ui.Press("dash:load:" + r.Id, row);
            ui.Circle(rx + 4.5f, ry + 8.5f, 4.5f, Theme.Hex(r.Color));
            var of = $"{r.Done} of {r.Total}";
            var ow = ui.Measure(of, Theme.Callout).X;
            Lines.Text(ui, of, rx + rw - ow, ry, Theme.Callout, t.Ink2);
            Lines.Text(ui, r.Code, rx + 17, ry, Theme.Semibold(Theme.Callout), t.Ink(), rw - 17 - ow - 8);
            ui.Bar("dash:load:bar:" + r.Id, rx, ry + 17 + 6, rw, r.Total > 0 ? (double)r.Done / r.Total : 0, Theme.Hex(r.Color));
            if (i.Clicked) engine.Go(Place.Section("courses/" + r.Id, "assignments"));
            ry += 30 + 14;
        }
        if (idle != null) Lines.Text(ui, idle, rx, ry, Theme.Caption, t.Ink2, rw);
        return card.Bottom;
    }
}
