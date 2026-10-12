using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;

namespace Simpl.Components.Glass;

/// <summary>
/// Liquid Glass for Windows: a shape of glass over what is behind it — the Mac's soft frosted kind (a popover, a
/// toolbar, a button). What is behind (<see cref="Backdrop"/>: the layer under the glass, never a layer the glass is
/// in) is drawn live into the shape a little past its edges, frosted by WPF's own blur, then the glass pass
/// (<see cref="GlassEffect"/>) bends it through the rim's bevel, enriches its colour, tints it and lights the rim. A
/// soft shadow lifts it off the page. Interactive glass answers the pointer: the rim's light turns toward it, and a
/// press swells the glass a little on a spring and lets it settle back.
/// </summary>
public class LiquidGlass : Grid
{
    private readonly Border shadow = new();
    private readonly Border glassHost = new();
    private readonly System.Windows.Shapes.Rectangle backdropRect = new();
    private readonly VisualBrush brush = new() { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill, AutoLayoutContent = false };
    private readonly BlurEffect blur = new() { KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
    private readonly GlassEffect effect = new();
    private readonly Border fallbackTint = new();
    private readonly Border fallbackRim = new();
    private readonly Border contentHost = new();
    private readonly ScaleTransform scale = new(1, 1);
    private readonly Spring press = new(0, 0.32, 0.62);
    private readonly Spring lightX = new(-0.6, 0.5, 1), lightY = new(-0.8, 0.5, 1);
    private Rect lastViewbox = Rect.Empty;
    private bool hooked;

    public LiquidGlass()
    {
        ClipToBounds = false;
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = scale;
        shadow.Background = Brushes.Black;
        shadow.Effect = new BlurEffect { Radius = 28, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
        shadow.IsHitTestVisible = false;
        backdropRect.Fill = brush;
        backdropRect.Effect = blur;
        backdropRect.IsHitTestVisible = false;
        glassHost.Child = backdropRect;
        glassHost.IsHitTestVisible = false;
        fallbackTint.IsHitTestVisible = false;
        fallbackRim.IsHitTestVisible = false;
        fallbackRim.BorderThickness = new Thickness(1);
        Children.Add(shadow);
        Children.Add(glassHost);
        Children.Add(fallbackTint);
        Children.Add(fallbackRim);
        Children.Add(contentHost);
        press.Changed += v =>
        {
            var s = 1 + v * PressGrowth;
            scale.ScaleX = scale.ScaleY = s;
            Apply();
        };
        lightX.Changed += _ => Apply();
        lightY.Changed += _ => Apply();
        Loaded += (_, _) => Hook(true);
        Unloaded += (_, _) => Hook(false);
        SizeChanged += (_, _) => Apply();
        MouseMove += (_, e) => { if (Interactive) AimLight(e.GetPosition(this)); };
        MouseLeave += (_, _) => { lightX.To(-0.6); lightY.To(-0.8); if (pressed) Release(); };
        PreviewMouseLeftButtonDown += (_, _) => { if (Interactive) { pressed = true; press.To(1); } };
        PreviewMouseLeftButtonUp += (_, _) => { if (pressed) Release(); };
        Apply();
    }

    private bool pressed;

    private void Release()
    {
        pressed = false;
        press.To(0);
    }

    // MARK: - What the glass is

    /// <summary>What is behind the glass: the layer under it (never one that holds the glass itself).</summary>
    public Visual? Backdrop
    {
        get => brush.Visual;
        set { brush.Visual = value; lastViewbox = Rect.Empty; Apply(); }
    }

    /// <summary>What sits on the glass.</summary>
    public UIElement? Content
    {
        get => contentHost.Child;
        set => contentHost.Child = value;
    }

    public Thickness Padding { get => contentHost.Padding; set => contentHost.Padding = value; }

    private double cornerRadius = 26;
    public double CornerRadius { get => cornerRadius; set { cornerRadius = value; Apply(); } }

    /// <summary>A capsule: the radius is half the height, whatever the size.</summary>
    public bool Capsule { get; set; }

    private double blurRadius = 26;
    /// <summary>How frosted: the blur of what is behind (the Mac's popovers ≈ 26–34).</summary>
    public double BlurRadius { get => blurRadius; set { blurRadius = value; Apply(); } }

    private double refraction = 10;
    /// <summary>How far the rim bends what is behind (pixels at the very edge).</summary>
    public double Refraction { get => refraction; set { refraction = value; Apply(); } }

    private double dispersion = 0.25;
    public double Dispersion { get => dispersion; set { dispersion = value; Apply(); } }

    private double bevel = 18;
    /// <summary>How wide the curved band along the rim is.</summary>
    public double Bevel { get => bevel; set { bevel = value; Apply(); } }

    private double saturation = 1.35;
    public double Saturation { get => saturation; set { saturation = value; Apply(); } }

    private Color tint = Color.FromRgb(255, 255, 255);
    public Color Tint { get => tint; set { tint = value; Apply(); } }

    private double tintAmount = 0.18;
    public double TintAmount { get => tintAmount; set { tintAmount = value; Apply(); } }

    private double brightness = 1.0;
    public double Brightness { get => brightness; set { brightness = value; Apply(); } }

    private double rim = 0.42;
    /// <summary>The rim's hairline of light.</summary>
    public double Rim { get => rim; set { rim = value; Apply(); } }

    private double glow = 0.05;
    /// <summary>The soft lift along the inside of the rim.</summary>
    public double Glow { get => glow; set { glow = value; Apply(); } }

    private double shadowOpacity = 0.22;
    public double ShadowOpacity { get => shadowOpacity; set { shadowOpacity = value; Apply(); } }

    /// <summary>Answers the pointer: its light turns toward it, a press swells it.</summary>
    public bool Interactive { get; set; }

    /// <summary>How much a press swells it (0.04: four per cent).</summary>
    public double PressGrowth { get; set; } = 0.035;

    /// <summary>The Mac's dark popover glass: warm grey-brown, deeply frosted, softly lit at the rim.</summary>
    public LiquidGlass Dark()
    {
        BlurRadius = 44;
        Tint = Color.FromRgb(128, 112, 96);
        TintAmount = 0.46;
        Brightness = 1.0;
        Saturation = 1.6;
        Rim = 0.3;
        Glow = 0.05;
        ShadowOpacity = 0.4;
        return this;
    }

    /// <summary>The Mac's light glass: a bright veil.</summary>
    public LiquidGlass Light()
    {
        BlurRadius = 44;
        Tint = Color.FromRgb(250, 248, 246);
        TintAmount = 0.5;
        Brightness = 1.02;
        Saturation = 1.6;
        Rim = 0.6;
        Glow = 0.08;
        ShadowOpacity = 0.16;
        return this;
    }

    // MARK: - Drawing

    private double Margin0 => Math.Max(blurRadius, refraction) + 6;

    private double Radius => Capsule ? Math.Min(ActualWidth, ActualHeight) / 2 : Math.Min(cornerRadius, Math.Min(ActualWidth, ActualHeight) / 2);

    private void Apply()
    {
        var w = ActualWidth;
        var h = ActualHeight;
        var m = Margin0;
        var r = Radius;
        var shaded = GlassEffect.Available;
        blur.Radius = blurRadius;
        glassHost.Margin = new Thickness(-m);
        glassHost.Effect = shaded ? effect : null;
        glassHost.Clip = shaded || w <= 0 ? null : new RectangleGeometry(new Rect(m, m, w, h), r, r);
        effect.Shape = new Point4D(w + 2 * m, h + 2 * m, m, r);
        effect.Optics = new Point4D(refraction, dispersion, bevel, saturation);
        var lx = lightX.Value;
        var ly = lightY.Value;
        var len = Math.Max(Math.Sqrt(lx * lx + ly * ly), 0.0001);
        effect.Light = new Point4D(lx / len, ly / len, rim, glow);
        effect.Tint = new Point4D(tint.R / 255.0, tint.G / 255.0, tint.B / 255.0, tintAmount);
        effect.Tone = new Point4D(brightness, 1, press.Value, 0);
        // without the shader: the tint and a rim of light drawn as layers over the frosted backdrop
        fallbackTint.Visibility = fallbackRim.Visibility = shaded ? Visibility.Collapsed : Visibility.Visible;
        fallbackTint.CornerRadius = fallbackRim.CornerRadius = new CornerRadius(r);
        fallbackTint.Background = new SolidColorBrush(Color.FromArgb((byte)(tintAmount * 255), tint.R, tint.G, tint.B));
        fallbackRim.BorderBrush = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Color.FromArgb((byte)(rim * 255), 255, 255, 255), 0),
                new(Color.FromArgb((byte)(rim * 60), 255, 255, 255), 0.5),
                new(Color.FromArgb((byte)(rim * 160), 255, 255, 255), 1),
            }, new Point(0, 0), new Point(1, 1));
        shadow.CornerRadius = new CornerRadius(r);
        shadow.Opacity = shadowOpacity;
        shadow.Margin = new Thickness(4, 10, 4, -6);
        contentHost.CornerRadius = new CornerRadius(r);
        Follow();
    }

    /// <summary>The rim's light turned toward the pointer, softly (it lights the edge nearest it).</summary>
    private void AimLight(Point at)
    {
        var dx = at.X - ActualWidth / 2;
        var dy = at.Y - ActualHeight / 2;
        var len = Math.Max(Math.Sqrt(dx * dx + dy * dy), 1);
        lightX.To(-dx / len);
        lightY.To(-dy / len);
    }

    private void Hook(bool on)
    {
        if (on == hooked) return;
        hooked = on;
        if (on) CompositionTarget.Rendering += OnFrame; else CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e) => Follow();

    /// <summary>Where the glass is over its backdrop, each frame: what is behind it is what the glass shows.</summary>
    private void Follow()
    {
        if (brush.Visual is not Visual back || !IsVisible || ActualWidth <= 0) return;
        try
        {
            if (!back.IsAncestorOf(this) && PresentationSource.FromVisual(back) == PresentationSource.FromVisual(this) && PresentationSource.FromVisual(this) != null)
            {
                var m = Margin0;
                var box = TransformToVisual(back).TransformBounds(new Rect(-m, -m, ActualWidth + 2 * m, ActualHeight + 2 * m));
                if (box != lastViewbox)
                {
                    lastViewbox = box;
                    brush.Viewbox = box;
                }
            }
        }
        catch (InvalidOperationException) { /* not in the same tree yet */ }
    }
}
