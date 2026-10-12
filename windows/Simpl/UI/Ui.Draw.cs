using System.Numerics;
using Simpl.Graphics;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Simpl.UI;

public sealed partial class Ui
{
    private Gpu? gpu;
    private GlassRenderer? glass;
    private ID2D1SolidColorBrush? brush;
    private readonly Dictionary<Font, IDWriteTextFormat> formats = new();
    private readonly Dictionary<(string, Font, int, int, int, bool), (IDWriteTextLayout Layout, long Used)> layouts = new();
    private string? iconFace;
    private IDWriteInlineObject? ellipsis;
    private readonly Dictionary<string, ID2D1Bitmap1?> images = new();
    public string? GlassError { get; private set; }

    /// <summary>The window's graphics, made (or made again: a lost device).</summary>
    public void Attach(Gpu g)
    {
        Detach();
        gpu = g;
        brush = g.D2d.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        try { glass = new GlassRenderer(g.Device); GlassError = null; }
        catch (Exception e) { glass = null; GlassError = e.Message; }
        glass?.Resize(g.Width, g.Height);
        using var fonts = g.DWrite.GetSystemFontCollection(false);
        iconFace = fonts.FindFamilyName("Segoe Fluent Icons", out _) ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    }

    public void Detach()
    {
        foreach (var l in layouts.Values) l.Layout.Dispose();
        layouts.Clear();
        foreach (var b in images.Values) b?.Dispose();
        images.Clear();
        foreach (var f in formats.Values) f.Dispose();
        formats.Clear();
        ellipsis?.Dispose(); ellipsis = null;
        brush?.Dispose(); brush = null;
        glass?.Dispose(); glass = null;
        gpu = null;
    }

    public void Resized() { if (gpu != null) glass?.Resize(gpu.Width, gpu.Height); }

    private ID2D1DeviceContext D => gpu!.D2d;

    private ID2D1SolidColorBrush Brush(Color4 c)
    {
        brush!.Color = c;
        return brush;
    }

    // ---- shapes ----

    public void Fill(R r, Color4 c) => Draw(() => D.FillRectangle(r.Raw, Brush(c)));

    /// <summary>A rounded rectangle as the glass draws its shapes: a capsule round, a card's corners a soft squircle's.</summary>
    public void Fill(R r, float radius, Color4 c)
    {
        if (radius <= 0) { Fill(r, c); return; }
        Draw(() =>
        {
            using var geo = Shape(r, radius);
            D.FillGeometry(geo, Brush(c));
        });
    }

    public void Stroke(R r, float radius, Color4 c, float width = 1)
    {
        Draw(() =>
        {
            using var geo = Shape(r.Inset(width / 2), Math.Max(0, radius - width / 2));
            D.DrawGeometry(geo, Brush(c), width);
        });
    }

    public void Circle(float cx, float cy, float radius, Color4 c) =>
        Draw(() => D.FillEllipse(new Ellipse(new Vector2(cx, cy), radius, radius), Brush(c)));

    public void Ring(float cx, float cy, float radius, Color4 c, float width) =>
        Draw(() => D.DrawEllipse(new Ellipse(new Vector2(cx, cy), radius, radius), Brush(c), width));

    /// <summary>A soft glow of colour fading out from its centre (a radial gradient).</summary>
    public void Glow(float cx, float cy, float radius, Color4 c)
    {
        Draw(() =>
        {
            var stops = new[] { new GradientStop { Position = 0, Color = c }, new GradientStop { Position = 1, Color = new Color4(c.R, c.G, c.B, 0) } };
            using var coll = D.CreateGradientStopCollection(stops);
            using var b = D.CreateRadialGradientBrush(new RadialGradientBrushProperties { Center = new Vector2(cx, cy), RadiusX = radius, RadiusY = radius }, coll);
            D.FillEllipse(new Ellipse(new Vector2(cx, cy), radius, radius), b);
        });
    }

    /// <summary>A rectangle fading from one colour at its top to another at its bottom (a scroll edge under the toolbar, a hero's shade).</summary>
    public void FillGradientV(R r, Color4 top, Color4 bottom)
    {
        Draw(() =>
        {
            var stops = new[] { new GradientStop { Position = 0, Color = top }, new GradientStop { Position = 1, Color = bottom } };
            using var coll = D.CreateGradientStopCollection(stops);
            using var b = D.CreateLinearGradientBrush(new LinearGradientBrushProperties { StartPoint = new Vector2(r.X, r.Y), EndPoint = new Vector2(r.X, r.Bottom) }, coll);
            D.FillRectangle(r.Raw, b);
        });
    }

    /// <summary>A rounded rectangle filled with a gradient running from its top left to its bottom right (a course card's hero).</summary>
    public void FillGradient(R r, float radius, Color4 from, Color4 to, bool topOnly = false)
    {
        Draw(() =>
        {
            var stops = new[] { new GradientStop { Position = 0, Color = from }, new GradientStop { Position = 1, Color = to } };
            using var coll = D.CreateGradientStopCollection(stops);
            using var b = D.CreateLinearGradientBrush(new LinearGradientBrushProperties { StartPoint = new Vector2(r.X, r.Y), EndPoint = new Vector2(r.Right, r.Bottom) }, coll);
            if (topOnly)
            {
                // round at the top, square at the bottom: the shape clipped under a taller one
                using var geo = Shape(new R(r.X, r.Y, r.W, r.H + radius * 2), radius);
                D.PushAxisAlignedClip(r.Raw, AntialiasMode.PerPrimitive);
                D.FillGeometry(geo, b);
                D.PopAxisAlignedClip();
            }
            else
            {
                using var geo = Shape(r, radius);
                D.FillGeometry(geo, b);
            }
        });
    }

    /// <summary>A rectangle rounded at its top corners only (a tower, a hero), filled with a gradient from top to bottom.</summary>
    public void FillTopRounded(R r, float radius, Color4 top, Color4 bottom)
    {
        Draw(() =>
        {
            var stops = new[] { new GradientStop { Position = 0, Color = top }, new GradientStop { Position = 1, Color = bottom } };
            using var coll = D.CreateGradientStopCollection(stops);
            using var b = D.CreateLinearGradientBrush(new LinearGradientBrushProperties { StartPoint = new Vector2(r.X, r.Y), EndPoint = new Vector2(r.X, r.Bottom) }, coll);
            var rad = Math.Min(radius, Math.Min(r.W / 2, r.H));
            using var geo = gpu!.D2dFactory.CreateRoundedRectangleGeometry(new RoundedRectangle { Rect = new RawRectF(r.X, r.Y, r.Right, r.Bottom + rad * 2), RadiusX = rad, RadiusY = rad });
            D.PushAxisAlignedClip(r.Raw, AntialiasMode.PerPrimitive);
            D.FillGeometry(geo, b);
            D.PopAxisAlignedClip();
        });
    }

    /// <summary>A dashed outline of a rectangle rounded at its top corners (a tower with no score yet).</summary>
    public void DashedTopRounded(R r, float radius, Color4 c, float width)
    {
        Draw(() =>
        {
            var rad = Math.Min(radius, Math.Min(r.W / 2, r.H));
            using var geo = gpu!.D2dFactory.CreateRoundedRectangleGeometry(new RoundedRectangle { Rect = new RawRectF(r.X + width / 2, r.Y + width / 2, r.Right - width / 2, r.Bottom + rad * 2), RadiusX = rad, RadiusY = rad });
            using var style = gpu!.D2dFactory.CreateStrokeStyle(new StrokeStyleProperties { DashStyle = DashStyle.Custom, DashCap = CapStyle.Flat }, new[] { 4f / width, 3f / width });
            D.PushAxisAlignedClip(r.Raw, AntialiasMode.PerPrimitive);
            D.DrawGeometry(geo, Brush(c), width, style);
            D.PopAxisAlignedClip();
        });
    }

    public void Line(float x0, float y0, float x1, float y1, Color4 c, float width = 1) =>
        Draw(() => D.DrawLine(new Vector2(x0, y0), new Vector2(x1, y1), Brush(c), width));

    /// <summary>A shield, filled (the SF Symbol lock.shield's): a flat top with rounded corners, straight sides, then a soft point.</summary>
    public void Shield(float cx, float cy, float w, float h, Color4 c)
    {
        Draw(() =>
        {
            using var path = gpu!.D2dFactory.CreatePathGeometry();
            using (var sink = path.Open())
            {
                float left = cx - w / 2, right = cx + w / 2, top = cy - h / 2, bottom = cy + h / 2;
                var r = w * 0.16f;               // the top corners
                var straight = top + h * 0.4f;   // the sides run straight this far down
                var pts = new List<Vector2>();
                for (var i = 0; i <= 6; i++) { var a = MathF.PI + MathF.PI / 2 * i / 6; pts.Add(new Vector2(left + r + r * MathF.Cos(a), top + r + r * MathF.Sin(a))); }
                for (var i = 0; i <= 6; i++) { var a = -MathF.PI / 2 + MathF.PI / 2 * i / 6; pts.Add(new Vector2(right - r + r * MathF.Cos(a), top + r + r * MathF.Sin(a))); }
                pts.Add(new Vector2(right, straight));
                for (var i = 1; i <= 12; i++) pts.Add(Quad(new Vector2(right, straight), new Vector2(right, bottom - h * 0.08f), new Vector2(cx, bottom), i / 12f));
                for (var i = 1; i <= 12; i++) pts.Add(Quad(new Vector2(cx, bottom), new Vector2(left, bottom - h * 0.08f), new Vector2(left, straight), i / 12f));
                sink.BeginFigure(pts[0], FigureBegin.Filled);
                for (var i = 1; i < pts.Count; i++) sink.AddLine(pts[i]);
                sink.EndFigure(FigureEnd.Closed);
                sink.Close();
            }
            D.FillGeometry(path, Brush(c));
        });
    }

    private static Vector2 Quad(Vector2 a, Vector2 b, Vector2 c, float t) => (1 - t) * (1 - t) * a + 2 * (1 - t) * t * b + t * t * c;

    /// <summary>A picture from a file (the app's icon), fitted into a rectangle; read once, kept with the graphics.</summary>
    public void Image(string path, R r, float opacity = 1)
    {
        if (gpu == null) return;
        if (!images.TryGetValue(path, out var bmp))
        {
            try
            {
                using var wic = new Vortice.WIC.IWICImagingFactory();
                using var decoder = wic.CreateDecoderFromFileName(path, System.IO.FileAccess.Read, Vortice.WIC.DecodeOptions.CacheOnLoad);
                using var frame = decoder.GetFrame(0);
                using var converter = wic.CreateFormatConverter();
                converter.Initialize(frame, Vortice.WIC.PixelFormat.Format32bppPBGRA).CheckError();
                bmp = D.CreateBitmapFromWicBitmap(converter, (BitmapProperties1?)null);
            }
            catch (Exception e) { Support.Log.Write($"image {path}: {e.Message}"); bmp = null; }
            images[path] = bmp;
        }
        if (bmp == null) return;
        var b = bmp;
        Draw(() => D.DrawBitmap(b, r.Raw, opacity, BitmapInterpolationMode.Linear, null));
    }

    /// <summary>An arc (a spinner's, a ring's), from an angle through a sweep (radians, clockwise from the top).</summary>
    public void Arc(float cx, float cy, float radius, float from, float sweep, Color4 c, float width)
    {
        Draw(() =>
        {
            using var path = gpu!.D2dFactory.CreatePathGeometry();
            using (var sink = path.Open())
            {
                Vector2 At(float a) => new(cx + radius * MathF.Sin(a), cy - radius * MathF.Cos(a));
                sink.BeginFigure(At(from), FigureBegin.Hollow);
                var steps = Math.Max(6, (int)(MathF.Abs(sweep) * 12));
                for (var i = 1; i <= steps; i++) sink.AddLine(At(from + sweep * i / steps));
                sink.EndFigure(FigureEnd.Open);
                sink.Close();
            }
            using var style = gpu!.D2dFactory.CreateStrokeStyle(new StrokeStyleProperties { StartCap = CapStyle.Round, EndCap = CapStyle.Round, LineJoin = LineJoin.Round });
            D.DrawGeometry(path, Brush(c), width, style);
        });
    }

    /// <summary>The geometry of a rounded rectangle (GlassStyle's corners), DIPs.</summary>
    private ID2D1Geometry Shape(R r, float radius)
    {
        var hw = r.W / 2;
        var hh = r.H / 2;
        var R0 = radius;
        var degree = GlassStyle.Corners(hw, hh, ref R0);
        if (degree == 0)
            return gpu!.D2dFactory.CreateRoundedRectangleGeometry(new RoundedRectangle { Rect = r.Raw, RadiusX = R0, RadiusY = R0 });
        var e = 2 / degree;
        var path = gpu!.D2dFactory.CreatePathGeometry();
        using var sink = path.Open();
        (float cx, float cy, float sx, float sy)[] cs =
        {
            (r.Right - R0, r.Y + R0, 1, -1), (r.Right - R0, r.Bottom - R0, 1, 1), (r.X + R0, r.Bottom - R0, -1, 1), (r.X + R0, r.Y + R0, -1, -1),
        };
        const int steps = 18;
        var begun = false;
        for (var c = 0; c < 4; c++)
        {
            for (var i = 0; i <= steps; i++)
            {
                var t = (float)i / steps * MathF.PI / 2;
                var a = c % 2 == 0 ? MathF.PI / 2 - t : t;
                var ux = MathF.Pow(MathF.Max(MathF.Cos(a), 0), e);
                var uy = MathF.Pow(MathF.Max(MathF.Sin(a), 0), e);
                var pt = new Vector2(cs[c].cx + cs[c].sx * R0 * ux, cs[c].cy + cs[c].sy * R0 * uy);
                if (!begun) { sink.BeginFigure(pt, FigureBegin.Filled); begun = true; }
                else sink.AddLine(pt);
            }
        }
        sink.EndFigure(FigureEnd.Closed);
        sink.Close();
        return path;
    }

    // ---- words ----

    private IDWriteTextFormat Format(Font f)
    {
        if (!formats.TryGetValue(f, out var fmt))
        {
            fmt = gpu!.DWrite.CreateTextFormat(f.Face, f.Weight, Vortice.DirectWrite.FontStyle.Normal, FontStretch.Normal, f.Size);
            formats[f] = fmt;
        }
        return fmt;
    }

    /// <summary>The words laid out (kept a while: the same words are laid out once).</summary>
    private IDWriteTextLayout Layout(string s, Font f, float maxW, TextAlignment align, int lines, bool strike = false)
    {
        var key = (s, f, maxW >= 100000 ? -1 : (int)MathF.Round(maxW * 4), (int)align, lines, strike);
        if (layouts.TryGetValue(key, out var e)) { layouts[key] = (e.Layout, frame); return e.Layout; }
        var w = maxW >= 100000 ? 100000 : Math.Max(1, maxW);
        var lineH = f.Size * 1.34f;
        var layout = gpu!.DWrite.CreateTextLayout(s, Format(f), w, lines > 0 ? lineH * lines + 1 : 100000);
        layout.TextAlignment = align;
        layout.WordWrapping = lines == 1 ? WordWrapping.NoWrap : WordWrapping.Wrap;
        if (strike) layout.SetStrikethrough(true, new TextRange(0, (uint)s.Length));
        if (maxW < 100000)
        {
            ellipsis ??= gpu.DWrite.CreateEllipsisTrimmingSign(Format(f));
            layout.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Character }, ellipsis);
        }
        layouts[key] = (layout, frame);
        return layout;
    }

    private void PurgeText()
    {
        foreach (var k in layouts.Where(p => p.Value.Used < frame - 300).Select(p => p.Key).ToList())
        {
            layouts[k].Layout.Dispose();
            layouts.Remove(k);
        }
    }

    /// <summary>How big the words are laid out (one line, or wrapped to maxW over at most lines; 0: as many as it takes).</summary>
    public Vector2 Measure(string s, Font f, float maxW = 100000, int lines = 1)
    {
        if (gpu == null || string.IsNullOrEmpty(s)) return new Vector2(0, f.Size * 1.34f);
        var m = Layout(s, f, maxW, TextAlignment.Leading, lines).Metrics;
        return new Vector2(Math.Min(m.WidthIncludingTrailingWhitespace, maxW), Math.Max(m.Height, f.Size * 1.2f));
    }

    /// <summary>Words at (x, y), their top; trimmed with … past maxW; returns their height.</summary>
    public float Text(string s, float x, float y, Font f, Color4 c, float maxW = 100000, TextAlignment align = TextAlignment.Leading, int lines = 1, bool strike = false)
    {
        if (gpu == null || string.IsNullOrEmpty(s)) return f.Size * 1.34f;
        var layout = Layout(s, f, maxW, align, lines, strike);
        Draw(() => D.DrawTextLayout(new Vector2(x, y), layout, Brush(c), DrawTextOptions.Clip));
        return Math.Max(layout.Metrics.Height, f.Size * 1.2f);
    }

    /// <summary>Words centred in a rectangle (one line).</summary>
    public void TextIn(string s, R r, Font f, Color4 c, TextAlignment align = TextAlignment.Center)
    {
        var h = Measure(s, f).Y;
        Text(s, r.X, r.Y + (r.H - h) / 2, f, c, r.W, align);
    }

    /// <summary>An icon (an SF Symbol's name, as Segoe Fluent Icons draws it) centred on (cx, cy).</summary>
    public void Icon(string symbol, float cx, float cy, float size, Color4 c, FontWeight weight = FontWeight.Regular) => Glyph(Components.Glyphs.Of(symbol), cx, cy, size, c, weight);

    /// <summary>An icon by its glyph (Segoe Fluent Icons) centred on (cx, cy). A heavier weight is the face's own if it has one, else drawn a little heavier.</summary>
    public void Glyph(string g, float cx, float cy, float size, Color4 c, FontWeight weight = FontWeight.Regular)
    {
        if (g.Length == 0 || iconFace == null) { Circle(cx, cy, size * 0.16f, c); return; }
        var f = new Font(iconFace, size, weight >= FontWeight.SemiBold ? FontWeight.SemiBold : FontWeight.Regular);
        var layout = Layout(g, f, size * 2, TextAlignment.Center, 1);
        var m = layout.Metrics;
        Draw(() => D.DrawTextLayout(new Vector2(cx - size, cy - m.Height / 2), layout, Brush(c)));
    }

    // ---- a frame ----

    /// <summary>The frame: each layer's glass over what is under it, then what is drawn on it.</summary>
    public void Render(Color4 ground)
    {
        if (gpu == null) return;
        var g = gpu;
        var d = g.D2d;
        d.Target = g.BackBitmap;
        d.BeginDraw();
        d.Transform = Matrix3x2.Identity;
        d.Clear(ground);
        foreach (var (_, ops) in layers)
        {
            if (ops.Glass.Count > 0 && glass != null)
            {
                d.EndDraw();
                g.Snapshot();
                ID3D11ShaderResourceViewOrNull frost = default;
                if (ops.Glass.Any(x => x.Frosted)) frost = new(glass.Frost(g.Ctx, g.CompositeSrv!, g.Width, g.Height, GlassStyle.FrostSigma * Scale));
                foreach (var (look, frosted) in ops.Glass)
                    glass.Draw(g.Ctx, g.BackRtv!, g.Width, g.Height, Scale, g.CompositeSrv!, frosted ? frost.View : null, look);
                d.BeginDraw();
            }
            RawRectF? clip = null;
            foreach (var (c, act) in ops.Draws)
            {
                if (!Equals(c, clip))
                {
                    if (clip != null) d.PopAxisAlignedClip();
                    if (c != null) d.PushAxisAlignedClip(c.Value, AntialiasMode.Aliased);
                    clip = c;
                }
                act();
            }
            if (clip != null) d.PopAxisAlignedClip();
        }
        d.EndDraw();
    }

    private readonly record struct ID3D11ShaderResourceViewOrNull(Vortice.Direct3D11.ID3D11ShaderResourceView? View);
}
