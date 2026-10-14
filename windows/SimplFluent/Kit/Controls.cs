using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Kit;

/// <summary>
/// Windows 11's SelectorBar (the Photos and Settings apps' view picker): its choices as words, the chosen one in the
/// primary text colour with the accent's short bar under it — the bar sliding to the one chosen.
/// </summary>
public sealed class SelectorBar : Grid
{
    private readonly Stack row = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly Canvas overlay = new() { IsHitTestVisible = false };
    private readonly Border bar = new() { Height = 3, Width = 16, CornerRadius = new CornerRadius(1.5) };
    private readonly List<(Button B, TextBlock T, string Name)> items = new();
    private int selected;
    private double? barAt;

    public event Action<int>? Changed;

    public SelectorBar(IReadOnlyList<string> choices, int selected)
    {
        this.selected = Math.Clamp(selected, 0, Math.Max(0, choices.Count - 1));
        bar.SetResourceReference(Border.BackgroundProperty, B.Accent);
        overlay.Children.Add(bar);
        for (var i = 0; i < choices.Count; i++)
        {
            var n = i;
            var t = K.Text(choices[i], Ty.Body);
            var b = K.Subtle(t, () => Select(n, true), new Thickness(12, 6, 12, 9));
            b.MinWidth = 44;
            items.Add((b, t, choices[i]));
            row.Children.Add(b);
        }
        Children.Add(row);
        Children.Add(overlay);
        Paint();
        LayoutUpdated += (_, _) => Place(false);
        // (the last layout can come before the choices are loaded — a bar opened with the rest of a sheet: placed once
        //  they are, not left at the corner)
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => Place(false), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public int Selected => selected;

    public void Select(int i, bool raise)
    {
        if (i == selected) return;
        selected = i;
        Paint();
        Place(true);
        if (raise) Changed?.Invoke(i);
    }

    private void Paint()
    {
        for (var i = 0; i < items.Count; i++)
        {
            items[i].T.Fg(i == selected ? B.Text : B.Text2);
            items[i].T.FontWeight = i == selected ? FontWeights.SemiBold : FontWeights.Normal;
            // (Narrator says which is chosen, as the bar under it shows)
            System.Windows.Automation.AutomationProperties.SetName(items[i].B, i == selected ? $"{items[i].Name}, selected" : items[i].Name);
        }
    }

    private void Place(bool animate)
    {
        if (selected >= items.Count) return;
        var b = items[selected].B;
        if (!b.IsLoaded || b.ActualWidth <= 0) return;
        Point p;
        try { p = b.TranslatePoint(new Point(0, 0), overlay); } catch { return; }
        var x = p.X + (b.ActualWidth - 16) / 2;
        Canvas.SetTop(bar, p.Y + b.ActualHeight - 3);
        if (barAt is { } from && Math.Abs(from - x) < 0.5) return;
        if (animate && barAt is { } start) Motion.FromTo(bar, Canvas.LeftProperty, start, x, Motion.Slow, Motion.Standard);
        else { bar.BeginAnimation(Canvas.LeftProperty, null); Canvas.SetLeft(bar, x); }
        barAt = x;
    }
}

/// <summary>
/// A round check (Microsoft To Do's, the Mac's): an empty ring that fills under the pointer's hint, green with a tick
/// once done — the tick drawn in as it is pressed.
/// </summary>
public sealed class CheckCircle : Button
{
    private readonly Ellipse ring = new() { StrokeThickness = 1.5 };
    private readonly Ellipse fill = new();
    private readonly SymbolIcon tick = K.Icon(SymbolRegular.Checkmark24, 12, Colors.White);
    private readonly SymbolIcon hint;
    private bool done;
    public event Action<bool>? Toggled;

    public CheckCircle(bool done, string label, double size = 20)
    {
        this.done = done;
        Width = size;
        Height = size;
        hint = K.Icon(SymbolRegular.Checkmark24, 11, B.Text2);
        hint.Opacity = 0;
        ring.SetResourceReference(Shape.StrokeProperty, B.StrongStroke);
        fill.Fill = new SolidColorBrush(Color.FromRgb(0x2f, 0xa8, 0x4f));
        // (the ring in a box of its own size: the button's style centres its content, which would otherwise be only as
        //  tall as the tick's symbol and squash the circle into a flat oval)
        var g = new Grid { Width = size, Height = size, Children = { fill, ring, hint, tick } };
        Content = g;
        SetResourceReference(StyleProperty, "SubtleButton");
        K.SetCorner(this, new CornerRadius(size / 2));
        Padding = new Thickness(0);
        MinWidth = 0;
        MinHeight = 0;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        Cursor = Cursors.Hand;
        ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(this, label);
        MouseEnter += (_, _) => { if (!this.done) hint.Opacity = 1; };
        MouseLeave += (_, _) => hint.Opacity = 0;
        Click += (_, e) =>
        {
            e.Handled = true; // (the row it sits in is not opened by its tick)
            this.done = !this.done;
            Paint(true);
            Toggled?.Invoke(this.done);
        };
        Paint(false);
    }

    public bool Done { get => done; set { done = value; Paint(false); } }

    private void Paint(bool animate)
    {
        fill.Visibility = done ? Visibility.Visible : Visibility.Hidden;
        tick.Visibility = done ? Visibility.Visible : Visibility.Hidden;
        ring.Visibility = done ? Visibility.Hidden : Visibility.Visible;
        hint.Opacity = 0;
        if (done && animate)
        {
            var s = new ScaleTransform(0.6, 0.6);
            fill.RenderTransformOrigin = new Point(0.5, 0.5);
            fill.RenderTransform = s;
            Motion.FromTo(s, ScaleTransform.ScaleXProperty, 0.6, 1, Motion.Medium, Motion.Decelerate);
            Motion.FromTo(s, ScaleTransform.ScaleYProperty, 0.6, 1, Motion.Medium, Motion.Decelerate);
            Motion.FadeIn(tick, Motion.Normal, 60);
        }
    }
}

/// <summary>A section's heading with what is at its right (a count, a link, a button) — the Dashboard's and others'.</summary>
public static class Heading
{
    public static FrameworkElement Section(string title, string? trailing = null, UIElement? action = null, Ty ty = Ty.Subtitle)
    {
        var g = K.Columns("auto, *, auto", K.Text(title, ty).Col(0));
        if (trailing != null) g.Children.Add(K.Text(trailing, Ty.Body, B.Text2).Margin(12, 0, 0, ty == Ty.Subtitle ? 2 : 0).Align(HorizontalAlignment.Left, VerticalAlignment.Bottom).Col(1));
        if (action is FrameworkElement a) { a.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(a.Col(2)); }
        g.Margin = new Thickness(0, 0, 0, 10);
        return g;
    }
}
