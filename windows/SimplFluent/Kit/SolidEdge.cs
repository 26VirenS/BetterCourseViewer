using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Simpl.Support;

namespace Simpl.Kit;

/// <summary>
/// (0.3.61) A solid white edge round everything an element draws — its words and its glyphs — as the web's ring of hard
/// white shadows (2.99.108) and the Mac's (1.3.166): each pixel takes white where anything of the element lies within a
/// pixel and a quarter of it, so the edge is crisp and even, with no glow fading out of it. WPF has no such effect of its
/// own: this is a small pixel shader (ps_2_0, which WPF also runs without a graphics card), compiled once by Windows' own
/// shader compiler (d3dcompiler_47, part of Windows since 8.1). Should that ever fail, <see cref="Available"/> is false
/// and the words keep the softer border they had (Scenes.PlainWords).
/// </summary>
public sealed class SolidEdge : ShaderEffect
{
    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(SolidEdge), 0);

    private static readonly Lazy<PixelShader?> Shader = new(Compile);

    /// <summary>Whether the edge can be drawn here (its shader compiled).</summary>
    public static bool Available => Shader.Value != null;

    public SolidEdge()
    {
        PixelShader = Shader.Value;
        // (c0: how far one pixel is across the element's own texture, each way — the taps are a pixel and a quarter out)
        DdxUvDdyUvRegisterIndex = 0;
        PaddingLeft = PaddingRight = PaddingTop = PaddingBottom = 2;
        UpdateShaderValue(InputProperty);
    }

    public Brush Input
    {
        get => (Brush)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    // twelve taps round the pixel, a pixel and a quarter out; the most of the element under any of them is the white's
    private const string Source = @"
sampler2D input : register(s0);
float4 dd : register(c0);
float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 c = tex2D(input, uv);
    float2 s = float2(dd.x, dd.w) * 1.25;
    float a = tex2D(input, uv + float2(1, 0) * s).a;
    a = max(a, tex2D(input, uv + float2(0.866, 0.5) * s).a);
    a = max(a, tex2D(input, uv + float2(0.5, 0.866) * s).a);
    a = max(a, tex2D(input, uv + float2(0, 1) * s).a);
    a = max(a, tex2D(input, uv + float2(-0.5, 0.866) * s).a);
    a = max(a, tex2D(input, uv + float2(-0.866, 0.5) * s).a);
    a = max(a, tex2D(input, uv + float2(-1, 0) * s).a);
    a = max(a, tex2D(input, uv + float2(-0.866, -0.5) * s).a);
    a = max(a, tex2D(input, uv + float2(-0.5, -0.866) * s).a);
    a = max(a, tex2D(input, uv + float2(0, -1) * s).a);
    a = max(a, tex2D(input, uv + float2(0.5, -0.866) * s).a);
    a = max(a, tex2D(input, uv + float2(0.866, -0.5) * s).a);
    return c + float4(a, a, a, a) * (1 - c.a);
}";

    private static PixelShader? Compile()
    {
        try
        {
            var src = System.Text.Encoding.ASCII.GetBytes(Source);
            var hr = D3DCompile(src, (IntPtr)src.Length, "SolidEdge", IntPtr.Zero, IntPtr.Zero, "main", "ps_2_0", 0, 0, out var code, out var errors);
            if (hr < 0 || code == null)
            {
                var why = errors == null ? $"0x{hr:x8}" : Marshal.PtrToStringAnsi(errors.GetBufferPointer(), (int)errors.GetBufferSize());
                Log.Write("solid edge: the shader did not compile: " + why);
                return null;
            }
            var bytes = new byte[(int)code.GetBufferSize()];
            Marshal.Copy(code.GetBufferPointer(), bytes, 0, bytes.Length);
            var shader = new PixelShader();
            shader.SetStreamSource(new MemoryStream(bytes));
            shader.Freeze();
            return shader;
        }
        catch (Exception e)
        {
            Log.Write("solid edge: " + e.Message);
            return null;
        }
    }

    [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ID3DBlob
    {
        [PreserveSig] IntPtr GetBufferPointer();
        [PreserveSig] IntPtr GetBufferSize();
    }

    [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern int D3DCompile(byte[] srcData, IntPtr srcDataSize, string sourceName, IntPtr defines, IntPtr include,
        string entryPoint, string target, uint flags1, uint flags2, out ID3DBlob? code, out ID3DBlob? errorMsgs);
}
