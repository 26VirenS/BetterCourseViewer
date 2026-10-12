// Simpl's glass (Liquid Glass after macOS 26's): the one shader both the showcase (windows/GlassNative: C++) and the
// app (windows/Simpl: C#) compile when they start. What each part does is told in windows/GlassNative/src/Glass.h.
cbuffer Shape : register(b0) {
    float4 uRes;        // xy: target size (px)
    float4 uBox;        // xy: centre, zw: half size (px)
    float4 uForm;       // x: corner radius (px), y: the squircle's degree (0: round corners), z: quad margin (px), w: px per DIP
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
        float n = uForm.y;
        float R = min(r, min(h.x, h.y));
        float2 q = a - (h - R);
        if (q.x > 0.0 && q.y > 0.0) {
            // a superellipse corner of degree n; its distance corrected by its gradient so it stays near true pixels
            q = max(q, 1e-5);
            float2 qn = pow(q, n);
            float m = pow(qn.x + qn.y, 1.0 / n);
            float2 g = pow(q, n - 1.0) / max(pow(m, n - 1.0), 1e-4);
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
