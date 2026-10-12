using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;

namespace Simpl.Components.Glass;

/// <summary>
/// The glass pass (Glass.fx) as a WPF pixel shader: over the frosted backdrop, the shape, its bevel's refraction and
/// dispersion, the colour, the rim's light. The compiled shader (Glass.ps) is built by fxc in CI and carried in the
/// assembly; where it is not (a build without fxc), <see cref="Available"/> is false and the glass draws without it.
/// </summary>
public sealed class GlassEffect : ShaderEffect
{
    private static readonly PixelShader? Shader = Load();

    private static PixelShader? Load()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("Glass.ps", StringComparison.OrdinalIgnoreCase));
            if (name == null) return null;
            using var s = asm.GetManifestResourceStream(name)!;
            var ms = new MemoryStream();
            s.CopyTo(ms);
            ms.Position = 0;
            var ps = new PixelShader();
            ps.SetStreamSource(ms);
            ps.Freeze();
            return ps;
        }
        catch { return null; }
    }

    /// <summary>The compiled shader is carried, and the graphics card can run it (WPF draws ps_3_0 in hardware only).</summary>
    public static bool Available => Shader != null && RenderCapability.IsPixelShaderVersionSupported(3, 0);

    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(GlassEffect), 0);
    public static readonly DependencyProperty ShapeProperty = DependencyProperty.Register(nameof(Shape), typeof(Point4D), typeof(GlassEffect), new UIPropertyMetadata(new Point4D(100, 100, 0, 20), PixelShaderConstantCallback(0)));
    public static readonly DependencyProperty OpticsProperty = DependencyProperty.Register(nameof(Optics), typeof(Point4D), typeof(GlassEffect), new UIPropertyMetadata(new Point4D(14, 0.3, 18, 1.3), PixelShaderConstantCallback(1)));
    public static readonly DependencyProperty LightProperty = DependencyProperty.Register(nameof(Light), typeof(Point4D), typeof(GlassEffect), new UIPropertyMetadata(new Point4D(-0.6, -0.8, 0.45, 0.06), PixelShaderConstantCallback(2)));
    public static readonly DependencyProperty TintProperty = DependencyProperty.Register(nameof(Tint), typeof(Point4D), typeof(GlassEffect), new UIPropertyMetadata(new Point4D(1, 1, 1, 0.2), PixelShaderConstantCallback(3)));
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(nameof(Tone), typeof(Point4D), typeof(GlassEffect), new UIPropertyMetadata(new Point4D(1, 1, 0, 0), PixelShaderConstantCallback(4)));

    public GlassEffect()
    {
        if (Shader != null) PixelShader = Shader;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(ShapeProperty);
        UpdateShaderValue(OpticsProperty);
        UpdateShaderValue(LightProperty);
        UpdateShaderValue(TintProperty);
        UpdateShaderValue(ToneProperty);
        // (the backdrop is drawn past the shape by the margin: nothing outside the element is sampled)
        PaddingLeft = PaddingTop = PaddingRight = PaddingBottom = 0;
    }

    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    public Point4D Shape { get => (Point4D)GetValue(ShapeProperty); set => SetValue(ShapeProperty, value); }
    public Point4D Optics { get => (Point4D)GetValue(OpticsProperty); set => SetValue(OpticsProperty, value); }
    public Point4D Light { get => (Point4D)GetValue(LightProperty); set => SetValue(LightProperty, value); }
    public Point4D Tint { get => (Point4D)GetValue(TintProperty); set => SetValue(TintProperty, value); }
    public Point4D Tone { get => (Point4D)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
}
