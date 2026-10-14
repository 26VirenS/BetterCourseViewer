using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

#pragma warning disable CA2255
internal static class GradeNeededRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.Register("need", c => new GradeNeededTool(c).View, c => new GradeNeededCompact(c).View);
}
#pragma warning restore CA2255

/// <summary>A piece of work not yet graded and its share of the final grade (native-app.js gradeNeeded).</summary>
internal sealed class NeedPiece
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double Worth { get; set; }
    public double? Pts { get; set; }
    public string? Group { get; set; }
    public double? GroupWeight { get; set; }
    public double? GroupPts { get; set; }
    public double? Total { get; set; }
    public string? Due { get; set; }
    /// <summary>(2.99.33) "28.5% of your grade".</summary>
    public string? WorthText { get; set; }
    /// <summary>(2.99.33) The picker's words: "Midterm 2 · 28.5% of your grade".</summary>
    public string? Label { get; set; }
    /// <summary>(2.99.33) Its group's share, said as it reads: "Midterms are 57% of the grade" (is/are as the name takes
    /// it); by points, "20 of the course’s 122 points".</summary>
    public string? GroupText { get; set; }
}

/// <summary>A course's score now and the work still to come (native-app.js gradeNeeded).</summary>
internal sealed class NeedData
{
    public string Id { get; set; } = "";
    public double? Now { get; set; }
    public bool? Weighted { get; set; }
    public bool? Own { get; set; }
    public List<NeedPiece> Pieces { get; set; } = new();
}

/// <summary>
/// The sum everyone does by hand, as the web's Grade needed (the Mac's NeedMath): the grade now, less what the piece will
/// move it, over what the piece is worth; the rest of the work is assumed to stay where it is.
/// </summary>
internal static class NeedMath
{
    /// <summary>The usual cut-offs (the GPA card's).</summary>
    public static readonly (string Letter, double Cut)[] Scale =
    {
        ("A+", 97), ("A", 93), ("A−", 90), ("B+", 87), ("B", 83), ("B−", 80), ("C+", 77), ("C", 73), ("C−", 70), ("D", 60),
    };

    public enum Kind { Ok, Over, Under }

    public sealed record Answer(int Pct, Kind Kind, double Ends100, double Ends0);

    /// <summary>The mark on work worth <paramref name="worth"/>% of the final grade that lands <paramref name="goal"/>% from
    /// <paramref name="now"/>% — or null when the numbers do not add up.</summary>
    public static Answer? Needed(double? now, double? worth, double? goal)
    {
        if (now is not { } n || worth is not { } wt || goal is not { } g || !double.IsFinite(n) || !double.IsFinite(wt) || !double.IsFinite(g)) return null;
        var w = wt / 100;
        if (w <= 0 || w > 1) return null;
        var x = (g - n * (1 - w)) / w;
        if (!double.IsFinite(x)) return null;
        var kind = x > 100 ? Kind.Over : x <= 0 ? Kind.Under : Kind.Ok;
        var pct = kind == Kind.Ok ? (int)Math.Ceiling(x - 1e-9) : kind == Kind.Over ? 100 : 0;
        return new Answer(pct, kind, R1(n * (1 - w) + 100 * w), R1(n * (1 - w)));
    }

    /// <summary>The next letter up from a grade (A+ for a grade already over every cut-off).</summary>
    public static (string Letter, double Cut) NextLetter(double now)
    {
        foreach (var step in Scale.Reverse()) if (step.Cut > now) return step;
        return Scale[0];
    }

    public static double R1(double v) => Math.Round(v * 10, MidpointRounding.AwayFromZero) / 10;

    /// <summary>"88", "88.5".</summary>
    public static string Text(double v)
    {
        var r = R1(v);
        if (Math.Abs(r) >= 1e9) return r.ToString(CultureInfo.InvariantCulture);
        return r == Math.Round(r) ? ((long)r).ToString(CultureInfo.InvariantCulture) : r.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A field's number ("88", "88.5%", "88,5"), or null.</summary>
    public static double? Number(string s)
    {
        var t = s.Replace("%", "").Replace(",", ".").Trim();
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;
    }

    public static string An(string letter) => letter.StartsWith('A') ? $"an {letter}" : $"a {letter}";

    /// <summary>The answer large: the mark, or that it is out of reach or already there.</summary>
    public static string Big(Answer? r) => r?.Kind switch
    {
        null => "—",
        Kind.Over => "Out of reach",
        Kind.Under => "Already there",
        _ => $"{r.Pct}%",
    };

    public static readonly Color Red = K.Tone("red"), Green = K.Tone("green");

    /// <summary>Grade needed's own colour (its tile's).</summary>
    public static Color Tint => ToolsCenter.KindOf("need")?.Color ?? K.Hex("#ff375f");
}

/// <summary>
/// What Grade needed is showing (the Mac's @State on GradeNeededTool), kept while its view comes and goes: the course
/// chosen (null until one is, "" for numbers of your own), what was read of it, the piece of work chosen, and the three
/// numbers as typed.
/// </summary>
internal sealed class NeedState
{
    public static NeedState Shared { get; } = new();

    public string? Course;
    /// <summary>Numbers of your own only because no course was there to choose yet: the first course to come is chosen.</summary>
    public bool WaitingForCourses;
    public NeedData? Data;
    public bool Loading;
    public string? Failed;
    public string Piece = "custom";
    public string Now = "", Worth = "", Goal = "", Letter = "custom";

    public event Action? Changed;

    public void Raise() => Changed?.Invoke();

    public IReadOnlyList<NeedPiece> Pieces => Data?.Pieces ?? (IReadOnlyList<NeedPiece>)Array.Empty<NeedPiece>();
    public NeedPiece? PickedPiece => Pieces.FirstOrDefault(p => p.Id == Piece);
    public double? WorthValue => PickedPiece?.Worth ?? NeedMath.Number(Worth);
    public NeedMath.Answer? Answer => NeedMath.Needed(NeedMath.Number(Now), WorthValue, NeedMath.Number(Goal));
}

/// <summary>
/// Grade needed (the Mac's GradeNeededTool): what it takes on the work still to come to finish with the grade wanted.
/// Every number comes from Canvas where it can — the course's current score, the weight of each piece of work left — and
/// the goal is a letter on the usual cut-offs or a number of your own. Out of reach and already there are said plainly,
/// and every letter's price is listed beside the answer.
/// </summary>
internal sealed class GradeNeededTool
{
    private readonly Engine.Engine engine;
    private readonly NeedState s = NeedState.Shared;
    private readonly ComboBox course = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ContentControl failed = new() { Focusable = false };
    private readonly TextBox now, worth, goal;
    private readonly TextBlock nowHint;
    private readonly ComboBox piece = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly FrameworkElement worthField;
    private readonly TextBlock pieceHint = K.Wrapped("", Ty.Caption, B.Text2);
    private readonly ComboBox letter = new() { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ContentControl worthNote = new() { Focusable = false };
    private readonly TextBlock foot = K.Wrapped("", Ty.Caption, B.Text2);
    private readonly ContentControl answer = new() { Focusable = false };
    private readonly ContentControl letters = new() { Focusable = false };
    private List<CourseRow>? coursesShown;
    private string? lmsShown;
    private NeedData? piecesShown;
    private bool syncing;

    public FrameworkElement View { get; }

    private string Lms => engine.LmsName;
    private CourseRow? Chosen => engine.Courses.FirstOrDefault(c => c.Id == s.Course);

    public GradeNeededTool(ToolContext c)
    {
        engine = c.Engine;
        // ---- the numbers ----
        course.SelectionChanged += (_, _) => { if (!syncing && Everyday.Chosen(course) is { } id) Choose(id); };
        AutomationProperties.SetName(course, "Course");
        now = ToolKit.Box("", "e.g. 88", t => { if (!syncing) { s.Now = t; s.Raise(); } });
        AutomationProperties.SetName(now, "Your grade now");
        (var nowField, nowHint) = Everyday.Field("Your grade now", now, "%");
        piece.SelectionChanged += (_, _) => { if (!syncing && Everyday.Chosen(piece) is { } id) ChoosePiece(id); };
        AutomationProperties.SetName(piece, "What is left");
        worth = ToolKit.Box("", "e.g. 25", t => { if (!syncing) { s.Worth = t; s.Raise(); } });
        AutomationProperties.SetName(worth, "Worth this much of the final grade");
        worthField = Everyday.Field("Worth this much of the final grade", worth, "%").View;
        foreach (var (l, cut) in NeedMath.Scale) letter.Items.Add(Everyday.Item($"{l} · {NeedMath.Text(cut)}%", l));
        letter.Items.Add(Everyday.Divider());
        letter.Items.Add(Everyday.Item("A number of my own", "custom"));
        letter.SelectionChanged += (_, _) => { if (!syncing && Everyday.Chosen(letter) is { } l) ChooseLetter(l); };
        AutomationProperties.SetName(letter, "The grade you want");
        goal = ToolKit.Box("", "e.g. 90", t => { if (!syncing) { s.Goal = t; s.Letter = "custom"; s.Raise(); } });
        AutomationProperties.SetName(goal, "The grade you want");
        var goalField = Everyday.Field("The grade you want", goal, "%", 160).View;

        var courseSection = Everyday.Section("Course", K.V(8, new LeftMax { Max = 460, Child = course }, failed));
        var nowSection = Everyday.Section("Now", nowField);
        var leftSection = Everyday.Section("What is left", K.V(10, piece, worthField, pieceHint));
        var pair = K.Columns("*, 22, *", nowSection.Col(0), leftSection.Col(2));
        nowSection.VerticalAlignment = VerticalAlignment.Top;
        leftSection.VerticalAlignment = VerticalAlignment.Top;
        var letterField = K.V(6, K.Text("A letter", Ty.BodyStrong, B.Text2), letter);
        letterField.VerticalAlignment = VerticalAlignment.Bottom;
        goalField.VerticalAlignment = VerticalAlignment.Bottom;
        var goalSection = Everyday.Section("Goal", K.Columns("auto, 16, auto", letterField.Col(0), goalField.Col(2)).Align(HorizontalAlignment.Left));
        var inputs = K.V(26, courseSection, pair, goalSection, worthNote, foot);

        // ---- the answer ----
        var results = K.V(24, answer, Everyday.Section("Every letter", letters));

        View = Everyday.Page(ToolKit.Columns(inputs, results, 420, 980));
        Start(c);
        Update();
        Everyday.Follow(View, () => { s.Changed += Update; engine.PropertyChanged += OnEngine; Update(); },
            () => { s.Changed -= Update; engine.PropertyChanged -= OnEngine; });
    }

    private void OnEngine(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(Engine.Engine.Courses) or nameof(Engine.Engine.LmsName))) return;
        if (s.WaitingForCourses && s.Course == "" && engine.Courses.Count > 0) { Pick(); return; }
        Update();
    }

    // ---- choosing ----

    /// <summary>Numbers handed over from the pin (no course then), else the first course with a score, as the Mac's start().</summary>
    private void Start(ToolContext c)
    {
        string? n = c.TakeInfo("now"), w = c.TakeInfo("worth"), g = c.TakeInfo("goal");
        if (n != null || w != null || g != null)
        {
            s.Course = "";
            s.WaitingForCourses = false;
            s.Data = null;
            s.Loading = false;
            s.Failed = null;
            s.Piece = "custom";
            s.Now = n ?? "";
            s.Worth = w ?? "";
            s.Goal = g ?? "";
            s.Letter = "custom";
            s.Raise();
            return;
        }
        if (s.Course == null) Pick();
    }

    private void Pick()
    {
        if ((engine.Courses.FirstOrDefault(x => x.Score != null) ?? engine.Courses.FirstOrDefault()) is { } pick)
        {
            s.WaitingForCourses = false;
            Choose(pick.Id);
        }
        else
        {
            s.Course = "";
            s.WaitingForCourses = true;
            s.Raise();
        }
    }

    private async void Choose(string id)
    {
        s.Failed = null;
        s.WaitingForCourses = false;
        if (id.Length == 0)
        {
            s.Course = "";
            s.Data = null;
            s.Loading = false;
            s.Piece = "custom";
            s.Raise();
            return;
        }
        s.Course = id;
        s.Data = null;
        s.Piece = "custom";
        if (engine.Courses.FirstOrDefault(x => x.Id == id)?.Score is { } score) SetNow(score);
        s.Loading = true;
        s.Raise();
        try
        {
            var d = await engine.Call<NeedData>("gradeNeeded", new { id });
            if (s.Course != id) return;
            s.Data = d;
            s.Loading = false;
            if (d.Now is { } n) SetNow(n);
            if (d.Pieces.FirstOrDefault(p => p.Worth > 0) is { } first)
            {
                s.Piece = first.Id;
                s.Worth = NeedMath.Text(first.Worth);
            }
            s.Raise();
        }
        catch (Exception e)
        {
            if (s.Course != id) return;
            s.Loading = false;
            s.Failed = $"The assignments could not be read ({e.Message.TrimEnd('.')}). Type what the work is worth.";
            Log.Write($"gradeNeeded {id}: {e.Message}");
            s.Raise();
        }
    }

    private void SetNow(double score)
    {
        s.Now = NeedMath.Text(score);
        var next = NeedMath.NextLetter(score);
        s.Letter = next.Letter;
        s.Goal = NeedMath.Text(next.Cut);
    }

    private void ChoosePiece(string id)
    {
        s.Piece = id;
        if (s.Pieces.FirstOrDefault(p => p.Id == id) is { } p) s.Worth = NeedMath.Text(p.Worth);
        s.Raise();
    }

    private void ChooseLetter(string l)
    {
        s.Letter = l;
        var step = NeedMath.Scale.FirstOrDefault(x => x.Letter == l);
        if (step.Letter != null) s.Goal = NeedMath.Text(step.Cut);
        s.Raise();
    }

    // ---- showing ----

    private void Update()
    {
        syncing = true;
        // the course menu: the courses (with their scores), then numbers of your own
        if (!ReferenceEquals(coursesShown, engine.Courses) || lmsShown != Lms)
        {
            coursesShown = engine.Courses;
            lmsShown = Lms;
            course.Items.Clear();
            foreach (var row in engine.Courses) course.Items.Add(Everyday.Item(row.Score == null ? row.Code : $"{row.Code} · {row.ScoreText}", row.Id));
            if (engine.Courses.Count > 0) course.Items.Add(Everyday.Divider());
            course.Items.Add(Everyday.Item($"Not from {Lms}: my own numbers", ""));
        }
        Everyday.Choose(course, s.Course ?? "");
        failed.Content = s.Failed == null ? null : ToolKit.Note(s.Failed);

        // the pieces of work left, once read
        var chosen = Chosen;
        piece.Visibility = chosen != null ? Visibility.Visible : Visibility.Collapsed;
        if (!ReferenceEquals(piecesShown, s.Data) || piece.Items.Count == 0)
        {
            piecesShown = s.Data;
            piece.Items.Clear();
            // (the engine's words: "Midterm 2 · 28.5% of your grade" — what the share is a share of, said)
            foreach (var p in s.Pieces) piece.Items.Add(Everyday.Item(p.Label is { Length: > 0 } l ? l : $"{p.Name} · {NeedMath.Text(p.Worth)}% of your grade", p.Id));
            if (s.Pieces.Count > 0) piece.Items.Add(Everyday.Divider());
            piece.Items.Add(Everyday.Item("Something else…", "custom"));
        }
        Everyday.Choose(piece, s.PickedPiece == null ? "custom" : s.Piece);
        worthField.Visibility = s.PickedPiece == null ? Visibility.Visible : Visibility.Collapsed;
        pieceHint.Text = PieceHint(chosen);

        if (now.Text != s.Now) now.Text = s.Now;
        if (worth.Text != s.Worth) worth.Text = s.Worth;
        if (goal.Text != s.Goal) goal.Text = s.Goal;
        Everyday.Choose(letter, s.Letter);
        Everyday.Say(nowHint, NowHint(chosen));
        syncing = false;

        worthNote.Content = s.WorthValue is { } w && (w <= 0 || w > 100)
            ? ToolKit.Note("The work left has to be worth something between 0 and 100% of the grade.")
            : null;
        foot.Text = $"Assumes the rest of your work stays at your grade now. {Lms} counts graded work only, so this is the mark on that one piece with everything else as it is.";

        answer.Content = AnswerCard(s.Answer);
        letters.Content = Letters();
    }

    private string NowHint(CourseRow? c)
    {
        if (c == null) return "Your current grade, as a percentage.";
        if (c.Score == null && s.Data?.Now == null) return $"{Lms} has no score for this course yet.";
        return s.Data?.Own == true ? $"From {Lms}, by your own weights." : $"From {Lms}: your current score, graded work only.";
    }

    private string PieceHint(CourseRow? chosen)
    {
        if (s.PickedPiece is { } p)
        {
            var pts = ToolKit.Plural((int)Math.Round((decimal)(p.Pts ?? 0), MidpointRounding.AwayFromZero), "point");
            // (the engine's words for the group, as the Mac's: "Midterms are 57% of the grade", is or are as the name takes it)
            if (s.Data?.Weighted == true)
            {
                var group = p.GroupText is { Length: > 0 } g ? g : $"{p.Group ?? "Its group"} is {NeedMath.Text(p.GroupWeight ?? 0)}% of the grade";
                return $"{group}{(s.Data?.Own == true ? " (your own weights)" : "")}; this is {pts} of the group’s {NeedMath.Text(p.GroupPts ?? 0)}.";
            }
            return p.GroupText is { Length: > 0 } t ? $"This is {t}." : $"{pts} of the course’s {NeedMath.Text(p.Total ?? 0)}.";
        }
        if (s.Loading) return "Reading the assignments…";
        if (chosen != null && s.Data != null && s.Pieces.Count == 0) return $"Nothing left ungraded that {Lms} knows of. Type what the work is worth.";
        return "How much of the final grade the work still to come is worth.";
    }

    /// <summary>The answer: the mark large in its colour (red out of reach, green already there), and what it means.</summary>
    private FrameworkElement AnswerCard(NeedMath.Answer? r)
    {
        var tint = r?.Kind == NeedMath.Kind.Over ? NeedMath.Red : r?.Kind == NeedMath.Kind.Under ? NeedMath.Green : NeedMath.Tint;
        var big = Everyday.Figures(NeedMath.Big(r), r?.Kind == NeedMath.Kind.Ok ? 76 : 44, FontWeights.Bold, B.Text2);
        if (r != null) big.Foreground = new SolidColorBrush(tint);
        var line = K.Wrapped(LineText(r), Ty.Body, B.Text2);
        var card = Everyday.TintedCard(K.V(8, Everyday.Shrinks(big), line), r == null ? null : tint, new Thickness(24));
        AutomationProperties.SetName(card, $"{NeedMath.Big(r)} {LineText(r)}");
        return card;
    }

    private string LineText(NeedMath.Answer? r)
    {
        if (r == null) return "Fill in the three numbers.";
        var place = s.PickedPiece is { } p ? $"on {p.Name}" : "on what is left";
        return r.Kind switch
        {
            NeedMath.Kind.Ok => $"{place} to finish with {GoalWord()}.",
            NeedMath.Kind.Over => $"Even 100% {place} ends at {NeedMath.Text(r.Ends100)}%. Aim for a goal under that.",
            _ => $"Even 0% {place} leaves you at {NeedMath.Text(r.Ends0)}%.",
        };
    }

    private string GoalWord()
    {
        if (NeedMath.Number(s.Goal) is not { } g) return "your goal";
        var step = NeedMath.Scale.FirstOrDefault(x => x.Letter == s.Letter);
        if (step.Letter != null && step.Cut == g) return $"{NeedMath.An(step.Letter)} ({NeedMath.Text(g)}%)";
        return $"{NeedMath.Text(g)}%";
    }

    /// <summary>Every letter's price, down to the first already there; the letter aimed for lit in Grade needed's colour.</summary>
    private FrameworkElement Letters()
    {
        var n = NeedMath.Number(s.Now);
        var worthValue = s.WorthValue;
        var rows = new List<(string Letter, string Text, bool Over)>();
        if (n != null && worthValue != null)
            foreach (var step in NeedMath.Scale)
            {
                if (NeedMath.Needed(n, worthValue, step.Cut) is not { } q) break;
                rows.Add((step.Letter, q.Kind switch { NeedMath.Kind.Over => "out of reach", NeedMath.Kind.Under => "already there", _ => $"{q.Pct}%" }, q.Kind == NeedMath.Kind.Over));
                if (q.Kind == NeedMath.Kind.Under) break;
            }
        if (rows.Count == 0) return K.Wrapped("The price of each letter shows once the numbers are in.", Ty.Body, B.Text2);
        var list = K.V(0);
        foreach (var row in rows)
        {
            if (list.Children.Count > 0) list.Children.Add(K.Divider(16));
            var price = K.Text(row.Text, Ty.Body, row.Over ? B.Text2 : B.Text);
            Typography.SetNumeralAlignment(price, FontNumeralAlignment.Tabular);
            var line = K.Columns("44, *, auto", K.Text(row.Letter, Ty.BodyStrong).Col(0), price.Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(2));
            var b = new Border { Child = line, Padding = new Thickness(16, 9, 16, 9) };
            if (row.Letter == s.Letter) b.Background = new SolidColorBrush(K.WithAlpha(NeedMath.Tint, 0.12));
            list.Children.Add(b);
        }
        return K.Card(list, new Thickness(0, 4, 0, 4));
    }
}

/// <summary>
/// Grade needed's pin, opened (the Mac's GradeNeededCompact): your grade now, what the work left is worth, the grade
/// wanted — and the mark it takes. Enter opens the whole tool with the three numbers.
/// </summary>
internal sealed class GradeNeededCompact
{
    /// <summary>The three numbers as typed, kept while the flyout comes and goes.</summary>
    private static string keptNow = "", keptWorth = "", keptGoal = "";

    private readonly ToolContext c;
    private readonly TextBox now, worth, goal;
    private readonly TextBlock big = Everyday.Figures("—", 34, FontWeights.Bold, B.Text2);
    private readonly TextBlock sub = K.Wrapped("", Ty.Body, B.Text2);

    public FrameworkElement View { get; }

    public GradeNeededCompact(ToolContext c)
    {
        this.c = c;
        // (the screenshot suite: --need-numbers 88,25,90 fills them in)
        if (Launch.Take("", "need-numbers") is { Length: > 0 } nums && nums.Split(',') is { Length: 3 } parts)
            (keptNow, keptWorth, keptGoal) = (parts[0].Trim(), parts[1].Trim(), parts[2].Trim());
        // (each number named for what it is, with its unit, as the Mac's: "Still to come" is a share of the grade, not a
        //  count of assignments)
        now = Box(keptNow, "88", "Grade now, percent", t => keptNow = t);
        worth = Box(keptWorth, "25", "Still to come, percent of the grade", t => keptWorth = t);
        goal = Box(keptGoal, "90", "Goal, percent", t => keptGoal = t);
        var fields = K.Columns("auto, 72, auto");
        void Row(int at, string label, TextBox box, string unit)
        {
            fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var t = K.Text(label, Ty.Body, B.Text2);
            t.FontWeight = FontWeights.SemiBold;
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Margin = new Thickness(0, at == 0 ? 0 : 8, 12, 0);
            box.Margin = new Thickness(0, at == 0 ? 0 : 8, 0, 0);
            var u = K.Text(unit, Ty.Body, B.Text2).Margin(8, at == 0 ? 0 : 8, 0, 0);
            u.VerticalAlignment = VerticalAlignment.Center;
            fields.Children.Add(t.Row(at).Col(0));
            fields.Children.Add(box.Row(at).Col(1));
            fields.Children.Add(u.Row(at).Col(2));
        }
        Row(0, "Grade now", now, "%");
        Row(1, "Still to come", worth, "% of grade");
        Row(2, "Goal", goal, "%");
        View = K.V(12, fields, K.V(2, Everyday.Shrinks(big), sub));
        Update();
    }

    private TextBox Box(string text, string placeholder, string name, Action<string> keep)
    {
        var b = ToolKit.Box(text, placeholder, t => { keep(t); Update(); });
        AutomationProperties.SetName(b, name);
        b.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            c.OpenFull(new Dictionary<string, string> { ["now"] = now.Text, ["worth"] = worth.Text, ["goal"] = goal.Text }, null);
        };
        return b;
    }

    private void Update()
    {
        if (now == null || worth == null || goal == null) return;
        var r = NeedMath.Needed(NeedMath.Number(now.Text), NeedMath.Number(worth.Text), NeedMath.Number(goal.Text));
        big.Text = NeedMath.Big(r);
        if (r == null) big.Fg(B.Text2);
        else big.Foreground = new SolidColorBrush(r.Kind == NeedMath.Kind.Over ? NeedMath.Red : NeedMath.Tint);
        sub.Text = r?.Kind switch
        {
            null => "Your grade now, the share of it still to come, the grade you want.",
            NeedMath.Kind.Ok => "on everything still to come",
            NeedMath.Kind.Over => "not even full marks would get there",
            _ => "the goal is already yours",
        };
    }
}
