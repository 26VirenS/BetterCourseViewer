using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

/// <summary>
/// Citation generator (the Mac's CitationTool): the style and the kind of source across the top, the details on the left,
/// and on the right the citation as it forms — its in-text form, what is still needed (a press puts the cursor there),
/// Copy and Save — with the saved citations under it. The source being cited is kept while the app runs, so the tool
/// opened again (from its tile, or from its pin) has it as it was.
/// </summary>
internal sealed class CitationTool : ContentControl
{
    // (the source being written: the Mac's @State, kept for the tool built again)
    private static string type = "website";
    private static readonly Dictionary<string, string> values = new();

    /// <summary>The source changed in one copy of the tool (the Tools page's, or the one over the window): the others follow.</summary>
    private static event Action<CitationTool>? Drafted;

    private string shownType = "";
    private bool syncing;

    private readonly string lms;
    private readonly Color tint = StudyKit.ToolColor("cite", "#30b0c7");
    private bool copied;
    private readonly SelectorBar styleBar;
    private readonly List<(string Key, Wpf.Ui.Controls.Button Button)> typeButtons = new();
    private readonly ContentControl details = StudyKit.Host(), result = StudyKit.Host(), savedList = StudyKit.Host();
    private readonly Dictionary<string, TextBox> boxes = new();
    private readonly List<(CiteField Field, Ellipse Dot, TextBlock Needed)> marks = new();

    public CitationTool(ToolContext c)
    {
        lms = c.Engine.LmsName;
        Focusable = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        CitationDemo.SeedIfAsked(lms);
        var handed = TakeHandoff(c);
        if (Launch.Take("", "cite-demo") is { } demo)
        {
            handed = true;
            var kind = demo is "true" or "partial" ? (demo == "partial" ? "website" : "journal") : demo.ToLowerInvariant();
            if (CiteBuilder.Types.Any(t => t.Key == kind))
            {
                type = kind;
                values.Clear();
                foreach (var (k, v) in CitationDemo.Sample(demo == "partial" ? "partial" : kind)) values[k] = v;
            }
        }

        styleBar = new SelectorBar(CiteBuilder.Styles.Select(s => s.Name).ToList(), StyleIndex) { HorizontalAlignment = HorizontalAlignment.Left };
        styleBar.Changed += i => CitationStore.Style = CiteBuilder.Styles[i].Key;
        System.Windows.Automation.AutomationProperties.SetName(styleBar, "Style");

        var styleGroup = K.V(6, K.Text("Style", Ty.BodyStrong, B.Text2), styleBar);
        styleGroup.Margin = new Thickness(0, 0, 28, 10);
        styleGroup.VerticalAlignment = VerticalAlignment.Bottom;
        var kinds = new WrapPanel();
        foreach (var (key, name, symbol) in CiteBuilder.Types)
        {
            var k = key;
            var b = K.Button(name, () => SetType(k), accent: key == type, icon: symbol);
            b.MinWidth = 0;
            b.Margin = new Thickness(0, 0, 8, 8);
            b.FontWeight = key == type ? FontWeights.SemiBold : FontWeights.Normal;
            typeButtons.Add((key, b));
            kinds.Children.Add(b);
        }
        var sourceGroup = K.V(6, K.Text("Source", Ty.BodyStrong, B.Text2), kinds);
        sourceGroup.Margin = new Thickness(0, 0, 0, 2);
        sourceGroup.VerticalAlignment = VerticalAlignment.Bottom;
        var top = new WrapPanel { Children = { styleGroup, sourceGroup } };

        var side = K.V(26, result, savedList);
        Content = ToolKit.Page(K.V(20, top, ToolKit.Columns(details, side, 420, 880, sideFirst: true)));
        Details();
        Result();
        Saved();
        StudyKit.Watch(this, h => CitationStore.Changed += h, h => CitationStore.Changed -= h, StoreChanged);
        Action<CitationTool> follow = OtherDrafted;
        StudyKit.Watch(this, h => Drafted += follow, h => Drafted -= follow, () => OtherDrafted(null));
        if (handed) Draft();
    }

    private void Draft()
    {
        if (!syncing) Drafted?.Invoke(this);
    }

    /// <summary>The source as another copy of the tool left it: its kind, and each field's words.</summary>
    private void OtherDrafted(CitationTool? from)
    {
        if (ReferenceEquals(from, this)) return;
        syncing = true;
        try
        {
            if (shownType != type) ShowType();
            else
                foreach (var (k, b) in boxes)
                    if (b.Text != Val(k)) b.Text = Val(k);
            copied = false;
            Marks();
            Result();
        }
        finally { syncing = false; }
    }

    private static int StyleIndex => Math.Max(0, Array.FindIndex(CiteBuilder.Styles, s => s.Key == CitationStore.Style));

    private IReadOnlyList<CiteField> Defs => CiteBuilder.Fields(type, lms);

    private static string Val(string key) => values.TryGetValue(key, out var v) ? v : "";

    private List<CiteField> Missing => Defs.Where(d => d.Required && Val(d.Key).Trim().Length == 0).ToList();

    private List<CiteSeg> Parts => CiteBuilder.Build(CitationStore.Style, type, values, lms);

    private void StoreChanged()
    {
        styleBar.Select(StyleIndex, false);
        Result();
        Saved();
    }

    /// <summary>A link (or a source) handed over from the pin: the generator opened with it in, as a new source.</summary>
    private static bool TakeHandoff(ToolContext c)
    {
        if (c.TakeInfo("style") is { } style && CiteBuilder.Styles.Any(s => s.Key == style)) CitationStore.Style = style;
        if (c.TakeInfo("link") is not { Length: > 0 } link) return false;
        var p = CiteBuilder.Prefill(link);
        type = p.Type;
        values.Clear();
        foreach (var (k, v) in p.Values) values[k] = v;
        return true;
    }

    private void SetType(string key)
    {
        if (shownType == key) return;
        type = key;
        copied = false;
        Draft();
        ShowType();
    }

    private void ShowType()
    {
        foreach (var (k, b) in typeButtons)
        {
            b.Appearance = k == type ? ControlAppearance.Primary : ControlAppearance.Secondary;
            b.FontWeight = k == type ? FontWeights.SemiBold : FontWeights.Normal;
        }
        Details();
        Motion.FadeIn(details, Motion.Normal);
        Result();
    }

    // ---- details ----

    private void Details()
    {
        shownType = type;
        boxes.Clear();
        marks.Clear();
        var body = K.V(14);
        foreach (var row in Rows(Defs))
        {
            var g = new Grid();
            for (var i = 0; i < row.Count; i++)
            {
                if (i > 0) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.Children.Add(Field(row[i]).Col(i * 2));
            }
            body.Children.Add(g);
        }
        body.Children.Add(K.Wrapped("Several authors: a semicolon between them. Leave out what you do not have; the citation shapes itself around it.", Ty.Caption, B.Text2));
        var clear = K.Link("Clear", Clear);
        clear.ToolTip = "Clear every field";
        details.Content = K.V(0, Heading.Section("Details", null, clear), body);
    }

    /// <summary>The fields in rows: a wide one alone, the short ones beside each other (three at most).</summary>
    private static List<List<CiteField>> Rows(IReadOnlyList<CiteField> defs)
    {
        var output = new List<List<CiteField>>();
        var run = new List<CiteField>();
        foreach (var d in defs)
        {
            if (d.Wide)
            {
                if (run.Count > 0) { output.Add(run); run = new(); }
                output.Add(new() { d });
            }
            else
            {
                run.Add(d);
                if (run.Count == 3) { output.Add(run); run = new(); }
            }
        }
        if (run.Count > 0) output.Add(run);
        return output;
    }

    private FrameworkElement Field(CiteField d)
    {
        var head = K.Columns("auto, auto, auto, *, auto", K.Text(d.Label, Ty.BodyStrong, B.Text2).Col(0));
        if (d.Required)
        {
            var dot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(6, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var needed = K.Text("needed", Ty.Caption);
            needed.Foreground = new SolidColorBrush(StudyKit.Orange);
            needed.Margin = new Thickness(6, 1, 0, 0);
            needed.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(dot.Col(1));
            head.Children.Add(needed.Col(2));
            marks.Add((d, dot, needed));
        }
        if (d.Key == "accessed")
        {
            var today = K.Link("Today", () => { if (boxes.TryGetValue("accessed", out var b)) b.Text = CiteBuilder.TodayWords(); }, Ty.Caption);
            today.Margin = new Thickness(0, -4, -6, -4);
            today.ToolTip = "Today’s date";
            head.Children.Add(today.Col(4));
        }
        var key = d.Key;
        var box = ToolKit.Box(Val(key), d.Hint, text =>
        {
            values[key] = text;
            copied = false;
            Draft();
            Marks();
            Result();
        });
        System.Windows.Automation.AutomationProperties.SetName(box, d.Label);
        boxes[key] = box;
        Marks();
        return K.V(6, head, box);
    }

    /// <summary>Each needed field's dot: orange while it is empty (and "needed" beside it), green once it is filled in.</summary>
    private void Marks()
    {
        foreach (var (f, dot, needed) in marks)
        {
            var empty = Val(f.Key).Trim().Length == 0;
            dot.Fill = new SolidColorBrush(empty ? StudyKit.Orange : K.WithAlpha(StudyKit.Green, 0.8));
            dot.ToolTip = empty ? "Needed" : "Filled in";
            needed.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void Clear()
    {
        values.Clear();
        copied = false;
        Draft();
        Details();
        Result();
    }

    private void FocusField(string key)
    {
        if (!boxes.TryGetValue(key, out var b)) return;
        b.Focus();
        Keyboard.Focus(b);
        b.BringIntoView();
    }

    // ---- the citation ----

    private void Result()
    {
        var missing = Missing;
        var ready = missing.Count == 0;
        var p = Parts;
        var style = CitationStore.Style;

        var chip = K.Chip(ready ? "Ready" : $"{missing.Count} to fill", K.Tone(ready ? "good" : "warn"), strong: true);
        var head = K.Columns("*, auto", StudyKit.Caps($"{CiteBuilder.StyleName(style)} · works cited").Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0), chip.Col(1));

        FrameworkElement citation;
        if (CiteBuilder.Plain(p).Length == 0)
        {
            var empty = K.Wrapped("The citation forms here as you fill in the details.", Ty.Body, B.Text2);
            empty.FontSize = 17;
            empty.LineHeight = 26;
            citation = empty;
        }
        else citation = StudyKit.Runs(p.Select(s => (s.Text, s.Italic)), 17, lineHeight: 26);

        var inText = CiteBuilder.InText(style, type, values, lms);
        var inRow = K.Columns("auto, auto, auto, *",
            K.Text("In text", Ty.BodyStrong, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Margin(0, 0, 8, 0).Col(0),
            StudyKit.Selectable(inText, Ty.Body, wrap: false).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(1),
            K.IconButton(SymbolRegular.Copy24, () => StudyKit.Copy(inText), "Copy the in-text citation", 14, 28).Margin(6, 0, 0, 0).Col(2));

        var v = K.V(14, head, citation, inRow);
        if (!ready)
        {
            var jumps = new WrapPanel();
            foreach (var m in missing)
            {
                var key = m.Key;
                var b = K.Button(m.Label, () => FocusField(key));
                b.MinWidth = 0;
                b.Padding = new Thickness(10, 3, 10, 4);
                b.FontSize = 12;
                b.Margin = new Thickness(0, 0, 6, 6);
                b.ToolTip = $"Fill in the {m.Label.ToLowerInvariant()}";
                jumps.Children.Add(b);
            }
            v.Children.Add(K.V(8, ToolKit.Note($"Add {string.Join(", ", missing.Select(m => m.Label.ToLowerInvariant()))} to finish this citation."), jumps));
        }

        var copy = K.Button(copied ? "Copied" : "Copy citation", () =>
        {
            StudyKit.CopyRich(new[] { (IReadOnlyList<CiteSeg>)p }, CiteBuilder.Plain(p));
            copied = true;
            Result();
        }, accent: true, icon: copied ? SymbolRegular.Checkmark24 : SymbolRegular.Copy24);
        copy.IsEnabled = ready;
        copy.ToolTip = "Copy the citation, italics and all";
        var save = K.Button("Save", () => CitationStore.Add(p, style), icon: SymbolRegular.Add24);
        save.MinWidth = 0;
        save.IsEnabled = ready;
        save.ToolTip = "Keep it in the list below";
        v.Children.Add(K.H(10, copy, save));

        result.Content = K.Card(v, new Thickness(22));
    }

    // ---- saved ----

    private void Saved()
    {
        var list = CitationStore.Saved;
        UIElement? copyList = null;
        if (list.Count > 0)
        {
            var link = K.Link("Copy list", CopyList);
            link.ToolTip = "Copy every saved citation, alphabetically";
            copyList = link;
        }
        var head = Heading.Section(list.Count == 0 ? "Saved" : $"Saved · {list.Count}", null, copyList);
        FrameworkElement body;
        if (list.Count == 0) body = K.Wrapped("Saved citations stay on this PC. Copy list gives them alphabetically.", Ty.Body, B.Text2);
        else
        {
            var rows = K.V(0);
            for (var i = 0; i < list.Count; i++)
            {
                if (i > 0) rows.Children.Add(K.Divider(14));
                rows.Children.Add(SavedRow(list[i]));
            }
            body = K.Card(rows, new Thickness(0, 4, 0, 4));
        }
        savedList.Content = K.V(0, head, body);
    }

    private FrameworkElement SavedRow(SavedCitation c)
    {
        var chip = K.Chip(c.Style, tint, strong: true);
        chip.VerticalAlignment = VerticalAlignment.Top;
        chip.Margin = new Thickness(0, 2, 10, 0);
        var text = StudyKit.Runs(c.Parts.Select(s => (s.Text, s.Italic)), 14);
        var copy = K.IconButton(SymbolRegular.Copy24, () => StudyKit.CopyRich(new[] { (IReadOnlyList<CiteSeg>)c.Parts }, c.Plain), "Copy this one", 14, 28);
        copy.VerticalAlignment = VerticalAlignment.Top;
        copy.Margin = new Thickness(6, 0, 0, 0);
        var remove = K.IconButton(SymbolRegular.Dismiss24, () => CitationStore.Remove(c), "Remove", 14, 28);
        remove.VerticalAlignment = VerticalAlignment.Top;
        var g = K.Columns("auto, *, auto, auto", chip.Col(0), text.Col(1), copy.Col(2), remove.Col(3));
        g.Margin = new Thickness(14, 10, 10, 10);
        return g;
    }

    /// <summary>Every saved citation, alphabetically, one to a paragraph — with their italics.</summary>
    public static void CopyList()
    {
        var list = CitationStore.Alphabetical;
        if (list.Count == 0) return;
        StudyKit.CopyRich(list.Select(c => (IReadOnlyList<CiteSeg>)c.Parts).ToList(), CitationStore.List);
    }
}

/// <summary>
/// The citation generator's pin, opened (the Mac's CitationCompact): a link to cite (it opens the generator with it in),
/// the style, and the last three citations saved, each with a copy button.
/// </summary>
internal sealed class CitationCompact : ContentControl
{
    private readonly ToolContext c;
    private readonly TextBox link;
    private readonly Wpf.Ui.Controls.Button cite;
    private readonly SelectorBar styleBar;
    private readonly ContentControl saved = StudyKit.Host();
    private readonly Color tint = StudyKit.ToolColor("cite", "#30b0c7");

    public CitationCompact(ToolContext c)
    {
        this.c = c;
        Focusable = false;
        CitationDemo.SeedIfAsked(c.Engine.LmsName);
        cite = K.Button("Cite", Cite, accent: true);
        cite.MinWidth = 0;
        cite.IsEnabled = false;
        cite.Margin = new Thickness(8, 0, 0, 0);
        link = ToolKit.Box("", "Paste a link to cite", t => cite.IsEnabled = t.Trim().Length > 0);
        link.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Cite();
        };
        System.Windows.Automation.AutomationProperties.SetName(link, "Link to cite");
        styleBar = new SelectorBar(CiteBuilder.Styles.Select(s => s.Name).ToList(), Math.Max(0, Array.FindIndex(CiteBuilder.Styles, s => s.Key == CitationStore.Style)))
        {
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        styleBar.Changed += i => CitationStore.Style = CiteBuilder.Styles[i].Key;
        Content = K.V(12, K.Columns("*, auto", link.Col(0), cite.Col(1)), styleBar, saved);
        Saved();
        StudyKit.Watch(this, h => CitationStore.Changed += h, h => CitationStore.Changed -= h, () =>
        {
            styleBar.Select(Math.Max(0, Array.FindIndex(CiteBuilder.Styles, s => s.Key == CitationStore.Style)), false);
            Saved();
        });
    }

    private void Saved()
    {
        var list = CitationStore.Saved;
        var head = K.Columns("*, auto", K.Text("Saved", Ty.BodyStrong, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0));
        if (list.Count > 0)
        {
            var copyList = K.Link("Copy list", CitationTool.CopyList, Ty.Caption);
            copyList.ToolTip = "Copy every saved citation, alphabetically";
            head.Children.Add(copyList.Col(1));
        }
        FrameworkElement body;
        if (list.Count == 0) body = K.Wrapped("Citations you save in the generator show here.", Ty.Body, B.Text2);
        else
        {
            var rows = K.V(6);
            foreach (var s in list.TakeLast(3).Reverse())
            {
                var style = K.Text(s.Style, Ty.Caption).Strong();
                style.FontSize = 11;
                style.Foreground = new SolidColorBrush(tint);
                style.Margin = new Thickness(0, 0, 8, 0);
                style.VerticalAlignment = VerticalAlignment.Center;
                var plain = K.Text(s.Plain, Ty.Body);
                plain.VerticalAlignment = VerticalAlignment.Center;
                plain.ToolTip = s.Plain;
                var item = s;
                var copy = K.IconButton(SymbolRegular.Copy24, () => StudyKit.CopyRich(new[] { (IReadOnlyList<CiteSeg>)item.Parts }, item.Plain), "Copy this citation", 14, 28);
                copy.Margin = new Thickness(4, 0, 0, 0);
                rows.Children.Add(K.Columns("auto, *, auto", style.Col(0), plain.Col(1), copy.Col(2)));
            }
            body = rows;
        }
        saved.Content = K.V(8, head, body);
    }

    private void Cite()
    {
        var v = link.Text.Trim();
        if (v.Length == 0) return;
        link.Text = "";
        c.OpenFull(new Dictionary<string, string> { ["link"] = v, ["style"] = CitationStore.Style }, null);
    }
}
