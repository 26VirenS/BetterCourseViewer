// Simpl's Liquid Glass for Windows: the final pass over what is behind a glass shape, already frosted (WPF's own blur,
// a pass before this one). Compiled for WPF's ShaderEffect (ps_3_0) by fxc in CI: windows/tools/shaders.ps1.
//
// The optics follow AuroraGlass's glass.hlsl (Apache-2.0, VeiQiuLab/AuroraGlass): a rounded-rectangle signed distance
// field gives the shape and its rim; within a bevel band along the rim the surface curves, and what is behind is bent
// inward through it (a quadratic profile: strongest at the very edge, flat by the bevel's width), each colour a little
// apart (dispersion). Then the Mac's look: colours a little richer (saturation), a tint, a thin bright rim — brightest
// where it faces the light, and on the far side too, as light runs round the edge of real glass — and a soft lift
// along the inside of the rim. Premultiplied alpha out, as WPF composes it.

sampler2D input : register(s0);

float4 shape   : register(c0); // x = width, y = height (the backdrop, margin included), z = margin, w = corner radius
float4 optics  : register(c1); // x = refraction (px), y = dispersion (0..1), z = bevel width (px), w = saturation
float4 light   : register(c2); // x, y = light direction (unit, screen space), z = rim strength, w = inner glow
float4 tint    : register(c3); // rgb = tint colour (0..1), a = tint amount
float4 tone    : register(c4); // x = brightness, y = opacity, z = pressed/lift (0..1), w = unused

float sdRoundRect(float2 p, float2 h, float r)
{
    float2 q = abs(p) - h + r;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 size = shape.xy;
    float m = shape.z;
    float2 px = uv * size;
    float2 c = size * 0.5;
    float2 h = c - m;
    float r = min(shape.w, min(h.x, h.y));
    float2 p = px - c;

    float sdf = sdRoundRect(p, h, r);
    float cover = 1.0 - smoothstep(-0.8, 0.8, sdf);
    if (cover <= 0.0) return float4(0, 0, 0, 0);

    // the rim's outward direction (the distance field's gradient)
    float gx = sdRoundRect(p + float2(1, 0), h, r) - sdRoundRect(p - float2(1, 0), h, r);
    float gy = sdRoundRect(p + float2(0, 1), h, r) - sdRoundRect(p - float2(0, 1), h, r);
    float2 g = float2(gx, gy);
    float gl = length(g);
    float2 n = gl > 0.0001 ? g / gl : float2(0, 0);
    float d = max(-sdf, 0.0);

    // refraction through the bevel: what is behind bent inward, strongest at the edge
    float bevel = max(optics.z, 1.0);
    float t = saturate(d / bevel);
    float k = (1.0 - t) * (1.0 - t);
    float2 off = (n * optics.x * k) / size;
    float disp = optics.y * 0.35;
    float3 col;
    col.r = tex2D(input, uv + off * (1.0 + disp)).r;
    col.g = tex2D(input, uv + off).g;
    col.b = tex2D(input, uv + off * (1.0 - disp)).b;
    float a = tex2D(input, uv + off).a;
    col = a > 0.0001 ? col / a : col; // (the frosted backdrop comes premultiplied)

    // the Mac's look: richer colour, the tint, the brightness
    float luma = dot(col, float3(0.299, 0.587, 0.114));
    col = lerp(float3(luma, luma, luma), col, optics.w);
    col = lerp(col, tint.rgb, tint.a);
    col *= tone.x;

    // over a backdrop that is partly see-through (a control's own layer: its words on nothing), what is not there is
    // the tint alone, as thin as it is — never black: premultiplied, the backdrop's share and the tint's share
    float3 pc = col * a + tint.rgb * tint.a * (1.0 - a);
    float pa = a + tint.a * (1.0 - a);

    // the rim: a hairline of light, brightest facing the light and on the far side; and a soft lift inside it
    float hair = 1.0 - saturate(d / 1.6);
    float facing = dot(-n, light.xy);
    float lit = 0.35 + 0.65 * pow(abs(facing), 1.5);
    float rimLight = hair * lit * light.z;
    float glow = 1.0 - saturate(d / (bevel * 0.9));
    float glowLight = glow * glow * light.w * (0.6 + 0.4 * saturate(facing));
    // pressed: a little light gathered in the middle
    float pressLight = tone.z * 0.06 * (1.0 - saturate(length(p / h)));
    float added = rimLight + glowLight + pressLight;
    pc += added;
    pa = saturate(pa + added);

    float k2 = cover * tone.y;
    return float4(min(pc, pa) * k2, pa * k2);
}
