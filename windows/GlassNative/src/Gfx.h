#pragma once
// The showcase's graphics: one Direct3D 11 device for everything (AuroraGlass draws the glass with it), Direct2D and
// DirectWrite on the same device for the backdrop, the words and the icons, and the textures the glass reads:
//   scene     — the backdrop as drawn (Direct2D's target), copied into
//   sceneMips — the same with its smaller copies (mips): the frost reads a smaller one, so a 24 px blur reads as a
//               deep frost at almost no cost (the Mac's popovers are frosted far past what a full-size blur of 24 gives);
//   composite — the frame so far (frosted panels and what is on them), for the lenses that refract everything under them.
#include <windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <d2d1_1.h>
#include <d2d1_1helper.h>
#include <dwrite.h>
#include <wrl/client.h>
#include <string>
#include <map>

using Microsoft::WRL::ComPtr;

struct Gfx {
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> ctx;
    ComPtr<IDXGISwapChain1> swap;
    ComPtr<ID3D11RenderTargetView> backRTV;
    ComPtr<ID2D1Factory1> d2dFactory;
    ComPtr<ID2D1Device> d2dDevice;
    ComPtr<ID2D1DeviceContext> d2d;
    ComPtr<ID2D1Bitmap1> backBitmap;
    ComPtr<IDWriteFactory> dwrite;

    ComPtr<ID3D11Texture2D> scene;          // Direct2D draws the backdrop here
    ComPtr<ID2D1Bitmap1> sceneBitmap;
    ComPtr<ID3D11Texture2D> sceneMips;      // the backdrop and its smaller copies
    ComPtr<ID3D11ShaderResourceView> sceneSRV[8];  // one view per mip level
    ComPtr<ID3D11ShaderResourceView> sceneAllSRV;  // every level: what GenerateMips fills
    UINT sceneMipCount = 1;
    ComPtr<ID3D11Texture2D> composite;      // the frame so far, for the lenses
    ComPtr<ID3D11ShaderResourceView> compositeSRV;

    UINT width = 0, height = 0;   // pixels
    float dpi = 96;
    float Scale() const { return dpi / 96.0f; }

    std::map<std::wstring, ComPtr<IDWriteTextFormat>> formats;

    bool Init(HWND hwnd) {
        UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
        D3D_FEATURE_LEVEL levels[] = { D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL_10_1 };
        HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, flags, levels, 3, D3D11_SDK_VERSION, &device, nullptr, &ctx);
        if (FAILED(hr)) hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, flags, levels, 3, D3D11_SDK_VERSION, &device, nullptr, &ctx);
        if (FAILED(hr)) return false;

        ComPtr<IDXGIDevice> dxgiDevice;
        device.As(&dxgiDevice);
        ComPtr<IDXGIAdapter> adapter;
        dxgiDevice->GetAdapter(&adapter);
        ComPtr<IDXGIFactory2> factory;
        adapter->GetParent(IID_PPV_ARGS(&factory));

        RECT rc;
        GetClientRect(hwnd, &rc);
        width = (UINT)std::max<LONG>(1, rc.right - rc.left);
        height = (UINT)std::max<LONG>(1, rc.bottom - rc.top);
        DXGI_SWAP_CHAIN_DESC1 sd = {};
        sd.Width = width;
        sd.Height = height;
        sd.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        sd.SampleDesc.Count = 1;
        sd.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
        sd.BufferCount = 2;
        sd.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
        sd.AlphaMode = DXGI_ALPHA_MODE_IGNORE;
        if (FAILED(factory->CreateSwapChainForHwnd(device.Get(), hwnd, &sd, nullptr, nullptr, &swap))) return false;
        factory->MakeWindowAssociation(hwnd, DXGI_MWA_NO_ALT_ENTER);

        D2D1_FACTORY_OPTIONS fo = {};
        if (FAILED(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, __uuidof(ID2D1Factory1), &fo, (void**)d2dFactory.GetAddressOf()))) return false;
        if (FAILED(d2dFactory->CreateDevice(dxgiDevice.Get(), &d2dDevice))) return false;
        if (FAILED(d2dDevice->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE, &d2d))) return false;
        d2d->SetTextAntialiasMode(D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE);
        if (FAILED(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory), (IUnknown**)dwrite.GetAddressOf()))) return false;
        dpi = (float)GetDpiForWindow(hwnd);
        return CreateSized();
    }

    void ReleaseSized() {
        d2d->SetTarget(nullptr);
        backBitmap.Reset();
        backRTV.Reset();
        sceneBitmap.Reset();
        scene.Reset();
        for (auto& s : sceneSRV) s.Reset();
        sceneAllSRV.Reset();
        sceneMips.Reset();
        compositeSRV.Reset();
        composite.Reset();
    }

    bool Resize(UINT w, UINT h) {
        if (w == 0 || h == 0 || (w == width && h == height)) return true;
        ReleaseSized();
        ctx->ClearState();
        ctx->Flush();
        width = w;
        height = h;
        if (FAILED(swap->ResizeBuffers(0, w, h, DXGI_FORMAT_UNKNOWN, 0))) return false;
        return CreateSized();
    }

    bool CreateSized() {
        ComPtr<ID3D11Texture2D> back;
        if (FAILED(swap->GetBuffer(0, IID_PPV_ARGS(&back)))) return false;
        if (FAILED(device->CreateRenderTargetView(back.Get(), nullptr, &backRTV))) return false;
        D2D1_BITMAP_PROPERTIES1 bp = D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS_CANNOT_DRAW,
            D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_IGNORE), dpi, dpi);
        ComPtr<IDXGISurface> backSurface;
        back.As(&backSurface);
        if (FAILED(d2d->CreateBitmapFromDxgiSurface(backSurface.Get(), &bp, &backBitmap))) return false;

        // the backdrop as Direct2D draws it (one level: Direct2D draws only into a texture of one level)
        D3D11_TEXTURE2D_DESC td = {};
        td.Width = width;
        td.Height = height;
        td.MipLevels = 1;
        td.ArraySize = 1;
        td.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        td.SampleDesc.Count = 1;
        td.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
        if (FAILED(device->CreateTexture2D(&td, nullptr, &scene))) return false;
        ComPtr<IDXGISurface> sceneSurface;
        scene.As(&sceneSurface);
        D2D1_BITMAP_PROPERTIES1 sp = D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_TARGET,
            D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED), dpi, dpi);
        if (FAILED(d2d->CreateBitmapFromDxgiSurface(sceneSurface.Get(), &sp, &sceneBitmap))) return false;

        // the backdrop with its smaller copies, for the frost
        D3D11_TEXTURE2D_DESC md = td;
        md.MipLevels = 0;
        md.MiscFlags = D3D11_RESOURCE_MISC_GENERATE_MIPS;
        if (FAILED(device->CreateTexture2D(&md, nullptr, &sceneMips))) return false;
        sceneMips->GetDesc(&md);
        sceneMipCount = std::min<UINT>(md.MipLevels, 8);
        if (FAILED(device->CreateShaderResourceView(sceneMips.Get(), nullptr, &sceneAllSRV))) return false;
        for (UINT i = 0; i < sceneMipCount; i++) {
            D3D11_SHADER_RESOURCE_VIEW_DESC vd = {};
            vd.Format = td.Format;
            vd.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
            vd.Texture2D.MostDetailedMip = i;
            vd.Texture2D.MipLevels = 1;
            if (FAILED(device->CreateShaderResourceView(sceneMips.Get(), &vd, &sceneSRV[i]))) return false;
        }
        // the frame so far, for the lenses
        D3D11_TEXTURE2D_DESC cd = td;
        cd.BindFlags = D3D11_BIND_SHADER_RESOURCE;
        if (FAILED(device->CreateTexture2D(&cd, nullptr, &composite))) return false;
        if (FAILED(device->CreateShaderResourceView(composite.Get(), nullptr, &compositeSRV))) return false;
        return true;
    }

    /// The backdrop as drawn, into the frost's texture and its smaller copies, and onto the frame.
    void PublishScene() {
        ctx->CopySubresourceRegion(sceneMips.Get(), 0, 0, 0, 0, scene.Get(), 0, nullptr);
        ctx->GenerateMips(sceneAllSRV.Get()); // (a view of every level: one of a single level fills none below it)
        ComPtr<ID3D11Texture2D> back;
        swap->GetBuffer(0, IID_PPV_ARGS(&back));
        ctx->CopyResource(back.Get(), scene.Get());
    }

    /// The frame so far, for the lenses.
    void Snapshot() {
        ComPtr<ID3D11Texture2D> back;
        swap->GetBuffer(0, IID_PPV_ARGS(&back));
        ctx->CopyResource(composite.Get(), back.Get());
    }

    /// A text format: the face, its size (DIPs), its weight; kept.
    IDWriteTextFormat* Font(const wchar_t* face, float size, DWRITE_FONT_WEIGHT weight = DWRITE_FONT_WEIGHT_NORMAL) {
        std::wstring key = std::wstring(face) + L"|" + std::to_wstring(size) + L"|" + std::to_wstring((int)weight);
        auto it = formats.find(key);
        if (it != formats.end()) return it->second.Get();
        ComPtr<IDWriteTextFormat> f;
        dwrite->CreateTextFormat(face, nullptr, weight, DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL, size, L"en-us", &f);
        if (f) {
            f->SetWordWrapping(DWRITE_WORD_WRAPPING_NO_WRAP);
            f->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
            DWRITE_TRIMMING trim = { DWRITE_TRIMMING_GRANULARITY_CHARACTER, 0, 0 };
            ComPtr<IDWriteInlineObject> ellipsis;
            dwrite->CreateEllipsisTrimmingSign(f.Get(), &ellipsis);
            f->SetTrimming(&trim, ellipsis.Get());
        }
        formats[key] = f;
        return f.Get();
    }
};
