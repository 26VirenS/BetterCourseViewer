using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Screens;
using Simpl.Support;
using Simpl.Tools.Verbs;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

// Verb Conjugations (0.3.15), as the Mac's: a verb in Spanish, French, German, Italian or Portuguese conjugated in
// every main tense of its language — regular verbs by their class's rules, stem and spelling changes applied, the
// most common irregular verbs from the language's own table (Tools/Study/Verbs/VerbEngine.cs, the same data files as
// the Mac's). Pronouns beside the forms, irregular cells tinted while the toggle is on, a list of verbs suggested as
// the verb is typed, and a tense's table (or every table) copied as plain text. The language and the last verb are
// remembered. Nothing leaves the PC.

#pragma warning disable CA2255
internal static class VerbsRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.Register("verbs", c => new VerbsTool(c), c => new VerbsCompact(c).View);
}
#pragma warning restore CA2255

/// <summary>What the tool shows (the whole tool's and the pin's each their own): the language, the verb typed, the table
/// made of it, and whether irregular cells are tinted (remembered: Prefs <c>SimplVerbs.*</c>).</summary>
internal sealed class VerbsState
{
    public static readonly VerbsState Full = new(), Quick = new();

    private const string LangKey = "SimplVerbs.lang", VerbKey = "SimplVerbs.verb", TintKey = "SimplVerbs.tint";

    public string Lang { get; private set; } = VerbLanguage.All.Any(l => l.Code == Prefs.String(LangKey)) ? Prefs.String(LangKey)! : "es";
    public string Typed { get; private set; } = Prefs.String(VerbKey) ?? "";
    public bool Tint { get; private set; } = Prefs.Bool(TintKey, true);
    public VerbEngine.Table? Table { get; private set; }
    /// <summary>The verb the table is of (the typed words, trimmed and lowered), or what was wrong with it.</summary>
    public string? Verb { get; private set; }
    public string? Problem { get; private set; }

    public event Action? Changed;

    public VerbLanguage Language => VerbLanguage.Load(Lang);

    public string LanguageName => VerbLanguage.All.First(l => l.Code == Lang).Name;

    public void SetLang(string code)
    {
        if (code == Lang || VerbLanguage.All.All(l => l.Code != code)) return;
        Lang = code;
        Prefs.Set(LangKey, code);
        Make();
    }

    public void SetTyped(string text)
    {
        if (text == Typed) return;
        Typed = text;
        Prefs.Set(VerbKey, text.Length == 0 ? null : text);
        Make();
    }

    public void SetTint(bool on)
    {
        Tint = on;
        Prefs.Set(TintKey, on);
        Changed?.Invoke();
    }

    /// <summary>The table of the verb typed: nothing while the field is empty; a word about a verb the engine cannot place.</summary>
    public void Make()
    {
        var verb = Typed.Trim().ToLowerInvariant();
        Table = null;
        Verb = null;
        Problem = null;
        if (verb.Length > 0)
        {
            try
            {
                var L = Language;
                if (!VerbEngine.CanConjugate(L, verb)) Problem = $"“{verb}” isn’t a verb {LanguageName} knows — try an infinitive, like {L.CommonVerbs.Skip(15).FirstOrDefault() ?? L.CommonVerbs[0]}.";
                else
                {
                    Table = VerbEngine.Make(L, verb);
                    Verb = verb;
                }
            }
            catch (Exception e)
            {
                Log.Write($"verbs {Lang} {verb}: {e.Message}");
                Problem = $"“{verb}” couldn’t be conjugated — try an infinitive.";
            }
        }
        Changed?.Invoke();
    }

    /// <summary>The screenshot suite's start (--verbs fr --verb "manger"), for the tool first opened.</summary>
    public void FromLaunch()
    {
        if (Launch.Take("", "verbs") is { Length: > 0 } l && VerbLanguage.All.Any(x => x.Code == l)) { Lang = l; Prefs.Set(LangKey, l); }
        if (Launch.Take("", "verb") is { Length: > 0 } v) { Typed = v; Prefs.Set(VerbKey, v); }
        Make();
        if (Shot.Asked && Verb != null) Log.Write($"verbs: shows {Lang} {Verb}");
    }

    /// <summary>A pronoun beside its form: French "je" elided to "j’" before a vowel or a mute h (j’ai, j’habite), as the
    /// data's conventions leave to the view.</summary>
    public static string Pronoun(string lang, string pronoun, string form)
    {
        if (lang != "fr" || pronoun != "je" || form.Length == 0 || form == VerbForms.Placeholder) return pronoun;
        return "aeiouyàâéèêëîïôûüœh".Contains(char.ToLowerInvariant(form[0])) ? "j’" : pronoun;
    }

    /// <summary>A tense's table as plain text: the verb and tense, then a pronoun and its form a line.</summary>
    public string Text(VerbTense t)
    {
        if (Table == null || Verb == null) return "";
        var L = Language;
        var f = Table.Forms[t.Id];
        var sb = new StringBuilder();
        sb.Append(Verb).Append(" — ").Append(t.Name).Append('\n');
        if (f.Single) sb.Append(f.One).Append('\n');
        else
        {
            var pronouns = t.Group == "imperativo" ? L.ImperativePronouns : L.Pronouns;
            for (var i = 0; i < f.Forms.Count; i++)
            {
                if (f.Forms[i] == VerbForms.Placeholder) continue;
                sb.Append(Pronoun(Lang, pronouns[i], f.Forms[i])).Append('\t').Append(f.Forms[i]).Append('\n');
            }
        }
        return sb.ToString();
    }

    public string AllText() => Table == null ? "" : string.Join("\n", Language.Tenses.Select(Text));
}

/// <summary>
/// Verb Conjugations (the Mac's VerbsTool): the language across the top, the verb field with its suggestions, and every
/// tense's table under its group's heading; the toggle for irregular cells and Copy all at the right.
/// </summary>
internal sealed class VerbsTool : ContentControl
{
    private readonly VerbsState s = VerbsState.Full;
    private readonly Color tint = ToolsCenter.KindOf("verbs")?.Color ?? K.Hex("#30b0c7");
    private readonly TextBox box;
    private readonly ContentControl suggestions = StudyKit.Host();
    private readonly ContentControl tables = StudyKit.Host();
    private readonly ContentControl note = StudyKit.Host();
    private readonly SelectorBar languages;
    private bool syncing;
    private string? said;

    public VerbsTool(ToolContext c)
    {
        Focusable = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        if (c.TakeInfo("verb") is { } handed) { if (c.TakeInfo("lang") is { } lang) s.SetLang(lang); s.SetTyped(handed); }
        s.FromLaunch();
        languages = new SelectorBar(VerbLanguage.All.Select(l => l.Name).ToList(), Array.FindIndex(VerbLanguage.All, l => l.Code == s.Lang));
        languages.Changed += i => { if (!syncing) s.SetLang(VerbLanguage.All[i].Code); };
        box = ToolKit.Box(s.Typed, "An infinitive, like hablar", t => { if (!syncing) { s.SetTyped(t); Suggest(t); } });
        box.FontSize = 18;
        box.MinHeight = 40;
        System.Windows.Automation.AutomationProperties.SetName(box, "Verb");
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; StudyKit.Put(suggestions, null); s.Make(); }
            if (e.Key == Key.Escape && suggestions.Content != null) { e.Handled = true; StudyKit.Put(suggestions, null); }
        };
        var toggle = new ToggleSwitch { Content = "Highlight irregular forms", IsChecked = s.Tint };
        toggle.Checked += (_, _) => s.SetTint(true);
        toggle.Unchecked += (_, _) => s.SetTint(false);
        var copyAll = K.Button("Copy all", () => StudyKit.Copy(s.AllText()), icon: SymbolRegular.Copy24);
        copyAll.ToolTip = "Copy every tense as plain text";
        var right = K.H(14, toggle, copyAll);
        right.VerticalAlignment = VerticalAlignment.Center;
        var field = K.V(6, K.Text("Verb", Ty.BodyStrong, B.Text2), new LeftMax { Max = 420, Child = K.V(4, box, suggestions) });
        var head = K.Columns("*, auto", field.Col(0), right.Margin(16, 20, 0, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Bottom).Col(1));
        var page = K.V(18, languages, head, note, tables);
        Content = ToolKit.Page(page, 1480);
        StudyKit.Watch(this, a => s.Changed += a, a => s.Changed -= a, Render);
        Render();
    }

    /// <summary>The verbs the language knows that start with what was typed, under the field; a press fills it in.</summary>
    private void Suggest(string typed)
    {
        var t = typed.Trim();
        if (t.Length == 0 || s.Language.Knows(t.ToLowerInvariant())) { StudyKit.Put(suggestions, null); return; }
        var found = VerbEngine.Suggest(s.Language, t);
        if (found.Count == 0) { StudyKit.Put(suggestions, null); return; }
        var list = K.V(0);
        foreach (var v in found)
        {
            var verb = v;
            var row = K.Subtle(K.Text(v, Ty.Body), () => { StudyKit.Put(suggestions, null); syncing = true; box.Text = verb; syncing = false; s.SetTyped(verb); box.CaretIndex = verb.Length; }, new Thickness(10, 6, 10, 6));
            row.HorizontalAlignment = HorizontalAlignment.Stretch;
            row.HorizontalContentAlignment = HorizontalAlignment.Left;
            list.Children.Add(row);
        }
        StudyKit.Put(suggestions, K.Card(list, new Thickness(4)));
    }

    private void Render()
    {
        syncing = true;
        languages.Select(Array.FindIndex(VerbLanguage.All, l => l.Code == s.Lang), false);
        if (box.Text != s.Typed) box.Text = s.Typed;
        syncing = false;
        StudyKit.Put(note, s.Problem != null ? ToolKit.Note(s.Problem) : null);
        StudyKit.Put(tables, s.Table == null ? Empty() : Tables());
        if (Shot.Asked && s.Verb != null)
        {
            var now = $"verbs: shows {s.Lang} {s.Verb}";
            if (now != said) { said = now; Log.Write(now); }
        }
        Shot.Moved();
    }

    private FrameworkElement? Empty()
    {
        if (s.Problem != null || s.Typed.Trim().Length > 0) return null;
        var L = s.Language;
        var chips = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var v in L.CommonVerbs.Take(12))
        {
            var verb = v;
            var b = K.Button(v, () => { syncing = true; box.Text = verb; syncing = false; s.SetTyped(verb); });
            b.MinWidth = 0;
            b.Margin = new Thickness(0, 0, 8, 8);
            chips.Children.Add(b);
        }
        return K.V(10, K.Wrapped($"Type a verb in its infinitive, or start with one of the most common {s.LanguageName} verbs.", Ty.Body, B.Text2), chips);
    }

    /// <summary>Every tense's table under its group's heading, the tables flowing across the page.</summary>
    private FrameworkElement Tables()
    {
        var L = s.Language;
        var v = K.V(22);
        foreach (var group in L.Tenses.Select(t => t.Group).Distinct())
        {
            var tenses = L.Tenses.Where(t => t.Group == group).ToList();
            var cards = tenses.Select(t => (UIElement)TenseCard(L, t)).ToList();
            v.Children.Add(K.V(10, K.Text(L.GroupNames.GetValueOrDefault(group, group), Ty.Subtitle), new MoreGrid(260, 14, cards, stretch: true)));
        }
        return v;
    }

    /// <summary>One tense: its name, Copy, and a pronoun and its form a row — irregular forms in the tool's colour.</summary>
    private FrameworkElement TenseCard(VerbLanguage L, VerbTense t)
    {
        var forms = s.Table!.Forms[t.Id];
        var odd = s.Table.Irregular[t.Id];
        var copy = K.IconButton(SymbolRegular.Copy24, () => StudyKit.Copy(s.Text(t)), $"Copy {t.Name}", 14, 28);
        var head = K.Columns("*, auto", K.Text(t.Name, Ty.BodyStrong).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0), copy.Col(1));
        var rows = K.V(0, head);
        var pronouns = t.Group == "imperativo" ? L.ImperativePronouns : L.Pronouns;
        if (forms.Single) rows.Children.Add(Cell(forms.One, odd[0]).Margin(0, 6, 0, 0));
        else
            for (var i = 0; i < forms.Forms.Count; i++)
            {
                var pronoun = K.Text(VerbsState.Pronoun(L.Code, pronouns[i], forms.Forms[i]), Ty.Body, B.Text2);
                pronoun.TextTrimming = TextTrimming.CharacterEllipsis;
                var row = K.Columns("96, *", pronoun.Col(0), Cell(forms.Forms[i], odd[i]).Col(1));
                row.Margin = new Thickness(0, i == 0 ? 6 : 3, 0, 0);
                rows.Children.Add(row);
            }
        var card = K.Card(rows, new Thickness(14, 10, 14, 12));
        card.VerticalAlignment = VerticalAlignment.Top;
        return card;
    }

    private FrameworkElement Cell(string form, bool irregular)
    {
        var t = K.Text(form, Ty.Body);
        t.TextWrapping = TextWrapping.Wrap;
        if (irregular && s.Tint && form != VerbForms.Placeholder)
        {
            t.FontWeight = FontWeights.SemiBold;
            t.Foreground = K.Brush(K.Darkened(tint));
            var b = new Border { Child = t, Background = K.Brush(K.WithAlpha(tint, Appearance.Dark ? 0.2 : 0.12)), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 2), HorizontalAlignment = HorizontalAlignment.Left };
            b.ToolTip = "Irregular";
            return b;
        }
        t.Margin = new Thickness(6, 1, 6, 2);
        return t;
    }
}

/// <summary>Verb Conjugations' pin, opened: the language, a verb, and its present tense; Open full screen carries them.</summary>
internal sealed class VerbsCompact
{
    private readonly VerbsState s = VerbsState.Quick;
    private readonly ComboBox lang = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox box;
    private readonly ContentControl table = StudyKit.Host();
    private bool syncing;

    public FrameworkElement View { get; }

    public VerbsCompact(ToolContext c)
    {
        if (Launch.Get("pin-open") == "verbs") s.FromLaunch();
        foreach (var (code, name) in VerbLanguage.All) lang.Items.Add(Everyday.Item(name, code));
        lang.SelectionChanged += (_, _) => { if (!syncing && Everyday.Chosen(lang) is { } code) s.SetLang(code); };
        System.Windows.Automation.AutomationProperties.SetName(lang, "Language");
        box = ToolKit.Box(s.Typed, "An infinitive", t => { if (!syncing) s.SetTyped(t); });
        System.Windows.Automation.AutomationProperties.SetName(box, "Verb");
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; c.OpenFull(Carried(), null); } };
        View = K.V(10, K.Columns("*, 8, *", lang.Col(0), box.Col(2)), table);
        c.Carry = Carried;
        StudyKit.Watch(View, a => s.Changed += a, a => s.Changed -= a, Render);
        if (s.Table == null) s.Make();
        Render();
    }

    private Dictionary<string, string>? Carried() => s.Typed.Trim().Length == 0 ? null : new Dictionary<string, string> { ["verb"] = s.Typed.Trim(), ["lang"] = s.Lang };

    private void Render()
    {
        syncing = true;
        Everyday.Choose(lang, s.Lang);
        if (box.Text != s.Typed) box.Text = s.Typed;
        syncing = false;
        if (s.Table == null || s.Verb == null)
        {
            StudyKit.Put(table, K.Wrapped(s.Problem ?? "The present tense shows here; Enter opens every tense.", Ty.Body, B.Text2));
            return;
        }
        var L = s.Language;
        var present = L.Tenses[0];
        var forms = s.Table.Forms[present.Id];
        var odd = s.Table.Irregular[present.Id];
        var rows = K.V(2, K.Text($"{s.Verb} — {present.Name}", Ty.BodyStrong));
        for (var i = 0; i < forms.Forms.Count; i++)
        {
            var form = K.Text(forms.Forms[i], Ty.Body);
            if (odd[i] && s.Tint) { form.FontWeight = FontWeights.SemiBold; form.Fg(B.AccentText); }
            rows.Children.Add(K.Columns("96, *", K.Text(VerbsState.Pronoun(L.Code, L.Pronouns[i], forms.Forms[i]), Ty.Body, B.Text2).Col(0), form.Col(1)));
        }
        StudyKit.Put(table, K.Card(rows, new Thickness(12, 8, 12, 10)));
    }
}
