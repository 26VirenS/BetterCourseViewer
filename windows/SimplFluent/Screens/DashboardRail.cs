using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

public sealed partial class DashboardScreen
{
    private FrameworkElement Rail(Today d, double w, bool narrow)
    {
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 28 };
        if (!narrow && View != 1) v.Children.Add(TodaySection(d, false));
        var sky = Skyline(w);
        var load = WeekLoad(d);
        if (narrow && w >= 760 && sky != null && load != null)
            v.Children.Add(K.Columns("*, 22, *", sky.Col(0), load.Col(2)));
        else
        {
            if (sky != null) v.Children.Add(sky);
            if (load != null) v.Children.Add(load);
        }
        return v;
    }

    // ---- Today: the day's work beside the view ----

    private FrameworkElement TodaySection(Today d, bool narrow)
    {
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        v.Children.Add(Heading.Section(d.List.Heading.Length > 0 ? d.List.Heading : "Today", d.List.Note));
        var rows = d.List.Rows;
        var list = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        if (rows.Count == 0) list.Children.Add(K.Empty(SymbolRegular.WeatherSunny24, d.List.Empty ?? "Nothing due."));
        for (var k = 0; k < rows.Count; k++)
        {
            if (k > 0) list.Children.Add(K.Divider(84));
            list.Children.Add(TodayLine(rows[k], narrow));
        }
        v.Children.Add(K.Card(list, new Thickness(4)));
        return v;
    }

    /// <summary>A row of the day's work (the Mac's DashTodayLine): the check, then the Dashboard's one row of work — its
    /// course and kind over its title, its flag, its time and points at its right (one over the other in the rail, side by
    /// side across a one-column page) — as Coming up draws the same work.</summary>
    private FrameworkElement TodayLine(WorkRow row, bool narrow)
    {
        var done = Done(row.Id, row.Done);
        var check = new CheckCircle(done, done ? "Mark as not done" : "Mark as done");
        check.Toggled += on => Tick(row.Id, on);
        var title = K.Wrapped(row.Title, Ty.Body, done ? B.Text2 : B.Text, 2);
        if (done) title.Struck();
        var custom = row.Custom == true;
        var type = row.Type ?? row.Id.Split(':')[0];
        var symbol = custom ? SymbolRegular.CheckboxChecked24 : Glyph.Work(type);
        // (a page from before the row's kind and points came: its line as it was, "Assignment · 10 pts", for the kind)
        var kind = row.Kind ?? (row.Pts == null ? row.Sub : null);
        var flags = row.Flag != null ? new[] { row.Flag } : null;
        var face = WorkFace(K.Hex(row.Color), symbol, row.Course, kind, flags, title, row.Pts, row.Time, stacked: !narrow, whenWidth: 90);
        var g = K.Columns("auto, *", check.Margin(6, 0, 12, 0).Col(0), face.Col(1));
        var b = K.Subtle(g, null, new Thickness(4, 10, 4, 10));
        b.Click += (_, _) => WorkPreview.Press(WorkPreview.Work(row, engine, on => Tick(row.Id, on)), b);
        return b;
    }

    // ---- the grades skyline (the Mac's DashSkyline) ----

    private static Color Lit(string band) => band switch
    {
        "A" => Color.FromRgb(0x4c, 0xd9, 0x64), "B" => Color.FromRgb(0xb5, 0xe0, 0x36), "C" => Color.FromRgb(0xff, 0xd4, 0x26),
        "D" => Color.FromRgb(0xff, 0x9f, 0x0a), _ => Color.FromRgb(0xff, 0x45, 0x3a),
    };

    private FrameworkElement? Skyline(double w)
    {
        var s = skyFresh.Data ?? skyKept.Data;
        if (s == null) return null;
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        v.Children.Add(Heading.Section("Grades", null, K.Link("All grades", () => engine.Go(Place.Grades))));
        var list = s.Courses;
        if (list.Count == 0) { v.Children.Add(K.Card(K.Empty(SymbolRegular.Building24, "No courses chosen yet."))); return v; }
        const double height = 150;
        var towerW = list.Count switch { <= 5 => 54.0, 6 => 48, 7 or 8 => 40, _ => 32 };
        var city = new Grid { Height = height + 24 };
        var names = new Grid();
        for (var k = 0; k < list.Count; k++)
        {
            city.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            names.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        for (var k = 0; k < list.Count; k++)
        {
            var c = list[k];
            var fraction = c.Score is { } sc ? Math.Max(0.16, Math.Min(1, sc / 100)) : 0.42;
            var th = Math.Round(height * fraction);
            var color = K.Hex(c.Color);
            var ghost = c.Score == null;
            var tower = Tower(c, towerW, th, color, ghost);
            // (its whole percent as the engine works it out from the shown figure, half up — never .NET's banker's rounding)
            var label = K.Text(c.ScoreWhole is { } whole ? $"{whole}%" : c.Score is { } s2 ? $"{(int)Math.Round(s2, MidpointRounding.AwayFromZero)}%" : "–", Ty.Caption, ghost ? B.Text2 : B.Text).Strong().Center();
            var col = new Stack { Orientation = Orientation.Vertical, Spacing = 4, VerticalAlignment = VerticalAlignment.Bottom, Children = { label, tower } };
            var id = c.Id;
            var b = K.Subtle(col, () => engine.Go(Place.Section("courses/" + id, "grades")), new Thickness(0), 6);
            b.VerticalAlignment = VerticalAlignment.Bottom;
            b.ToolTip = $"{c.Name}: {(c.Score != null && !string.IsNullOrEmpty(c.ScoreText) ? c.ScoreText : c.Score is { } s3 ? $"{Math.Round((decimal)s3, 1, MidpointRounding.AwayFromZero):0.#}%" : "no grade yet")}";
            city.Children.Add(b.Col(k));
            // the towers rise, one after another, the first time the Dashboard shows (Windows' decelerate)
            if (!skyRisen)
            {
                var st = new ScaleTransform(1, 0);
                tower.RenderTransformOrigin = new Point(0.5, 1);
                tower.RenderTransform = st;
                Motion.FromTo(st, ScaleTransform.ScaleYProperty, 0, 1, 520, Motion.Decelerate, 80 + Math.Min(k, 8) * 70);
                Motion.FadeIn(label, Motion.Medium, 320 + Math.Min(k, 8) * 70);
            }
            var name = K.Text(c.Name.ToUpperInvariant(), Ty.Caption, B.Text2).Center();
            name.FontSize = 11;
            name.FontWeight = FontWeights.SemiBold;
            names.Children.Add(name.Margin(2, 0, 2, 0).Col(k));
        }
        skyRisen = true;
        var street = new Border { Height = 1 }.Bg(B.Divider);
        var legend = K.H(10, K.Text("Window colour = score", Ty.Caption, B.Text2));
        foreach (var bnd in new[] { "A", "B", "C", "D", "F" })
            legend.Children.Add(K.H(4, new Rectangle { Width = 8, Height = 8, RadiusX = 1.5, RadiusY = 1.5, Fill = K.Brush(Lit(bnd)), VerticalAlignment = VerticalAlignment.Center }, K.Text(bnd, Ty.Caption, B.Text2).Strong()));
        var card = K.Card(K.V(8, city, street, names, legend.Margin(0, 6, 0, 0)), new Thickness(18, 16, 18, 14));
        v.Children.Add(card);
        return v;
    }

    /// <summary>A tower: rounded at the top, its colour deepening toward the street; its windows lit by how each piece of work went.</summary>
    private FrameworkElement Tower(DashSkyCourse c, double w, double h, Color color, bool ghost)
    {
        var g = new Grid { Width = w, Height = h, HorizontalAlignment = HorizontalAlignment.Center };
        var body = new Border { CornerRadius = new CornerRadius(7, 7, 0, 0) };
        if (ghost)
        {
            body.Bg(B.Subtle);
            body.BorderThickness = new Thickness(1, 1, 1, 0);
            body.BorderBrush = K.Brush(Color.FromArgb(120, 0x8a, 0x8a, 0x8a));
        }
        else body.Background = new LinearGradientBrush(color, K.Mix(color, Colors.Black, 0.34), 90);
        g.Children.Add(body);
        if (c.Windows is { Count: > 0 } wins && w > 12 && h > 12) g.Children.Add(Windows(wins, w, h, ghost));
        return g;
    }

    private FrameworkElement Windows(List<DashSkyWindow> windows, double w, double h, bool ghost)
    {
        var up = windows.Where(x => x.Free != true).ToList();
        var down = windows.Where(x => x.Free == true).ToList();
        const double top = 7;
        var bottom = down.Count == 0 ? 0 : 7.0;
        var between = up.Count == 0 || down.Count == 0 ? 0 : 6.0;
        var (cols, s, gap) = Fit(windows.Count, w - 10, h - top - bottom - between);
        var gridW = cols * s + (cols - 1) * gap;
        var x0 = Math.Round((w - gridW) / 2);
        var canvas = new Canvas { Width = w, Height = h };
        void Put(DashSkyWindow win, double x, double y)
        {
            var r = new Rectangle { Width = s, Height = s, RadiusX = 1.5, RadiusY = 1.5, Fill = K.Brush(WindowColor(win, ghost)) };
            if (!string.IsNullOrEmpty(win.Title)) r.ToolTip = win.Title;
            Canvas.SetLeft(r, x);
            Canvas.SetTop(r, y);
            canvas.Children.Add(r);
        }
        for (var k = 0; k < up.Count; k++) Put(up[k], x0 + (k % cols) * (s + gap), top + (k / cols) * (s + gap));
        var downRows = (down.Count + cols - 1) / cols;
        var y0 = h - bottom - downRows * s - Math.Max(downRows - 1, 0) * gap;
        for (var k = 0; k < down.Count; k++) Put(down[k], x0 + (k % cols) * (s + gap), y0 + (k / cols) * (s + gap));
        return canvas;
    }

    private static (int Cols, double Size, double Gap) Fit(int n, double w, double h)
    {
        const double g = 3;
        var bestCols = 4;
        var bestSize = 0.0;
        foreach (var cols in new[] { 4, 3, 5, 6, 7, 8 })
        {
            var rows = Math.Max(1, (n + cols - 1) / cols);
            var across = (w - (cols - 1) * g) / cols;
            var down = (h - (rows - 1) * g) / rows;
            var s = Math.Min(7, Math.Min(across, down));
            if (s > bestSize + 0.01) { bestCols = cols; bestSize = s; }
        }
        return (bestCols, Math.Max(2, Math.Floor(bestSize * 2) / 2), g);
    }

    private static Color WindowColor(DashSkyWindow w, bool ghost)
    {
        if (w.Band is { } band) return w.Free == true ? Color.FromRgb(0x9e, 0x9e, 0x9e) : Lit(band);
        if (w.Free == true) return Color.FromArgb(235, 92, 92, 97);
        return ghost ? Color.FromArgb(76, 0x8a, 0x8a, 0x8a) : Color.FromArgb(56, 0, 0, 0);
    }

    // ---- the week's load (the Mac's DashWeekLoad) ----

    /// <summary>The week's load: a line a course, about 28 tall — its colour, its name, a short bar and "2 / 9 handed in"
    /// — under the caption the engine gives ("Handed in this week"), each a way to the course's assignments.</summary>
    private FrameworkElement? WeekLoad(Today d)
    {
        if (d.Load == null || d.HasCourses == false) return null;
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        v.Children.Add(Heading.Section("Week load", string.IsNullOrEmpty(d.LoadCaption) ? "Handed in this week" : d.LoadCaption));
        var rows = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        if (d.Load.Count == 0) rows.Children.Add(K.Empty(SymbolRegular.WeatherSunny24, "Nothing assigned this week."));
        foreach (var r in d.Load)
        {
            var color = K.Hex(r.Color);
            var words = string.IsNullOrEmpty(r.Text) ? $"{r.Done} / {r.Total} handed in" : r.Text!;
            var bar = K.Bar(r.Total > 0 ? (double)r.Done / r.Total : 0, color);
            bar.Width = 56;
            var line = K.Columns("auto, *, auto, auto",
                K.Dot(color, 8).Col(0),
                // (a long code on two lines rather than cut short)
                K.Wrapped(r.Code, Ty.Body, B.Text, 2).Margin(8, 0, 10, 0).Align(HorizontalAlignment.Stretch, VerticalAlignment.Center).Col(1),
                bar.Margin(0, 0, 10, 0).Col(2),
                K.Text(words, Ty.Caption, B.Text2).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(3));
            var id = r.Id;
            var row = K.Subtle(line, () => engine.Go(Place.Section("courses/" + id, "assignments")), new Thickness(10, 5, 10, 5));
            row.MinHeight = 28;
            row.ToolTip = $"{r.Code}: {words}";
            System.Windows.Automation.AutomationProperties.SetName(row, $"{r.Code}, {words}");
            rows.Children.Add(row);
        }
        if (d.Idle is > 0) rows.Children.Add(K.Text($"{d.Idle} {(d.Idle == 1 ? "course" : "courses")} with nothing assigned this week", Ty.Caption, B.Text2).Margin(10, 6, 10, 4));
        v.Children.Add(K.Card(rows, new Thickness(6)));
        return v;
    }
}
