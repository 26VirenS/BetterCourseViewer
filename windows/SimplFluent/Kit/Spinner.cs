using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Simpl.Kit;

/// <summary>
/// (0.3.75) Windows 11's busy ring, drawn by the app: an arc of the accent on a faint track, turning — and turning only
/// while it is on the screen. WPF-UI's ProgressRing turned for as long as it was in the window, hidden or folded away
/// with its page too (To Do &amp; Calendar's, kept hidden between loads; a quiz's; a page kept out of view), and every
/// ring turning kept WPF drawing the whole window sixty times a second, whatever else it did.
/// </summary>
public sealed class Spinner : FrameworkElement
{
    public static readonly DependencyProperty BrushProperty = DependencyProperty.Register(nameof(Brush), typeof(Brush), typeof(Spinner),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The arc's colour (the accent's).</summary>
    public Brush? Brush
    {
        get => (Brush?)GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    private readonly RotateTransform turn = new();
    private bool turning;

    public Spinner(double size = 24)
    {
        Width = size;
        Height = size;
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Center;
        RenderTransform = turn;
        RenderTransformOrigin = new Point(0.5, 0.5);
        SetResourceReference(BrushProperty, B.Accent);
        IsHitTestVisible = false;
        IsVisibleChanged += (_, _) => Turn(IsVisible);
        Unloaded += (_, _) => Turn(false);
        System.Windows.Automation.AutomationProperties.SetName(this, "Loading");
    }

    /// <summary>Turning (a turn a second, constant, so linear) while on the screen; still, and costing nothing, while not.</summary>
    private void Turn(bool on)
    {
        if (on == turning) return;
        turning = on;
        if (on) turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever });
        else turn.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        // (WinUI's ring: its stroke an eighth of its size, at least 1.5 px; round ends)
        var stroke = Math.Clamp(size / 8, 1.5, 4);
        var r = (size - stroke) / 2;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        var track = new Pen(new SolidColorBrush(Color.FromArgb(0x24, 0x80, 0x80, 0x80)), stroke);
        dc.DrawEllipse(null, track, c, r, r);
        if (Brush is not { } brush) return;
        var pen = new Pen(brush, stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        // (a quarter turn and a little more of the ring, from the top)
        const double sweep = 110;
        static Point On(Point c, double r, double deg) => new(c.X + r * Math.Sin(deg * Math.PI / 180), c.Y - r * Math.Cos(deg * Math.PI / 180));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(On(c, r, 0), false, false);
            ctx.ArcTo(On(c, r, sweep), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }
}
