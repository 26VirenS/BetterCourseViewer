using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Simpl.Components.Glass;

namespace GlassShowcase;

/// <summary>
/// A switch as iOS 26 and macOS 26 draw it: a capsule track, and a knob that is solid white at rest but turns into a
/// lens of glass while it is held — swelling, showing the track through itself — then slides across on a spring.
/// </summary>
public sealed class GlassToggle : Grid
{
    private readonly Border track = new() { CornerRadius = new CornerRadius(15) };
    private readonly Grid trackLayer = new();
    private readonly Grid knob = new() { Width = 26, Height = 26, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    private readonly LiquidGlass lens;
    private readonly Border solid = new() { CornerRadius = new CornerRadius(13), Background = Brushes.White };
    private readonly TranslateTransform move = new();
    private readonly Spring x = new(0, 0.32, 0.72), hold = new(0, 0.28, 0.8);
    private bool on;
    private static readonly Color OnColor = Color.FromRgb(52, 199, 89);

    public event Action<bool>? Toggled;

    public GlassToggle(bool isOn = false)
    {
        Width = 54;
        Height = 32;
        Cursor = Cursors.Hand;
        trackLayer.Children.Add(track);
        Children.Add(trackLayer);
        lens = new LiquidGlass { Backdrop = trackLayer, Capsule = true, BlurRadius = 2, Refraction = 7, Bevel = 9, TintAmount = 0.06, Saturation = 1.3, Rim = 0.7, Glow = 0.1, ShadowOpacity = 0.25, PressGrowth = 0 };
        knob.Children.Add(lens);
        knob.Children.Add(solid);
        knob.RenderTransform = move;
        knob.Margin = new Thickness(3, 0, 0, 0);
        solid.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Direction = 270, Opacity = 0.25 };
        Children.Add(knob);
        on = isOn;
        x.Snap(on ? 1 : 0);
        Paint();
        x.Changed += _ => Paint();
        hold.Changed += _ => Paint();
        MouseLeftButtonDown += (_, e) => { CaptureMouse(); hold.To(1); e.Handled = true; };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!IsMouseCaptured) return;
            ReleaseMouseCapture();
            hold.To(0);
            on = !on;
            x.To(on ? 1 : 0);
            Toggled?.Invoke(on);
            e.Handled = true;
        };
    }

    public bool IsOn => on;

    private void Paint()
    {
        var h = hold.Value;
        // the knob swells and stretches while held (a lens over the track), solid again at rest
        knob.Width = 26 + 14 * h;
        knob.Height = 26 + 4 * h;
        move.X = x.Value * (Width - 6 - 26) - 7 * h * x.Value;
        move.Y = 0;
        solid.Opacity = 1 - 0.92 * h;
        solid.CornerRadius = new CornerRadius(knob.Height / 2);
        var off = Color.FromArgb(70, 120, 120, 128);
        var t = Math.Clamp(x.Value, 0, 1);
        track.Background = new SolidColorBrush(Color.FromArgb(
            (byte)(off.A + (255 - off.A) * t), (byte)(off.R + (OnColor.R - off.R) * t), (byte)(off.G + (OnColor.G - off.G) * t), (byte)(off.B + (OnColor.B - off.B) * t)));
    }
}

/// <summary>
/// A slider: a thin track filled to its value, and a knob that becomes a lens of glass while dragged — following the
/// pointer one to one, settling on a spring when let go.
/// </summary>
public sealed class GlassSlider : Grid
{
    private readonly Grid trackLayer = new();
    private readonly Border track = new() { Height = 6, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.FromArgb(70, 120, 120, 128)) };
    private readonly Border fill = new() { Height = 6, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Color.FromRgb(0, 122, 255)) };
    private readonly Grid knob = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    private readonly LiquidGlass lens;
    private readonly Border solid = new() { Background = Brushes.White };
    private readonly TranslateTransform move = new();
    private readonly Spring hold = new(0, 0.26, 0.8), pos;
    private double value;

    public event Action<double>? ValueChanged;

    public GlassSlider(double initial = 0.5, double width = 220)
    {
        Width = width;
        Height = 34;
        Cursor = Cursors.Hand;
        Background = Brushes.Transparent;
        trackLayer.Children.Add(track);
        trackLayer.Children.Add(fill);
        Children.Add(trackLayer);
        lens = new LiquidGlass { Backdrop = trackLayer, Capsule = true, BlurRadius = 1, Refraction = 6, Bevel = 8, TintAmount = 0.04, Saturation = 1.4, Rim = 0.75, Glow = 0.1, ShadowOpacity = 0.25, PressGrowth = 0 };
        knob.Children.Add(lens);
        knob.Children.Add(solid);
        solid.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Direction = 270, Opacity = 0.25 };
        knob.RenderTransform = move;
        Children.Add(knob);
        value = initial;
        pos = new Spring(initial, 0.3, 0.82);
        pos.Changed += _ => Paint();
        hold.Changed += _ => Paint();
        Loaded += (_, _) => Paint();
        MouseLeftButtonDown += (_, e) => { CaptureMouse(); hold.To(1); Drag(e.GetPosition(this).X, true); e.Handled = true; };
        MouseMove += (_, e) => { if (IsMouseCaptured) Drag(e.GetPosition(this).X, false); };
        MouseLeftButtonUp += (_, e) => { if (!IsMouseCaptured) return; ReleaseMouseCapture(); hold.To(0); e.Handled = true; };
    }

    public double Value => value;

    private void Drag(double px, bool jump)
    {
        value = Math.Clamp((px - 14) / Math.Max(Width - 28, 1), 0, 1);
        if (jump) pos.To(value); else pos.Snap(value);
        ValueChanged?.Invoke(value);
    }

    private void Paint()
    {
        var h = hold.Value;
        var kw = 28 + 16 * h;
        var kh = 20 + 8 * h;
        knob.Width = kw;
        knob.Height = kh;
        solid.CornerRadius = new CornerRadius(kh / 2);
        solid.Opacity = 1 - 0.9 * h;
        var x = 14 + pos.Value * (Width - 28);
        move.X = x - kw / 2;
        fill.Width = Math.Max(6, x);
    }
}

/// <summary>
/// A segmented control (the Mac Dashboard's Cards · List · Activity): the choices in a capsule of glass, and a thumb — a
/// lens of glass over the words — that slides to the one chosen on a spring, swelling as it goes.
/// </summary>
public sealed class GlassSegmented : Grid
{
    private readonly Grid labels = new();
    private readonly LiquidGlass thumb;
    private readonly TranslateTransform move = new();
    private readonly Spring x, stretch = new(0, 0.3, 0.7);
    private readonly string[] items;
    private readonly double segment;
    private int selected;

    public event Action<int>? Changed;

    public GlassSegmented(string[] items, double segmentWidth = 84, int selected = 0, bool darkText = false)
    {
        this.items = items;
        segment = segmentWidth;
        this.selected = selected;
        Height = 34;
        Width = segment * items.Length;
        Cursor = Cursors.Hand;
        Background = Brushes.Transparent;
        for (var i = 0; i < items.Length; i++)
        {
            labels.ColumnDefinitions.Add(new ColumnDefinition());
            var t = new TextBlock
            {
                Text = items[i], FontSize = 14, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(darkText ? Color.FromArgb(220, 0, 0, 0) : Color.FromArgb(235, 255, 255, 255)),
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            };
            SetColumn(t, i);
            labels.Children.Add(t);
        }
        Children.Add(labels);
        thumb = new LiquidGlass
        {
            Backdrop = labels, Capsule = true, BlurRadius = 0.5, Refraction = 9, Bevel = 12, Dispersion = 0.35, TintAmount = darkText ? 0.78 : 0.2,
            Tint = Colors.White, Saturation = 1.3, Rim = 0.6, Glow = 0.12, ShadowOpacity = 0.15, PressGrowth = 0,
            Width = segment - 6, Height = 28, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0, 0, 0), RenderTransform = move,
        };
        Children.Add(thumb);
        x = new Spring(selected, 0.34, 0.74);
        x.Changed += _ => Paint();
        stretch.Changed += _ => Paint();
        Paint();
        MouseLeftButtonUp += (_, e) =>
        {
            var i = Math.Clamp((int)(e.GetPosition(this).X / segment), 0, items.Length - 1);
            Select(i);
        };
    }

    public void Select(int i)
    {
        if (i == selected) return;
        selected = i;
        stretch.To(1);
        x.To(i);
        _ = Task.Delay(140).ContinueWith(_ => Dispatcher.Invoke(() => stretch.To(0)));
        Changed?.Invoke(i);
    }

    private void Paint()
    {
        var s = stretch.Value;
        thumb.Width = segment - 6 + 22 * s;
        thumb.Height = 28 + 6 * s;
        move.X = x.Value * segment - 11 * s;
    }
}
