using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Screens;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

// The Word Counter (tools; the Mac's WordCounterTool): an essay pasted or typed, counted as it changes — words,
// characters with and without spaces, sentences, paragraphs, how long it takes to read or to say, the average and the
// longest word — against a target of so many words, with the words it uses most. The counting is Model/WordCount.cs.

#pragma warning disable CA2255
internal static class WordCounterRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.Register("words", c => new WordCounterTool(c).View, c => new WordCounterCompact(c).View);
}
#pragma warning restore CA2255

/// <summary>
/// The text and the target, one for the whole tool and its pin alike (a pin's few lines are the essay's), kept on this
/// PC (Prefs <c>SimplWords.text</c>, <c>SimplWords.target</c>) so the essay is there the next time.
/// </summary>
internal sealed class WordsState
{
    public static WordsState Shared { get; } = new();

    private const string TextKey = "SimplWords.text", TargetKey = "SimplWords.target";

    public string Text { get; private set; }
    public string Target { get; private set; }
    public WordStats Stats { get; private set; }

    public event Action? Changed;

    private WordsState()
    {
        Text = Prefs.String(TextKey) ?? "";
        Target = Prefs.String(TargetKey) ?? "";
        Stats = WordCount.Count(Text);
    }

    public void SetText(string t)
    {
        if (Text == t) return;
        Text = t;
        Stats = WordCount.Count(t);
        Prefs.Set(TextKey, t.Length == 0 ? null : t);
        Changed?.Invoke();
    }

    public void SetTarget(string t)
    {
        if (Target == t) return;
        Target = t;
        Prefs.Set(TargetKey, t.Length == 0 ? null : t);
        Changed?.Invoke();
    }

    /// <summary>The target as a count, or none ("500", "500 words").</summary>
    public int? TargetWords => int.TryParse(new string(Target.Where(char.IsDigit).ToArray()), out var n) && n > 0 ? n : null;

    /// <summary>The screenshot suite's start: --words-text "…" (\n for a line break), --words-target 500 — or the text as
    /// --tool-state, as the other tools take theirs.</summary>
    public void FromLaunch(string? state)
    {
        var text = Launch.Take("", "words-text") ?? state;
        if (text is { Length: > 0 }) SetText(text.Replace("\\n", "\n"));
        if (Launch.Take("", "words-target") is { Length: > 0 } t) SetTarget(t);
    }
}

/// <summary>
/// The Word Counter (the Mac's WordCounterTool): the text large at the left, counted as it is typed, with the count in a
/// line over it; at the right the counts, the target with how far it is, and the words used most.
/// </summary>
internal sealed class WordCounterTool
{
    private readonly WordsState s = WordsState.Shared;
    private readonly TextBox editor;
    private readonly TextBox target;
    private readonly TextBlock summary = K.Wrapped("", Ty.Body, B.Text2);
    private readonly TextBlock big = Everyday.Figures("0", 56, FontWeights.Bold);
    private readonly ContentControl rows = new() { Focusable = false };
    private readonly ContentControl progress = new() { Focusable = false };
    private readonly TextBlock toGo = K.Wrapped("", Ty.Body, B.Text2);
    private readonly ContentControl top = new() { Focusable = false };
    private readonly Wpf.Ui.Controls.Button copy, clear;
    private bool syncing;
    private int said = -1;

    public FrameworkElement View { get; }

    public WordCounterTool(ToolContext c)
    {
        s.FromLaunch(Everyday.LaunchState("words"));
        editor = new TextBox
        {
            Text = s.Text, PlaceholderText = "Paste or type your essay here.", AcceptsReturn = true, AcceptsTab = false,
            TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 300, MaxHeight = 560,
            ClearButtonEnabled = false, VerticalContentAlignment = VerticalAlignment.Top,
        };
        editor.TextChanged += (_, _) => { if (!syncing) s.SetText(editor.Text); };
        AutomationProperties.SetName(editor, "Your text");
        clear = K.Button("Clear", () => s.SetText(""), icon: SymbolRegular.Delete24);
        clear.HorizontalAlignment = HorizontalAlignment.Left;
        var main = K.V(10, summary, editor, K.H(10, clear, K.Text("Counted as you type.", Ty.Caption, B.Text3).Align(HorizontalAlignment.Left, VerticalAlignment.Center)));

        // ---- the counts ----
        copy = K.Button("Copy", () => Everyday.Copy(WordCount.Report(s.Stats)), icon: SymbolRegular.Copy24);
        copy.HorizontalAlignment = HorizontalAlignment.Left;
        var bigRow = K.H(8, Everyday.Shrinks(big), K.Text("words", Ty.BodyLarge, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Bottom).Margin(0, 0, 0, 10));
        big.ContextMenu = MoreKit.Menu(("Copy counts", SymbolRegular.Copy24, () => Everyday.Copy(WordCount.Report(s.Stats))));
        var counts = K.Card(K.V(10, bigRow, rows, copy.Margin(0, 4, 0, 0)), new Thickness(20, 16, 20, 20));

        // ---- the target ----
        target = ToolKit.Box(s.Target, "e.g. 500", t => { if (!syncing) s.SetTarget(t); });
        AutomationProperties.SetName(target, "Target words");
        var targetField = ToolKit.Field("Target", target, suffix: "words", width: 180);
        var targetCard = K.Card(K.V(10, targetField, progress, toGo), new Thickness(16));

        var side = K.V(20, counts, Everyday.Section("Target", targetCard), Everyday.Section("Most used words", top));
        View = Everyday.Page(ToolKit.Columns(main, side, 380, 860));
        Update();
        Everyday.Follow(View, () => { s.Changed += Update; Update(); }, () => s.Changed -= Update);
    }

    private void Update()
    {
        syncing = true;
        if (editor.Text != s.Text) editor.Text = s.Text;
        if (target.Text != s.Target) target.Text = s.Target;
        syncing = false;
        var st = s.Stats;
        summary.Text = st.Words == 0 ? "Nothing to count yet." : $"{WordCount.N(st.Words)} {(st.Words == 1 ? "word" : "words")} · {WordCount.N(st.Characters)} characters · {WordCount.Clock(st.ReadingTime)} to read";
        big.Text = WordCount.N(st.Words);
        AutomationProperties.SetName(big, $"{WordCount.N(st.Words)} words");
        clear.IsEnabled = s.Text.Length > 0;
        copy.IsEnabled = st.Words > 0;
        rows.Content = Rows(st);
        // the target: how far along, and what is left
        var tint = ToolsCenter.KindOf("words")?.Color ?? K.Hex("#8e8e93");
        if (s.TargetWords is { } t)
        {
            var frac = Math.Min(1, (double)st.Words / t);
            progress.Content = K.Bar(frac, st.Words >= t ? K.Tone("green") : tint, 6);
            toGo.Text = $"{WordCount.ToTarget(st.Words, t)} · {Math.Round(100.0 * st.Words / t)}% of {WordCount.N(t)}";
        }
        else
        {
            progress.Content = null;
            toGo.Text = "Set a target to see how far along you are.";
        }
        // the words used most, the stop words left out
        if (st.TopWords.Count == 0) top.Content = K.Wrapped("The words you use most show here, the little words (the, and, of) left out.", Ty.Body, B.Text2);
        else
        {
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var (word, n) in st.TopWords)
            {
                var chip = K.Chip($"{word} ×{n}", tint);
                chip.Margin = new Thickness(0, 0, 6, 6);
                chip.ToolTip = $"{word}: {ToolKit.Plural(n, "time")}";
                wrap.Children.Add(chip);
            }
            top.Content = wrap;
        }
        if (Shot.Asked && said != st.Words) { said = st.Words; Log.Write($"words: shows {st.Words} words"); }
    }

    /// <summary>The counts under the big figure, each with its name.</summary>
    private static FrameworkElement Rows(WordStats st)
    {
        var list = K.V(0);
        void Row(string name, string value)
        {
            if (list.Children.Count > 0) list.Children.Add(K.Divider());
            var v = K.Text(value, Ty.Body);
            v.FontFeatures();
            list.Children.Add(K.Columns("*, auto", K.Text(name, Ty.Body, B.Text2).Col(0), v.Margin(12, 0, 0, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(1)).Margin(0, 7, 0, 7));
        }
        Row("Characters", WordCount.N(st.Characters));
        Row("Without spaces", WordCount.N(st.CharactersNoSpaces));
        Row("Sentences", WordCount.N(st.Sentences));
        Row("Paragraphs", WordCount.N(st.Paragraphs));
        Row("Reading time", WordCount.Clock(st.ReadingTime));
        Row("Speaking time", WordCount.Clock(st.SpeakingTime));
        Row("Average word", st.Words == 0 ? "—" : $"{WordCount.Tenth(st.AverageWordLength)} letters");
        Row("Longest word", st.LongestWord.Length == 0 ? "—" : st.LongestWord);
        return list;
    }
}

/// <summary>The Word Counter's pin, opened (the Mac's WordCounterCompact): a few lines of the text and its counts; the
/// text is the whole tool's, so Open full screen shows the same essay with every count.</summary>
internal sealed class WordCounterCompact
{
    private readonly WordsState s = WordsState.Shared;
    private readonly TextBox editor;
    private readonly TextBlock line = K.Wrapped("", Ty.Body, B.Text2);
    private readonly TextBlock big = Everyday.Figures("0", 30, FontWeights.Bold);
    private bool syncing;

    public FrameworkElement View { get; }

    public WordCounterCompact(ToolContext c)
    {
        s.FromLaunch(null);
        editor = new TextBox
        {
            Text = s.Text, PlaceholderText = "Paste your text here.", AcceptsReturn = true, AcceptsTab = false, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 96, MaxHeight = 180, ClearButtonEnabled = false, VerticalContentAlignment = VerticalAlignment.Top,
        };
        editor.TextChanged += (_, _) => { if (!syncing) s.SetText(editor.Text); };
        AutomationProperties.SetName(editor, "Your text");
        big.ContextMenu = MoreKit.Menu(("Copy counts", SymbolRegular.Copy24, () => Everyday.Copy(WordCount.Report(s.Stats))));
        var figure = K.H(8, Everyday.Shrinks(big), K.Text("words", Ty.Body, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Bottom).Margin(0, 0, 0, 5));
        View = K.V(10, editor, K.V(2, figure, line));
        Update();
        Everyday.Follow(View, () => { s.Changed += Update; Update(); }, () => s.Changed -= Update);
    }

    private void Update()
    {
        syncing = true;
        if (editor.Text != s.Text) editor.Text = s.Text;
        syncing = false;
        var st = s.Stats;
        big.Text = WordCount.N(st.Words);
        line.Text = $"{WordCount.N(st.Characters)} characters · {WordCount.N(st.Sentences)} {(st.Sentences == 1 ? "sentence" : "sentences")} · {WordCount.Clock(st.ReadingTime)} to read"
            + (s.TargetWords is { } t ? $" · {WordCount.ToTarget(st.Words, t)}" : "");
    }
}
