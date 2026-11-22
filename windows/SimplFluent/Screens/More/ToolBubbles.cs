using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Simpl.Kit;
using Simpl.Support;
using Button = System.Windows.Controls.Button;
using Image = System.Windows.Controls.Image;

namespace Simpl.Screens;

// (0.3.110, the Mac's ToolBubbles.swift) External Tools as the watch's honeycomb: a bubble a tool, the one needed next in
// the middle and the rest in rings round it, each smaller than the last. A bubble is filled with its tool's own colour,
// taken from the logo on the tool's site; the logo's mark sits on it in white (or, a logo that is a picture, the picture
// in a small round window), and the tool's name runs along its lower edge. A badge says how many are still to do: red
// with something missing, orange with something due today.

/// <summary>What a tool's logo makes of its bubble: its brand's colour and its mark. <c>glyph</c>: the mark, white, cut to
/// its own edges; <c>picture</c>: the logo as it is (ALEKS's chart), shown in a round window; <c>none</c>: no logo sharp
/// enough was found — its first letter, on its course's colour.</summary>
internal sealed record ToolLook(string Kind, string Brand, double Aspect, DateTime At);

/// <summary>The logos found, by host: kept in the app's folder (Tool logos) and in memory, each looked for once a run at
/// most — found again after 30 days, a miss after 3.</summary>
internal static class ToolLogos
{
    private static readonly Dictionary<string, ToolLook> looks = new();
    private static readonly Dictionary<string, BitmapSource> images = new();
    private static readonly HashSet<string> asked = new();

    /// <summary>A host's logo has come (its key, as <see cref="Key"/> makes it).</summary>
    public static event Action<string>? Changed;

    public static ToolLook? Look(string host) => looks.TryGetValue(Key(host), out var l) ? l : null;

    public static BitmapSource? Image(string host) => images.TryGetValue(Key(host), out var i) ? i : null;

    /// <summary>A host as a file's name: lower case, letters, digits, dots and dashes.</summary>
    public static string Key(string host)
    {
        var k = new string((host ?? "").ToLowerInvariant().Where(c => c < 128 && (char.IsLetterOrDigit(c) || c == '.' || c == '-')).ToArray());
        return k.Length > 120 ? k[..120] : k;
    }

    private static string Folder
    {
        get
        {
            var f = System.IO.Path.Combine(AppPaths.Root, "Tool logos");
            try { Directory.CreateDirectory(f); } catch { /* read-only: nothing kept */ }
            return f;
        }
    }

    /// <summary>Its logo looked for (on the UI thread): the one kept if it is recent, else found again on its site, the kept
    /// one shown meanwhile. A picture waits for it.</summary>
    public static async void Want(string host)
    {
        var k = Key(host);
        if (k.Length == 0 || !asked.Add(k)) return;
        if (Kept(k) is { } kept)
        {
            looks[k] = kept.Look;
            if (kept.Image != null) images[k] = kept.Image;
            if (DateTime.UtcNow - kept.Look.At < TimeSpan.FromDays(kept.Look.Kind == "none" ? 3 : 30)) return;
        }
        Shot.Began();
        try
        {
            var (look, png) = await Task.Run(() => LogoStudy.Make(k));
            // (a miss keeps a logo found before, if there was one)
            if (look.Kind == "none" && looks.TryGetValue(k, out var had) && had.Kind != "none") return;
            Keep(k, look, png);
            if (png != null && Load(png) is { } img) images[k] = img; else images.Remove(k);
            looks[k] = look;
            Log.Write($"tool logo: {k} {look.Kind}");
            Changed?.Invoke(k);
        }
        catch (Exception e) { Log.Write($"tool logo: {k} failed ({e.GetType().Name})"); }
        finally { Shot.Ended(); }
    }

    private static BitmapSource? Load(byte[] png)
    {
        try
        {
            var b = new BitmapImage();
            b.BeginInit();
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.StreamSource = new MemoryStream(png);
            b.EndInit();
            b.Freeze();
            return b;
        }
        catch { return null; }
    }

    private static (ToolLook Look, BitmapSource? Image)? Kept(string k)
    {
        try
        {
            var meta = System.IO.Path.Combine(Folder, k + ".json");
            if (!File.Exists(meta) || JsonSerializer.Deserialize<ToolLook>(File.ReadAllText(meta)) is not { } look) return null;
            if (look.Kind == "none") return (look, null);
            var file = System.IO.Path.Combine(Folder, k + ".png");
            return File.Exists(file) && Load(File.ReadAllBytes(file)) is { } img ? (look, img) : null;
        }
        catch { return null; }
    }

    private static void Keep(string k, ToolLook look, byte[]? png)
    {
        try
        {
            if (png != null) File.WriteAllBytes(System.IO.Path.Combine(Folder, k + ".png"), png);
            File.WriteAllText(System.IO.Path.Combine(Folder, k + ".json"), JsonSerializer.Serialize(look));
        }
        catch { /* not kept: found again next time */ }
    }
}

/// <summary>A tool's logo found on its own site, as a browser finds a site's icon: the icons its home page names (<c>&lt;link
/// rel=icon&gt;</c>, <c>apple-touch-icon</c>), those its web manifest names, then <c>/apple-touch-icon.png</c> and
/// <c>/favicon.ico</c> — the sharpest of them, tried on the tool's host, then on its site's own domain. Under 48 pixels is
/// too blurred to use.</summary>
internal static class LogoFinder
{
    private static readonly HttpClient Http = MakeClient();

    private static HttpClient MakeClient()
    {
        var h = new HttpClient(new HttpClientHandler { UseCookies = false, AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(12) };
        h.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0");
        h.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
        return h;
    }

    private static readonly Regex LinkTag = new(@"<link\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Attr = new(@"([A-Za-z_:][-A-Za-z0-9_:.]*)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'>]+))", RegexOptions.Compiled);

    public static async Task<BitmapSource?> Find(string host)
    {
        var site = Domain(host);
        var sites = new List<string> { host };
        if (site != host) { sites.Add(site); sites.Add("www." + site); }
        var seen = new HashSet<string>();
        BitmapSource? best = null;
        foreach (var s in sites.Distinct())
        {
            if (!Uri.TryCreate($"https://{s}/", UriKind.Absolute, out var start) || await Get(start) is not { } page) continue;
            var html = System.Text.Encoding.UTF8.GetString(page.Data, 0, Math.Min(page.Data.Length, 800_000));
            foreach (var c in (await Candidates(html, page.Url)).Take(6))
            {
                if (!seen.Add(c.Url.AbsoluteUri)) continue;
                if (await Get(c.Url) is not { } file || file.Data.Length <= 200 || Decode(file.Data) is not { } img) continue;
                if (best == null || img.PixelWidth > best.PixelWidth) best = img;
            }
            if (best is { PixelWidth: >= 120 }) break; // (sharp enough: the site's own domain is not needed)
        }
        return best is { PixelWidth: >= 48 } ? best : null;
    }

    private static async Task<(byte[] Data, Uri Url)?> Get(Uri url)
    {
        try
        {
            using var res = await Http.GetAsync(url);
            if (!res.IsSuccessStatusCode) return null;
            return (await res.Content.ReadAsByteArrayAsync(), res.RequestMessage?.RequestUri ?? url);
        }
        catch { return null; }
    }

    /// <summary>The domain a host is part of: "learn.zybooks.com" → "zybooks.com", "lms.school.ac.uk" → "school.ac.uk".</summary>
    public static string Domain(string host)
    {
        var parts = host.Split('.');
        if (parts.Length <= 2) return host;
        if (parts[^1].Length == 2 && new[] { "co", "ac", "edu", "com", "org", "net", "gov" }.Contains(parts[^2])) return string.Join('.', parts[^3..]);
        return string.Join('.', parts[^2..]);
    }

    /// <summary>The icons a page names, the largest first, with the two every site may have at the end.</summary>
    private static async Task<List<(int Size, Uri Url)>> Candidates(string html, Uri baseUrl)
    {
        var icons = new List<(int Size, Uri Url)>();
        Uri? manifest = null;
        foreach (Match m in LinkTag.Matches(html))
        {
            var a = new Dictionary<string, string>();
            foreach (Match am in Attr.Matches(m.Value))
            {
                var v = am.Groups[2].Success ? am.Groups[2].Value : am.Groups[3].Success ? am.Groups[3].Value : am.Groups[4].Value;
                a[am.Groups[1].Value.ToLowerInvariant()] = v.Replace("&amp;", "&");
            }
            var rel = a.GetValueOrDefault("rel", "").ToLowerInvariant();
            if (!a.TryGetValue("href", out var href) || href.Length == 0 || !Uri.TryCreate(baseUrl, href, out var u)) continue;
            if (rel.Contains("manifest")) { manifest = u; continue; }
            if (!rel.Contains("icon") || rel.Contains("mask-icon")) continue;
            var size = SizeOf(a.GetValueOrDefault("sizes"));
            if (size == 0 && rel.Contains("apple-touch")) size = 180;
            icons.Add((size, u));
        }
        if (manifest != null && await Get(manifest) is { } file)
        {
            try
            {
                using var doc = JsonDocument.Parse(file.Data);
                if (doc.RootElement.TryGetProperty("icons", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var icon in list.EnumerateArray())
                    {
                        if (icon.ValueKind != JsonValueKind.Object || !icon.TryGetProperty("src", out var src) || src.GetString() is not { Length: > 0 } s) continue;
                        if (!Uri.TryCreate(file.Url, s, out var u)) continue;
                        icons.Add((SizeOf(icon.TryGetProperty("sizes", out var z) ? z.GetString() : null), u));
                    }
                }
            }
            catch { /* not a manifest after all */ }
        }
        var origin = new UriBuilder(baseUrl.Scheme, baseUrl.Host, baseUrl.Port);
        origin.Path = "/apple-touch-icon.png";
        icons.Add((180, origin.Uri));
        origin.Path = "/favicon.ico";
        icons.Add((16, origin.Uri));
        // (an SVG is not read here: WPF has no reader for it)
        return icons.Where(i => !i.Url.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            .Select((i, n) => (i, n)).OrderByDescending(x => x.i.Size).ThenBy(x => x.n).Select(x => x.i).ToList();
    }

    /// <summary>"16x16 32x32" → 32.</summary>
    private static int SizeOf(string? sizes) =>
        (sizes ?? "").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s.Split('x')[0], out var n) ? n : 0).DefaultIfEmpty(0).Max();

    /// <summary>An icon file read: the largest image in it (an .ico holds several).</summary>
    public static BitmapSource? Decode(byte[] data)
    {
        try
        {
            using var ms = new MemoryStream(data);
            var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource? best = null;
            foreach (var f in dec.Frames) if (best == null || f.PixelWidth > best.PixelWidth) best = f;
            if (best is not { PixelWidth: > 0, PixelHeight: > 0 }) return null;
            best.Freeze();
            return best;
        }
        catch { return null; }
    }
}

/// <summary>What a logo is made of, read from its pixels (the Mac's LogoStudy, step for step): whether it is a mark on
/// nothing, a mark on a square of one colour (Piazza, Gradescope) or a picture (ALEKS); its brand's colour — a coloured
/// square's, else the strongest hue in the mark that white can sit on — deepened until white writing on it reads; and the
/// mark alone, white, cut to its edges, without the line of writing some icons set under it.</summary>
internal static class LogoStudy
{
    private const int N = 192;

    public static async Task<(ToolLook Look, byte[]? Png)> Make(string host)
    {
        var miss = new ToolLook("none", "", 1, DateTime.UtcNow);
        if (await LogoFinder.Find(host) is not { } img || Study(img) is not { } done) return (miss, null);
        try
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(done.Image));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return (done.Look, ms.ToArray());
        }
        catch { return (miss, null); }
    }

    /// <summary>The image drawn into an N×N square, centred, kept to its shape (premultiplied BGRA).</summary>
    private static byte[] Fit(BitmapSource img)
    {
        var src = new FormatConvertedBitmap(img, PixelFormats.Pbgra32, null, 0);
        int w = src.PixelWidth, h = src.PixelHeight;
        var px = new byte[w * h * 4];
        src.CopyPixels(px, w * 4, 0);
        var sc = Math.Min((double)N / w, (double)N / h);
        double dw = w * sc, dh = h * sc, ox = (N - dw) / 2, oy = (N - dh) / 2;
        var o = new byte[N * N * 4];
        for (var ty = 0; ty < N; ty++)
        {
            for (var tx = 0; tx < N; tx++)
            {
                if (tx + 1 <= ox || tx >= ox + dw || ty + 1 <= oy || ty >= oy + dh) continue;
                double b = 0, g = 0, r = 0, a = 0;
                if (sc < 1)
                {
                    // (smaller: every source pixel under it, averaged)
                    int x0 = Math.Max(0, (int)Math.Floor((tx - ox) / sc)), x1 = Math.Min(w, (int)Math.Ceiling((tx + 1 - ox) / sc));
                    int y0 = Math.Max(0, (int)Math.Floor((ty - oy) / sc)), y1 = Math.Min(h, (int)Math.Ceiling((ty + 1 - oy) / sc));
                    var n = 0;
                    for (var y = y0; y < y1; y++)
                    {
                        for (var x = x0; x < x1; x++)
                        {
                            var k = (y * w + x) * 4;
                            b += px[k]; g += px[k + 1]; r += px[k + 2]; a += px[k + 3];
                            n++;
                        }
                    }
                    if (n == 0) continue;
                    b /= n; g /= n; r /= n; a /= n;
                }
                else
                {
                    // (larger: between the four nearest)
                    var sx = Math.Clamp((tx + 0.5 - ox) / sc - 0.5, 0, w - 1);
                    var sy = Math.Clamp((ty + 0.5 - oy) / sc - 0.5, 0, h - 1);
                    int x0 = (int)sx, y0 = (int)sy, x1 = Math.Min(w - 1, x0 + 1), y1 = Math.Min(h - 1, y0 + 1);
                    double fx = sx - x0, fy = sy - y0;
                    foreach (var (x, y, f) in new[] { (x0, y0, (1 - fx) * (1 - fy)), (x1, y0, fx * (1 - fy)), (x0, y1, (1 - fx) * fy), (x1, y1, fx * fy) })
                    {
                        var k = (y * w + x) * 4;
                        b += px[k] * f; g += px[k + 1] * f; r += px[k + 2] * f; a += px[k + 3] * f;
                    }
                }
                var t = (ty * N + tx) * 4;
                o[t] = (byte)Math.Round(b); o[t + 1] = (byte)Math.Round(g); o[t + 2] = (byte)Math.Round(r); o[t + 3] = (byte)Math.Round(a);
            }
        }
        return o;
    }

    public static (ToolLook Look, BitmapSource Image)? Study(BitmapSource img)
    {
        const int count = N * N;
        var p = Fit(img);
        // (its pixels, top row first, the colour without the alpha multiplied in)
        double[] r = new double[count], g = new double[count], b = new double[count], a = new double[count];
        for (var k = 0; k < count; k++)
        {
            double al = p[k * 4 + 3];
            a[k] = al;
            if (al <= 0) continue;
            b[k] = Math.Min(255, p[k * 4] * 255 / al);
            g[k] = Math.Min(255, p[k * 4 + 1] * 255 / al);
            r[k] = Math.Min(255, p[k * 4 + 2] * 255 / al);
        }
        double Dist(int k, double[] c) => Math.Sqrt((r[k] - c[0]) * (r[k] - c[0]) + (g[k] - c[1]) * (g[k] - c[1]) + (b[k] - c[2]) * (b[k] - c[2]));

        // a filled square: one colour behind a mark, or a picture
        double[]? plate = null;
        var picture = false;
        if ((double)a.Count(v => v > 200) / count > 0.8)
        {
            var m = (int)(N * 0.1);
            var ring = new List<int>();
            for (var t = m; t < N - m; t += 2) ring.AddRange(new[] { m * N + t, (N - 1 - m) * N + t, t * N + m, t * N + (N - 1 - m) });
            var med = new[] { r, g, b }.Select(ch => { var v = ring.Select(i => ch[i]).OrderBy(x => x).ToList(); return v[v.Count / 2]; }).ToArray();
            var near = ring.Count(i => a[i] > 200 && Dist(i, med) < 38);
            if ((double)near / ring.Count > 0.7) plate = med; else picture = true;
        }

        // its brand's colour
        double[]? brand = null;
        if (plate != null)
        {
            var (_, s, l) = Hsl(plate);
            if (s >= 0.2 && l <= 0.88 && l >= 0.1) brand = plate;
        }
        if (brand == null)
        {
            var bins = new double[36, 4];
            double seen = 0, sr = 0, sg = 0, sb = 0;
            for (var k = 0; k < count; k++)
            {
                if (a[k] < 160) continue;
                if (plate != null && Dist(k, plate) < 60) continue;
                var c = new[] { r[k], g[k], b[k] };
                seen++; sr += c[0]; sg += c[1]; sb += c[2];
                var (h, s, l) = Hsl(c);
                if (s < 0.3 || l < 0.1 || l > 0.75) continue;
                var y = Lum(c);
                var w = s * s * (y < 0.5 ? 1 : Math.Max(0.12, 1 - (y - 0.5) * 3));
                var i = (int)(h / 10) % 36;
                bins[i, 0] += w; bins[i, 1] += c[0] * w; bins[i, 2] += c[1] * w; bins[i, 3] += c[2] * w;
            }
            int best = -1;
            double bestWeight = 0;
            for (var i = 0; i < 36; i++)
            {
                var w = bins[(i + 35) % 36, 0] * 0.5 + bins[i, 0] + bins[(i + 1) % 36, 0] * 0.5;
                if (w > bestWeight) { bestWeight = w; best = i; }
            }
            var pick = best < 0 ? -1 : new[] { best, (best + 35) % 36, (best + 1) % 36 }.OrderByDescending(i => bins[i, 0]).First();
            brand = pick >= 0 && bestWeight > seen * 0.02 && bins[pick, 0] > 0
                ? new[] { bins[pick, 1] / bins[pick, 0], bins[pick, 2] / bins[pick, 0], bins[pick, 3] / bins[pick, 0] }
                : new[] { sr / Math.Max(1, seen), sg / Math.Max(1, seen), sb / Math.Max(1, seen) };
        }
        var deep = brand;
        for (var step = 0; step < 14 && Lum(deep) > 0.5; step++) deep = deep.Select(v => v * 0.9).ToArray();
        var hex = "#" + string.Concat(deep.Select(v => ((int)Math.Clamp(Math.Round(v), 0, 255)).ToString("x2")));

        if (picture)
        {
            var whole = BitmapSource.Create(N, N, 96, 96, PixelFormats.Pbgra32, null, p, N * 4);
            whole.Freeze();
            return (new ToolLook("picture", hex, 1, DateTime.UtcNow), whole);
        }

        // the mark, as how much of it is there, pixel by pixel
        var ink = new double[count];
        int gx0 = N, gy0 = N, gx1 = -1, gy1 = -1;
        for (var j = 0; j < N; j++)
        {
            for (var i = 0; i < N; i++)
            {
                var k = j * N + i;
                var v = a[k] / 255;
                if (plate != null) v *= Math.Clamp((Dist(k, plate) - 28) / 60, 0, 1);
                ink[k] = v;
                if (v > 0.12) { gx0 = Math.Min(gx0, i); gy0 = Math.Min(gy0, j); gx1 = Math.Max(gx1, i); gy1 = Math.Max(gy1, j); }
            }
        }
        if (gx1 < 0) return null;
        // (a line of writing set under the mark, after a gap: left out — it runs most of the way across; a part of the mark
        //  under a gap, as Top Hat's square under its T, does not)
        var rows = new List<double>();
        for (var j = gy0; j <= gy1; j++) { double t = 0; for (var i = gx0; i <= gx1; i++) t += ink[j * N + i]; rows.Add(t); }
        var total = rows.Sum();
        var run = 0;
        for (var j = 0; j < rows.Count; j++)
        {
            run = rows[j] < 0.5 ? run + 1 : 0;
            if (run < N * 0.025) continue;
            double below = rows.Skip(j).Sum();
            var under = rows.Count - j;
            int bx0 = N, bx1 = -1;
            for (var jj = gy0 + j; jj <= gy1; jj++)
                for (var i = gx0; i <= gx1; i++)
                    if (ink[jj * N + i] > 0.3) { bx0 = Math.Min(bx0, i); bx1 = Math.Max(bx1, i); }
            var wide = bx1 >= bx0 && bx1 - bx0 + 1 >= (gx1 - gx0 + 1) * 0.6;
            if (wide && below > 0 && below < total * 0.25 && under < rows.Count * 0.32) { gy1 = gy0 + j - run; break; }
        }
        int gw = gx1 - gx0 + 1, gh = gy1 - gy0 + 1;
        if (gw <= 0 || gh <= 0) return null;
        var o = new byte[gw * gh * 4];
        for (var j = 0; j < gh; j++)
        {
            for (var i = 0; i < gw; i++)
            {
                var v = (byte)Math.Clamp(Math.Round(ink[(j + gy0) * N + i + gx0] * 255), 0, 255);
                var k = (j * gw + i) * 4;
                // (white, its alpha multiplied in)
                o[k] = v; o[k + 1] = v; o[k + 2] = v; o[k + 3] = v;
            }
        }
        var glyph = BitmapSource.Create(gw, gh, 96, 96, PixelFormats.Pbgra32, null, o, gw * 4);
        glyph.Freeze();
        return (new ToolLook("glyph", hex, (double)gw / gh, DateTime.UtcNow), glyph);
    }

    /// <summary>Hue in degrees, saturation and lightness (0…1) of an RGB colour (0…255).</summary>
    public static (double H, double S, double L) Hsl(double[] c)
    {
        double r = c[0] / 255, g = c[1] / 255, b = c[2] / 255;
        double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), l = (mx + mn) / 2, d = mx - mn;
        if (d <= 0) return (0, 0, l);
        var s = d / (1 - Math.Abs(2 * l - 1));
        var h = mx == r ? ((g - b) / d) % 6 : mx == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (((h * 60) + 360) % 360, s, l);
    }

    /// <summary>How light a colour reads (0…1).</summary>
    public static double Lum(double[] c) => (0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]) / 255;
}

/// <summary>How a bubble's badge reads: blue, orange (something due today) or red (something missing).</summary>
internal enum BubbleTone { Plain, Today, Late }

/// <summary>A tool's bubble at a size: its colour, its mark and (<c>named</c>) its name along its lower edge — drawn again
/// as its logo comes.</summary>
internal sealed class ToolOrb : Grid
{
    private readonly string name, host, color;
    private readonly double size;
    private readonly bool named;

    public ToolOrb(string name, string host, string color, double size, bool named = true)
    {
        this.name = name;
        this.host = host ?? "";
        this.color = color;
        this.size = size;
        this.named = named;
        Width = size;
        Height = size;
        if (this.host.Length > 0)
        {
            ToolLogos.Want(this.host);
            Loaded += (_, _) => { ToolLogos.Changed -= OnChanged; ToolLogos.Changed += OnChanged; Draw(); };
            Unloaded += (_, _) => ToolLogos.Changed -= OnChanged;
        }
        Draw();
    }

    private void OnChanged(string k)
    {
        if (k == ToolLogos.Key(host)) Draw();
    }

    private void Draw()
    {
        Children.Clear();
        var look = host.Length > 0 && ToolLogos.Look(host) is { Kind: not "none" } l ? l : null;
        var img = look != null ? ToolLogos.Image(host) : null;
        if (img == null) look = null;
        var c = OrbPaint.Rgb(look?.Brand ?? color, deepen: look == null);
        var fill = new RadialGradientBrush { Center = new Point(0.32, 0.24), GradientOrigin = new Point(0.32, 0.24), RadiusX = 0.8, RadiusY = 0.8 };
        fill.GradientStops.Add(new GradientStop(OrbPaint.Mix(c, Colors.White, 0.26), 0));
        fill.GradientStops.Add(new GradientStop(c, 0.54));
        fill.GradientStops.Add(new GradientStop(OrbPaint.Mix(c, Colors.Black, 0.28), 1));
        fill.Freeze();
        Children.Add(new Ellipse
        {
            Fill = fill,
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.3, BlurRadius = size * 0.14, ShadowDepth = size * 0.04, Direction = 270 },
        });
        // (light on its upper half, as on glass)
        Children.Add(new Ellipse
        {
            Width = size * 0.72, Height = size * 0.42, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, size * 0.06, 0, 0), IsHitTestVisible = false,
            Fill = new LinearGradientBrush(Color.FromArgb(77, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90),
        });
        var lift = new TranslateTransform(0, named ? -size * 0.05 : 0);
        if (look != null && img != null && look.Kind == "picture")
        {
            var side = size * (named ? 0.5 : 0.62);
            Children.Add(new Ellipse
            {
                Width = side, Height = side, RenderTransform = lift, IsHitTestVisible = false,
                Fill = new ImageBrush(img) { Stretch = Stretch.UniformToFill, Viewbox = new Rect(0.045, 0.045, 0.91, 0.91) },
                Stroke = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), StrokeThickness = Math.Max(1.5, size * 0.013),
                Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.25, BlurRadius = 6, ShadowDepth = 1.5, Direction = 270 },
            });
        }
        else if (look != null && img != null)
        {
            double box = size * (named ? 0.42 : 0.5), sq = Math.Sqrt(Math.Clamp(look.Aspect, 0.2, 5));
            var k = Math.Min(1, Math.Min(size * 0.56 / (box * sq), size * 0.44 / (box / sq)));
            var mark = new Image
            {
                Source = img, Width = box * sq * k, Height = box / sq * k, Stretch = Stretch.Fill, RenderTransform = lift, IsHitTestVisible = false,
                Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.22, BlurRadius = 2, ShadowDepth = 1, Direction = 270 },
            };
            RenderOptions.SetBitmapScalingMode(mark, BitmapScalingMode.HighQuality);
            Children.Add(mark);
        }
        else
        {
            Children.Add(new TextBlock
            {
                Text = name.Length > 0 ? name[..1].ToUpperInvariant() : "•", FontSize = size * (named ? 0.36 : 0.44), FontWeight = FontWeights.Bold,
                FontFamily = K.DisplayFace, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = lift, IsHitTestVisible = false,
                Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.22, BlurRadius = 2, ShadowDepth = 1, Direction = 270 },
            });
        }
        if (named) Children.Add(new CurvedName(name, size));
        Children.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)), StrokeThickness = 1, IsHitTestVisible = false });
    }
}

/// <summary>The bubbles' colours, worked out from "#rrggbb".</summary>
internal static class OrbPaint
{
    public static Color Rgb(string? hex, bool deepen)
    {
        var c = K.Hex(hex, Color.FromRgb(0x8e, 0x8e, 0x93));
        if (!deepen) return c;
        double r = c.R, g = c.G, b = c.B;
        for (var step = 0; step < 14 && (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255 > 0.5; step++) { r *= 0.9; g *= 0.9; b *= 0.9; }
        return Color.FromRgb((byte)Math.Round(r), (byte)Math.Round(g), (byte)Math.Round(b));
    }

    /// <summary>A colour moved toward another by a share.</summary>
    public static Color Mix(Color a, Color b, double f) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * f), (byte)Math.Round(a.G + (b.G - a.G) * f), (byte)Math.Round(a.B + (b.B - a.B) * f));
}

/// <summary>A name set along a bubble's lower edge, a letter at a time, reading left to right — each turned to face the
/// middle, the whole centred on the bottom, its size brought down until it fits in 148° of the circle.</summary>
internal sealed class CurvedName : FrameworkElement
{
    private readonly string[] letters;
    private readonly double d;
    private static readonly Typeface Face = new(K.TextFace, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public CurvedName(string text, double diameter)
    {
        var all = new List<string>();
        var e = StringInfo.GetTextElementEnumerator(text ?? "");
        while (e.MoveNext()) all.Add(e.GetTextElement());
        letters = all.ToArray();
        d = diameter;
        Width = d;
        Height = d;
        IsHitTestVisible = false;
        Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.3, BlurRadius = 2, ShadowDepth = 1, Direction = 270 };
    }

    private FormattedText Ft(string s, double size, double dpi) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, size, Brushes.White, dpi);

    protected override void OnRender(DrawingContext dc)
    {
        if (letters.Length == 0) return;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var r = d / 2;
        var size = Math.Min(14.5, Math.Max(10.5, d * 0.096));
        double[] Widths(double s) => letters.Select(ch => Ft(ch, s, dpi).WidthIncludingTrailingWhitespace + s * 0.03).ToArray();
        var widths = Widths(size);
        var baseline = r - d * 0.07 - size * 0.22;
        var room = baseline * 148 * Math.PI / 180;
        if (widths.Sum() > room)
        {
            size = Math.Max(8.5, size * room / widths.Sum());
            widths = Widths(size);
            baseline = r - d * 0.07 - size * 0.22;
        }
        // (spaced by the letters' middles, a little inside the line they stand on)
        var middle = baseline - size * 0.355;
        var sum = widths.Sum();
        double run = 0;
        for (var i = 0; i < letters.Length; i++)
        {
            var w = widths[i];
            var angle = (run + w / 2 - sum / 2) / middle;
            var ft = Ft(letters[i], size, dpi);
            dc.PushTransform(new TranslateTransform(r + baseline * Math.Sin(angle), r + baseline * Math.Cos(angle)));
            dc.PushTransform(new RotateTransform(-angle * 180 / Math.PI));
            dc.DrawText(ft, new Point(-ft.WidthIncludingTrailingWhitespace / 2, -ft.Baseline));
            dc.Pop();
            dc.Pop();
            run += w;
        }
    }
}

/// <summary>Where each bubble goes (the Mac's HoneyPlan): rows of a honeycomb as wide as the page has room for, the cells
/// nearest the middle taken (a row costs more than a step to the side, so they spread across before down), in rings
/// outward — the middle, the six round it, then the next — each ring's bubbles smaller. A narrow page draws it all smaller,
/// down to 62%. Places are within the whole's own box, <see cref="Width"/> by <see cref="Height"/>.</summary>
internal sealed class HoneyPlan
{
    public sealed record Cell(double X, double Y, double Size);

    public List<Cell> Cells { get; } = new();
    public double Width { get; private set; }
    public double Height { get; private set; }

    private const double Bubble = 152, StepX = 182, StepY = 160, Pad = 16;

    private static double Scale(double w) => Math.Max(0.62, Math.Min(1, w / (StepX * 2 + Bubble + 24)));

    private static int Across(double w)
    {
        var s = Scale(w);
        return (int)(Math.Max(0, (w - Bubble * s - 16) / 2) / (StepX * s));
    }

    /// <summary>What the plan depends on in the page's width: built again only when it changes.</summary>
    public static (int, int) KeyOf(double w) => (Across(w), (int)Math.Round(Scale(w) * 20));

    public static HoneyPlan Make(int n, double w)
    {
        var plan = new HoneyPlan();
        if (n <= 0 || w <= 0) return plan;
        var s = Scale(w);
        double d = Bubble * s, px = StepX * s, py = StepY * s;
        var half = Math.Max(0, (w - d - 16) / 2);
        var across = (int)(half / px);
        var rowsEach = Math.Max(2, n / Math.Max(1, across * 2 + 1) + 2);
        var spots = new List<(double X, double Y, double Cost)>();
        for (var row = -rowsEach; row <= rowsEach; row++)
        {
            var odd = row % 2 != 0;
            for (var q = -(across + 1); q <= across + 1; q++)
            {
                var x = px * (q + (odd ? 0.5 : 0));
                if (Math.Abs(x) > half + 0.5) continue;
                var y = py * row;
                spots.Add((x, y, Math.Sqrt((x / px) * (x / px) + (y / py * 1.6) * (y / py * 1.6))));
            }
        }
        double[] sizes = { 1, 0.86, 0.74, 0.66, 0.6 };
        var chosen = spots.OrderBy(p => p.Cost).ThenBy(p => p.Y).ThenBy(p => p.X).Take(n)
            .Select(p =>
            {
                var ring = (int)Math.Round(Math.Sqrt(p.X * p.X + p.Y * p.Y) / px, MidpointRounding.AwayFromZero);
                return (p.X, p.Y, Ring: ring, Angle: Math.Atan2(p.Y, p.X), Size: d * sizes[Math.Min(ring, sizes.Length - 1)]);
            })
            .OrderBy(p => p.Ring).ThenBy(p => p.Angle).ToList();
        var left = chosen.Min(p => p.X - p.Size / 2);
        var right = chosen.Max(p => p.X + p.Size / 2);
        var top = chosen.Min(p => p.Y - p.Size / 2);
        var bottom = chosen.Max(p => p.Y + p.Size / 2);
        foreach (var p in chosen) plan.Cells.Add(new Cell(p.X - left + Pad, p.Y - top + Pad, p.Size));
        plan.Width = right - left + Pad * 2;
        plan.Height = bottom - top + Pad * 2;
        return plan;
    }
}

/// <summary>A bubble that is a button: a touch larger under the pointer or the keyboard, a touch smaller while pressed,
/// ringed in the accent colour while it is lit (its list open) or has the keyboard.</summary>
internal sealed class BubbleButton : Button
{
    private static readonly ControlTemplate Bare = MakeBare();
    private readonly ScaleTransform scale = new(1, 1);
    private readonly Ellipse ring;
    private bool lit;

    private static ControlTemplate MakeBare()
    {
        var t = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
        t.Seal();
        return t;
    }

    public BubbleButton(FrameworkElement orb, FrameworkElement? badge, double size, Action click)
    {
        Template = Bare;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        var g = new Grid { Width = size, Height = size };
        ring = new Ellipse { Margin = new Thickness(-6), StrokeThickness = 2.5, Opacity = 0, IsHitTestVisible = false };
        ring.SetResourceReference(Shape.StrokeProperty, B.Accent);
        g.Children.Add(orb);
        g.Children.Add(ring);
        if (badge != null)
        {
            badge.HorizontalAlignment = HorizontalAlignment.Right;
            badge.VerticalAlignment = VerticalAlignment.Top;
            badge.Margin = size < 110 ? new Thickness(0, -3, -4, 0) : new Thickness(0, -2, -6, 0);
            g.Children.Add(badge);
        }
        Content = g;
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = scale;
        Click += (_, e) => { e.Handled = true; click(); };
        MouseEnter += (_, _) => Update();
        MouseLeave += (_, _) => Update();
        IsKeyboardFocusedChanged += (_, _) => Update();
    }

    /// <summary>Its list is open.</summary>
    public bool Lit
    {
        get => lit;
        set { lit = value; Update(); }
    }

    protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsPressedChanged(e);
        Update();
    }

    private void Update()
    {
        var up = IsMouseOver || IsKeyboardFocused || lit;
        ring.Opacity = up ? 0.9 : 0;
        var to = IsPressed ? 0.96 : up ? 1.04 : 1;
        Motion.To(scale, ScaleTransform.ScaleXProperty, to, IsPressed ? Motion.Fast : Motion.Normal, Motion.Decelerate);
        Motion.To(scale, ScaleTransform.ScaleYProperty, to, IsPressed ? Motion.Fast : Motion.Normal, Motion.Decelerate);
    }

    /// <summary>A bubble's count: white on blue, orange or red, ringed in the page's colour.</summary>
    public static FrameworkElement Badge(int count, BubbleTone tone)
    {
        var color = tone switch
        {
            BubbleTone.Late => Color.FromRgb(0xE8, 0x11, 0x23),
            BubbleTone.Today => Color.FromRgb(0xF7, 0x63, 0x0C),
            _ => Appearance.Accent,
        };
        var text = new TextBlock
        {
            Text = count.ToString(CultureInfo.CurrentCulture), FontSize = 12.5, FontWeight = FontWeights.SemiBold, FontFamily = K.TextFace,
            Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 1),
        };
        var b = new Border
        {
            Child = text, MinWidth = 29, Height = 29, CornerRadius = new CornerRadius(14.5), Padding = new Thickness(6, 0, 6, 0),
            Background = new SolidColorBrush(color), BorderThickness = new Thickness(2.5), IsHitTestVisible = false,
        };
        b.SetResourceReference(Border.BorderBrushProperty, B.Window);
        return b;
    }
}
