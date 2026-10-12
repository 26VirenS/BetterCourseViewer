using System.Runtime.InteropServices;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using D2DAlphaMode = Vortice.DCommon.AlphaMode;

namespace Simpl.Graphics;

/// <summary>
/// The window's graphics (the showcase's Gfx, windows/GlassNative): one Direct3D 11 device for everything — the glass
/// is drawn with it (<see cref="GlassRenderer"/>) — Direct2D and DirectWrite on the same device for everything else, a
/// swap chain on the window, and the frame so far (<see cref="Composite"/>) for each layer of glass to read what is
/// under it.
/// </summary>
public sealed class Gpu : IDisposable
{
    public ID3D11Device Device { get; }
    public ID3D11DeviceContext Ctx { get; }
    public ID2D1Factory1 D2dFactory { get; }
    public ID2D1DeviceContext D2d { get; }
    public IDWriteFactory DWrite { get; }
    private readonly ID2D1Device d2dDevice;
    private readonly IDXGISwapChain1 swap;

    public ID3D11RenderTargetView? BackRtv { get; private set; }
    public ID2D1Bitmap1? BackBitmap { get; private set; }
    private ID3D11Texture2D? back;
    /// <summary>The frame so far: what the next layer of glass reads.</summary>
    public ID3D11Texture2D? Composite { get; private set; }
    public ID3D11ShaderResourceView? CompositeSrv { get; private set; }

    public int Width { get; private set; }
    public int Height { get; private set; }
    public float Dpi { get; private set; } = 96;
    /// <summary>Pixels per DIP.</summary>
    public float Scale => Dpi / 96f;
    public string Adapter { get; } = "";

    public Gpu(IntPtr hwnd, int width, int height, float dpi)
    {
        var levels = new[] { Vortice.Direct3D.FeatureLevel.Level_11_1, Vortice.Direct3D.FeatureLevel.Level_11_0, Vortice.Direct3D.FeatureLevel.Level_10_1 };
        var flags = DeviceCreationFlags.BgraSupport;
        ID3D11Device? dev;
        ID3D11DeviceContext? ctx;
        if (D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Hardware, flags, levels, out dev, out ctx).Failure)
            D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Warp, flags, levels, out dev, out ctx).CheckError();
        Device = dev!;
        Ctx = ctx!;

        using var dxgiDevice = Device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();
        Adapter = adapter.Description.Description;
        using var factory = adapter.GetParent<IDXGIFactory2>();
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        var sd = new SwapChainDescription1
        {
            Width = (uint)Width,
            Height = (uint)Height,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = Vortice.DXGI.AlphaMode.Ignore,
            Scaling = Scaling.Stretch,
        };
        swap = factory.CreateSwapChainForHwnd(Device, hwnd, sd);
        factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter);

        D2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(Vortice.Direct2D1.FactoryType.SingleThreaded);
        d2dDevice = D2dFactory.CreateDevice(dxgiDevice);
        D2d = d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
        D2d.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale;
        DWrite = Vortice.DirectWrite.DWrite.DWriteCreateFactory<IDWriteFactory>(Vortice.DirectWrite.FactoryType.Shared);
        SetDpi(dpi);
        CreateSized();
    }

    /// <summary>The display's scale, for Direct2D too: its context draws at 96 DPI unless told.</summary>
    private void SetDpi(float dpi)
    {
        Dpi = dpi;
        D2d.SetDpi(dpi, dpi);
    }

    /// <summary>A new size or a new display scale: every target made again.</summary>
    public void Resize(int width, int height, float dpi)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height && Math.Abs(dpi - Dpi) < 0.01f) return;
        ReleaseSized();
        Ctx.ClearState();
        Ctx.Flush();
        Width = width;
        Height = height;
        SetDpi(dpi);
        swap.ResizeBuffers(0, (uint)Width, (uint)Height, Format.Unknown, SwapChainFlags.None).CheckError();
        CreateSized();
    }

    private void CreateSized()
    {
        back = swap.GetBuffer<ID3D11Texture2D>(0);
        BackRtv = Device.CreateRenderTargetView(back);
        using var surface = back.QueryInterface<IDXGISurface>();
        BackBitmap = D2d.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(
            new PixelFormat(Format.B8G8R8A8_UNorm, D2DAlphaMode.Ignore), Dpi, Dpi, BitmapOptions.Target | BitmapOptions.CannotDraw));
        var cd = new Texture2DDescription
        {
            Width = (uint)Width,
            Height = (uint)Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            BindFlags = BindFlags.ShaderResource,
        };
        Composite = Device.CreateTexture2D(cd);
        CompositeSrv = Device.CreateShaderResourceView(Composite);
    }

    private void ReleaseSized()
    {
        D2d.Target = null;
        BackBitmap?.Dispose(); BackBitmap = null;
        BackRtv?.Dispose(); BackRtv = null;
        back?.Dispose(); back = null;
        CompositeSrv?.Dispose(); CompositeSrv = null;
        Composite?.Dispose(); Composite = null;
    }

    /// <summary>The frame so far, for the glass of the next layer.</summary>
    public void Snapshot() => Ctx.CopyResource(Composite!, back!);

    public void Present(bool vsync) => swap.Present(vsync ? 1u : 0u, PresentFlags.None);

    /// <summary>The frame as it is: BGRA rows, opaque (the screenshot suite's picture).</summary>
    public (byte[] Pixels, int Width, int Height) ReadBack()
    {
        var d = back!.Description;
        d.Usage = ResourceUsage.Staging;
        d.BindFlags = BindFlags.None;
        d.CPUAccessFlags = CpuAccessFlags.Read;
        d.MiscFlags = ResourceOptionFlags.None;
        using var staging = Device.CreateTexture2D(d);
        Ctx.CopyResource(staging, back);
        var m = Ctx.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        var rows = new byte[Width * 4 * Height];
        for (var y = 0; y < Height; y++)
            Marshal.Copy(m.DataPointer + (nint)(y * m.RowPitch), rows, y * Width * 4, Width * 4);
        Ctx.Unmap(staging, 0);
        for (var i = 3; i < rows.Length; i += 4) rows[i] = 255;
        return (rows, Width, Height);
    }

    public void Dispose()
    {
        ReleaseSized();
        D2d.Dispose();
        d2dDevice.Dispose();
        D2dFactory.Dispose();
        DWrite.Dispose();
        swap.Dispose();
        Ctx.Dispose();
        Device.Dispose();
    }
}
