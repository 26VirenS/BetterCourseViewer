using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;

namespace Simpl.Shell;

/// <summary>
/// (0.3.80, the Mac's 1.3.182 MergedIntroSheet) After an update, once (Support/WhatsNew.cs's MergedIntro): the scene
/// playing over and over, a line on the two as one, Quick Plan's card, Got it and Open To Do &amp; Calendar.
/// </summary>
public static class MergedIntroSheet
{
    public static void Show(Engine.Engine engine, Window over)
    {
        var scene = new MergeScene { Height = 190, Margin = new Thickness(28, 30, 28, 0) };
        var sub = K.Wrapped("Your tasks now sit beside your week, in one tab.", Ty.Body, B.Text2);
        sub.TextAlignment = TextAlignment.Center;
        var head = K.V(6, K.Text("To Do and Calendar are one", Ty.Subtitle).Center(), sub);
        head.Margin = new Thickness(28, 18, 28, 0);
        var words = K.V(2, K.Text("Quick Plan", Ty.BodyStrong),
                        K.Wrapped("Drag a task onto the calendar to set time aside for it. Drop it on a day and Simpl finds a free spot.", Ty.Body, B.Text2));
        var tile = K.IconTile(SymbolRegular.HandDraw24, MergeScene.AccentIcon, 32).Align(HorizontalAlignment.Left, VerticalAlignment.Top);
        var card = K.Card(K.Columns("auto, *", tile.Col(0), words.Margin(12, 0, 0, 0).Col(1)), new Thickness(14));
        card.Margin = new Thickness(28, 18, 28, 4);
        var s = new Sheet { Body = K.V(0, scene, head, card), Bare = true, Width = 460, HoldsUpdates = false, Floats = true, Light = true, Name = "To Do & Calendar" };
        s.Add("Got it", SheetButtonKind.Primary);
        s.Add("Open To Do & Calendar", SheetButtonKind.Secondary, () => engine.Go(Place.Calendar));
        Dialogs.Show(s, over);
        Log.Write($"intro: To Do & Calendar shown{(Motion.On ? ", its scene playing" : ", its scene still")}");
    }
}

/// <summary>
/// The scene (the Mac's MergeScene, drawn here by hand): To Do and the calendar apart, then together under one name; a
/// row of To Do taken up, carried onto Wednesday's morning and set down there as a plan (its dashed edge and clock); a
/// while to see it, then the whole fading for the next time round. With motion off (and in a picture), the scene as it
/// ends, still.
/// </summary>
internal sealed class MergeScene : FrameworkElement
{
    /// <summary>One time round, in seconds.</summary>
    private const double Cycle = 6.4;
    /// <summary>The moment shown still: all of it done, nothing fading.</summary>
    private const double Still = 4.6;

    private static readonly Color[] RowTones = { Color.FromRgb(0x30, 0xd1, 0x58), Color.FromRgb(0xff, 0x9f, 0x0a), Color.FromRgb(0x0a, 0x84, 0xff), Color.FromRgb(0xff, 0x37, 0x5f) };
    private static readonly double[] RowLengths = { 0.78, 0.62, 0.7, 0.5 };
    /// <summary>The week's own events: day, hour row, colour.</summary>
    private static readonly (int Day, int Row, Color Tone)[] Events =
    {
        (0, 2, Color.FromRgb(0x30, 0xd1, 0x58)), (3, 0, Color.FromRgb(0xff, 0x9f, 0x0a)), (4, 3, Color.FromRgb(0x0a, 0x84, 0xff)), (1, 3, Color.FromRgb(0xff, 0x37, 0x5f)),
    };

    // layout, in pixels inside the scene
    private const double Pad = 26, Top = 24, RowHeight = 20, RowStep = 28, RowsTop = 14, HourTop = 30, HourStep = 30;

    private readonly Stopwatch clock = new();
    private bool running;

    private static Color Ground => Appearance.Dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xf3, 0xf3, 0xf3);
    private static Color Ink => Appearance.Dark ? Colors.White : Colors.Black;
    /// <summary>The accent as a symbol is drawn in it, standing 3 to 1 on the ground.</summary>
    public static Color AccentIcon => Appearance.Reach(Appearance.Accent, Ground, 3);

    public MergeScene()
    {
        IsHitTestVisible = false;
        System.Windows.Automation.AutomationProperties.SetName(this, "To Do and the calendar come together as one; a task dragged onto the calendar becomes a plan");
        Loaded += (_, _) =>
        {
            if (!Motion.On || running) return;
            running = true;
            clock.Restart();
            CompositionTarget.Rendering += Tick;
        };
        Unloaded += (_, _) =>
        {
            if (!running) return;
            running = false;
            CompositionTarget.Rendering -= Tick;
        };
    }

    private void Tick(object? sender, EventArgs e) => InvalidateVisual();

    /// <summary>0 to 1 over the seconds from <paramref name="a"/> to <paramref name="b"/>, eased in and out.</summary>
    private static double Step(double t, double a, double b)
    {
        var x = Math.Clamp((t - a) / (b - a), 0, 1);
        return x < 0.5 ? 4 * x * x * x : 1 - Math.Pow(-2 * x + 2, 3) / 2;
    }

    private static SolidColorBrush Brush(Color c, double alpha) => new(Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * c.A), c.R, c.G, c.B));

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var t = running ? clock.Elapsed.TotalSeconds % Cycle : Still;
        var merge = Step(t, 0.4, 1.3);
        var lift = Step(t, 1.8, 2.1);
        var carry = Step(t, 2.1, 3.1);
        var land = Step(t, 3.1, 3.4);
        var shown = Step(t, 0, 0.3) * (1 - Step(t, 5.8, 6.3));
        var inner = w - 2 * Pad;
        var todoWidth = inner * 0.36;
        var calWidth = inner - todoWidth;
        var height = h - Top - 4;
        var apart = 22 * (1 - merge);
        var todoX = Pad - apart;
        var calX = Pad + todoWidth + apart;
        var dayWidth = calWidth / 5;
        var card = Appearance.Dark ? Color.FromRgb(0x2b, 0x2b, 0x2b) : Colors.White;
        var line = Brush(Ink, 0.1);
        var stroke = new Pen(line, 1);

        dc.PushOpacity(shown);
        // one card round both, once they are together
        dc.PushOpacity(merge);
        dc.DrawRoundedRectangle(Brush(card, 1), stroke, new Rect(Pad + 0.5, Top + 0.5, inner - 1, height - 1), 14, 14);
        dc.DrawLine(stroke, new Point(Pad + todoWidth, Top + 8), new Point(Pad + todoWidth, Top + height - 8));
        dc.Pop();
        // each on its own card while apart
        dc.PushOpacity(1 - merge);
        dc.DrawRoundedRectangle(Brush(card, 1), stroke, new Rect(todoX + 0.5, Top + 0.5, todoWidth - 1, height - 1), 14, 14);
        dc.DrawRoundedRectangle(Brush(card, 1), stroke, new Rect(calX + 0.5, Top + 0.5, calWidth - 1, height - 1), 14, 14);
        dc.Pop();

        // To Do: four rows (the second faint once it is taken up)
        for (var i = 0; i < 4; i++)
        {
            dc.PushOpacity(lift > 0 && i == 1 ? 0.25 : 1);
            Row(dc, i, new Rect(todoX + 10, Top + RowsTop + i * RowStep, todoWidth - 20, RowHeight), null);
            dc.Pop();
        }

        // the week: its days' heads, its hours' lines and its own events
        for (var c = 0; c < 5; c++)
        {
            var x = calX + dayWidth * c;
            dc.DrawRoundedRectangle(Brush(Ink, 0.22), null, new Rect(x + dayWidth / 2 - 9, Top + 12, 18, 5), 2.5, 2.5);
            if (c > 0) dc.DrawLine(new Pen(Brush(Ink, 0.08), 1), new Point(x, Top + HourTop - 6), new Point(x, Top + height - 8));
        }
        for (var r = 0; r < 4; r++)
        {
            var y = Top + HourTop + r * HourStep;
            dc.DrawLine(new Pen(Brush(Ink, 0.08), 1), new Point(calX + 6, y), new Point(calX + calWidth - 6, y));
        }
        foreach (var (day, row, tone) in Events)
            dc.DrawRoundedRectangle(Brush(tone, 0.3), null, new Rect(calX + dayWidth * day + 3, Top + HourTop + row * HourStep + 3, dayWidth - 6, 22), 5, 5);

        // the names: each over its own, then one over both
        Label(dc, "To Do", todoX + todoWidth / 2, 1 - merge);
        Label(dc, "Calendar", calX + calWidth / 2, 1 - merge);
        Label(dc, "To Do & Calendar", w / 2, merge);

        // the row carried onto the week, a plan once it is set down
        if (lift > 0)
        {
            var from = new Point(todoX + todoWidth / 2, Top + RowsTop + RowStep + RowHeight / 2);
            var to = new Point(calX + dayWidth * 2.5, Top + HourTop + HourStep + 14);
            var arc = Math.Sin(carry * Math.PI) * 24;
            var at = new Point(from.X + (to.X - from.X) * carry, from.Y + (to.Y - from.Y) * carry - arc);
            var pw = (todoWidth - 20) + ((dayWidth - 6) - (todoWidth - 20)) * land;
            var ph = RowHeight + 2 * land;
            var box = new Rect(at.X - pw / 2, at.Y - ph / 2, pw, ph);
            var grow = 1 + 0.06 * lift * (1 - land);
            dc.PushTransform(new ScaleTransform(grow, grow, at.X, at.Y));
            // (its shadow while it is held up)
            dc.DrawRoundedRectangle(Brush(Colors.Black, 0.16 * lift * (1 - land)), null, new Rect(box.X + 1, box.Y + 4, box.Width, box.Height), 7, 7);
            dc.PushOpacity(1 - land);
            Row(dc, 1, box, card);
            dc.Pop();
            dc.PushOpacity(land);
            var accent = Appearance.Accent;
            var icon = AccentIcon;
            var dash = new Pen(new SolidColorBrush(icon), 1.2) { DashStyle = new DashStyle(new double[] { 2.5, 1.7 }, 0) };
            dc.DrawRoundedRectangle(Brush(accent, 0.28), dash, new Rect(box.X + 0.6, box.Y + 0.6, box.Width - 1.2, box.Height - 1.2), 5, 5);
            Clock(dc, new Point(box.X + 9, box.Y + box.Height / 2), Appearance.Reach(accent, Ground, 4.5));
            dc.Pop();
            dc.Pop();
            // the pointer that carries it
            dc.PushOpacity(lift * (1 - land));
            Pointer(dc, new Point(at.X + 8, at.Y + 2));
            dc.Pop();
        }
        dc.Pop();
    }

    /// <summary>A row of To Do: a tick, a line of words and its course's dot, on its own faint ground (or a card's).</summary>
    private static void Row(DrawingContext dc, int i, Rect r, Color? ground)
    {
        if (ground is { } g) dc.DrawRoundedRectangle(Brush(g, 1), null, r, 6, 6);
        dc.DrawRoundedRectangle(Brush(Ink, 0.05), null, r, 6, 6);
        var mid = r.Y + r.Height / 2;
        dc.DrawEllipse(null, new Pen(Brush(Ink, 0.35), 1.5), new Point(r.X + 11, mid), 4.25, 4.25);
        dc.DrawRoundedRectangle(Brush(Ink, 0.2), null, new Rect(r.X + 22, mid - 3, Math.Max(8, (r.Width - 40) * RowLengths[i]), 6), 3, 3);
        dc.DrawEllipse(new SolidColorBrush(RowTones[i]), null, new Point(r.Right - 9, mid), 3, 3);
    }

    private void Label(DrawingContext dc, string s, double x, double alpha)
    {
        if (alpha <= 0.01) return;
        var text = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                                     new Typeface(K.TextFace, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 11,
                                     Brush(Ink, 0.6 * alpha), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(x - text.Width / 2, Top - 12 - text.Height / 2));
    }

    /// <summary>A clock face: a ring and its two hands.</summary>
    private static void Clock(DrawingContext dc, Point c, Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 1.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawEllipse(null, pen, c, 4, 4);
        dc.DrawLine(pen, c, new Point(c.X, c.Y - 2.4));
        dc.DrawLine(pen, c, new Point(c.X + 1.8, c.Y));
    }

    /// <summary>An arrow pointer, its tip at <paramref name="tip"/>.</summary>
    private static void Pointer(DrawingContext dc, Point tip)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(tip, true, true);
            ctx.LineTo(new Point(tip.X, tip.Y + 14), true, false);
            ctx.LineTo(new Point(tip.X + 3.6, tip.Y + 10.6), true, false);
            ctx.LineTo(new Point(tip.X + 6.2, tip.Y + 16), true, false);
            ctx.LineTo(new Point(tip.X + 8.4, tip.Y + 15), true, false);
            ctx.LineTo(new Point(tip.X + 5.9, tip.Y + 9.8), true, false);
            ctx.LineTo(new Point(tip.X + 10.4, tip.Y + 9.8), true, false);
        }
        g.Freeze();
        dc.DrawGeometry(Brushes.Black, new Pen(Brushes.White, 1.2) { LineJoin = PenLineJoin.Round }, g);
    }
}
