using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Kit;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// The quiz popup's shared pieces (the Mac's QuizLayout, QuizFloatingBar, QuizOptionText and its small helpers): its
/// measures, the foot each pane's buttons sit in (Windows 11's dialog foot), a section's heading, a coloured edge beside
/// an answer, Canvas's text with its formulas typeset and its blanks marked, and the symbols of the quiz's rules.
/// </summary>
internal static class QuizUi
{
    /// <summary>The width from which a pane puts a column beside the page (the questions, the score, the summary).</summary>
    public const double Wide = 900;
    /// <summary>The readable width of a question and of a list of answers.</summary>
    public const double Column = 840;

    public static readonly Color Green = K.Tone("green");
    public static readonly Color Red = K.Tone("red");
    public static readonly Color Orange = K.Tone("orange");

    /// <summary>The quiz's colour: its course's, else the Mac's blue.</summary>
    public static Color TintOf(string? hex) => K.Hex(hex, Color.FromRgb(0x0a, 0x84, 0xff));

    private static readonly HashSet<string> KeepCase = new(StringComparer.Ordinal) { "Canvas", "Browser", "I" };

    /// <summary>
    /// The page's words for a button or a heading (written in the Mac's Title Case: "Begin Attempt", "Partly Right") in
    /// Windows 11's sentence case: "Begin attempt", "Partly right". Names keep theirs ("Canvas", "LockDown Browser").
    /// </summary>
    public static string Sentence(string? s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        var words = s.Split(' ');
        for (var i = 1; i < words.Length; i++)
        {
            var w = words[i];
            if (w.Length < 2 || !char.IsUpper(w[0]) || KeepCase.Contains(w) || w.Skip(1).Any(char.IsUpper)) continue; // (a name, LockDown, an acronym)
            words[i] = char.ToLowerInvariant(w[0]) + w[1..];
        }
        return string.Join(' ', words);
    }

    /// <summary>A colour as words on the page (its course's, a verdict's): deepened on light, lightened on dark.</summary>
    public static SolidColorBrush Ink(Color c) => new(K.Darkened(c));

    /// <summary>The foot of a pane, as Windows 11's dialogs have it: the dialog's own fill, a hairline over it.</summary>
    public static Border Foot(UIElement content)
    {
        var b = new Border { Child = content, Padding = new Thickness(24, 14, 24, 14), BorderThickness = new Thickness(0, 1, 0, 0) };
        b.SetResourceReference(Border.BackgroundProperty, "ContentDialogBackground");
        b.SetResourceReference(Border.BorderBrushProperty, "ContentDialogSeparatorBorderBrush");
        return b;
    }

    /// <summary>A part of a page under its heading (the Mac's PageSection).</summary>
    public static FrameworkElement Section(string title, UIElement content, string? trailing = null)
    {
        var head = K.Columns("*, auto", K.Text(title, Ty.Subtitle, B.Text, TextWrapping.Wrap).Col(0));
        if (!string.IsNullOrEmpty(trailing)) head.Children.Add(K.Text(trailing!, Ty.Caption, B.Text2).Align(HorizontalAlignment.Right, VerticalAlignment.Bottom).Col(1));
        return K.V(10, head, content);
    }

    /// <summary>Numbers that keep their place as they change (a clock, a count).</summary>
    public static TextBlock Digits(this TextBlock t)
    {
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    /// <summary>Windows 11's button with its words, a symbol before them (or after: Next), the accent's for the one to press.</summary>
    public static Wpf.Ui.Controls.Button Button(string text, Action? click, bool accent = false, SymbolRegular? icon = null, bool after = false, ControlAppearance? look = null)
    {
        var b = K.Button(text, click, accent, after ? null : icon);
        if (after && icon is { } i)
        {
            // (the words and the symbol take the button's own colours, as its words would)
            var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
            var mark = new SymbolIcon { Symbol = i, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
            b.Content = K.H(8, label, mark);
        }
        if (look is { } l) b.Appearance = l;
        return b;
    }

    /// <summary>What a coloured line at its left edge marks (an answer, the worked solution): no wash, no box.</summary>
    public static FrameworkElement Edge(UIElement child, Color color, double alpha = 0.65)
    {
        var bar = new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Background = K.Brush(K.WithAlpha(color, alpha)) };
        return K.Columns("3, 11, *", bar.Col(0), child.Col(2));
    }

    /// <summary>The edge in the theme's quiet grey (an answer not marked yet).</summary>
    public static FrameworkElement QuietEdge(UIElement child)
    {
        var bar = new Border { Width = 3, CornerRadius = new CornerRadius(1.5) }.Bg(B.StrongStroke);
        return K.Columns("3, 11, *", bar.Col(0), child.Col(2));
    }

    // ---- Canvas's text ----

    /// <summary>Canvas's own HTML (a question, an option, a solution): its links through the app, its formulas typeset
    /// (Kit/Formula), a blank's numbered mark as a mark — or, once its menu has a pick, the word picked in its place.</summary>
    public static RichText Rich(Engine.Engine engine, string html, double size, IReadOnlyDictionary<int, string>? picked = null)
    {
        var r = new RichText(BlankMarks(html, picked), u => engine.GoTo(u), engine.BaseUrl, size);
        return r;
    }

    /// <summary>The engine's mark for a blank in a question's words (a numbered pill: its style, as native-app.js writes it).</summary>
    private static readonly Regex Blank = new(@"<span style=""display:inline-block;min-width:1\.5em;[^""]*"">([^<]{1,4})</span>", RegexOptions.Compiled);

    /// <summary>Each blank's mark as RichText draws one (data-blank: its number, and the word picked for it, if any).</summary>
    public static string BlankMarks(string html, IReadOnlyDictionary<int, string>? picked = null)
    {
        if (string.IsNullOrEmpty(html) || html.IndexOf("min-width:1.5em", StringComparison.Ordinal) < 0) return html ?? "";
        return Blank.Replace(html, m =>
        {
            var n = m.Groups[1].Value;
            if (picked != null && int.TryParse(n, out var k) && picked.TryGetValue(k, out var word) && word.Length > 0)
                return $"<span data-blank=\"{n}\" data-picked=\"1\">{WebUtility.HtmlEncode(word)}</span>";
            return $"<span data-blank=\"{n}\">{n}</span>";
        });
    }

    /// <summary>
    /// An option's words, or Canvas's own content where it has none (a formula, a picture): a click on it is the row's
    /// (the Mac's QuizOptionText).
    /// </summary>
    public static FrameworkElement OptionText(Engine.Engine engine, string text, string html)
    {
        if (html.Length > 0 && (text.Length == 0 || html.Contains("<img", StringComparison.OrdinalIgnoreCase)))
        {
            var r = Rich(engine, html, 15);
            r.IsHitTestVisible = false;
            r.Focusable = false;
            r.IsTabStop = false;
            r.VerticalAlignment = VerticalAlignment.Center;
            return r;
        }
        var t = K.Wrapped(text.Length == 0 ? "—" : text, Ty.Body);
        t.VerticalAlignment = VerticalAlignment.Center;
        return t;
    }

    // ---- the rules' symbols (the page names SF Symbols) ----

    public static (SymbolRegular Symbol, bool Filled) RuleSymbol(string sf) => sf switch
    {
        "lock.fill" => (SymbolRegular.LockClosed24, true),
        "checkmark.circle.fill" => (SymbolRegular.CheckmarkCircle24, true),
        "person.2.fill" => (SymbolRegular.People24, true),
        "timer" => (SymbolRegular.Timer24, false),
        "rectangle.portrait" => (SymbolRegular.DocumentOnePage24, false),
        "bolt.fill" => (SymbolRegular.Flash24, true),
        "calendar.badge.clock" => (SymbolRegular.CalendarClock24, false),
        "key.fill" => (SymbolRegular.Key24, true),
        "network" => (SymbolRegular.NetworkCheck24, false),
        "lock.shield.fill" => (SymbolRegular.ShieldLock24, true),
        _ => (SymbolRegular.Info24, false),
    };

    /// <summary>A rule's colour by the page's word for it.</summary>
    public static SymbolIcon RuleIcon(QuizIntro.Rule r)
    {
        var (s, filled) = RuleSymbol(r.Symbol);
        return r.Tint switch
        {
            "orange" => K.Icon(s, 18, Orange, filled),
            "green" => K.Icon(s, 18, Green, filled),
            _ => K.Icon(s, 18, B.Text2, filled),
        };
    }

    /// <summary>A small spinner beside a quiet line (Starting your attempt…, Submitting…).</summary>
    public static FrameworkElement Working(string text, Ty ty = Ty.Body) => K.H(10, K.Spinner(16), K.Text(text, ty, B.Text2));

    /// <summary>A page that scrolls as one: Windows' scroll bar, nothing across.</summary>
    public static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Focusable = false, PanningMode = PanningMode.VerticalOnly,
    };

    /// <summary>Whether the keyboard is with something being typed in (or a menu being picked from): the attempt's own keys
    /// stand aside meanwhile.</summary>
    public static bool Typing()
    {
        var f = Keyboard.FocusedElement as DependencyObject;
        return f switch
        {
            System.Windows.Controls.Primitives.TextBoxBase t => !t.IsReadOnly,
            System.Windows.Controls.PasswordBox => true,
            ComboBox or ComboBoxItem => true,
            _ => false,
        };
    }
}

/// <summary>
/// A question the popup asks over itself (the Mac's confirmation dialogs and alerts: Leave the quiz?, Submit this
/// attempt?, the access code, time is up), drawn as Windows 11's ContentDialog inside the popup — the popup dimmed
/// behind, one at a time (another waits its turn), the accent button the default (Enter), the other the way out (Escape).
/// </summary>
internal sealed class QuizAskLayer : Grid
{
    public sealed class Ask
    {
        public string Title { get; init; } = "";
        public string Message { get; init; } = "";
        public FrameworkElement? Extra { get; init; }
        public string Go { get; init; } = "OK";
        public Action? OnGo { get; init; }
        public bool Danger { get; init; }
        public string? Cancel { get; init; }
        public Action? OnCancel { get; init; }
        /// <summary>The thing to type in, focused as it shows.</summary>
        public UIElement? Focus { get; init; }
    }

    private readonly Border smoke = new();
    private readonly Queue<Ask> waiting = new();
    private Ask? current;
    private Border? card;
    private Wpf.Ui.Controls.Button? goButton;

    /// <summary>A question came or went (the popup keeps Escape to itself meanwhile).</summary>
    public event Action? OpenChanged;

    public bool IsOpen => current != null;

    public QuizAskLayer()
    {
        Visibility = Visibility.Collapsed;
        smoke.Background = K.Brush(Color.FromArgb(Support.Appearance.Dark ? (byte)110 : (byte)70, 0, 0, 0));
        smoke.MouseLeftButtonDown += (_, e) => e.Handled = true;
        Children.Add(smoke);
    }

    public void Show(Ask a)
    {
        if (current != null) { waiting.Enqueue(a); return; }
        current = a;
        card = Build(a);
        Children.Add(card);
        Visibility = Visibility.Visible;
        Motion.FadeIn(smoke, Motion.Normal);
        Motion.Pop(card);
        OpenChanged?.Invoke();
        Support.Shot.Moved();
        Dispatcher.BeginInvoke(() =>
        {
            var first = a.Focus ?? goButton;
            if (first != null) { first.Focus(); Keyboard.Focus(first); }
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>The question put away, its own way out taken (Escape, Cancel).</summary>
    public void Dismiss()
    {
        if (current is not { } a) return;
        Close();
        a.OnCancel?.Invoke();
    }

    private void Choose()
    {
        if (current is not { } a) return;
        Close();
        a.OnGo?.Invoke();
    }

    private void Close()
    {
        var c = card;
        current = null;
        card = null;
        goButton = null;
        void Gone()
        {
            if (c != null) Children.Remove(c);
            if (waiting.Count > 0) { Show(waiting.Dequeue()); return; }
            if (current == null) Visibility = Visibility.Collapsed;
            OpenChanged?.Invoke();
        }
        if (waiting.Count > 0 || c == null) { Gone(); return; }
        Motion.FadeOut(smoke, () => { }, Motion.Normal);
        Motion.Unpop(c, Gone);
    }

    /// <summary>The keys while a question is up: Escape is its way out, Enter its default (a focused button presses itself).</summary>
    public void Press(KeyEventArgs e)
    {
        if (current == null) return;
        if (e.Key == Key.Escape) { Dismiss(); e.Handled = true; }
        else if (e.Key == Key.Enter && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.ButtonBase)
        {
            if (goButton is { IsEnabled: true }) { Choose(); e.Handled = true; }
        }
    }

    private Border Build(Ask a)
    {
        var top = K.V(12);
        var title = K.Text(a.Title, Ty.Subtitle, B.Text, TextWrapping.Wrap);
        title.TextTrimming = TextTrimming.None;
        top.Children.Add(title);
        if (a.Message.Length > 0) top.Children.Add(K.Wrapped(a.Message, Ty.Body));
        if (a.Extra != null) top.Children.Add(a.Extra);
        var content = new Border { Child = top, Padding = new Thickness(24) };
        content.SetResourceReference(Border.BackgroundProperty, "ContentDialogTopOverlay");

        var row = new Grid();
        var go = new Wpf.Ui.Controls.Button
        {
            Content = a.Go, HorizontalAlignment = HorizontalAlignment.Stretch,
            Appearance = a.Danger ? ControlAppearance.Danger : ControlAppearance.Primary,
        };
        go.Click += (_, _) => Choose();
        goButton = go;
        if (a.Cancel != null)
        {
            foreach (var w in new[] { "*", "8", "*" }) row.ColumnDefinitions.Add(new ColumnDefinition { Width = K.Length(w) });
            var cancel = new Wpf.Ui.Controls.Button { Content = a.Cancel, HorizontalAlignment = HorizontalAlignment.Stretch, Appearance = ControlAppearance.Secondary };
            cancel.Click += (_, _) => Dismiss();
            row.Children.Add(go.Col(0));
            row.Children.Add(cancel.Col(2));
        }
        else
        {
            foreach (var w in new[] { "*", "8", "*" }) row.ColumnDefinitions.Add(new ColumnDefinition { Width = K.Length(w) });
            row.Children.Add(go.Col(2));
        }
        var foot = new Border { Child = row, Padding = new Thickness(24), BorderThickness = new Thickness(0, 1, 0, 0) };
        foot.SetResourceReference(Border.BackgroundProperty, "ContentDialogBackground");
        foot.SetResourceReference(Border.BorderBrushProperty, "ContentDialogSeparatorBorderBrush");

        var grid = K.Rows("auto, auto", content.Row(0), foot.Row(1));
        grid.SizeChanged += (_, e) => grid.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 7, 7);
        var c = new Border
        {
            Child = grid, Width = 420, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Effect = K.Shadow(64, 24, 0.32), SnapsToDevicePixels = true,
        };
        c.SetResourceReference(Border.BackgroundProperty, "ContentDialogBackground");
        c.SetResourceReference(Border.BorderBrushProperty, "ContentDialogBorderBrush");
        c.MouseLeftButtonDown += (_, e) => e.Handled = true;
        KeyboardNavigation.SetTabNavigation(c, KeyboardNavigationMode.Cycle);
        return c;
    }
}
