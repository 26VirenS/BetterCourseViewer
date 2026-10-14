using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using RichTextBox = System.Windows.Controls.RichTextBox;

namespace Simpl.Tools;

/// <summary>
/// What the citation generator and the flashcards share: their models watched while a view is on screen (the Mac's
/// @ObservedObject), text in runs you can select with its italics, a citation put on the clipboard as rich text and plain
/// text, and the buttons and wells their pages are made of.
/// </summary>
internal static class StudyKit
{
    // the Mac's system colours for right, wrong, still learning and starred
    public static Color Green => K.Tone("good");
    public static Color Orange => K.Tone("warn");
    public static Color Red => K.Tone("bad");
    public static Color Yellow => K.Tone("yellow");

    /// <summary>A tool's own colour (the Mac's ToolKind.color).</summary>
    public static Color ToolColor(string key, string fallback) => ToolsCenter.KindOf(key)?.Color ?? K.Hex(fallback);

    /// <summary>
    /// A model's change handled while the view is on screen, let go of once it is off (the Mac's @ObservedObject): on
    /// screen again, the view catches up with what changed meanwhile.
    /// </summary>
    public static void Watch(FrameworkElement view, Action<Action> add, Action<Action> remove, Action changed)
    {
        var first = true;
        view.Loaded += (_, _) =>
        {
            remove(changed);
            add(changed);
            if (!first) changed();
            first = false;
        };
        view.Unloaded += (_, _) => remove(changed);
    }

    /// <summary>A part of a page drawn again in place (a host for what changes).</summary>
    public static ContentControl Host() => new() { Focusable = false, IsTabStop = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };

    /// <summary>What a host shows; with nothing, it takes no room (nor the spacing round it).</summary>
    public static void Put(ContentControl host, UIElement? content)
    {
        host.Content = content;
        host.Visibility = content == null ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- text ----

    /// <summary>Runs of text, some in italics, that you can select (the Mac's CiteBuilder.text with .textSelection).</summary>
    public static RichTextBox Runs(IEnumerable<(string Text, bool Italic)> runs, double size, string brush = B.Text, double? lineHeight = null)
    {
        var p = new Paragraph { Margin = new Thickness(0) };
        foreach (var (text, italic) in runs)
            p.Inlines.Add(new Run(text) { FontStyle = italic ? FontStyles.Italic : FontStyles.Normal });
        var doc = new FlowDocument(p) { PagePadding = new Thickness(4, 0, 4, 0), FontFamily = K.TextFace, FontSize = size, TextAlignment = TextAlignment.Left };
        doc.LineHeight = lineHeight ?? size * 1.4;
        doc.SetResourceReference(FlowDocument.ForegroundProperty, brush);
        var box = new RichTextBox
        {
            Style = new Style(typeof(RichTextBox)), Document = doc, IsReadOnly = true, IsDocumentEnabled = false, BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, Padding = new Thickness(0), Margin = new Thickness(-4, 0, -4, 0), FontFamily = K.TextFace, FontSize = size,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Cursor = Cursors.IBeam, FocusVisualStyle = null, IsTabStop = false,
        };
        box.SetResourceReference(Control.ForegroundProperty, brush);
        box.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.SelectionBrushProperty, B.Accent);
        PassWheel(box);
        return box;
    }

    /// <summary>A line you can select and copy from (the Mac's Text with .textSelection), in the type ramp.</summary>
    public static System.Windows.Controls.TextBox Selectable(string text, Ty ty, string brush = B.Text, bool wrap = true)
    {
        var (size, weight, face, _) = K.Ramp(ty);
        var t = new System.Windows.Controls.TextBox
        {
            Style = new Style(typeof(System.Windows.Controls.TextBox)), Text = text, IsReadOnly = true, BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, Padding = new Thickness(0), Margin = new Thickness(-2, 0, -2, 0), FontSize = size, FontWeight = weight, FontFamily = face,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, IsTabStop = false, FocusVisualStyle = null, Cursor = Cursors.IBeam,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        t.SetResourceReference(Control.ForegroundProperty, brush);
        t.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.SelectionBrushProperty, B.Accent);
        PassWheel(t);
        return t;
    }

    /// <summary>The page's scroll wheel passes over text that is as tall as it is.</summary>
    private static void PassWheel(UIElement e)
    {
        e.PreviewMouseWheel += (_, a) =>
        {
            a.Handled = true;
            var up = new MouseWheelEventArgs(a.MouseDevice, a.Timestamp, a.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = e };
            ((e as FrameworkElement)?.Parent as UIElement ?? VisualTreeHelper.GetParent(e) as UIElement)?.RaiseEvent(up);
        };
    }

    /// <summary>A small strong label over a part (the Mac's tracked small caps; on Windows in sentence case, as every
    /// label is: "MLA 9 · works cited", "Term").</summary>
    public static TextBlock Caps(string text, string brush = B.Text2, double size = 12)
    {
        var words = text.Length == 0 ? text : char.ToUpper(text[0], System.Globalization.CultureInfo.CurrentCulture) + text[1..];
        var t = K.Text(words, Ty.Caption, brush).Strong();
        t.FontSize = size;
        return t;
    }

    /// <summary>A number that does not shake as it changes (a count, a clock).</summary>
    public static TextBlock Figure(string text, Ty ty, Color? color = null, string brush = B.Text)
    {
        var t = K.Text(text, ty, brush);
        if (color is { } c) t.Foreground = new SolidColorBrush(c);
        t.FontFeatures();
        t.VerticalAlignment = VerticalAlignment.Center;
        return t;
    }

    /// <summary>The Mac's EmptyNote: a quiet symbol and a line where something is missing.</summary>
    public static FrameworkElement EmptyNote(string text, SymbolRegular symbol)
    {
        var row = K.H(8, K.Icon(symbol, 16, B.Text3), K.Text(text, Ty.Body, B.Text2));
        row.Margin = new Thickness(0, 12, 0, 12);
        return row;
    }

    /// <summary>The Mac's ContentUnavailableView with its actions: a quiet symbol, what is missing, why, and what to do.</summary>
    public static FrameworkElement Unavailable(SymbolRegular icon, string title, string? description, double minHeight, params UIElement[] actions)
    {
        var v = K.V(12, K.Icon(icon, 44, B.Text3), K.Text(title, Ty.Subtitle).Center());
        if (!string.IsNullOrEmpty(description)) v.Children.Add(K.Wrapped(description!, Ty.Body, B.Text2).Center());
        if (actions.Length > 0)
        {
            var row = K.H(10, actions);
            row.HorizontalAlignment = HorizontalAlignment.Center;
            row.Margin = new Thickness(0, 4, 0, 0);
            v.Children.Add(row);
        }
        v.HorizontalAlignment = HorizontalAlignment.Center;
        v.VerticalAlignment = VerticalAlignment.Center;
        v.MaxWidth = 460;
        return new Grid { MinHeight = minHeight, Children = { v } };
    }

    // ---- buttons ----

    /// <summary>Windows 11's button with only a symbol (the Mac's glass button with an icon).</summary>
    public static Wpf.Ui.Controls.Button IconBtn(SymbolRegular s, Action click, string tip, bool filled = false, Color? tint = null)
    {
        var icon = new SymbolIcon { Symbol = s, Filled = filled, FontSize = 16 };
        if (tint is { } t) icon.Foreground = new SolidColorBrush(t);
        var b = new Wpf.Ui.Controls.Button { Icon = icon, Content = null, Appearance = ControlAppearance.Secondary, Padding = new Thickness(11, 8, 11, 8), MinWidth = 0, ToolTip = tip };
        AutomationProperties.SetName(b, tip);
        b.Click += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    /// <summary>A button filled with a colour that means something (Know in green, Still learning in orange).</summary>
    public static Wpf.Ui.Controls.Button Tinted(string text, Color color, Action click, SymbolRegular? icon = null)
    {
        var b = new Wpf.Ui.Controls.Button
        {
            Content = text, MinWidth = 96, Padding = new Thickness(18, 9, 18, 10), FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White, Background = new SolidColorBrush(color), BorderBrush = new SolidColorBrush(K.Mix(color, Colors.Black, 0.08)),
            MouseOverBackground = new SolidColorBrush(K.Mix(color, Colors.White, 0.1)), MouseOverBorderBrush = new SolidColorBrush(K.Mix(color, Colors.White, 0.1)),
            PressedBackground = new SolidColorBrush(K.Mix(color, Colors.Black, 0.12)), PressedBorderBrush = new SolidColorBrush(K.Mix(color, Colors.Black, 0.12)),
            PressedForeground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
        };
        if (icon is { } i) b.Icon = new SymbolIcon { Symbol = i, Foreground = Brushes.White };
        b.Click += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    /// <summary>A star you press to keep a card for a closer look: yellow and filled while it is starred.</summary>
    public static Button Star(bool on, Action toggle, double box = 32)
    {
        var icon = on ? K.Icon(SymbolRegular.Star24, 16, Yellow, filled: true) : K.Icon(SymbolRegular.Star24, 16, B.Text2);
        var b = K.Subtle(icon, toggle, new Thickness(0), 4);
        b.Width = box;
        b.Height = box;
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        b.ToolTip = on ? "Unstar" : "Star";
        AutomationProperties.SetName(b, on ? "Unstar" : "Star");
        return b;
    }

    /// <summary>
    /// A tile in a well (the Mac's Theme.well under a choice, a Match tile): quietly darker under the pointer, washed in a
    /// colour when it is chosen, right or wrong.
    /// </summary>
    public sealed class Well
    {
        public Border Root { get; }
        public Button Button { get; }
        private readonly Border fill;

        public Well(UIElement content, Action click, Thickness pad, double corner = 8)
        {
            fill = new Border { Child = content, Padding = pad, CornerRadius = new CornerRadius(corner) };
            Button = K.Subtle(fill, click, new Thickness(0), corner);
            Button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            Button.VerticalContentAlignment = VerticalAlignment.Stretch;
            Root = new Border { Child = Button, CornerRadius = new CornerRadius(corner) }.Bg(B.Subtle);
        }

        /// <summary>The wash over it (null: the well alone).</summary>
        public void Tint(Color? c, double alpha) => fill.Background = c is { } t ? new SolidColorBrush(K.WithAlpha(t, alpha)) : Brushes.Transparent;
    }

    // ---- lists ----

    public static List<T> Shuffled<T>(IEnumerable<T> items)
    {
        var list = items.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    /// <summary>A name for a file, without the characters Windows does not take in one.</summary>
    public static string FileName(string name)
    {
        var bad = System.IO.Path.GetInvalidFileNameChars();
        var s = new string(name.Select(ch => bad.Contains(ch) ? '-' : ch).ToArray()).Trim().TrimEnd('.');
        return s.Length == 0 ? "flashcards" : s;
    }

    // ---- the clipboard ----

    /// <summary>Plain text on the clipboard (the Mac's copyToPasteboard).</summary>
    public static void Copy(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); } catch (Exception e) { Log.Write($"copy: {e.Message}"); }
    }

    /// <summary>
    /// Citations on the clipboard with their italics — as rich text (RTF, for Word) and HTML (for a page, Google Docs) —
    /// and as plain text for anything else; several go one to a paragraph.
    /// </summary>
    public static void CopyRich(IReadOnlyList<IReadOnlyList<CiteSeg>> paragraphs, string plain)
    {
        if (string.IsNullOrEmpty(plain)) return;
        var data = new DataObject();
        data.SetData(DataFormats.Rtf, Rtf(paragraphs));
        data.SetData(DataFormats.Html, Html(paragraphs));
        data.SetData(DataFormats.UnicodeText, plain);
        data.SetData(DataFormats.Text, plain);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { Clipboard.SetDataObject(data, true); return; }
            catch (Exception e) when (attempt < 2) { Log.Write($"copy: {e.Message}"); System.Threading.Thread.Sleep(40); }
            catch (Exception e) { Log.Write($"copy: {e.Message}"); }
        }
    }

    private static string Rtf(IReadOnlyList<IReadOnlyList<CiteSeg>> paragraphs)
    {
        var sb = new StringBuilder(@"{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\froman\fcharset0 Times New Roman;}}\f0\fs24 ");
        for (var i = 0; i < paragraphs.Count; i++)
        {
            if (i > 0) sb.Append(@"\par ");
            foreach (var seg in paragraphs[i])
            {
                if (seg.Italic) sb.Append(@"{\i ");
                foreach (var ch in seg.Text)
                {
                    switch (ch)
                    {
                        case '\\': sb.Append(@"\\"); break;
                        case '{': sb.Append(@"\{"); break;
                        case '}': sb.Append(@"\}"); break;
                        case '\n': sb.Append(@"\line "); break;
                        case '\r': break;
                        default:
                            if (ch > 127) sb.Append(@"\u").Append((short)ch).Append('?');
                            else sb.Append(ch);
                            break;
                    }
                }
                if (seg.Italic) sb.Append('}');
            }
        }
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>The clipboard's HTML format: a header of byte offsets (UTF-8) round the fragment.</summary>
    private static string Html(IReadOnlyList<IReadOnlyList<CiteSeg>> paragraphs)
    {
        string Inline(IReadOnlyList<CiteSeg> p) => string.Concat(p.Select(s => s.Italic ? $"<i>{WebUtility.HtmlEncode(s.Text)}</i>" : WebUtility.HtmlEncode(s.Text)));
        var fragment = paragraphs.Count == 1 ? $"<span>{Inline(paragraphs[0])}</span>" : string.Concat(paragraphs.Select(p => $"<p>{Inline(p)}</p>"));
        const string head = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        const string pre = "<html><body>\r\n<!--StartFragment-->";
        const string post = "<!--EndFragment-->\r\n</body></html>";
        var u = Encoding.UTF8;
        var startHtml = u.GetByteCount(string.Format(head, 0, 0, 0, 0));
        var startFragment = startHtml + u.GetByteCount(pre);
        var endFragment = startFragment + u.GetByteCount(fragment);
        var endHtml = endFragment + u.GetByteCount(post);
        return string.Format(head, startHtml, endHtml, startFragment, endFragment) + pre + fragment + post;
    }
}
