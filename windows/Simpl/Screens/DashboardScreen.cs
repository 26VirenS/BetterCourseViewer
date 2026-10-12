using Simpl.Graphics;
using Simpl.Models;
using Simpl.Support;
using Simpl.UI;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Simpl.Screens;

/// <summary>
/// The Dashboard (the Mac's DashboardView): the day at a glance — a greeting; six counters; then one of three views,
/// chosen in the toolbar and kept — Cards (the courses), List (everything coming up, day by day, ticked off where it is
/// done) and Activity (the school's recent activity); beside it on a wide window, under it on a narrow one: today's
/// work and the week's load per course. Every card is glass over the page.
/// </summary>
public sealed class DashboardScreen
{
    private readonly Engine.Engine engine;
    private readonly GlassHost host;
    private readonly Loader<Today> today;
    private readonly Loader<TodayCounts> counts;
    private readonly Loader<DashListData> list;
    private readonly Loader<DashCoursesData> courses;
    private readonly Loader<DashActivityData> activity;
    private readonly Dictionary<string, bool> ticked = new();
    private int view;
    private static readonly string[] Views = { "Cards", "List", "Activity" };

    public DashboardScreen(Engine.Engine engine, GlassHost host)
    {
        this.engine = engine;
        this.host = host;
        today = new(host);
        counts = new(host);
        list = new(host);
        courses = new(host);
        activity = new(host);
        view = Launch.Get("dash-view") switch { "cards" => 0, "activity" => 2, "list" => 1, _ => (int)Prefs.Double("dashView", 1) };
    }

    /// <summary>Cards · List · Activity, and New Task: the screen's own buttons in the toolbar, right-aligned at <paramref name="right"/>.</summary>
    public void ToolbarItems(Ui ui, float right, float y, float minX)
    {
        var views = engine.OnBrightspace ? Views[..2] : Views;
        var w = 92f * views.Length + 6;
        var plus = new R(right - AppView.Bar - 4, y, AppView.Bar + 4, AppView.Bar);
        var seg = new R(plus.X - 10 - w, y, w, AppView.Bar);
        if (seg.X < minX) return;
        var v = Math.Min(view, views.Length - 1);
        if (ui.Segmented("dash:view", seg, views, ref v)) { view = v; Prefs.Set("dashView", v); }
        if (ui.IconCapsule("dash:new", plus.X, y, new[] { "plus" }, null, AppView.Bar) == 0) engine.AskNewTask();
    }

    public void Frame(Ui ui, R page, float top)
    {
        today.Ensure(engine, "today");
        counts.Ensure(engine, "todayCounts", new { kept = false });
        var t = ui.T;
        var vp = new R(page.X, 0, page.W, page.H);
        var off = ui.BeginScroll("page", vp);
        var x = page.X + 8;
        var width = page.W - 30;
        var y = top - off + 6;

        var d = today.Data;
        if (d == null)
        {
            if (today.Error is { } err) Problem(ui, new R(x, y + 40, width, 160), err, () => today.Again());
            else ui.Spinner(page.CenterX, y + 120, 12);
            ui.EndScroll("page", vp, 400);
            return;
        }

        // the greeting
        ui.Text(Greeting(d), x, y, Theme.LargeTitle, t.Ink(), width);
        y += 46;
        if (!string.IsNullOrEmpty(d.DateLine)) { ui.Text(d.DateLine!, x, y, Theme.Body, t.Ink2, width); y += 30; }
        y += 14;

        // the counters
        y = Counters(ui, d, x, y, width) + 26;

        // the view, and beside it (or under it) the rail
        var two = width >= 900;
        var railW = Math.Clamp(MathF.Round(width * 0.3f), 340, 440);
        var mainW = two ? width - railW - 24 : width;
        var mainEnd = view switch
        {
            0 => Cards(ui, x, y, mainW),
            2 => Activity(ui, x, y, mainW),
            _ => ListView(ui, x, y, mainW),
        };
        var railEnd = two ? Rail(ui, d, x + mainW + 24, y, railW) : Rail(ui, d, x, mainEnd + 26, width);
        var end = Math.Max(mainEnd, railEnd);
        ui.EndScroll("page", vp, end + off + 40);
    }

    private static string Greeting(Today d)
    {
        var first = (d.Me?.Name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var hour = DateTime.Now.Hour;
        var part = hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
        return first.Length == 0 ? part : $"{part}, {first}";
    }

    // ---- the counters ----

    private static readonly string[] Order = { "today", "tomorrow", "next", "overdue", "unread", "graded" };

    private float Counters(Ui ui, Today d, float x, float y, float width)
    {
        var t = ui.T;
        var cols = width >= 900 ? 6 : width >= 520 ? 3 : 2;
        var items = d.Counters.OrderBy(c => Array.IndexOf(Order, c.Key) is var i && i < 0 ? 99 : i).ToList();
        var gap = 14f;
        var w = (width - gap * (cols - 1)) / cols;
        const float h = 108;
        for (var k = 0; k < items.Count; k++)
        {
            var c = items[k];
            var r = new R(x + (k % cols) * (w + gap), y + (k / cols) * (h + gap), w, h);
            ui.Glass(r, 18);
            var i = ui.Press("dash:counter:" + c.Key, r);
            if (i.Hovered) ui.Fill(r, 18, t.Hover);
            var (value, note) = Count(c);
            ui.Text(c.Label, r.X + 16, r.Y + 13, Theme.Semibold(Theme.Callout), t.Tone(c.Tone), r.W - 28);
            if (value == null) ui.Spinner(r.X + 28, r.Y + 56, 9);
            else ui.Text(value, r.X + 16, r.Y + 32, new Font(Theme.DisplayFace, 34, FontWeight.Bold), t.Ink(), r.W - 28);
            if (!string.IsNullOrEmpty(note)) ui.Text(note!, r.X + 16, r.Bottom - 27, Theme.Caption, t.Ink2, r.W - 28);
            if (i.Clicked) engine.Go(c.Key is "unread" ? Engine.Place.Notifications : c.Key is "graded" ? Engine.Place.Grades : Engine.Place.Todo);
        }
        var rows = (items.Count + cols - 1) / cols;
        return y + rows * h + Math.Max(0, rows - 1) * gap;
    }

    /// <summary>A counter's number and note: Overdue and Graded from the counts read on their own (they take longer).</summary>
    private (string? Value, string? Note) Count(Counter c)
    {
        var k = counts.Data;
        if (c.Key == "overdue" && k?.Overdue is { } o) return (o.ToString(), k.OverdueNote ?? c.Note);
        if (c.Key == "graded" && k?.Graded is { } g) return (g.ToString(), k.GradedNote ?? c.Note);
        if (c.Pending == true || c.Value == null) return (null, c.Note);
        return (c.Value.Value.ToString(), c.Note);
    }

    // ---- List: everything coming up, day by day ----

    private float ListView(Ui ui, float x, float y, float w)
    {
        list.Ensure(engine, "dashList");
        var t = ui.T;
        var l = list.Data;
        if (l == null)
        {
            if (list.Error is { } e) return Problem(ui, new R(x, y, w, 150), e, () => list.Again());
            ui.Spinner(x + w / 2, y + 40, 10);
            return y + 80;
        }
        if (l.Days.Count == 0)
        {
            var r = new R(x, y, w, 120);
            ui.Glass(r, 18);
            ui.Icon("checkmark.circle", r.CenterX, r.Y + 42, 26, t.Green);
            ui.Text(l.Empty ?? "Nothing coming up.", r.X, r.Y + 70, Theme.Body, t.Ink2, r.W, TextAlignment.Center);
            return r.Bottom;
        }
        foreach (var day in l.Days)
        {
            ui.Text(day.Title, x + 4, y, Theme.Semibold(Theme.Title3), t.Ink(), w * 0.6f);
            if (!string.IsNullOrEmpty(day.Date)) ui.Text(day.Date!, x + 4 + ui.Measure(day.Title, Theme.Semibold(Theme.Title3)).X + 10, y + 4, Theme.Callout, t.Ink2, w * 0.4f);
            y += 34;
            const float rowH = 56;
            var card = new R(x, y, w, day.Rows.Count * rowH + 8);
            ui.Glass(card, 18);
            var ry = card.Y + 4;
            for (var k = 0; k < day.Rows.Count; k++)
            {
                var row = day.Rows[k];
                var r = new R(card.X + 4, ry, card.W - 8, rowH);
                WorkLine(ui, "dash:list:" + row.Id, r, row.Id, row.Title, row.Course, row.Color, Join(row.Time, row.Points), row.Done, row.Url, Components.Glyphs.Work(row.Kind ?? row.Type));
                if (k < day.Rows.Count - 1) ui.Line(r.X + 52, r.Bottom, r.Right - 12, r.Bottom, t.Separator);
                ry += rowH;
            }
            y = card.Bottom + 22;
        }
        return y;
    }

    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));

    /// <summary>A piece of work on a line: its tick, its title, its course and when; a click opens it.</summary>
    private void WorkLine(Ui ui, string id, R r, string workId, string title, string? course, string? color, string? meta, bool done, string? url, string glyph)
    {
        var t = ui.T;
        var on = ticked.TryGetValue(workId, out var tk) ? tk : done;
        var i = ui.Row(id, r, false, 12);
        if (ui.Check(id + ":tick", r.X + 24, r.CenterY, on, Theme.Hex(color))) Tick(workId, !on);
        else if (i.Clicked && !string.IsNullOrEmpty(url)) engine.OpenWeb(url!, title);
        var ink = on ? t.Ink3 : t.Ink();
        ui.Text(title, r.X + 48, r.Y + 9, Theme.Body, ink, r.W - 60);
        var sub = Join(course, meta);
        ui.Fill(new R(r.X + 48, r.Y + 35, 8, 8), 2.5f, Theme.Hex(color));
        ui.Glyph(glyph, r.Right - 22, r.CenterY, 13, t.Ink3);
        ui.Text(sub, r.X + 62, r.Y + 30, Theme.Footnote, t.Ink2, r.W - 100);
    }

    /// <summary>Ticked off (or back on): shown at once; the school told; read again.</summary>
    private async void Tick(string id, bool done)
    {
        ticked[id] = done;
        host.Invalidate();
        var ok = await engine.Act("complete", new { id, done });
        if (!ok) ticked[id] = !done;
        else
        {
            await Task.Delay(450);
            list.Again();
            today.Again();
            engine.DataChanged();
            ticked.Remove(id);
        }
        host.Invalidate();
    }

    // ---- Cards: the courses ----

    private float Cards(Ui ui, float x, float y, float w)
    {
        courses.Ensure(engine, "dashCourses");
        var t = ui.T;
        var c = courses.Data;
        if (c == null)
        {
            if (courses.Error is { } e) return Problem(ui, new R(x, y, w, 150), e, () => courses.Again());
            ui.Spinner(x + w / 2, y + 40, 10);
            return y + 80;
        }
        var cols = w >= 900 ? 3 : w >= 560 ? 2 : 1;
        const float gap = 16, h = 168;
        var cw = (w - gap * (cols - 1)) / cols;
        for (var k = 0; k < c.Rows.Count; k++)
        {
            var row = c.Rows[k];
            var r = new R(x + (k % cols) * (cw + gap), y + (k / cols) * (h + gap), cw, h);
            var color = Theme.Hex(row.Color);
            ui.Glass(r, 20, GlassKind.Panel, color, 0.22f);
            var i = ui.Press("dash:card:" + row.Id, r);
            if (i.Hovered) ui.Fill(r, 20, t.Hover);
            ui.Fill(new R(r.X + 16, r.Y + 18, 30, 30), 8, color);
            ui.TextIn(row.Code.Length > 3 ? row.Code[..3] : row.Code, new R(r.X + 16, r.Y + 18, 30, 30), Theme.Semibold(Theme.Caption2), new Color4(1, 1, 1, 1));
            ui.Text(row.Code, r.X + 56, r.Y + 15, Theme.Headline, t.Ink(), r.W - 140);
            ui.Text(row.Name ?? row.Sub ?? "", r.X + 56, r.Y + 36, Theme.Footnote, t.Ink2, r.W - 140);
            var score = row.ScoreText ?? (row.Score is { } s ? $"{s:0.#}%" : "");
            if (score.Length > 0)
            {
                var sw = ui.Measure(score, Theme.Bold(Theme.Title3)).X;
                ui.Text(score, r.Right - 16 - sw, r.Y + 17, Theme.Bold(Theme.Title3), t.Ink(), sw + 2);
                if (!string.IsNullOrEmpty(row.Grade)) ui.Text(row.Grade!, r.Right - 16 - sw, r.Y + 42, Theme.Caption, t.Ink2, sw + 2);
            }
            var ny = r.Y + 78;
            if (row.Next is { } n)
            {
                ui.Text("Next", r.X + 16, ny, Theme.Semibold(Theme.Caption), t.Ink3);
                ui.Text(n.Title, r.X + 16, ny + 18, Theme.Callout, t.Ink(), r.W - 32);
                if (!string.IsNullOrEmpty(n.When)) ui.Text(n.When!, r.X + 16, ny + 39, Theme.Footnote, t.Ink2, r.W - 32);
            }
            else ui.Text("Nothing due soon", r.X + 16, ny + 18, Theme.Callout, t.Ink2, r.W - 32);
            var chips = Join(row.DueToday is > 0 ? $"{row.DueToday} due today" : null, row.Unread is > 0 ? $"{row.Unread} unread" : null);
            if (chips.Length > 0) ui.Text(chips, r.X + 16, r.Bottom - 26, Theme.Semibold(Theme.Caption), t.Orange, r.W - 32);
            if (i.Clicked) engine.Go(Engine.Place.Home("courses/" + row.Id));
        }
        var rows = (c.Rows.Count + cols - 1) / cols;
        if (c.Rows.Count == 0)
        {
            ui.Text(c.Empty ?? "No courses.", x, y + 10, Theme.Body, t.Ink2, w);
            return y + 40;
        }
        return y + rows * h + (rows - 1) * gap;
    }

    // ---- Activity ----

    private float Activity(Ui ui, float x, float y, float w)
    {
        activity.Ensure(engine, "dashActivity");
        var t = ui.T;
        var a = activity.Data;
        if (a == null)
        {
            if (activity.Error is { } e) return Problem(ui, new R(x, y, w, 150), e, () => activity.Again());
            ui.Spinner(x + w / 2, y + 40, 10);
            return y + 80;
        }
        if (a.Rows.Count == 0)
        {
            ui.Text(a.Empty ?? "No recent activity.", x + 4, y, Theme.Body, t.Ink2, w);
            return y + 30;
        }
        const float rowH = 74;
        var card = new R(x, y, w, a.Rows.Count * rowH + 8);
        ui.Glass(card, 18);
        var ry = card.Y + 4;
        for (var k = 0; k < a.Rows.Count; k++)
        {
            var row = a.Rows[k];
            var r = new R(card.X + 4, ry, card.W - 8, rowH);
            var i = ui.Row("dash:act:" + row.Id, r, false, 12);
            ui.Fill(new R(r.X + 12, r.Y + 14, 30, 30), 8, Theme.Hex(row.Color, 0.9f));
            ui.Glyph(Components.Glyphs.Work(row.Kind ?? row.Type), r.X + 27, r.Y + 29, 14, new Color4(1, 1, 1, 1));
            ui.Text(row.Title, r.X + 54, r.Y + 9, row.Unread == true ? Theme.Semibold(Theme.Body) : Theme.Body, t.Ink(), r.W - 150);
            ui.Text(Join(row.Course, row.When), r.X + 54, r.Y + 31, Theme.Footnote, t.Ink2, r.W - 70);
            if (!string.IsNullOrEmpty(row.Preview)) ui.Text(row.Preview!, r.X + 54, r.Y + 50, Theme.Footnote, t.Ink3, r.W - 70);
            if (row.Unread == true) ui.Circle(r.Right - 16, r.Y + 20, 4.5f, t.Accent);
            if (i.Clicked && !string.IsNullOrEmpty(row.Url)) engine.OpenWeb(row.Url!, row.Title);
            if (k < a.Rows.Count - 1) ui.Line(r.X + 54, r.Bottom, r.Right - 12, r.Bottom, t.Separator);
            ry += rowH;
        }
        return card.Bottom;
    }

    // ---- the rail: today's work, the week's load ----

    private float Rail(Ui ui, Today d, float x, float y, float w)
    {
        var t = ui.T;
        ui.Text(d.List.Heading.Length > 0 ? d.List.Heading : "Today", x + 4, y, Theme.Semibold(Theme.Title3), t.Ink(), w);
        y += 34;
        const float rowH = 56;
        var rows = d.List.Rows;
        if (rows.Count == 0)
        {
            var r = new R(x, y, w, 96);
            ui.Glass(r, 18);
            ui.Text(d.List.Empty ?? "Nothing due today.", r.X + 16, r.Y + 38, Theme.Body, t.Ink2, r.W - 32, TextAlignment.Center);
            y = r.Bottom + 22;
        }
        else
        {
            var card = new R(x, y, w, rows.Count * rowH + 8);
            ui.Glass(card, 18);
            var ry = card.Y + 4;
            for (var k = 0; k < rows.Count; k++)
            {
                var row = rows[k];
                var r = new R(card.X + 4, ry, card.W - 8, rowH);
                WorkLine(ui, "dash:today:" + row.Id, r, row.Id, row.Title, row.CourseName ?? row.Course, row.Color, Join(row.Time ?? row.When, row.Meta), row.Done, row.Url, Components.Glyphs.Work(row.Type));
                if (k < rows.Count - 1) ui.Line(r.X + 52, r.Bottom, r.Right - 12, r.Bottom, t.Separator);
                ry += rowH;
            }
            y = card.Bottom + 22;
        }

        if (d.Load is { Count: > 0 } load)
        {
            ui.Text("This week", x + 4, y, Theme.Semibold(Theme.Title3), t.Ink(), w);
            y += 34;
            var card = new R(x, y, w, load.Count * 34 + 22);
            ui.Glass(card, 18);
            var ly = card.Y + 14;
            var codeW = 96f;
            foreach (var l in load)
            {
                ui.Text(l.Code, card.X + 16, ly + 1, Theme.Semibold(Theme.Footnote), t.Ink(), codeW - 8);
                var bar = new R(card.X + 16 + codeW, ly + 6, card.W - 32 - codeW - 50, 8);
                ui.Fill(bar, 4, t.Separator);
                var frac = l.Total > 0 ? (float)l.Done / l.Total : 0;
                if (frac > 0) ui.Fill(new R(bar.X, bar.Y, Math.Max(8, bar.W * frac), bar.H), 4, Theme.Hex(l.Color));
                ui.Text($"{l.Done}/{l.Total}", bar.Right + 10, ly + 1, Theme.Footnote, t.Ink2, 44);
                ly += 34;
            }
            y = card.Bottom + 22;
        }
        return y;
    }

    // ---- what could not be read ----

    private float Problem(Ui ui, R r, string message, Action again)
    {
        var t = ui.T;
        ui.Glass(r, 18);
        ui.Icon("exclamationmark.triangle", r.CenterX, r.Y + 34, 22, t.Orange);
        ui.Text(message, r.X + 24, r.Y + 58, Theme.Callout, t.Ink2, r.W - 48, TextAlignment.Center, 2);
        if (ui.Button("problem:again:" + message.GetHashCode(), new R(r.CenterX - 60, r.Bottom - 48, 120, 34), "Try Again")) { again(); host.Invalidate(); }
        return r.Bottom;
    }
}
