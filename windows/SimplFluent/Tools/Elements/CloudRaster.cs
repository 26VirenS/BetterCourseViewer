namespace Simpl.Tools;

// The electron cloud drawn in software (WPF's Viewport3D has no points): every dot projected through the same camera as
// the Mac's SceneKit view (38° high, 4 units back), splatted as a small anti-aliased disc into float buffers, and toned
// into premultiplied BGRA pixels for a WriteableBitmap — glowing (added up, in linear light) on a dark card, inked
// (alpha-composited) on a light one; the subshells not chosen drawn underneath in one quiet grey. No WPF in here: it is
// arrays in, pixels out (the view, CloudPicture in Atom3D.cs, owns the bitmap, the turning and the timing).

/// <summary>The software rasteriser of the Cloud view (cloud-spec.md §3, §5.2).</summary>
public sealed class CloudRaster
{
    // ---- the look (cloud-spec.md §3) ----
    public const double TanHalfFov = 0.34432761328966527; // tan(19°): SceneKit's 38° field, across the view's smaller side
    public const double CameraDistance = 4.0;              // scene units, the frame radius 1 unit: the spec's (the view
                                                           // frames it by SceneFrame, Atom3DView.CloudDistance)
    public const double PointSize = 0.012;                 // a dot's diameter in scene units …
    public const double DotMin = 1.0, DotMax = 3.0;        // … held between 1 and 3 DIPs on screen
    public const float LightAlpha = 0.55f;                 // light: each dot's opacity
    public const float DarkGain = 0.45f;                   // dark: each dot's light, added in linear light
    public const float RestAlpha = 0.35f;                  // the subshells not chosen: each dot's opacity, in grey
    public const double NucleusRadius = 0.024, HaloRadius = 0.036; // scene units

    /// <summary>s, p, d, f: light and dark (sRGB hex), and the rest's grey.</summary>
    public static readonly string[] LightHex = { "#C0392B", "#9A7400", "#1F6FB2", "#1E8449" };
    public static readonly string[] DarkHex = { "#FF7A6E", "#FFD34D", "#64B5FF", "#5EE08F" };
    public const string RestLight = "#D1D1D6", RestDark = "#48484A";
    public const string NucleusLight = "#1D1D1F", NucleusDark = "#FFFFFF", HaloLight = "#FFFFFF", HaloDark = "#1C1C1E";

    public int Width { get; private set; }
    public int Height { get; private set; }
    /// <summary>Premultiplied BGRA (0xAARRGGBB as an int), row by row: what WriteableBitmap.WritePixels takes.</summary>
    public int[] Pixels { get; private set; } = Array.Empty<int>();

    private float[] cov = Array.Empty<float>(), cr = Array.Empty<float>(), cg = Array.Empty<float>(), cb = Array.Empty<float>(), rest = Array.Empty<float>();
    private int lastX0, lastY0, lastX1 = -1, lastY1 = -1;

    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height) return;
        Width = width;
        Height = height;
        var n = width * height;
        cov = new float[n]; cr = new float[n]; cg = new float[n]; cb = new float[n]; rest = new float[n];
        Pixels = new int[n];
        lastX0 = lastY0 = 0;
        lastX1 = lastY1 = -1;
    }

    private static (float R, float G, float B) Srgb(string hex)
    {
        var v = Convert.ToInt32(hex.TrimStart('#'), 16);
        return (((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f);
    }

    private static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    private static float ToSrgbExact(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;

    /// <summary>Linear light 0…1 to sRGB, by a 4096-step table (the tone runs per pixel per frame).</summary>
    private static readonly float[] SrgbTable = Enumerable.Range(0, 4097).Select(i => ToSrgbExact(i / 4096f)).ToArray();

    private static float ToSrgb(float c) => SrgbTable[(int)(Math.Clamp(c, 0f, 1f) * 4096f + 0.5f)];

    /// <summary>1 − e^(−x), the coverage of x dots' worth of ink, by a table on 0…16 in 1/256 steps.</summary>
    private static readonly float[] CoverTable = Enumerable.Range(0, 4097).Select(i => 1f - MathF.Exp(-i / 256f)).ToArray();

    private static float Cover(float x) => x >= 16f ? 1f : CoverTable[(int)(x * 256f + 0.5f)];

    /// <summary>
    /// Draws the cloud. Points in Bohr radii (x, y, z), each point's subshell index and each subshell's l; focus is the
    /// chosen subshell's index (−1: all of them); frame is the radius (Bohr radii) that becomes one scene unit; m is the
    /// turn (3×3, row-major, for column vectors: q = m·p) — the drag's, the spin's and the pose's together; distance is
    /// the camera's (4 unless zoomed); dipScale the screen's pixels per DIP.
    /// </summary>
    public void Draw(double[] x, double[] y, double[] z, byte[] sub, int[] subL, int focus, double frame, double[] m,
                     double distance, double dipScale, bool dark)
    {
        int w = Width, h = Height;
        // (only what the last frame touched is cleared: the cloud seldom fills the view)
        if (lastX1 >= lastX0)
            for (var yy = lastY0; yy <= lastY1; yy++)
            {
                int j0 = yy * w + lastX0, n = lastX1 - lastX0 + 1;
                Array.Clear(cov, j0, n); Array.Clear(cr, j0, n); Array.Clear(cg, j0, n); Array.Clear(cb, j0, n); Array.Clear(rest, j0, n);
                Array.Clear(Pixels, j0, n);
            }
        // (the lens across the view's smaller side, as every 3D view's: a tall narrow view keeps the cloud's sides)
        var f = Math.Min(w, h) / 2.0 / TanHalfFov;
        var inv = 1.0 / frame;
        double dMin = DotMin * dipScale, dMax = DotMax * dipScale;
        // each type's colour, as it is added (dark: linear light × gain; light: sRGB × alpha)
        var col = new (float R, float G, float B)[4];
        for (var t = 0; t < 4; t++)
        {
            var c = Srgb(dark ? DarkHex[t] : LightHex[t]);
            col[t] = dark ? (ToLinear(c.R) * DarkGain, ToLinear(c.G) * DarkGain, ToLinear(c.B) * DarkGain) : (c.R * LightAlpha, c.G * LightAlpha, c.B * LightAlpha);
        }
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (var i = 0; i < x.Length; i++)
        {
            double px = x[i] * inv, py = y[i] * inv, pz = z[i] * inv;
            var qx = m[0] * px + m[1] * py + m[2] * pz;
            var qy = m[3] * px + m[4] * py + m[5] * pz;
            var qz = m[6] * px + m[7] * py + m[8] * pz;
            var zc = distance - qz;
            if (zc <= 0.05) continue;
            var sx = w / 2.0 + qx * f / zc;
            var sy = h / 2.0 - qy * f / zc;
            var r = Math.Clamp(PointSize * f / zc, dMin, dMax) / 2;
            var reach = r + 0.5;
            int ax = (int)Math.Floor(sx - reach), bx = (int)Math.Floor(sx + reach);
            int ay = (int)Math.Floor(sy - reach), by = (int)Math.Floor(sy + reach);
            if (bx < 0 || by < 0 || ax >= w || ay >= h) continue;
            ax = Math.Max(ax, 0); ay = Math.Max(ay, 0); bx = Math.Min(bx, w - 1); by = Math.Min(by, h - 1);
            if (ax < x0) x0 = ax;
            if (ay < y0) y0 = ay;
            if (bx > x1) x1 = bx;
            if (by > y1) y1 = by;
            var on = focus < 0 || sub[i] == focus;
            var c = col[Math.Clamp(subL[sub[i]], 0, 3)];
            for (var yy = ay; yy <= by; yy++)
            {
                var dy = yy + 0.5 - sy;
                var row = yy * w;
                for (var xx = ax; xx <= bx; xx++)
                {
                    var dx = xx + 0.5 - sx;
                    var k = (float)(reach - Math.Sqrt(dx * dx + dy * dy));
                    if (k <= 0) continue;
                    if (k > 1) k = 1;
                    var j = row + xx;
                    if (!on) { rest[j] += RestAlpha * k; continue; }
                    if (!dark) cov[j] += LightAlpha * k;
                    cr[j] += c.R * k; cg[j] += c.G * k; cb[j] += c.B * k;
                }
            }
        }
        // the tone: the rest as grey ink; then the chosen as light added on top (dark) or ink over it (light)
        var g = Srgb(dark ? RestDark : RestLight);
        if (x1 >= x0)
        {
            for (var yy = y0; yy <= y1; yy++)
                for (var xx = x0; xx <= x1; xx++)
                {
                    var j = yy * w + xx;
                    if (rest[j] == 0 && cr[j] == 0 && cg[j] == 0 && cb[j] == 0) continue;
                    float restCov = Cover(rest[j]);
                    float pr, pg, pb, a;
                    if (dark)
                    {
                        float er = ToSrgb(Math.Min(cr[j], 1f)), eg = ToSrgb(Math.Min(cg[j], 1f)), eb = ToSrgb(Math.Min(cb[j], 1f));
                        pr = Math.Min(1f, g.R * restCov + er);
                        pg = Math.Min(1f, g.G * restCov + eg);
                        pb = Math.Min(1f, g.B * restCov + eb);
                        a = Math.Max(restCov, Math.Max(pr, Math.Max(pg, pb)));
                    }
                    else
                    {
                        var s = cov[j];
                        float fc = Cover(s);
                        float ir = s > 0 ? cr[j] / s : 0, ig = s > 0 ? cg[j] / s : 0, ib = s > 0 ? cb[j] / s : 0;
                        pr = ir * fc + g.R * restCov * (1 - fc);
                        pg = ig * fc + g.G * restCov * (1 - fc);
                        pb = ib * fc + g.B * restCov * (1 - fc);
                        a = fc + restCov * (1 - fc);
                    }
                    if (a <= 0) continue;
                    Pixels[j] = Pack(pr, pg, pb, a);
                }
        }
        // the nucleus: a halo, then the dot, at the centre (always on top; not to scale)
        var rn = NucleusRadius * f / distance;
        var rh = HaloRadius * f / distance;
        Disc(w / 2.0, h / 2.0, rh, Srgb(dark ? HaloDark : HaloLight));
        Disc(w / 2.0, h / 2.0, rn, Srgb(dark ? NucleusDark : NucleusLight));
        var hr = (int)Math.Ceiling(rh + 1);
        lastX0 = Math.Max(0, Math.Min(x0, w / 2 - hr)); lastY0 = Math.Max(0, Math.Min(y0, h / 2 - hr));
        lastX1 = Math.Min(w - 1, Math.Max(x1, w / 2 + hr)); lastY1 = Math.Min(h - 1, Math.Max(y1, h / 2 + hr));
    }

    /// <summary>An opaque anti-aliased disc composited over the pixels (premultiplied "over").</summary>
    private void Disc(double cx, double cy, double radius, (float R, float G, float B) c)
    {
        var reach = radius + 0.5;
        int ax = Math.Max(0, (int)Math.Floor(cx - reach)), bx = Math.Min(Width - 1, (int)Math.Floor(cx + reach));
        int ay = Math.Max(0, (int)Math.Floor(cy - reach)), by = Math.Min(Height - 1, (int)Math.Floor(cy + reach));
        for (var yy = ay; yy <= by; yy++)
            for (var xx = ax; xx <= bx; xx++)
            {
                double dx = xx + 0.5 - cx, dy = yy + 0.5 - cy;
                var k = (float)Math.Clamp(reach - Math.Sqrt(dx * dx + dy * dy), 0, 1);
                if (k <= 0) continue;
                var j = yy * Width + xx;
                var p = Pixels[j];
                float a0 = ((p >> 24) & 255) / 255f, r0 = ((p >> 16) & 255) / 255f, g0 = ((p >> 8) & 255) / 255f, b0 = (p & 255) / 255f;
                Pixels[j] = Pack(c.R * k + r0 * (1 - k), c.G * k + g0 * (1 - k), c.B * k + b0 * (1 - k), k + a0 * (1 - k));
            }
    }

    private static int Pack(float r, float g, float b, float a)
    {
        static int B8(float v) => (int)(Math.Clamp(v, 0f, 1f) * 255f + 0.5f);
        int ai = B8(a);
        // (premultiplied: no channel above alpha)
        int ri = Math.Min(B8(r), ai), gi = Math.Min(B8(g), ai), bi = Math.Min(B8(b), ai);
        return (ai << 24) | (ri << 16) | (gi << 8) | bi;
    }

    /// <summary>The rotation the cloud is shown at before any drag or spin: tipped 1 radian about x (its z axis toward the
    /// viewer and up), then −0.6 radian about y. Row-major, for column vectors.</summary>
    public static double[] Pose()
    {
        double ca = Math.Cos(-1.0), sa = Math.Sin(-1.0), cb = Math.Cos(-0.6), sb = Math.Sin(-0.6);
        // Ry(b) · Rx(a)
        return new[]
        {
            cb, sb * sa, sb * ca,
            0, ca, -sa,
            -sb, cb * sa, cb * ca,
        };
    }

    /// <summary>a·b for 3×3 row-major matrices.</summary>
    public static double[] Multiply(double[] a, double[] b)
    {
        var o = new double[9];
        for (var r = 0; r < 3; r++)
            for (var c = 0; c < 3; c++)
                o[r * 3 + c] = a[r * 3] * b[c] + a[r * 3 + 1] * b[3 + c] + a[r * 3 + 2] * b[6 + c];
        return o;
    }

    /// <summary>A turn about the vertical (y) axis by an angle in radians (the slow spin).</summary>
    public static double[] AboutY(double angle)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        return new[] { c, 0, s, 0, 1, 0, -s, 0, c };
    }
}
