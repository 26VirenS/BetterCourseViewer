#pragma once
// Simpl's glass, modelled on what macOS 26 draws for Liquid Glass (Core Animation's private glassBackground filter, as
// taken apart in AlexStrNik/ShatteredGlass, and the look of the Mac's popover measured from a screenshot):
//   - the frost: what is behind, copied at a quarter of its size (as Core Animation's backdrop does) and blurred by a
//     true Gaussian — one smooth blur, no blotches;
//   - the edge: a band at the rim that bends what is behind outward, so what lies just past the edge is drawn into it
//     (glassBackground's inner refraction: a band of ~12 pt reading up to ~40 pt beyond);
//   - the tone: what is behind kept at ~62% and lifted toward the material's colour (on dark: a light warm grey; a tint
//     colours it), its colours made a little richer;
//   - the light: a thin rim, brightest along the top and the bottom, drawn from the glass's own colour made brighter (the
//     two highlights at opposite angles of CASDFGlassHighlightEffect); a hairline of dark just outside; a soft shadow;
//   - the shape: a rounded rectangle, or a squircle (continuous corners, as SwiftUI's .continuous: a superellipse of
//     degree 4 over 1.528 × the radius).
// The same shader draws a lens (a knob, a thumb, the bubble): no frost, the middle magnified, the rim bending hard.
// Its HLSL is compiled when the app starts (d3dcompiler_47, part of Windows).
#include "Gfx.h"
#include <d3dcompiler.h>
#include <string>

static const char* kGlassHlsl = R"HLSL(
cbuffer Shape : register(b0) {
    float4 uRes;        // xy: target size (px)
    float4 uBox;        // xy: centre, zw: half size (px)
    float4 uForm;       // x: corner radius (px), y: squircle (0/1), z: quad margin (px), w: px per DIP
    float4 uOptics;     // x: frost mix, y: pull (px), z: band (px), w: magnify
    float4 uTone;       // x: keep (how much of what is behind shows), y: saturation, z: brightness, w: dispersion
    float4 uColor;      // rgb: the material's colour (what the rest lifts toward)
    float4 uLight;      // x: rim strength, y: rim width (px), z: edge glow, w: dark outline
    float4 uShadow;     // x: opacity, y: blur (px), z: offset y (px), w: dark (0/1)
};

Texture2D tSharp : register(t0);
Texture2D tFrost : register(t1);
SamplerState sLinear : register(s0);

struct VOut { float4 pos : SV_Position; };

VOut GlassVS(uint id : SV_VertexID) {
    float2 corner = float2(id & 1, id >> 1) * 2.0 - 1.0;
    float2 px = uBox.xy + corner * (uBox.zw + uForm.z);
    VOut o;
    o.pos = float4(px.x / uRes.x * 2.0 - 1.0, 1.0 - px.y / uRes.y * 2.0, 0, 1);
    return o;
}

// signed distance to the shape (px, < 0 inside)
float ShapeSdf(float2 p) {
    float2 h = uBox.zw;
    float r = uForm.x;
    float2 a = abs(p);
    if (uForm.y > 0.5) {
        float R = min(r * 1.528, min(h.x, h.y));
        float2 q = a - (h - R);
        if (q.x > 0.0 && q.y > 0.0) {
            // a superellipse corner, degree 4; its distance corrected by its gradient so it stays near true pixels
            float2 q2 = q * q, q4 = q2 * q2;
            float m = pow(q4.x + q4.y, 0.25);
            float2 g = (q2 * q) / max(m * m * m, 1e-4);
            return (m - R) / max(length(g), 1e-3);
        }
        float2 e = a - h;
        return min(max(e.x, e.y), 0.0) + length(max(e, 0.0));
    }
    r = min(r, min(h.x, h.y));
    float2 q = a - h + r;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

float3 Saturate3(float3 c, float s) {
    float l = dot(c, float3(0.2126, 0.7152, 0.0722));
    return max(lerp(float3(l, l, l), c, s), 0.0);
}

float3 Behind(float2 uv, float2 off) {
    float disp = uTone.w;
    float2 r = (off * (1.0 + disp)) / uRes.xy, g = off / uRes.xy, b = (off * (1.0 - disp)) / uRes.xy;
    float3 sharp = float3(tSharp.SampleLevel(sLinear, uv + r, 0).r, tSharp.SampleLevel(sLinear, uv + g, 0).g, tSharp.SampleLevel(sLinear, uv + b, 0).b);
    if (uOptics.x <= 0.001) return sharp;
    float3 frost = float3(tFrost.SampleLevel(sLinear, uv + r, 0).r, tFrost.SampleLevel(sLinear, uv + g, 0).g, tFrost.SampleLevel(sLinear, uv + b, 0).b);
    return lerp(sharp, frost, uOptics.x);
}

float4 GlassPS(VOut i) : SV_Target {
    float2 px = i.pos.xy;
    float2 p = px - uBox.xy;
    float k = uForm.w;
    float d = ShapeSdf(p);
    float e = 0.5;
    float2 n = float2(ShapeSdf(p + float2(e, 0)) - ShapeSdf(p - float2(e, 0)), ShapeSdf(p + float2(0, e)) - ShapeSdf(p - float2(0, e)));
    n = length(n) > 1e-5 ? normalize(n) : float2(0, -1);

    // outside: the soft shadow and the dark hairline
    float shadowD = ShapeSdf(p - float2(0, uShadow.z));
    float shadow = uShadow.x * (1.0 - smoothstep(-uShadow.y * 0.35, uShadow.y, shadowD));
    float outline = uLight.w * saturate(1.0 - abs(d - 0.5 * k) / (0.9 * k));
    float outsideA = saturate(max(shadow, outline));
    float cover = saturate(0.5 - d);
    if (cover <= 0.0) return float4(0, 0, 0, outsideA);

    // the edge: a band that bends what is behind outward (a round bevel: steep at the rim, flat where it meets the face)
    float din = max(-d, 0.0);
    float u = saturate(1.0 - din / max(uOptics.z, 1.0));
    float bevel = 1.0 - sqrt(saturate(1.0 - u * u));
    float2 off = n * uOptics.y * bevel;
    // a lens magnifies its middle
    off -= p * uOptics.w * (1.0 - bevel);

    float2 uv = px / uRes.xy;
    float3 c = Behind(uv, off);
    c = Saturate3(c, uTone.y);
    c = c * uTone.x + uColor.rgb * (1.0 - uTone.x);
    c *= uTone.z;

    // the light: a glow just inside the rim, and the rim itself — brightest along the top and the bottom
    c += uLight.z * exp(-din / (3.0 * k));
    float rimMask = saturate(1.0 - (din - 0.25 * k) / max(uLight.y, 0.5));
    float facing = pow(abs(n.y), 1.5);
    float3 rimC = uShadow.w > 0.5 ? c * 1.6 + 0.04 : c * 1.08 + 0.16;
    c = lerp(c, rimC, saturate(uLight.x * rimMask * (0.1 + 0.9 * facing)));

    float3 glass = saturate(c);
    // premultiplied: the glass over its own outside
    return float4(glass * cover, cover + outsideA * (1.0 - cover));
}

// ---- the frost: down to a quarter, then a Gaussian, one way then the other ----

cbuffer BlurPass : register(b1) {
    float4 uPass;   // xy: source texel size, zw: direction (texels) for the blur
    float4 uBlur;   // x: sigma (texels), y: radius (taps)
};

VOut FullVS(uint id : SV_VertexID) {
    float2 t = float2((id << 1) & 2, id & 2);
    VOut o;
    o.pos = float4(t * float2(2, -2) + float2(-1, 1), 0, 1);
    return o;
}

Texture2D tSrc : register(t0);

float4 DownPS(VOut i) : SV_Target {
    // four bilinear taps a texel either side: a 4 × 4 tent of the source
    float2 dim;
    tSrc.GetDimensions(dim.x, dim.y);
    float2 uv = i.pos.xy * 2.0 / dim;
    float2 t = uPass.xy;
    float4 s = tSrc.SampleLevel(sLinear, uv + t * float2(-1, -1), 0) + tSrc.SampleLevel(sLinear, uv + t * float2(1, -1), 0)
             + tSrc.SampleLevel(sLinear, uv + t * float2(-1, 1), 0) + tSrc.SampleLevel(sLinear, uv + t * float2(1, 1), 0);
    return s * 0.25;
}

float4 BlurPS(VOut i) : SV_Target {
    float2 dim;
    tSrc.GetDimensions(dim.x, dim.y);
    float2 uv = i.pos.xy / dim;
    float sigma = max(uBlur.x, 0.01);
    int radius = (int)uBlur.y;
    float4 sum = tSrc.SampleLevel(sLinear, uv, 0);
    float total = 1.0;
    [loop] for (int k = 1; k <= radius; k++) {
        float w = exp(-0.5 * (k * k) / (sigma * sigma));
        float2 o = uPass.zw * k / dim;
        sum += (tSrc.SampleLevel(sLinear, uv + o, 0) + tSrc.SampleLevel(sLinear, uv - o, 0)) * w;
        total += 2.0 * w;
    }
    return sum / total;
}
)HLSL";

/// One shape of glass, in pixels, with everything that makes up its look.
struct GlassLook {
    float cx = 0, cy = 0, hw = 0, hh = 0, radius = 0;
    bool squircle = false;
    float frost = 1, pull = 0, band = 1, magnify = 0;
    float keep = 1, saturation = 1, brightness = 1, dispersion = 0;
    float r = 0.5f, g = 0.5f, b = 0.5f;
    float rim = 0, rimWidth = 1, glow = 0, outline = 0;
    float shadow = 0, shadowBlur = 1, shadowY = 0;
    bool dark = true;
};

struct Glass {
    ComPtr<ID3D11VertexShader> glassVS, fullVS;
    ComPtr<ID3D11PixelShader> glassPS, downPS, blurPS;
    ComPtr<ID3D11Buffer> shapeCB, passCB;
    ComPtr<ID3D11SamplerState> sampler;
    ComPtr<ID3D11BlendState> blend;
    ComPtr<ID3D11RasterizerState> raster;
    // the frost's textures: half, quarter, and the quarter blurred one way, then the other
    ComPtr<ID3D11Texture2D> tex[4];
    ComPtr<ID3D11RenderTargetView> rtv[4];
    ComPtr<ID3D11ShaderResourceView> srv[4];
    UINT w = 0, h = 0;
    std::string error;

    bool Compile(ID3D11Device* dev, const char* entry, const char* target, ComPtr<ID3DBlob>& out) {
        ComPtr<ID3DBlob> err;
        HRESULT hr = D3DCompile(kGlassHlsl, strlen(kGlassHlsl), "Glass.hlsl", nullptr, nullptr, entry, target, D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, &out, &err);
        if (FAILED(hr)) {
            error += std::string(entry) + ": " + (err ? (const char*)err->GetBufferPointer() : "failed") + "\n";
            return false;
        }
        return true;
    }

    bool Init(ID3D11Device* dev) {
        ComPtr<ID3DBlob> b;
        if (!Compile(dev, "GlassVS", "vs_5_0", b) || FAILED(dev->CreateVertexShader(b->GetBufferPointer(), b->GetBufferSize(), nullptr, &glassVS))) return false;
        if (!Compile(dev, "FullVS", "vs_5_0", b) || FAILED(dev->CreateVertexShader(b->GetBufferPointer(), b->GetBufferSize(), nullptr, &fullVS))) return false;
        if (!Compile(dev, "GlassPS", "ps_5_0", b) || FAILED(dev->CreatePixelShader(b->GetBufferPointer(), b->GetBufferSize(), nullptr, &glassPS))) return false;
        if (!Compile(dev, "DownPS", "ps_5_0", b) || FAILED(dev->CreatePixelShader(b->GetBufferPointer(), b->GetBufferSize(), nullptr, &downPS))) return false;
        if (!Compile(dev, "BlurPS", "ps_5_0", b) || FAILED(dev->CreatePixelShader(b->GetBufferPointer(), b->GetBufferSize(), nullptr, &blurPS))) return false;

        D3D11_BUFFER_DESC cb = {};
        cb.ByteWidth = 8 * 16;
        cb.Usage = D3D11_USAGE_DYNAMIC;
        cb.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        cb.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
        if (FAILED(dev->CreateBuffer(&cb, nullptr, &shapeCB))) return false;
        cb.ByteWidth = 2 * 16;
        if (FAILED(dev->CreateBuffer(&cb, nullptr, &passCB))) return false;

        D3D11_SAMPLER_DESC sd = {};
        sd.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
        sd.AddressU = sd.AddressV = sd.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
        sd.MaxLOD = D3D11_FLOAT32_MAX;
        if (FAILED(dev->CreateSamplerState(&sd, &sampler))) return false;

        D3D11_BLEND_DESC bd = {};
        bd.RenderTarget[0].BlendEnable = TRUE;
        bd.RenderTarget[0].SrcBlend = D3D11_BLEND_ONE;
        bd.RenderTarget[0].DestBlend = D3D11_BLEND_INV_SRC_ALPHA;
        bd.RenderTarget[0].BlendOp = D3D11_BLEND_OP_ADD;
        bd.RenderTarget[0].SrcBlendAlpha = D3D11_BLEND_ONE;
        bd.RenderTarget[0].DestBlendAlpha = D3D11_BLEND_INV_SRC_ALPHA;
        bd.RenderTarget[0].BlendOpAlpha = D3D11_BLEND_OP_ADD;
        bd.RenderTarget[0].RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_ALL;
        if (FAILED(dev->CreateBlendState(&bd, &blend))) return false;

        D3D11_RASTERIZER_DESC rd = {};
        rd.FillMode = D3D11_FILL_SOLID;
        rd.CullMode = D3D11_CULL_NONE;
        rd.DepthClipEnable = TRUE;
        return SUCCEEDED(dev->CreateRasterizerState(&rd, &raster));
    }

    bool Resize(ID3D11Device* dev, UINT width, UINT height) {
        if (width == w && height == h && tex[0]) return true;
        w = width;
        h = height;
        UINT sizes[4][2] = { { std::max(1u, w / 2), std::max(1u, h / 2) }, { std::max(1u, w / 4), std::max(1u, h / 4) },
                             { std::max(1u, w / 4), std::max(1u, h / 4) }, { std::max(1u, w / 4), std::max(1u, h / 4) } };
        for (int i = 0; i < 4; i++) {
            tex[i].Reset(); rtv[i].Reset(); srv[i].Reset();
            D3D11_TEXTURE2D_DESC td = {};
            td.Width = sizes[i][0];
            td.Height = sizes[i][1];
            td.MipLevels = 1;
            td.ArraySize = 1;
            td.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
            td.SampleDesc.Count = 1;
            td.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
            if (FAILED(dev->CreateTexture2D(&td, nullptr, &tex[i]))) return false;
            if (FAILED(dev->CreateRenderTargetView(tex[i].Get(), nullptr, &rtv[i]))) return false;
            if (FAILED(dev->CreateShaderResourceView(tex[i].Get(), nullptr, &srv[i]))) return false;
        }
        return true;
    }

    /// The frost of what is behind (sigma in full-size pixels): its quarter-size copy blurred, in srv[3].
    ID3D11ShaderResourceView* Frost(ID3D11DeviceContext* ctx, ID3D11ShaderResourceView* source, UINT srcW, UINT srcH, float sigmaPx) {
        ID3D11ShaderResourceView* none[2] = {};
        auto pass = [&](int target, ID3D11ShaderResourceView* src, ID3D11PixelShader* ps, float tx, float ty, float dx, float dy, float sigma) {
            ctx->PSSetShaderResources(0, 2, none);
            D3D11_TEXTURE2D_DESC td;
            tex[target]->GetDesc(&td);
            D3D11_VIEWPORT vp = { 0, 0, (float)td.Width, (float)td.Height, 0, 1 };
            ctx->RSSetViewports(1, &vp);
            ctx->OMSetRenderTargets(1, rtv[target].GetAddressOf(), nullptr);
            D3D11_MAPPED_SUBRESOURCE m;
            if (SUCCEEDED(ctx->Map(passCB.Get(), 0, D3D11_MAP_WRITE_DISCARD, 0, &m))) {
                float* f = (float*)m.pData;
                float data[8] = { tx, ty, dx, dy, sigma, std::min(64.0f, std::ceil(sigma * 3)), 0, 0 };
                memcpy(f, data, sizeof(data));
                ctx->Unmap(passCB.Get(), 0);
            }
            ctx->PSSetConstantBuffers(1, 1, passCB.GetAddressOf());
            ctx->PSSetShaderResources(0, 1, &src);
            ctx->PSSetShader(ps, nullptr, 0);
            ctx->Draw(3, 0);
        };
        ctx->IASetInputLayout(nullptr);
        ctx->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        ctx->VSSetShader(fullVS.Get(), nullptr, 0);
        ctx->PSSetSamplers(0, 1, sampler.GetAddressOf());
        ctx->OMSetBlendState(nullptr, nullptr, 0xffffffff);
        ctx->RSSetState(raster.Get());
        pass(0, source, downPS.Get(), 1.0f / srcW, 1.0f / srcH, 0, 0, 0);
        D3D11_TEXTURE2D_DESC half;
        tex[0]->GetDesc(&half);
        pass(1, srv[0].Get(), downPS.Get(), 1.0f / half.Width, 1.0f / half.Height, 0, 0, 0);
        float s = std::max(sigmaPx / 4.0f, 0.01f);
        pass(2, srv[1].Get(), blurPS.Get(), 0, 0, 1, 0, s);
        pass(3, srv[2].Get(), blurPS.Get(), 0, 0, 0, 1, s);
        ctx->PSSetShaderResources(0, 2, none);
        return srv[3].Get();
    }

    /// A shape of glass onto the target, reading what is behind it from sharp (and its frost).
    void Draw(ID3D11DeviceContext* ctx, ID3D11RenderTargetView* target, UINT tw, UINT th, float scale,
              ID3D11ShaderResourceView* sharp, ID3D11ShaderResourceView* frost, const GlassLook& l) {
        float margin = l.shadowBlur * 1.2f + std::fabs(l.shadowY) + 3 * scale;
        D3D11_MAPPED_SUBRESOURCE m;
        if (FAILED(ctx->Map(shapeCB.Get(), 0, D3D11_MAP_WRITE_DISCARD, 0, &m))) return;
        float data[32] = {
            (float)tw, (float)th, 0, 0,
            l.cx, l.cy, l.hw, l.hh,
            l.radius, l.squircle ? 1.0f : 0.0f, margin, scale,
            l.frost, l.pull, l.band, l.magnify,
            l.keep, l.saturation, l.brightness, l.dispersion,
            l.r, l.g, l.b, 1,
            l.rim, l.rimWidth, l.glow, l.outline,
            l.shadow, l.shadowBlur, l.shadowY, l.dark ? 1.0f : 0.0f,
        };
        memcpy(m.pData, data, sizeof(data));
        ctx->Unmap(shapeCB.Get(), 0);

        D3D11_VIEWPORT vp = { 0, 0, (float)tw, (float)th, 0, 1 };
        ctx->RSSetViewports(1, &vp);
        ctx->RSSetState(raster.Get());
        ctx->OMSetRenderTargets(1, &target, nullptr);
        ctx->OMSetBlendState(blend.Get(), nullptr, 0xffffffff);
        ctx->IASetInputLayout(nullptr);
        ctx->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLESTRIP);
        ctx->VSSetShader(glassVS.Get(), nullptr, 0);
        ctx->VSSetConstantBuffers(0, 1, shapeCB.GetAddressOf());
        ctx->PSSetShader(glassPS.Get(), nullptr, 0);
        ctx->PSSetConstantBuffers(0, 1, shapeCB.GetAddressOf());
        ID3D11ShaderResourceView* views[2] = { sharp, frost ? frost : sharp };
        ctx->PSSetShaderResources(0, 2, views);
        ctx->PSSetSamplers(0, 1, sampler.GetAddressOf());
        ctx->Draw(4, 0);
        ID3D11ShaderResourceView* none[2] = {};
        ctx->PSSetShaderResources(0, 2, none);
        ctx->OMSetBlendState(nullptr, nullptr, 0xffffffff);
    }
};
