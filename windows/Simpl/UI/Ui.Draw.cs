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
    private readonly Dictionary<(string, Font, int, int, int), (IDWriteTextLayout Layout, long Used)> layouts = new();
    private string? iconFace;
    private IDWriteInlineObject? ellipsis;
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

    public void Line(float x0, float y0, float x1, float y1, Color4 c, float width = 1) =>
        Draw(() => D.DrawLine(new Vector2(x0, y0), new Vector2(x1, y1), Brush(c), width));

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
    private IDWriteTextLayout Layout(string s, Font f, float maxW, TextAlignment align, int lines)
    {
        var key = (s, f, maxW >= 100000 ? -1 : (int)MathF.Round(maxW * 4), (int)align, lines);
        if (layouts.TryGetValue(key, out var e)) { layouts[key] = (e.Layout, frame); return e.Layout; }
        var w = maxW >= 100000 ? 100000 : Math.Max(1, maxW);
        var lineH = f.Size * 1.34f;
        var layout = gpu!.DWrite.CreateTextLayout(s, Format(f), w, lines > 0 ? lineH * lines + 1 : 100000);
        layout.TextAlignment = align;
        layout.WordWrapping = lines == 1 ? WordWrapping.NoWrap : WordWrapping.Wrap;
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
    public float Text(string s, float x, float y, Font f, Color4 c, float maxW = 100000, TextAlignment align = TextAlignment.Leading, int lines = 1)
    {
        if (gpu == null || string.IsNullOrEmpty(s)) return f.Size * 1.34f;
        var layout = Layout(s, f, maxW, align, lines);
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
    public void Icon(string symbol, float cx, float cy, float size, Color4 c) => Glyph(Components.Glyphs.Of(symbol), cx, cy, size, c);

    /// <summary>An icon by its glyph (Segoe Fluent Icons) centred on (cx, cy).</summary>
    public void Glyph(string g, float cx, float cy, float size, Color4 c)
    {
        if (g.Length == 0 || iconFace == null) { Circle(cx, cy, size * 0.16f, c); return; }
        var f = new Font(iconFace, size);
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
