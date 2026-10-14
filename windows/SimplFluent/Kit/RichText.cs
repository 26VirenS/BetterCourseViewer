using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Simpl.Kit;

/// <summary>
/// Canvas's own rich text inside a native screen (an announcement, a discussion, an assignment's instructions, a page;
/// the Mac's RichText): the HTML the page has already cleaned, drawn as Windows text — paragraphs, headings, lists,
/// quotes, tables, code, pictures at the column's width, links that go through the app — in the theme's colours, its
/// height as tall as what it holds, the page's scroll wheel passing over it. Pictures come with the school's session.
/// </summary>
public sealed class RichText : RichTextBox
{
    private readonly Action<string>? onLink;
    private readonly Uri? baseUrl;
    private static readonly HttpClient Http = new(new HttpClientHandler { UseCookies = false, AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(20) };
    /// <summary>The school's session for pictures (the engine's cookies), set once the engine is up.</summary>
    public static Func<Uri, Task<string?>>? Cookies;

    public RichText(string html, Action<string>? onLink = null, Uri? baseUrl = null, double size = 14)
    {
        this.onLink = onLink;
        this.baseUrl = baseUrl;
        Style = new Style(typeof(RichTextBox));
        IsReadOnly = true;
        IsDocumentEnabled = true;
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        Padding = new Thickness(0);
        Margin = new Thickness(-4, 0, -4, 0);
        FontFamily = K.TextFace;
        FontSize = size;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        SetResourceReference(ForegroundProperty, B.Text);
        SetResourceReference(SelectionBrushProperty, B.Accent);
        Cursor = Cursors.Arrow;
        FocusVisualStyle = null;
        Document = Build(html, size);
        // (the wheel is the page's: what this holds is as tall as it is)
        PreviewMouseWheel += (_, e) =>
        {
            e.Handled = true;
            var up = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = this };
            (Parent as UIElement ?? VisualTreeHelper.GetParent(this) as UIElement)?.RaiseEvent(up);
        };
    }

    // ---- HTML into a document ----

    private sealed class Node
    {
        public string Tag = "";           // "" for text
        public string Text = "";
        public Dictionary<string, string> Attrs = new(StringComparer.OrdinalIgnoreCase);
        public List<Node> Kids = new();
        public Node? Parent;
    }

    private static readonly HashSet<string> Void = new(StringComparer.OrdinalIgnoreCase) { "br", "img", "hr", "input", "meta", "link", "source", "col", "wbr", "area", "base", "embed", "param", "track" };
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase) { "script", "style", "head", "title", "noscript", "template", "svg", "button", "form", "select", "textarea" };
    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "section", "article", "header", "footer", "main", "aside", "nav", "figure", "figcaption", "h1", "h2", "h3", "h4", "h5", "h6",
        "ul", "ol", "li", "blockquote", "pre", "table", "hr", "dl", "dt", "dd", "details", "summary", "center", "address", "iframe", "video", "audio", "object",
    };

    /// <summary>A tolerant parse: unclosed tags close with their parent, stray closings are let go.</summary>
    private static Node Parse(string html)
    {
        var root = new Node { Tag = "#root" };
        var cur = root;
        var i = 0;
        while (i < html.Length)
        {
            var lt = html.IndexOf('<', i);
            if (lt < 0) { AddText(cur, html[i..]); break; }
            if (lt > i) AddText(cur, html[i..lt]);
            if (html.AsSpan(lt).StartsWith("<!--"))
            {
                var end = html.IndexOf("-->", lt + 4, StringComparison.Ordinal);
                i = end < 0 ? html.Length : end + 3;
                continue;
            }
            var gt = html.IndexOf('>', lt + 1);
            if (gt < 0) { AddText(cur, html[lt..]); break; }
            var inner = html[(lt + 1)..gt].Trim();
            i = gt + 1;
            if (inner.Length == 0 || inner[0] == '!' || inner[0] == '?') continue;
            if (inner[0] == '/')
            {
                var name = inner[1..].Trim().Split(' ')[0].ToLowerInvariant();
                for (var n = cur; n != null && n != root; n = n.Parent)
                    if (n.Tag == name) { cur = n.Parent ?? root; break; }
                continue;
            }
            var selfClose = inner.EndsWith('/');
            if (selfClose) inner = inner[..^1];
            var sp = inner.IndexOfAny(new[] { ' ', '\n', '\t', '\r' });
            var tag = (sp < 0 ? inner : inner[..sp]).ToLowerInvariant();
            var node = new Node { Tag = tag, Parent = cur };
            if (sp > 0) ParseAttrs(inner[sp..], node.Attrs);
            if (Skip.Contains(tag))
            {
                // its content left out entirely
                var close = html.IndexOf("</" + tag, i, StringComparison.OrdinalIgnoreCase);
                if (close >= 0) { var g2 = html.IndexOf('>', close); i = g2 < 0 ? html.Length : g2 + 1; }
                continue;
            }
            // a paragraph or list item closes the one still open before it
            if (tag is "p" or "li" or "dt" or "dd" or "tr" or "td" or "th")
                for (var n = cur; n != null && n != root; n = n.Parent)
                {
                    if (n.Tag == tag || (tag == "li" && n.Tag is "ul" or "ol") || (tag is "td" or "th" && n.Tag == "tr") || (tag == "tr" && n.Tag is "table" or "tbody" or "thead")) { if (n.Tag == tag) { cur = n.Parent ?? root; node.Parent = cur; } break; }
                }
            cur.Kids.Add(node);
            if (!selfClose && !Void.Contains(tag)) cur = node;
        }
        return root;
    }

    private static void AddText(Node cur, string raw)
    {
        if (raw.Length == 0) return;
        cur.Kids.Add(new Node { Text = WebUtility.HtmlDecode(raw), Parent = cur });
    }

    private static void ParseAttrs(string s, Dictionary<string, string> attrs)
    {
        var i = 0;
        while (i < s.Length)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            var start = i;
            while (i < s.Length && s[i] != '=' && !char.IsWhiteSpace(s[i])) i++;
            if (i <= start) { i++; continue; }
            var name = s[start..i];
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i < s.Length && s[i] == '=')
            {
                i++;
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                string value;
                if (i < s.Length && (s[i] == '"' || s[i] == '\''))
                {
                    var q = s[i++];
                    var end = s.IndexOf(q, i);
                    if (end < 0) end = s.Length;
                    value = s[i..end];
                    i = end + 1;
                }
                else
                {
                    var vs = i;
                    while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;
                    value = s[vs..i];
                }
                attrs[name] = WebUtility.HtmlDecode(value);
            }
            else attrs[name] = "";
        }
    }

    private FlowDocument Build(string html, double size)
    {
        var doc = new FlowDocument { PagePadding = new Thickness(4, 0, 4, 0), FontFamily = K.TextFace, FontSize = size, LineHeight = size * 1.45, TextAlignment = TextAlignment.Left };
        doc.SetResourceReference(FlowDocument.ForegroundProperty, B.Text);
        var root = Parse(html ?? "");
        Blocks(root, doc.Blocks, size);
        // (no gap above the first block nor below the last)
        if (doc.Blocks.FirstBlock is { } first) first.Margin = new Thickness(first.Margin.Left, 0, first.Margin.Right, first.Margin.Bottom);
        if (doc.Blocks.LastBlock is { } last) last.Margin = new Thickness(last.Margin.Left, last.Margin.Top, last.Margin.Right, 0);
        return doc;
    }

    /// <summary>A node's children as blocks: runs of inline content gathered into paragraphs.</summary>
    private void Blocks(Node parent, BlockCollection into, double size)
    {
        Paragraph? open = null;
        foreach (var n in parent.Kids)
        {
            if (n.Tag.Length > 0 && BlockTags.Contains(n.Tag) || n.Tag == "img" && IsLargeImage(n))
            {
                open = null;
                if (Block(n, size) is { } b) into.Add(b);
                continue;
            }
            if (n.Tag.Length == 0 && open == null && string.IsNullOrWhiteSpace(n.Text)) continue;
            open ??= NewParagraph();
            if (open.Inlines.Count == 0 || !into.Contains(open)) into.Add(open);
            Inlines(n, open.Inlines, size);
        }
        // (a paragraph that only holds spaces is dropped)
        foreach (var p in into.OfType<Paragraph>().ToList())
            if (!HasWords(p.Inlines) && !p.Inlines.OfType<InlineUIContainer>().Any() && !p.Inlines.OfType<LineBreak>().Any()) into.Remove(p);
    }

    /// <summary>Inline content with words in it (read off its runs: a range's text fails on a list item not yet in a
    /// document).</summary>
    private static bool HasWords(InlineCollection inlines) => inlines.Any(i => i switch
    {
        Run r => !string.IsNullOrWhiteSpace(r.Text),
        Span sp => HasWords(sp.Inlines),
        InlineUIContainer => true,
        _ => false,
    });

    private static Paragraph NewParagraph() => new() { Margin = new Thickness(0, 0, 0, 10) };

    private static bool IsLargeImage(Node n) =>
        !(int.TryParse(n.Attrs.GetValueOrDefault("width"), out var w) && w <= 48) && !(n.Attrs.GetValueOrDefault("class") ?? "").Contains("equation");

    private Block? Block(Node n, double size)
    {
        switch (n.Tag)
        {
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
            {
                var p = new Paragraph { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6), FontFamily = K.DisplayFace };
                p.FontSize = n.Tag switch { "h1" => 24, "h2" => 20, "h3" => 18, "h4" => 16, _ => size };
                p.LineHeight = p.FontSize * 1.3;
                foreach (var k in n.Kids) Inlines(k, p.Inlines, size);
                return p;
            }
            case "ul" or "ol":
            {
                var list = new List { MarkerStyle = n.Tag == "ol" ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc, Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(22, 0, 0, 0) };
                foreach (var k in n.Kids)
                {
                    if (k.Tag != "li") { if (k.Tag.Length > 0 || !string.IsNullOrWhiteSpace(k.Text)) { var li0 = new ListItem(); Blocks(new Node { Kids = { k } }, li0.Blocks, size); if (li0.Blocks.Count > 0) list.ListItems.Add(li0); } continue; }
                    var li = new ListItem { Margin = new Thickness(0, 0, 0, 3) };
                    Blocks(k, li.Blocks, size);
                    foreach (var b in li.Blocks) b.Margin = new Thickness(0, 0, 0, 2);
                    list.ListItems.Add(li);
                }
                return list.ListItems.Count > 0 ? list : null;
            }
            case "blockquote":
            {
                var s = new Section { Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(12, 2, 0, 2), BorderThickness = new Thickness(3, 0, 0, 0) };
                s.SetResourceReference(Section.BorderBrushProperty, B.Accent);
                s.SetResourceReference(Section.ForegroundProperty, B.Text2);
                Blocks(n, s.Blocks, size);
                return s;
            }
            case "pre":
            {
                var p = new Paragraph { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = size - 1, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 10) };
                p.SetResourceReference(Paragraph.BackgroundProperty, B.Subtle);
                p.Inlines.Add(new Run(TextOf(n).TrimEnd('\n')));
                return p;
            }
            case "hr":
            {
                var line = new Border { Height = 1, Margin = new Thickness(0, 6, 0, 6) };
                line.SetResourceReference(Border.BackgroundProperty, B.Divider);
                return new BlockUIContainer(line);
            }
            case "img":
                return Picture(n) is { } img ? new BlockUIContainer(img) { Margin = new Thickness(0, 0, 0, 10) } : null;
            case "iframe" or "video" or "audio" or "object":
            {
                var src = n.Attrs.GetValueOrDefault("src") ?? n.Kids.FirstOrDefault(k => k.Tag == "source")?.Attrs.GetValueOrDefault("src") ?? n.Attrs.GetValueOrDefault("data");
                if (string.IsNullOrEmpty(src)) return null;
                return new BlockUIContainer(Media(n, src)) { Margin = new Thickness(0, 2, 0, 12) };
            }
            case "table":
                return TableOf(n, size);
            default:
            {
                var s = new Section { Margin = new Thickness(0, 0, 0, n.Tag == "p" ? 10 : 4) };
                if (n.Tag is "dd") s.Padding = new Thickness(18, 0, 0, 0);
                if (n.Tag is "dt" or "summary") s.FontWeight = FontWeights.SemiBold;
                if (n.Tag is "center") s.TextAlignment = TextAlignment.Center;
                if (AlignOf(n) is { } align) s.TextAlignment = align;
                Blocks(n, s.Blocks, size);
                if (s.Blocks.Count == 0) return null;
                if (s.Blocks.Count == 1 && s.Blocks.FirstBlock is Paragraph only)
                {
                    s.Blocks.Remove(only);
                    only.Margin = s.Margin;
                    only.FontWeight = s.FontWeight;
                    only.TextAlignment = s.TextAlignment;
                    only.Padding = s.Padding;
                    return only;
                }
                return s;
            }
        }
    }

    private static TextAlignment? AlignOf(Node n)
    {
        var style = (n.Attrs.GetValueOrDefault("style") ?? "").ToLowerInvariant().Replace(" ", "");
        if (style.Contains("text-align:center") || (n.Attrs.GetValueOrDefault("align") ?? "") == "center") return TextAlignment.Center;
        if (style.Contains("text-align:right")) return TextAlignment.Right;
        return null;
    }

    private Block? TableOf(Node n, double size)
    {
        var rows = new List<Node>();
        void Collect(Node x) { foreach (var k in x.Kids) { if (k.Tag == "tr") rows.Add(k); else if (k.Tag is "thead" or "tbody" or "tfoot") Collect(k); } }
        Collect(n);
        if (rows.Count == 0) return null;
        var cols = rows.Max(r => r.Kids.Count(k => k.Tag is "td" or "th"));
        if (cols == 0) return null;
        var t = new System.Windows.Documents.Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 12), BorderThickness = new Thickness(1) };
        t.SetResourceReference(System.Windows.Documents.Table.BorderBrushProperty, B.CardStroke);
        for (var c = 0; c < cols; c++) t.Columns.Add(new TableColumn());
        var group = new TableRowGroup();
        foreach (var r in rows)
        {
            var row = new TableRow();
            foreach (var cell in r.Kids.Where(k => k.Tag is "td" or "th"))
            {
                var tc = new TableCell { Padding = new Thickness(8, 5, 8, 5), BorderThickness = new Thickness(0, 0, 1, 1) };
                tc.SetResourceReference(TableCell.BorderBrushProperty, B.Divider);
                if (cell.Tag == "th") { tc.FontWeight = FontWeights.SemiBold; tc.SetResourceReference(TableCell.BackgroundProperty, B.Subtle); }
                if (int.TryParse(cell.Attrs.GetValueOrDefault("colspan"), out var span) && span > 1) tc.ColumnSpan = span;
                Blocks(cell, tc.Blocks, size);
                foreach (var b in tc.Blocks) b.Margin = new Thickness(0);
                if (tc.Blocks.Count == 0) tc.Blocks.Add(new Paragraph());
                row.Cells.Add(tc);
            }
            group.Rows.Add(row);
        }
        t.RowGroups.Add(group);
        return t;
    }

    private void Inlines(Node n, InlineCollection into, double size)
    {
        if (n.Tag.Length == 0)
        {
            var text = Collapse(n.Text);
            if (text.Length > 0) into.Add(new Run(text));
            return;
        }
        switch (n.Tag)
        {
            case "br": into.Add(new LineBreak()); return;
            case "img":
                if (Picture(n) is { } img) into.Add(new InlineUIContainer(img) { BaselineAlignment = BaselineAlignment.Center });
                return;
            case "a":
            {
                var href = n.Attrs.GetValueOrDefault("href");
                var span = string.IsNullOrEmpty(href) ? (Span)new Span() : Link(href!, null);
                foreach (var k in n.Kids) Inlines(k, span.Inlines, size);
                into.Add(span);
                return;
            }
        }
        Span s = n.Tag switch
        {
            "b" or "strong" => new Bold(),
            "i" or "em" or "cite" or "var" => new Italic(),
            "u" or "ins" => new Underline(),
            _ => new Span(),
        };
        switch (n.Tag)
        {
            case "s" or "strike" or "del": s.TextDecorations = TextDecorations.Strikethrough; break;
            case "code" or "kbd" or "samp" or "tt":
                s.FontFamily = new FontFamily("Cascadia Mono, Consolas");
                s.SetResourceReference(Span.BackgroundProperty, B.Subtle);
                break;
            case "sup": Typography.SetVariants(s, FontVariants.Superscript); break;
            case "sub": Typography.SetVariants(s, FontVariants.Subscript); break;
            case "mark": s.Background = new SolidColorBrush(Color.FromArgb(90, 0xff, 0xd4, 0x26)); break;
            case "small": s.FontSize = size - 2; break;
        }
        var style = (n.Attrs.GetValueOrDefault("style") ?? "").ToLowerInvariant();
        if (style.Contains("font-weight:bold") || style.Contains("font-weight: bold") || style.Contains("font-weight:700") || style.Contains("font-weight: 700")) s.FontWeight = FontWeights.Bold;
        if (style.Contains("font-style:italic") || style.Contains("font-style: italic")) s.FontStyle = FontStyles.Italic;
        if (style.Contains("underline")) s.TextDecorations = TextDecorations.Underline;
        foreach (var k in n.Kids)
        {
            if (k.Tag.Length > 0 && BlockTags.Contains(k.Tag)) { s.Inlines.Add(new LineBreak()); foreach (var kk in k.Kids) Inlines(kk, s.Inlines, size); s.Inlines.Add(new LineBreak()); }
            else Inlines(k, s.Inlines, size);
        }
        into.Add(s);
    }

    private Hyperlink Link(string href, Inline? content)
    {
        var h = content == null ? new Hyperlink() : new Hyperlink(content);
        h.SetResourceReference(Hyperlink.ForegroundProperty, B.AccentText);
        h.TextDecorations = null;
        h.Cursor = Cursors.Hand;
        h.MouseEnter += (_, _) => h.TextDecorations = TextDecorations.Underline;
        h.MouseLeave += (_, _) => h.TextDecorations = null;
        h.ToolTip = href;
        h.Click += (_, _) =>
        {
            if (onLink != null) onLink(Absolute(href));
            else if (Uri.TryCreate(Absolute(href), UriKind.Absolute, out var u)) Engine.Engine.OpenExternally(u);
        };
        return h;
    }

    private string Absolute(string href) => baseUrl != null && Uri.TryCreate(baseUrl, href, out var u) ? u.ToString() : href;

    // ---- a video, a sound, another page set in the text ----

    /// <summary>
    /// A video, a sound or another page set in the text (an iframe — Canvas's media player —, a video, an audio, an
    /// object), drawn as what it is: a video as a 16:9 frame with its name and a play button, a sound or a page as a row
    /// with its name; a click plays or opens it in a window of its own. (A page's frame cannot sit in Windows text: a web
    /// view floats over whatever scrolls past it.) Its name is the frame's own title ("Video player for jump.mp4" →
    /// "jump.mp4"), else its file's, else its site's.
    /// </summary>
    private FrameworkElement Media(Node n, string src)
    {
        var abs = Absolute(src);
        var type = (n.Attrs.GetValueOrDefault("data-media-type") ?? "").ToLowerInvariant();
        var kind = n.Tag == "audio" || type == "audio" || abs.Contains("type=audio", StringComparison.OrdinalIgnoreCase) ? "audio"
            : n.Tag == "video" || type == "video" || abs.Contains("type=video", StringComparison.OrdinalIgnoreCase) || abs.Contains("/media_objects", StringComparison.OrdinalIgnoreCase) ? "video"
            : "page";
        Uri.TryCreate(abs, UriKind.Absolute, out var u);
        var name = MediaName(n, u);
        var word = kind switch { "video" => "Video", "audio" => "Audio", _ => "Embedded page" };
        var elsewhere = u != null && baseUrl != null && !string.Equals(u.Host, baseUrl.Host, StringComparison.OrdinalIgnoreCase) ? u.Host : null;
        var line = elsewhere != null && elsewhere != name ? $"{word} · {elsewhere}" : word;
        var verb = kind == "page" ? "Open" : "Play";
        void Go()
        {
            if (onLink != null) onLink(abs);
            else if (u != null) Engine.Engine.OpenExternally(u);
        }

        System.Windows.Controls.Button b;
        if (kind == "video")
        {
            // a frame of the video's shape, dark as a player is, its play button in the middle, its name at its foot
            var circle = new Grid { Width = 56, Height = 56, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            circle.Children.Add(new System.Windows.Shapes.Ellipse { Fill = K.Brush(Color.FromArgb(235, 255, 255, 255)) });
            var play = K.Icon(Wpf.Ui.Controls.SymbolRegular.Play24, 24, Color.FromRgb(0x1c, 0x1c, 0x1e), filled: true);
            play.Margin = new Thickness(3, 0, 0, 0); // (the triangle's weight sits left of its box: set right by eye)
            circle.Children.Add(play);
            var grow = new ScaleTransform(1, 1);
            circle.RenderTransformOrigin = new Point(0.5, 0.5);
            circle.RenderTransform = grow;
            var title = new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
            var sub = new TextBlock { Text = line, FontSize = 12, Foreground = K.Brush(Color.FromArgb(200, 255, 255, 255)), TextTrimming = TextTrimming.CharacterEllipsis };
            var foot = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16, 0, 16, 12), Children = { title, sub } };
            var frame = new Grid
            {
                MinHeight = 140,
                Background = new LinearGradientBrush(Color.FromRgb(0x2c, 0x2c, 0x30), Color.FromRgb(0x10, 0x10, 0x12), 90),
                Children = { circle, foot },
            };
            frame.SizeChanged += (_, e) =>
            {
                if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) < 0.5) return;
                frame.Height = Math.Round(e.NewSize.Width * 9 / 16);
                frame.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, frame.Height), 8, 8);
            };
            b = K.Subtle(frame, Go, new Thickness(0), 8);
            b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            // (the play button leans in a little under the pointer: quick, and only where Windows' animations are on)
            b.MouseEnter += (_, _) => { if (Motion.On) { Motion.To(grow, ScaleTransform.ScaleXProperty, 1.06, Motion.Normal, Motion.Out); Motion.To(grow, ScaleTransform.ScaleYProperty, 1.06, Motion.Normal, Motion.Out); } };
            b.MouseLeave += (_, _) => { if (Motion.On) { Motion.To(grow, ScaleTransform.ScaleXProperty, 1, Motion.Normal, Motion.Out); Motion.To(grow, ScaleTransform.ScaleYProperty, 1, Motion.Normal, Motion.Out); } };
        }
        else
        {
            // a row: its symbol on a tile, its name over what it is, and Play or Open at its end
            var tile = K.IconTile(kind == "audio" ? Wpf.Ui.Controls.SymbolRegular.MusicNote224 : Wpf.Ui.Controls.SymbolRegular.Globe24, Support.Appearance.Accent, 36);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
            words.Children.Add(K.Text(name, Ty.BodyStrong));
            words.Children.Add(K.Text(line, Ty.Caption, B.Text2));
            var act = K.Text(verb, Ty.Body, B.AccentText).Strong();
            act.VerticalAlignment = VerticalAlignment.Center;
            var row = K.Columns("auto, *, auto", tile.Col(0), words.Col(1), act.Col(2));
            var card = new Border { Child = row, Padding = new Thickness(12, 10, 16, 10), CornerRadius = new CornerRadius(8) }.Bg(B.Card).Stroke(B.CardStroke);
            b = K.Subtle(card, Go, new Thickness(0), 8);
            b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        }
        b.Cursor = Cursors.Hand;
        b.ToolTip = $"{verb} in a window of its own";
        System.Windows.Automation.AutomationProperties.SetName(b, $"{verb} {word.ToLowerInvariant()}: {name}");
        // (as wide as the text's column, up to 560, at its left)
        var holder = new Grid();
        holder.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1000, GridUnitType.Star), MaxWidth = 560 });
        holder.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(b, 0);
        holder.Children.Add(b);
        return holder;
    }

    /// <summary>What a frame set in the text is called: Canvas's player says "Video player for jump.mp4" (→ "jump.mp4");
    /// else the frame's own title, its file's name, or its site.</summary>
    private static string MediaName(Node n, Uri? u)
    {
        var title = (n.Attrs.GetValueOrDefault("title") ?? "").Trim();
        var player = System.Text.RegularExpressions.Regex.Match(title, @"^(?:video|audio|media)\s+player\s+for\s+(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (player.Success) return player.Groups[1].Value.Trim();
        if (title.Length > 0) return title;
        if (u != null)
        {
            var last = Uri.UnescapeDataString(u.Segments.LastOrDefault()?.Trim('/') ?? "");
            if (System.IO.Path.HasExtension(last)) return last;
            return u.Host;
        }
        return "Embedded content";
    }

    private static string Collapse(string s)
    {
        var sb = new StringBuilder(s.Length);
        var space = false;
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c) && c != ' ') { if (!space) sb.Append(' '); space = true; }
            else { sb.Append(c); space = false; }
        }
        return sb.ToString();
    }

    private static string TextOf(Node n)
    {
        if (n.Tag.Length == 0) return n.Text;
        if (n.Tag == "br") return "\n";
        return string.Concat(n.Kids.Select(TextOf));
    }

    /// <summary>A picture at no more than the column's width (its own size where it says), loaded with the school's session.</summary>
    private FrameworkElement? Picture(Node n)
    {
        var src = n.Attrs.GetValueOrDefault("src");
        if (string.IsNullOrEmpty(src)) return null;
        var abs = Absolute(src!);
        var img = new System.Windows.Controls.Image { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left };
        if (double.TryParse(n.Attrs.GetValueOrDefault("width"), out var w) && w > 0) img.MaxWidth = w;
        if (double.TryParse(n.Attrs.GetValueOrDefault("height"), out var h) && h > 0) img.MaxHeight = h;
        img.ToolTip = n.Attrs.GetValueOrDefault("alt") is { Length: > 0 } alt ? alt : null;
        if (abs.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = abs.IndexOf(',');
            if (comma > 0 && abs[..comma].Contains("base64"))
                try { if (Decoding.Picture(Convert.FromBase64String(abs[(comma + 1)..])) is { } p) Decoding.Show(img, p); } catch { return null; }
            return img;
        }
        if (!Uri.TryCreate(abs, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https")) return null;
        _ = Load(img, u);
        return img;
    }

    private static async Task Load(System.Windows.Controls.Image img, Uri u)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, u);
            if (Cookies != null && await Cookies(u) is { Length: > 0 } c) req.Headers.TryAddWithoutValidation("Cookie", c);
            using var res = await Http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return;
            var bytes = await res.Content.ReadAsByteArrayAsync();
            // (decoded off the window's thread, no wider than the screen needs)
            if (await Task.Run(() => Decoding.Picture(bytes)) is { } p) Decoding.Show(img, p);
        }
        catch { /* a picture that will not come: nothing drawn */ }
    }
}
