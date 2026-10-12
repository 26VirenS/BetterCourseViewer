using System.IO;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Simpl.Graphics;

/// <summary>
/// One shape of glass, in pixels, with everything its look is made of (the showcase's GlassLook, windows/GlassNative).
/// </summary>
public struct GlassLook
{
    public float Cx, Cy, Hw, Hh, Radius;
    /// <summary>The corners' superellipse degree (0: round corners).</summary>
    public float Squircle;
    public float Frost, Pull, Band, Magnify;
    public float Keep, Saturation, Brightness, Dispersion;
    public float R, G, B;
    public float Rim, RimWidth, Glow, Outline;
    public float Shadow, ShadowBlur, ShadowY;
    public bool Dark;
    /// <summary>Where it may draw (px), its shadow too: a screen's own part of the window.</summary>
    public Vortice.RawRect? Clip;

    public GlassLook()
    {
        Frost = 1; Band = 1; Keep = 1; Saturation = 1; Brightness = 1; R = G = B = 0.5f; RimWidth = 1; ShadowBlur = 1; Dark = true;
    }
}

/// <summary>
/// Simpl's glass (windows/Shared/Glass.hlsl, the showcase's own, modelled on macOS 26's Liquid Glass): the frost — a
/// quarter-size copy of what is behind, blurred by a true Gaussian — and each shape of glass: its edge bending what lies
/// past it, its tone, its rim of light, its dark hairline and its shadow.
/// </summary>
public sealed class GlassRenderer : IDisposable
{
    private readonly ID3D11Device dev;
    private readonly ID3D11VertexShader glassVS, fullVS;
    private readonly ID3D11PixelShader glassPS, downPS, blurPS;
    private readonly ID3D11Buffer shapeCB, passCB;
    private readonly ID3D11SamplerState sampler;
    private readonly ID3D11BlendState blend;
    private readonly ID3D11RasterizerState raster, rasterClip;
    // the frost's textures: half, quarter, and the quarter blurred one way, then the other
    private readonly ID3D11Texture2D?[] tex = new ID3D11Texture2D?[4];
    private readonly ID3D11RenderTargetView?[] rtv = new ID3D11RenderTargetView?[4];
    private readonly ID3D11ShaderResourceView?[] srv = new ID3D11ShaderResourceView?[4];
    private int w, h;

    public static string Source()
    {
        using var s = typeof(GlassRenderer).Assembly.GetManifestResourceStream("Simpl.Glass.hlsl")
            ?? throw new InvalidOperationException("The glass's shader is missing from the app.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public GlassRenderer(ID3D11Device device)
    {
        dev = device;
        var src = Source();
        glassVS = dev.CreateVertexShader(Compile(src, "GlassVS", "vs_5_0"));
        fullVS = dev.CreateVertexShader(Compile(src, "FullVS", "vs_5_0"));
        glassPS = dev.CreatePixelShader(Compile(src, "GlassPS", "ps_5_0"));
        downPS = dev.CreatePixelShader(Compile(src, "DownPS", "ps_5_0"));
        blurPS = dev.CreatePixelShader(Compile(src, "BlurPS", "ps_5_0"));

        shapeCB = dev.CreateBuffer(new BufferDescription(8 * 16, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        passCB = dev.CreateBuffer(new BufferDescription(2 * 16, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        sampler = dev.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp, TextureAddressMode.Clamp, TextureAddressMode.Clamp));

        var bd = new BlendDescription();
        bd.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = Blend.One,
            DestinationBlend = Blend.InverseSourceAlpha,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.One,
            DestinationBlendAlpha = Blend.InverseSourceAlpha,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All,
        };
        blend = dev.CreateBlendState(bd);
        raster = dev.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid) { DepthClipEnable = true });
        rasterClip = dev.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid) { DepthClipEnable = true, ScissorEnable = true });
    }

    private static byte[] Compile(string src, string entry, string profile)
    {
        var hr = Compiler.Compile(src, entry, "Glass.hlsl", profile, out var blob, out var err);
        if (hr.Failure || blob == null)
            throw new InvalidOperationException($"{entry}: " + (err != null ? Marshal.PtrToStringAnsi(err.BufferPointer) : "failed"));
        var bytes = blob.AsBytes();
        blob.Dispose();
        err?.Dispose();
        return bytes;
    }

    public void Resize(int width, int height)
    {
        if (width == w && height == h && tex[0] != null) return;
        w = width;
        h = height;
        for (var i = 0; i < 4; i++)
        {
            srv[i]?.Dispose(); rtv[i]?.Dispose(); tex[i]?.Dispose();
            var div = i == 0 ? 2 : 4;
            var td = new Texture2DDescription
            {
                Width = (uint)Math.Max(1, w / div),
                Height = (uint)Math.Max(1, h / div),
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            };
            tex[i] = dev.CreateTexture2D(td);
            rtv[i] = dev.CreateRenderTargetView(tex[i]!);
            srv[i] = dev.CreateShaderResourceView(tex[i]!);
        }
    }

    private static void Write(ID3D11DeviceContext ctx, ID3D11Buffer cb, float[] data)
    {
        var m = ctx.Map(cb, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        Marshal.Copy(data, 0, m.DataPointer, data.Length);
        ctx.Unmap(cb, 0);
    }

    /// <summary>The frost of what is behind (sigma in full-size pixels): its quarter-size copy blurred.</summary>
    public ID3D11ShaderResourceView Frost(ID3D11DeviceContext ctx, ID3D11ShaderResourceView source, int srcW, int srcH, float sigmaPx)
    {
        void Pass(int target, ID3D11ShaderResourceView src, ID3D11PixelShader ps, float tx, float ty, float dx, float dy, float sigma)
        {
            ctx.PSUnsetShaderResources(0, 2);
            var td = tex[target]!.Description;
            ctx.RSSetViewport(new Viewport(0, 0, td.Width, td.Height));
            ctx.OMSetRenderTargets(rtv[target]!);
            Write(ctx, passCB, new[] { tx, ty, dx, dy, sigma, Math.Min(64f, MathF.Ceiling(sigma * 3)), 0, 0 });
            ctx.PSSetConstantBuffer(1, passCB);
            ctx.PSSetShaderResource(0, src);
            ctx.PSSetShader(ps);
            ctx.Draw(3, 0);
        }
        ctx.IASetInputLayout(null);
        ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        ctx.VSSetShader(fullVS);
        ctx.PSSetSampler(0, sampler);
        ctx.OMSetBlendState(null);
        ctx.RSSetState(raster);
        Pass(0, source, downPS, 1f / srcW, 1f / srcH, 0, 0, 0);
        var half = tex[0]!.Description;
        Pass(1, srv[0]!, downPS, 1f / half.Width, 1f / half.Height, 0, 0, 0);
        var s = Math.Max(sigmaPx / 4f, 0.01f);
        Pass(2, srv[1]!, blurPS, 0, 0, 1, 0, s);
        Pass(3, srv[2]!, blurPS, 0, 0, 0, 1, s);
        ctx.PSUnsetShaderResources(0, 2);
        return srv[3]!;
    }

    /// <summary>A shape of glass onto the target, reading what is behind it from sharp (and its frost).</summary>
    public void Draw(ID3D11DeviceContext ctx, ID3D11RenderTargetView target, int tw, int th, float scale,
        ID3D11ShaderResourceView sharp, ID3D11ShaderResourceView? frost, in GlassLook l)
    {
        var margin = l.ShadowBlur * 1.2f + MathF.Abs(l.ShadowY) + 3 * scale;
        Write(ctx, shapeCB, new[]
        {
            tw, th, 0, 0,
            l.Cx, l.Cy, l.Hw, l.Hh,
            l.Radius, l.Squircle, margin, scale,
            l.Frost, l.Pull, l.Band, l.Magnify,
            l.Keep, l.Saturation, l.Brightness, l.Dispersion,
            l.R, l.G, l.B, 1,
            l.Rim, l.RimWidth, l.Glow, l.Outline,
            l.Shadow, l.ShadowBlur, l.ShadowY, l.Dark ? 1f : 0f,
        });
        ctx.RSSetViewport(new Viewport(0, 0, tw, th));
        if (l.Clip is { } c)
        {
            ctx.RSSetState(rasterClip);
            ctx.RSSetScissorRect(c.Left, c.Top, c.Right - c.Left, c.Bottom - c.Top);
        }
        else ctx.RSSetState(raster);
        ctx.OMSetRenderTargets(target);
        ctx.OMSetBlendState(blend);
        ctx.IASetInputLayout(null);
        ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
        ctx.VSSetShader(glassVS);
        ctx.VSSetConstantBuffer(0, shapeCB);
        ctx.PSSetShader(glassPS);
        ctx.PSSetConstantBuffer(0, shapeCB);
        ctx.PSSetShaderResources(0, new[] { sharp, frost ?? sharp });
        ctx.PSSetSampler(0, sampler);
        ctx.Draw(4, 0);
        ctx.PSUnsetShaderResources(0, 2);
        ctx.OMSetBlendState(null);
        ctx.RSSetState(raster);
    }

    public void Dispose()
    {
        for (var i = 0; i < 4; i++) { srv[i]?.Dispose(); rtv[i]?.Dispose(); tex[i]?.Dispose(); }
        glassVS.Dispose(); fullVS.Dispose(); glassPS.Dispose(); downPS.Dispose(); blurPS.Dispose();
        shapeCB.Dispose(); passCB.Dispose(); sampler.Dispose(); blend.Dispose(); raster.Dispose(); rasterClip.Dispose();
    }
}
